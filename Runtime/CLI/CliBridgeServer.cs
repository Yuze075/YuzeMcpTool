#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    public sealed class CliBridgeOptions
    {
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 0;

        public int MaxRequestBytes { get; set; } = 1024 * 1024;

        public int DefaultEvalTimeoutSeconds { get; set; } = 30;

        public bool RequireToken { get; set; } = true;

        public string Token { get; set; } = string.Empty;
    }

    public sealed class CliBridgeState
    {
        public bool IsRunning { get; internal set; }

        public string Host { get; internal set; } = string.Empty;

        public int Port { get; internal set; }

        public string Endpoint { get; internal set; } = string.Empty;

        public string Transport { get; internal set; } = string.Empty;

        public string LastError { get; internal set; } = string.Empty;

        public int ActiveSessionCount { get; internal set; }

        public DateTime StartedAtUtc { get; internal set; }

        public string Token { get; internal set; } = string.Empty;

        public bool RequireToken { get; internal set; }

        public IReadOnlyList<EvalSessionSnapshot> Sessions { get; internal set; } = Array.Empty<EvalSessionSnapshot>();

        public string Status => IsRunning ? "running" : "stopped";
    }

    public sealed class CliBridgeServer : IDisposable
    {
        private const string ProtocolVersion = "1.0";
        private static readonly Lazy<CliBridgeServer> LazyShared = new(() => new CliBridgeServer());
        private readonly object _syncRoot = new();
        private readonly HashSet<CliBridgeConnection> _connections = new();
        private CliBridgeOptions _options = new();
        private CliBridgeState _state = new();
        private ICliBridgeTransport? _transport;
        private CancellationTokenSource? _cancellation;

        private CliBridgeServer()
        {
        }

        public static CliBridgeServer Shared => LazyShared.Value;

        public CliBridgeState State
        {
            get
            {
                lock (_syncRoot)
                {
                    _state.ActiveSessionCount = _connections.Count;
                    _state.Sessions = _connections
                        .Select(connection => connection.ToSnapshot())
                        .ToList();
                    return _state;
                }
            }
        }

        public void Start(CliBridgeOptions? options = null)
        {
            lock (_syncRoot)
            {
                if (_transport is { IsRunning: true }) return;

                _options = options ?? new CliBridgeOptions();
                if (_options.RequireToken && string.IsNullOrWhiteSpace(_options.Token))
                    _options.Token = Guid.NewGuid().ToString("N");

                _cancellation = new CancellationTokenSource();
                _transport = CliBridgeTransportFactory.Create();

                try
                {
                    var result = _transport.Start(_options, RunConnectionAsync, _cancellation.Token);
                    if (!result.Success)
                    {
                        _state.LastError = result.Error;
                        _state.IsRunning = false;
                        _state.Endpoint = string.Empty;
                        _state.Port = 0;
                        Debug.LogWarning($"[UnityEvalTool] CLI bridge transport '{_transport.Name}' did not start: {result.Error}");
                        _transport.Stop();
                        _transport = null;
                        _cancellation.Dispose();
                        _cancellation = null;
                        return;
                    }

                    MainThreadDispatcher.Initialize();
                    UnityLogBuffer.Start();

                    _state = new CliBridgeState
                    {
                        IsRunning = true,
                        Host = result.Host,
                        Port = result.Port,
                        Endpoint = result.Endpoint,
                        Transport = _transport.Name,
                        LastError = string.Empty,
                        StartedAtUtc = DateTime.UtcNow,
                        Token = _options.Token,
                        RequireToken = _options.RequireToken
                    };
                    Debug.Log($"[UnityEvalTool] CLI bridge started via {_transport.Name}: {_state.Endpoint}");
                }
                catch (Exception ex)
                {
                    _state.LastError = ex.Message;
                    _state.IsRunning = false;
                    _state.Endpoint = string.Empty;
                    _state.Port = 0;
                    _transport?.Stop();
                    _transport = null;
                    _cancellation?.Dispose();
                    _cancellation = null;
                    Debug.LogError($"[UnityEvalTool] Failed to start CLI bridge: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
                StopCore();
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
                catch
                {
                    // Transport shutdown is best effort.
                }
                _transport = null;
            }

            foreach (var connection in _connections.ToArray())
                connection.Dispose();
            _connections.Clear();

            _state.IsRunning = false;
            _state.Endpoint = string.Empty;
            _state.Transport = string.Empty;
            _state.Port = 0;
            _state.ActiveSessionCount = 0;
            _state.Sessions = Array.Empty<EvalSessionSnapshot>();
        }

        private async Task RunConnectionAsync(ICliBridgeTransportConnection transportConnection, CancellationToken cancellationToken)
        {
            var connection = new CliBridgeConnection(this, transportConnection, _options);
            lock (_syncRoot)
                _connections.Add(connection);

            await connection.RunAsync(cancellationToken);
        }

        private void Remove(CliBridgeConnection connection)
        {
            lock (_syncRoot)
                _connections.Remove(connection);
        }

        private sealed class CliBridgeConnection : IDisposable
        {
            private readonly CliBridgeServer _owner;
            private readonly ICliBridgeTransportConnection _connection;
            private readonly CliBridgeOptions _options;
            private readonly EvalSession _session = new(Guid.NewGuid().ToString("N"), "cli", "unity");
            private readonly EvalExecutor _evalTool;
            private bool _authorized;
            private bool _closeRequested;

            public CliBridgeConnection(CliBridgeServer owner, ICliBridgeTransportConnection connection, CliBridgeOptions options)
            {
                _owner = owner;
                _connection = connection;
                _options = options;
                _evalTool = new EvalExecutor(new EvalOptions { DefaultEvalTimeoutSeconds = options.DefaultEvalTimeoutSeconds, MaxRequestBodyBytes = options.MaxRequestBytes });
            }

            public async Task RunAsync(CancellationToken cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var line = await _connection.ReadLineAsync(cancellationToken);
                        if (line == null) return;
                        if (Encoding.UTF8.GetByteCount(line) > _options.MaxRequestBytes)
                        {
                            await WriteErrorAsync(null, "RequestTooLarge", "CLI bridge request is too large.", cancellationToken);
                            continue;
                        }

                        var response = await HandleLineAsync(line, cancellationToken);
                        await _connection.WriteLineAsync(LitJson.Stringify(response), cancellationToken);
                        if (_closeRequested) return;
                    }
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        Debug.LogWarning($"[UnityEvalTool] CLI bridge connection ended: {ex.Message}");
                }
                finally
                {
                    Dispose();
                    _owner.Remove(this);
                }
            }

            public void Dispose()
            {
                _session.Dispose();
                try
                {
                    _connection.Dispose();
                }
                catch
                {
                    // Connection shutdown is best effort.
                }
            }

            public EvalSessionSnapshot ToSnapshot() => _session.ToSnapshot();

            private async Task<Dictionary<string, object?>> HandleLineAsync(string line, CancellationToken cancellationToken)
            {
                object? id = null;
                try
                {
                    var request = EvalData.AsObject(LitJson.Parse(line));
                    if (request == null) return ResponseError(null, "InvalidRequest", "Request must be a JSON object.");
                    request.TryGetValue("id", out id);
                    var method = EvalData.GetString(request, "method") ?? string.Empty;
                    var parameters = EvalData.AsObject(request.TryGetValue("params", out var rawParams) ? rawParams : null) ?? EvalData.Obj();

                    if (!_authorized && method != "hello")
                        return ResponseError(id, "Unauthorized", "CLI bridge hello with a valid token is required.");

                    switch (method)
                    {
                        case "hello":
                            return HandleHello(id, parameters);
                        case "ping":
                            return ResponseOk(id, EvalData.Obj(("timeUtc", DateTime.UtcNow.ToString("O"))));
                        case "tools/catalog":
                            return ResponseOk(id, await MainThreadDispatcher.RunAsync(() =>
                                EvalToolCatalog.GetCliCatalog(EvalData.GetBool(parameters, "refresh", false))));
                        case "tools/refresh":
                            return ResponseOk(id, await MainThreadDispatcher.RunAsync(() => EvalToolCatalog.GetCliCatalog(true)));
                        case "evalJsCode":
                            return ResponseOk(id, await HandleEvalAsync(id, parameters, cancellationToken));
                        case "close":
                            _closeRequested = true;
                            return ResponseOk(id, EvalData.Obj(("closed", true)));
                        default:
                            return ResponseError(id, "MethodNotFound", $"CLI bridge method '{method}' was not found.");
                    }
                }
                catch (Exception ex)
                {
                    return ResponseError(id, "InternalError", ex.Message);
                }
            }

            private Dictionary<string, object?> HandleHello(object? id, Dictionary<string, object?> parameters)
            {
                var token = EvalData.GetString(parameters, "token") ?? string.Empty;
                if (_options.RequireToken && !string.Equals(token, _options.Token, StringComparison.Ordinal))
                    return ResponseError(id, "Unauthorized", "Invalid CLI bridge token.");

                _authorized = true;
                return ResponseOk(id, EvalData.Obj(
                    ("protocolVersion", ProtocolVersion),
                    ("sessionId", _session.Id),
                    ("requireToken", _options.RequireToken),
                    ("transport", _owner.State.Transport),
                    ("unityVersion", Application.unityVersion),
                    ("isEditor", Application.isEditor),
                    ("platform", Application.platform.ToString())
                ));
            }

            private async Task<Dictionary<string, object?>> HandleEvalAsync(object? id, Dictionary<string, object?> parameters, CancellationToken cancellationToken)
            {
                var args = EvalData.Obj(
                    ("code", EvalData.GetString(parameters, "code") ?? string.Empty),
                    ("timeout", EvalData.GetInt(parameters, "timeout", _options.DefaultEvalTimeoutSeconds)),
                    ("resetSession", EvalData.GetBool(parameters, "resetSession", false))
                );
                return await _evalTool.ExecuteAsync(_session, Convert.ToString(id) ?? Guid.NewGuid().ToString("N"), args, cancellationToken);
            }

            private Task WriteErrorAsync(object? id, string code, string message, CancellationToken cancellationToken) =>
                _connection.WriteLineAsync(LitJson.Stringify(ResponseError(id, code, message)), cancellationToken);

            private static Dictionary<string, object?> ResponseOk(object? id, object? result) =>
                EvalData.Obj(("id", id), ("ok", true), ("result", result));

            private static Dictionary<string, object?> ResponseError(object? id, string code, string message) =>
                EvalData.Obj(("id", id), ("ok", false), ("error", EvalData.Obj(("code", code), ("message", message))));
        }
    }
}
