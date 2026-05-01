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
        private const string ServerName = "YuzeMcpTool";
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
                var prefix = $"http://{_options.Host}:{_options.Port}/";
                _listener.Prefixes.Add(prefix);

                try
                {
                    _listener.Start();
                    _state = new McpServerState
                    {
                        IsRunning = true,
                        Endpoint = $"http://{_options.Host}:{_options.Port}/mcp",
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

                if (request.Url.AbsolutePath == "/health")
                {
                    var health = await MainThreadDispatcher.RunAsync(BuildHealthObject);
                    await WriteJsonAsync(response, 200, health);
                    return;
                }

                if (request.Url.AbsolutePath != "/mcp")
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

                    _sessions.Remove(sessionId);
                    WriteNoContent(response, 200);
                    return;
                }

                if (request.HttpMethod != "POST")
                {
                    await WriteJsonAsync(response, 405, LitJson.Obj(("error", "Method Not Allowed")));
                    return;
                }

                var body = await ReadBodyAsync(request, _options.MaxRequestBodyBytes);
                var parsed = LitJson.Parse(body);
                var result = await HandleJsonRpcAsync(parsed, request, response, cancellationToken);

                if (result == null)
                {
                    WriteNoContent(response, 202);
                    return;
                }

                await WriteJsonAsync(response, 200, result);
            }
            catch (FormatException ex)
            {
                await WriteJsonAsync(response, 400, JsonRpcError(null, -32700, "Parse error: " + ex.Message));
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                await WriteJsonAsync(response, 500, JsonRpcError(null, -32603, "Internal error: " + ex.Message));
            }
        }

        private async Task<object?> HandleJsonRpcAsync(object? parsed, HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
        {
            if (parsed is List<object?> batch)
            {
                var responses = new List<object?>();
                foreach (var item in batch)
                {
                    var result = await HandleSingleJsonRpcAsync(item, request, response, cancellationToken);
                    if (result != null) responses.Add(result);
                }
                return responses.Count == 0 ? null : responses;
            }

            return await HandleSingleJsonRpcAsync(parsed, request, response, cancellationToken);
        }

        private async Task<object?> HandleSingleJsonRpcAsync(object? parsed, HttpListenerRequest request, HttpListenerResponse response, CancellationToken cancellationToken)
        {
            var message = LitJson.AsObject(parsed);
            if (message == null)
                return JsonRpcError(null, -32600, "Invalid Request");

            message.TryGetValue("id", out var id);
            var hasId = message.ContainsKey("id");
            var method = LitJson.GetString(message, "method") ?? string.Empty;

            if (string.IsNullOrEmpty(method))
                return JsonRpcError(id, -32600, "Invalid Request: missing method.");

            if (method == "initialize")
                return HandleInitialize(message, response);

            if (!TryResolveSession(request, out var session, out var error))
                return JsonRpcError(id, error.Code, error.Message);

            if (!hasId)
                return null;

            session.Touch();
            return method switch
            {
                "ping" => JsonRpcResult(id, LitJson.Obj()),
                "tools/list" => JsonRpcResult(id, LitJson.Obj(("tools", LitJson.Arr(EvalJsCodeTool.ToolDefinition())))),
                "tools/call" => await HandleToolCallAsync(id, message, session, cancellationToken),
                _ => JsonRpcError(id, -32601, $"Method '{method}' was not found.")
            };
        }

        private object HandleInitialize(Dictionary<string, object?> message, HttpListenerResponse response)
        {
            message.TryGetValue("id", out var id);
            var parameters = LitJson.AsObject(message.TryGetValue("params", out var p) ? p : null);
            var requestedProtocol = parameters != null ? LitJson.GetString(parameters, "protocolVersion") : null;
            var clientInfo = parameters != null ? LitJson.AsObject(parameters.TryGetValue("clientInfo", out var c) ? c : null) : null;
            var clientName = clientInfo != null ? LitJson.GetString(clientInfo, "name") ?? string.Empty : string.Empty;
            var protocol = string.IsNullOrWhiteSpace(requestedProtocol) ? ProtocolVersion : requestedProtocol!;
            var session = _sessions.Create(protocol, clientName, _options.MaxSessions);
            response.Headers["Mcp-Session-Id"] = session.Id;

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

        private bool TryResolveSession(HttpListenerRequest request, out McpSession session, out (int Code, string Message) error)
        {
            var sessionId = request.Headers["mcp-session-id"] ?? string.Empty;
            if (string.IsNullOrEmpty(sessionId))
            {
                session = null!;
                error = (-32000, "Mcp-Session-Id header is required.");
                return false;
            }

            if (!_sessions.TryGet(sessionId, out session))
            {
                error = (-32001, "Session not found. Reinitialize the MCP client.");
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
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, mcp-session-id, mcp-protocol-version";
            response.Headers["Access-Control-Expose-Headers"] = "mcp-session-id";
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
            response.OutputStream.Close();
        }
    }
}
