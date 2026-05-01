#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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
        private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
        {
            ProtocolVersion,
            FallbackProtocolVersion,
        };
        private static readonly Lazy<McpServer> LazyShared = new(() => new McpServer());

        private readonly object _syncRoot = new();
        private McpSessionRegistry _sessions = new();
        private McpServerOptions _options = new();
        private McpServerState _state = new();
        private HttpListener? _listener;
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
#if UNITY_WEBGL
            Debug.LogWarning("[YuzeMcpTool] HttpListener MCP server is not available on WebGL.");
            return;
#else
            lock (_syncRoot)
            {
                if (_listener != null && _listener.IsListening) return;

                _options = options ?? new McpServerOptions();
                _evalTool = new EvalJsCodeTool(_options);
                _sessions.Dispose();
                _sessions = new McpSessionRegistry();
                _cancellation = new CancellationTokenSource();
                _listener = new HttpListener();

                try
                {
                    foreach (var prefix in BuildListenerPrefixes(_options))
                        _listener.Prefixes.Add(prefix);

                    _listener.Start();
                    _state = new McpServerState
                    {
                        IsRunning = true,
                        Endpoint = $"http://{FormatHttpHost(string.IsNullOrWhiteSpace(_options.Host) ? "127.0.0.1" : _options.Host)}:{_options.Port}/mcp",
                        StartedAtUtc = DateTime.UtcNow,
                        LastError = string.Empty,
                    };

                    MainThreadDispatcher.Initialize();
                    UnityLogBuffer.Start();
                    _ = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
                    Debug.Log($"[YuzeMcpTool] MCP server started: {_state.Endpoint}");
                }
                catch (Exception ex)
                {
                    _state.LastError = ex.Message;
                    _state.IsRunning = false;
                    _listener.Close();
                    _listener = null;
                    Debug.LogError($"[YuzeMcpTool] Failed to start MCP server: {ex.Message}");
                }
            }
