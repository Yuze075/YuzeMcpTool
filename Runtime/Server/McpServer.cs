#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    public sealed class McpServer : IDisposable
    {
        private const string ProtocolVersion = "2025-06-18";
        private const string FallbackProtocolVersion = "2025-03-26";
        private const string ServerName = "YuzeMcpTool";
        private const string ManualOwnerId = "manual";
        private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
        {
            ProtocolVersion,
            FallbackProtocolVersion,
        };
        private static readonly Lazy<McpServer> LazyShared = new(() => new McpServer());

        private readonly object _syncRoot = new();
        private readonly HashSet<string> _startOwners = new(StringComparer.Ordinal);
        private McpSessionRegistry _sessions = new();
        private McpServerOptions _options = new();
        private McpServerState _state = new();
        private IMcpTransport? _transport;
        private CancellationTokenSource? _cancellation;
        private EvalJsCodeTool _evalTool;

        private McpServer()
        {
            _evalTool = new EvalJsCodeTool(_options);
        }

        public static McpServer Shared => LazyShared.Value;

        public McpServerState State
        {
            get
            {
                lock (_syncRoot)
                {
                    _state.ActiveSessionCount = _sessions.Count;
                    _state.Sessions = _sessions.GetSnapshots();
                    _state.UptimeSeconds = _state.IsRunning && _state.StartedAtUtc != default
                        ? Math.Max(0, (DateTime.UtcNow - _state.StartedAtUtc).TotalSeconds)
                        : 0;
                    return _state;
                }
            }
        }

        public void Start(McpServerOptions? options = null)
        {
            StartWithOwner(ManualOwnerId, options);
        }

        public void StartWithOwner(string ownerId, McpServerOptions? options = null)
        {
            lock (_syncRoot)
            {
                ownerId = NormalizeOwnerId(ownerId);
                _startOwners.Add(ownerId);
                if (_transport is { IsRunning: true }) return;

                _options = options ?? new McpServerOptions();
                _evalTool = new EvalJsCodeTool(_options);
                _sessions.Dispose();
                _sessions = new McpSessionRegistry();
                _cancellation = new CancellationTokenSource();
                _transport = McpTransportFactory.Create();

                try
                {
                    var result = _transport.Start(_options, ProcessRequestAsync, _cancellation.Token);
                    if (!result.Success)
                    {
                        _state.LastError = result.Error;
                        _state.IsRunning = false;
                        _state.Endpoint = string.Empty;
                        Debug.LogWarning($"[YuzeMcpTool] MCP transport '{_transport.Name}' did not start: {result.Error}");
                        _transport.Stop();
                        _transport = null;
                        _cancellation?.Dispose();
                        _cancellation = null;
                        _startOwners.Remove(ownerId);
                        return;
                    }

                    _state = new McpServerState
                    {
                        IsRunning = true,
                        Endpoint = result.Endpoint,
                        StartedAtUtc = DateTime.UtcNow,
                        LastError = string.Empty,
                    };

                    MainThreadDispatcher.Initialize();
                    UnityLogBuffer.Start();
                    Debug.Log($"[YuzeMcpTool] MCP server started via {_transport.Name}: {_state.Endpoint}");
                }
                catch (Exception ex)
                {
                    _state.LastError = ex.Message;
                    _state.IsRunning = false;
                    _state.Endpoint = string.Empty;
                    _transport?.Stop();
                    _transport = null;
                    _cancellation?.Dispose();
                    _cancellation = null;
                    _startOwners.Remove(ownerId);
                    Debug.LogError($"[YuzeMcpTool] Failed to start MCP server: {ex.Message}");
                }
            }
        }

        public void StopOwner(string ownerId)
        {
            lock (_syncRoot)
            {
                _startOwners.Remove(NormalizeOwnerId(ownerId));
                if (_startOwners.Count > 0) return;
                StopCore();
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _startOwners.Clear();
                StopCore();
            }
        }

        public void Dispose() => Stop();

        private void StopCore()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;

            if (_transport != null)
            {
                try
                {
                    _transport.Stop();
                }
                catch (Exception)
                {
                    // Transport shutdown is best effort.
                }
                _transport = null;
            }

            _sessions.Dispose();
            _sessions = new McpSessionRegistry();
            _state.IsRunning = false;
            _state.Endpoint = string.Empty;
            _state.ActiveSessionCount = 0;
            _state.Sessions = Array.Empty<McpSessionSnapshot>();
            _state.UptimeSeconds = 0;
        }

        private static string NormalizeOwnerId(string ownerId) =>
            string.IsNullOrWhiteSpace(ownerId) ? ManualOwnerId : ownerId;

        private async Task<McpTransportResponse> ProcessRequestAsync(McpTransportRequest request, CancellationToken cancellationToken)
        {
            // Transport implementations normalize HTTP details; this method owns MCP routing and JSON-RPC semantics.
            var headers = BuildCorsHeaders();

            try
            {
                if (!IsOriginAllowed(request))
                {
                    return McpTransportResponse.Json(403, JsonRpcError(null, -32000, "Forbidden origin."), headers);
                }

                if (request.Method == "OPTIONS")
                    return McpTransportResponse.NoContent(204, headers);

                if (IsPath(request.Path, "/health"))
                {
                    var health = await MainThreadDispatcher.RunAsync(BuildHealthObject);
                    return McpTransportResponse.Json(200, health, headers);
                }

                if (!IsPath(request.Path, "/mcp"))
                {
                    return McpTransportResponse.Json(404, McpData.Obj(("error", "Not Found")), headers);
                }

                if (request.Method == "DELETE")
                {
                    var sessionId = request.GetHeader("mcp-session-id") ?? string.Empty;
                    if (string.IsNullOrEmpty(sessionId))
                        return McpTransportResponse.Json(400, JsonRpcError(null, -32000, "Mcp-Session-Id header is required."), headers);

                    if (!_sessions.TryRemoveIdle(sessionId, out var isRunning))
                    {
                        if (isRunning)
                            return McpTransportResponse.Json(409, JsonRpcError(null, -32003, "Session has an active eval. Retry after eval completes."), headers);
                        return McpTransportResponse.Json(404, JsonRpcError(null, -32001, "Session not found."), headers);
                    }

                    return McpTransportResponse.NoContent(200, headers);
                }

                if (request.Method != "POST")
                {
                    headers["Allow"] = "POST, GET, OPTIONS, DELETE";
                    return McpTransportResponse.Json(405, McpData.Obj(("error", "Method Not Allowed")), headers);
                }

                if (Encoding.UTF8.GetByteCount(request.Body) > _options.MaxRequestBodyBytes)
                    throw new InvalidOperationException($"MCP request body exceeds {_options.MaxRequestBodyBytes} bytes.");

                var parsed = LitJson.Parse(request.Body);
                var result = await HandleJsonRpcAsync(parsed, request, headers, cancellationToken);

                return result.Body == null
                    ? McpTransportResponse.NoContent(result.StatusCode, headers)
                    : McpTransportResponse.Json(result.StatusCode, result.Body, headers);
            }
            catch (LitJson.JsonException ex)
            {
                return McpTransportResponse.Json(400, JsonRpcError(null, -32700, "Parse error: " + ex.Message), headers);
            }
            catch (FormatException ex)
            {
                return McpTransportResponse.Json(400, JsonRpcError(null, -32700, "Parse error: " + ex.Message), headers);
            }
            catch (InvalidOperationException ex)
            {
                return McpTransportResponse.Json(413, JsonRpcError(null, -32000, ex.Message), headers);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return McpTransportResponse.Json(500, JsonRpcError(null, -32603, "Internal error: " + ex.Message), headers);
            }
        }

        private async Task<McpHttpResult> HandleJsonRpcAsync(
            object? parsed,
            McpTransportRequest request,
            Dictionary<string, string> responseHeaders,
            CancellationToken cancellationToken)
        {
            if (parsed is List<object?> batch)
            {
                var statusCode = 202;
                var responses = new List<object?>();
                foreach (var item in batch)
                {
                    var result = await HandleSingleJsonRpcAsync(item, request, responseHeaders, cancellationToken);
                    statusCode = SelectBatchStatusCode(statusCode, result.StatusCode);
                    if (result.Body != null) responses.Add(result.Body);
                }
                return new McpHttpResult(responses.Count == 0 ? statusCode : SelectResponseStatusCode(statusCode), responses.Count == 0 ? null : responses);
            }

            return await HandleSingleJsonRpcAsync(parsed, request, responseHeaders, cancellationToken);
        }

        private async Task<McpHttpResult> HandleSingleJsonRpcAsync(
            object? parsed,
            McpTransportRequest request,
            Dictionary<string, string> responseHeaders,
            CancellationToken cancellationToken)
        {
            var message = McpData.AsObject(parsed);
            if (message == null)
                return new McpHttpResult(400, JsonRpcError(null, -32600, "Invalid Request"));

            message.TryGetValue("id", out var id);
            var hasId = message.ContainsKey("id");
            var method = McpData.GetString(message, "method") ?? string.Empty;

            if (string.IsNullOrEmpty(method))
                return new McpHttpResult(400, JsonRpcError(id, -32600, "Invalid Request: missing method."));

            if (method == "initialize")
                return new McpHttpResult(200, HandleInitialize(message, responseHeaders));

            if (!TryResolveSession(request, out var session, out var error))
                return new McpHttpResult(error.HttpStatusCode, JsonRpcError(id, error.Code, error.Message));

            if (!TryValidateProtocolHeader(request, session, out var protocolError))
                return new McpHttpResult(400, JsonRpcError(id, protocolError.Code, protocolError.Message));

            if (!hasId)
                return new McpHttpResult(202, null);

            session.Touch();
            return method switch
            {
                "ping" => new McpHttpResult(200, JsonRpcResult(id, McpData.Obj())),
                "tools/list" => new McpHttpResult(200, JsonRpcResult(id, McpData.Obj(("tools", McpData.Arr(EvalJsCodeTool.ToolDefinition()))))),
                "tools/call" => new McpHttpResult(200, await HandleToolCallAsync(id, message, session, cancellationToken)),
                _ => new McpHttpResult(200, JsonRpcError(id, -32601, $"Method '{method}' was not found."))
            };
        }

        private object HandleInitialize(Dictionary<string, object?> message, Dictionary<string, string> responseHeaders)
        {
            message.TryGetValue("id", out var id);
            var parameters = McpData.AsObject(message.TryGetValue("params", out var p) ? p : null);
            var requestedProtocol = parameters != null ? McpData.GetString(parameters, "protocolVersion") : null;
            var clientInfo = parameters != null ? McpData.AsObject(parameters.TryGetValue("clientInfo", out var c) ? c : null) : null;
            var clientName = clientInfo != null ? McpData.GetString(clientInfo, "name") ?? string.Empty : string.Empty;
            var protocol = NegotiateProtocolVersion(requestedProtocol);
            var session = _sessions.Create(protocol, clientName, _options.MaxSessions);
            responseHeaders["MCP-Session-Id"] = session.Id;

            return JsonRpcResult(id, McpData.Obj(
                ("protocolVersion", protocol),
                ("capabilities", McpData.Obj(
                    ("tools", McpData.Obj())
                )),
                ("serverInfo", McpData.Obj(
                    ("name", ServerName),
                    ("version", "1.0.0")
                ))
            ));
        }

        private async Task<object> HandleToolCallAsync(object? id, Dictionary<string, object?> message, McpSession session, CancellationToken cancellationToken)
        {
            var parameters = McpData.AsObject(message.TryGetValue("params", out var p) ? p : null);
            if (parameters == null)
                return JsonRpcError(id, -32602, "Invalid tools/call params.");

            var toolName = McpData.GetString(parameters, "name") ?? string.Empty;
            if (toolName != "evalJsCode")
                return JsonRpcError(id, -32602, $"Unknown tool '{toolName}'. This server only exposes evalJsCode.");

            var args = McpData.AsObject(parameters.TryGetValue("arguments", out var a) ? a : null) ?? new Dictionary<string, object?>();
            var requestId = Convert.ToString(id) ?? Guid.NewGuid().ToString("N");
            var result = await _evalTool.ExecuteAsync(session, requestId, args, cancellationToken);
            return JsonRpcResult(id, result);
        }

        private bool TryResolveSession(McpTransportRequest request, out McpSession session, out (int HttpStatusCode, int Code, string Message) error)
        {
            var sessionId = request.GetHeader("mcp-session-id") ?? string.Empty;
            if (string.IsNullOrEmpty(sessionId))
            {
                session = null!;
                error = (400, -32000, "MCP-Session-Id header is required.");
                return false;
            }

            if (!_sessions.TryGet(sessionId, out session))
            {
                error = (404, -32001, "Session not found. Reinitialize the MCP client.");
                return false;
            }

            error = default;
            return true;
        }

        private static bool TryValidateProtocolHeader(McpTransportRequest request, McpSession session, out (int Code, string Message) error)
        {
            var protocol = request.GetHeader("mcp-protocol-version");
            if (string.IsNullOrWhiteSpace(protocol))
            {
                error = default;
                return true;
            }

            if (!SupportedProtocolVersions.Contains(protocol!))
            {
                error = (-32002, $"Unsupported MCP-Protocol-Version '{protocol}'.");
                return false;
            }

            if (!string.Equals(protocol, session.ProtocolVersion, StringComparison.Ordinal))
            {
                error = (-32002, $"MCP-Protocol-Version '{protocol}' does not match negotiated session protocol '{session.ProtocolVersion}'.");
                return false;
            }

            error = default;
            return true;
        }

        private object BuildHealthObject()
        {
            _sessions.RemoveIdle(TimeSpan.FromSeconds(_options.SessionIdleTimeoutSeconds));
            return McpData.Obj(
                ("status", State.Status),
                ("server", ServerName),
                ("endpoint", State.Endpoint),
                ("transport", _transport?.Name ?? string.Empty),
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("puerTsBackend", PuerTsBackendFactory.SelectedBackendName),
                ("activeSessions", _sessions.Count),
                ("unity", McpData.Obj(
                    ("version", Application.unityVersion),
                    ("platform", Application.platform.ToString()),
                    ("isEditor", Application.isEditor),
                    ("isPlaying", Application.isPlaying)
                ))
#if UNITY_EDITOR
                , ("editor", EditorStatusProvider.GetStateObject())
#endif
            );
        }

        private static object JsonRpcResult(object? id, object? result) =>
            McpData.Obj(("jsonrpc", "2.0"), ("result", result), ("id", id));

        private static object JsonRpcError(object? id, int code, string message) =>
            McpData.Obj(
                ("jsonrpc", "2.0"),
                ("error", McpData.Obj(("code", code), ("message", message))),
                ("id", id)
            );

        private static string NegotiateProtocolVersion(string? requestedProtocol)
        {
            if (string.IsNullOrWhiteSpace(requestedProtocol)) return ProtocolVersion;
            return SupportedProtocolVersions.Contains(requestedProtocol!) ? requestedProtocol! : ProtocolVersion;
        }

        private static int SelectBatchStatusCode(int currentStatusCode, int nextStatusCode)
        {
            if (nextStatusCode >= 400) return nextStatusCode;
            return currentStatusCode == 202 ? nextStatusCode : currentStatusCode;
        }

        private static int SelectResponseStatusCode(int statusCode) => statusCode == 202 ? 200 : statusCode;

        private static Dictionary<string, string> BuildCorsHeaders()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Access-Control-Allow-Origin"] = "*",
                ["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS, DELETE",
                ["Access-Control-Allow-Headers"] = "Content-Type, MCP-Session-Id, MCP-Protocol-Version, mcp-session-id, mcp-protocol-version",
                ["Access-Control-Expose-Headers"] = "MCP-Session-Id, mcp-session-id"
            };
        }

        private static bool IsOriginAllowed(McpTransportRequest request)
        {
            var origin = request.GetHeader("Origin");
            if (string.IsNullOrWhiteSpace(origin)) return true;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            return uri.IsLoopback;
        }

        private static bool IsPath(string actualPath, string expectedPath)
        {
            var normalized = actualPath.TrimEnd('/');
            if (string.IsNullOrEmpty(normalized)) normalized = "/";
            return string.Equals(normalized, expectedPath, StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct McpHttpResult
        {
            public McpHttpResult(int statusCode, object? body)
            {
                StatusCode = statusCode;
                Body = body;
            }

            public int StatusCode { get; }

            public object? Body { get; }
        }
    }
}
