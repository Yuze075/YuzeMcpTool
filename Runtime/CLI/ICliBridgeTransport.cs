#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal interface ICliBridgeTransport : IDisposable
    {
        string Name { get; }

        string Endpoint { get; }

        bool IsRunning { get; }

        CliBridgeTransportStartResult Start(
            CliBridgeOptions options,
            Func<ICliBridgeTransportConnection, CancellationToken, Task> connectionHandler,
            CancellationToken cancellationToken);

        void Stop();
    }

    internal interface ICliBridgeTransportConnection : IDisposable
    {
        string RemoteEndpoint { get; }

        Task<string?> ReadLineAsync(CancellationToken cancellationToken);

        Task WriteLineAsync(string line, CancellationToken cancellationToken);
    }

    internal readonly struct CliBridgeTransportStartResult
    {
        private CliBridgeTransportStartResult(bool success, string host, int port, string endpoint, string error)
        {
            Success = success;
            Host = host;
            Port = port;
            Endpoint = endpoint;
            Error = error;
        }

        public bool Success { get; }

        public string Host { get; }

        public int Port { get; }

        public string Endpoint { get; }

        public string Error { get; }

        public static CliBridgeTransportStartResult Started(string host, int port, string endpoint) =>
            new(true, host, port, endpoint, string.Empty);

        public static CliBridgeTransportStartResult Failed(string error) =>
            new(false, string.Empty, 0, string.Empty, error);
    }

    internal static class CliBridgeTransportFactory
    {
        public static ICliBridgeTransport Create()
        {
#if UNITY_WEBGL
            return new WebSocketCliBridgeTransport();
#else
            return new TcpCliBridgeTransport();
#endif
        }
    }

    internal static class CliBridgeTransportUtility
    {
        public static string NormalizeHost(string? host) => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

        public static string BuildEndpoint(string host, int port) => $"{FormatHost(host)}:{port}";

        public static string FormatHost(string host)
        {
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                return host;
            return host.Contains(":") ? "[" + host + "]" : host;
        }

        public static bool IsAnyHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            var normalized = host.Trim();
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
                normalized = normalized[1..^1];
            return string.Equals(normalized, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "::", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "*", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "+", StringComparison.OrdinalIgnoreCase);
        }
    }
}
