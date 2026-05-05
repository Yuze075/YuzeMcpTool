#nullable enable
#if !UNITY_WEBGL
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal sealed class TcpCliBridgeTransport : ICliBridgeTransport
    {
        private readonly HashSet<TcpCliBridgeTransportConnection> _connections = new();
        private readonly object _syncRoot = new();
        private Func<ICliBridgeTransportConnection, CancellationToken, Task>? _connectionHandler;
        private TcpListener? _listener;
        private CancellationToken _cancellationToken;

        public string Name => "Tcp";

        public string Endpoint { get; private set; } = string.Empty;

        public bool IsRunning => _listener != null;

        public CliBridgeTransportStartResult Start(
            CliBridgeOptions options,
            Func<ICliBridgeTransportConnection, CancellationToken, Task> connectionHandler,
            CancellationToken cancellationToken)
        {
            if (IsRunning) return CliBridgeTransportStartResult.Started(CliBridgeTransportUtility.NormalizeHost(options.Host), options.Port, Endpoint);

            var host = CliBridgeTransportUtility.NormalizeHost(options.Host);
            _connectionHandler = connectionHandler;
            _cancellationToken = cancellationToken;

            try
            {
                var address = ParseBindAddress(host);
                _listener = new TcpListener(address, Math.Max(0, options.Port));
                _listener.Start();
                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Endpoint = CliBridgeTransportUtility.BuildEndpoint(host, port);
                _ = Task.Run(AcceptLoopAsync, cancellationToken);
                return CliBridgeTransportStartResult.Started(host, port, Endpoint);
            }
            catch (Exception ex)
            {
                Stop();
                return CliBridgeTransportStartResult.Failed(ex.Message);
            }
        }

        public void Stop()
        {
            try
            {
                _listener?.Stop();
            }
            catch
            {
                // Listener shutdown is best effort.
            }
            _listener = null;

            lock (_syncRoot)
            {
                foreach (var connection in _connections)
                    connection.Dispose();
                _connections.Clear();
            }

            _connectionHandler = null;
            Endpoint = string.Empty;
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync()
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener!.AcceptTcpClientAsync();
                }
                catch
                {
                    if (_cancellationToken.IsCancellationRequested) return;
                    continue;
                }

                var connection = new TcpCliBridgeTransportConnection(client);
                lock (_syncRoot)
                    _connections.Add(connection);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_connectionHandler != null)
                            await _connectionHandler(connection, _cancellationToken);
                    }
                    finally
                    {
                        lock (_syncRoot)
                            _connections.Remove(connection);
                        connection.Dispose();
                    }
                }, _cancellationToken);
            }
        }

        private static IPAddress ParseBindAddress(string host)
        {
            if (CliBridgeTransportUtility.IsAnyHost(host)) return host.Contains(":") ? IPAddress.IPv6Any : IPAddress.Any;
            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host[1..^1];
            if (IPAddress.TryParse(host, out var address)) return address;
            throw new InvalidOperationException($"Unsupported CLI bridge host '{host}'. Use an IP address, localhost, or 0.0.0.0.");
        }

        private sealed class TcpCliBridgeTransportConnection : ICliBridgeTransportConnection
        {
            private readonly TcpClient _client;
            private readonly StreamReader _reader;
            private readonly StreamWriter _writer;

            public TcpCliBridgeTransportConnection(TcpClient client)
            {
                _client = client;
                var stream = client.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
                _writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                RemoteEndpoint = Convert.ToString(client.Client.RemoteEndPoint) ?? string.Empty;
            }

            public string RemoteEndpoint { get; }

            public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _reader.ReadLineAsync();
            }

            public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _writer.WriteLineAsync(line);
            }

            public void Dispose()
            {
                try
                {
                    _writer.Dispose();
                    _reader.Dispose();
                    _client.Close();
                }
                catch
                {
                    // Connection shutdown is best effort.
                }
            }
        }
    }
}
#endif