#endif
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                _cancellation?.Cancel();
                _cancellation?.Dispose();
                _cancellation = null;

                if (_listener != null)
                {
                    try
                    {
                        _listener.Stop();
                        _listener.Close();
                    }
                    catch (Exception)
                    {
                        // Listener shutdown is best effort.
                    }
                    _listener = null;
                }

                _sessions.Dispose();
                _sessions = new McpSessionRegistry();
                _state.IsRunning = false;
                _state.ActiveSessionCount = 0;
                _state.Sessions = Array.Empty<McpSessionSnapshot>();
                _state.UptimeSeconds = 0;
            }
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    if (_listener == null) return;
                    context = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    continue;
                }

                _ = Task.Run(() => ProcessContextAsync(context, cancellationToken), cancellationToken);
            }
        }

        private async Task ProcessContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;
            var response = context.Response;
            SetCorsHeaders(response);

            try
            {
                if (!IsOriginAllowed(request))
                {
                    await WriteJsonAsync(response, 403, JsonRpcError(null, -32000, "Forbidden origin."));
                    return;
                }

                if (request.HttpMethod == "OPTIONS")
                {
                    WriteNoContent(response, 204);
                    return;
                }

                if (request.Url == null)
                {
                    await WriteJsonAsync(response, 400, LitJson.Obj(("error", "Bad Request")));
                    return;
                }

                if (IsPath(request.Url.AbsolutePath, "/health"))
                {
                    var health = await MainThreadDispatcher.RunAsync(BuildHealthObject);
                    await WriteJsonAsync(response, 200, health);
                    return;
                }

                if (!IsPath(request.Url.AbsolutePath, "/mcp"))
                {
                    await WriteJsonAsync(response, 404, LitJson.Obj(("error", "Not Found")));
                    return;
                }

                if (request.HttpMethod == "DELETE")
                {
                    var sessionId = request.Headers["mcp-session-id"] ?? string.Empty;
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        await WriteJsonAsync(response, 400, JsonRpcError(null, -32000, "Mcp-Session-Id header is required."));
                        return;
                    }

                    if (!_sessions.Remove(sessionId))
                    {
                        await WriteJsonAsync(response, 404, JsonRpcError(null, -32001, "Session not found."));
                        return;
                    }

                    WriteNoContent(response, 200);
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    response.Headers["Allow"] = "POST, GET, OPTIONS, DELETE";
                    await WriteJsonAsync(response, 405, LitJson.Obj(("error", "Method Not Allowed")));
                    return;
                }

                var body = await ReadBodyAsync(request, _options.MaxRequestBodyBytes);
                var parsed = LitJson.Parse(body);
                var result = await HandleJsonRpcAsync(parsed, request, response, cancellationToken);

                if (result.Body == null)
                {
                    WriteNoContent(response, result.StatusCode);
                    return;
                }

                await WriteJsonAsync(response, result.StatusCode, result.Body);
            }
            catch (LitJson.JsonException ex)
            {
                await WriteJsonAsync(response, 400, JsonRpcError(null, -32700, "Parse error: " + ex.Message));
            }
            catch (FormatException ex)
            {
                await WriteJsonAsync(response, 400, JsonRpcError(null, -32700, "Parse error: " + ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                await WriteJsonAsync(response, 413, JsonRpcError(null, -32000, ex.Message));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                await WriteJsonAsync(response, 500, JsonRpcError(null, -32603, "Internal error: " + ex.Message));
            }
        }

        private async Task<McpHttpResult> HandleJsonRpcAsync(object? parsed, HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
        {
            if (parsed is List<object?> batch)
            {
                var statusCode = 202;
                var responses = new List<object?>();
                foreach (var item in batch)
                {
                    var result = await HandleSingleJsonRpcAsync(item, request, response, cancellationToken);
                    statusCode = SelectBatchStatusCode(statusCode, result.StatusCode);
                    if (result.Body != null) responses.Add(result.Body);
                }
                return new McpHttpResult(responses.Count == 0 ? statusCode : SelectResponseStatusCode(statusCode), responses.Count == 0 ? null : responses);
            }

            return await HandleSingleJsonRpcAsync(parsed, request, response, cancellationToken);
        }

        private async Task<McpHttpResult> HandleSingleJsonRpcAsync(object? parsed, HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
        {
            var message = LitJson.AsObject(parsed);
            if (message == null)
                return new McpHttpResult(400, JsonRpcError(null, -32600, "Invalid Request"));

            message.TryGetValue("id", out var id);
            var hasId = message.ContainsKey("id");
            var method = LitJson.GetString(message, "method") ?? string.Empty;

            if (string.IsNullOrEmpty(method))
                return new McpHttpResult(400, JsonRpcError(id, -32600, "Invalid Request: missing method."));

            if (method == "initialize")
                return new McpHttpResult(200, HandleInitialize(message, response));

            if (!TryResolveSession(request, out var session, out var error))
                return new McpHttpResult(error.HttpStatusCode, JsonRpcError(id, error.Code, error.Message));

            if (!TryValidateProtocolHeader(request, session, out var protocolError))
                return new McpHttpResult(400, JsonRpcError(id, protocolError.Code, protocolError.Message));

            if (!hasId)
                return new McpHttpResult(202, null);

            session.Touch();
            return method switch
            {
                "ping" => new McpHttpResult(200, JsonRpcResult(id, LitJson.Obj())),
                "tools/list" => new McpHttpResult(200, JsonRpcResult(id, LitJson.Obj(("tools", LitJson.Arr(EvalJsCodeTool.ToolDefinition()))))),
                "tools/call" => new McpHttpResult(200, await HandleToolCallAsync(id, message, session, cancellationToken)),
                _ => new McpHttpResult(200, JsonRpcError(id, -32601, $"Method '{method}' was not found."))
            };
        }

        private object HandleInitialize(Dictionary<string, object?> message, HttpListenerResponse response)
        {
            message.TryGetValue("id", out var id);
            var parameters = LitJson.AsObject(message.TryGetValue("params", out var p) ? p : null);
            var requestedProtocol = parameters != null ? LitJson.GetString(parameters, "protocolVersion") : null;
            var clientInfo = parameters != null ? LitJson.AsObject(parameters.TryGetValue("clientInfo", out var c) ? c : null) : null;
            var clientName = clientInfo != null ? LitJson.GetString(clientInfo, "name") ?? string.Empty : string.Empty;
            var protocol = NegotiateProtocolVersion(requestedProtocol);
            var session = _sessions.Create(protocol, clientName, _options.MaxSessions);
            response.Headers["MCP-Session-Id"] = session.Id;

            return JsonRpcResult(id, LitJson.Obj(
                ("protocolVersion", protocol),
                ("capabilities", LitJson.Obj(
                    ("tools", LitJson.Obj())
                )),
                ("serverInfo", LitJson.Obj(
                    ("name", ServerName),
                    ("version", "1.0.0")
                ))
            ));
        }

        private async Task<object> HandleToolCallAsync(object? id, Dictionary<string, object?> message, McpSession session, CancellationToken cancellationToken)
        {
            var parameters = LitJson.AsObject(message.TryGetValue("params", out var p) ? p : null);
            if (parameters == null)
                return JsonRpcError(id, -32602, "Invalid tools/call params.");

            var toolName = LitJson.GetString(parameters, "name") ?? string.Empty;
            if (toolName != "evalJsCode")
                return JsonRpcError(id, -32602, $"Unknown tool '{toolName}'. This server only exposes evalJsCode.");

            var args = LitJson.AsObject(parameters.TryGetValue("arguments", out var a) ? a : null) ?? new Dictionary<string, object?>();
            var requestId = Convert.ToString(id) ?? Guid.NewGuid().ToString("N");
            var result = await _evalTool.ExecuteAsync(session, requestId, args, cancellationToken);
            return JsonRpcResult(id, result);
        }

        private bool TryResolveSession(HttpListenerRequest request, out McpSession session, out (int HttpStatusCode, int Code, string Message) error)
        {
            var sessionId = request.Headers["mcp-session-id"] ?? string.Empty;
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

        private static bool TryValidateProtocolHeader(HttpListenerRequest request, McpSession session, out (int Code, string Message) error)
        {
            var protocol = request.Headers["mcp-protocol-version"];
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
            return LitJson.Obj(
                ("status", State.Status),
                ("server", ServerName),
                ("endpoint", State.Endpoint),
                ("environment", CommandUtilities.GetEnvironmentObject()),
                ("puerTsBackend", PuerTsBackendFactory.SelectedBackendName),
                ("activeSessions", _sessions.Count),
                ("unity", LitJson.Obj(
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
            LitJson.Obj(("jsonrpc", "2.0"), ("result", result), ("id", id));

        private static object JsonRpcError(object? id, int code, string message) =>
            LitJson.Obj(
                ("jsonrpc", "2.0"),
                ("error", LitJson.Obj(("code", code), ("message", message))),
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

        private static async Task<string> ReadBodyAsync(HttpListenerRequest request, int maxBytes)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
            var buffer = new char[4096];
            var builder = new StringBuilder();
            while (true)
            {
                var read = await reader.ReadAsync(buffer, 0, buffer.Length);
                if (read <= 0) break;
                builder.Append(buffer, 0, read);
                if (builder.Length > maxBytes)
                    throw new InvalidOperationException($"MCP request body exceeds {maxBytes} bytes.");
            }
            return builder.ToString();
        }

        private static void SetCorsHeaders(HttpListenerResponse response)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS, DELETE";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, MCP-Session-Id, MCP-Protocol-Version, mcp-session-id, mcp-protocol-version";
            response.Headers["Access-Control-Expose-Headers"] = "MCP-Session-Id, mcp-session-id";
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object body)
        {
            var text = LitJson.Stringify(body);
            var bytes = Encoding.UTF8.GetBytes(text);
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private static void WriteNoContent(HttpListenerResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = 0;
            response.OutputStream.Close();
        }

        private static List<string> BuildListenerPrefixes(McpServerOptions options)
        {
            var hosts = new List<string>();
            AddUniqueHost(hosts, string.IsNullOrWhiteSpace(options.Host) ? "127.0.0.1" : options.Host);

            if (options.BindLocalhostAliases && IsLoopbackHost(options.Host))
            {
                AddUniqueHost(hosts, "127.0.0.1");
                AddUniqueHost(hosts, "localhost");
            }

            var prefixes = new List<string>(hosts.Count);
            foreach (var host in hosts)
                prefixes.Add($"http://{FormatHttpHost(host)}:{options.Port}/");
            return prefixes;
        }

        private static void AddUniqueHost(List<string> hosts, string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return;
            foreach (var existing in hosts)
            {
                if (string.Equals(existing, host, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            hosts.Add(host);
        }

        private static string FormatHttpHost(string host)
        {
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                return host;
            return host.Contains(":") ? "[" + host + "]" : host;
        }

        private static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            var normalized = host.Trim();
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
                normalized = normalized[1..^1];
            return string.Equals(normalized, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOriginAllowed(HttpListenerRequest request)
        {
            var origin = request.Headers["Origin"];
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
