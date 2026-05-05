#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal interface IMcpTransport : IDisposable
    {
        string Name { get; }

        string Endpoint { get; }

        bool IsRunning { get; }

        McpTransportStartResult Start(
            McpServerOptions options,
            Func<McpTransportRequest, CancellationToken, Task<McpTransportResponse>> requestHandler,
            CancellationToken cancellationToken);

        void Stop();
    }

    internal sealed class McpTransportRequest
    {
        private readonly Dictionary<string, string> _headers;

        public McpTransportRequest(string method, string path, IReadOnlyDictionary<string, string> headers, string body)
        {
            Method = method;
            Path = string.IsNullOrWhiteSpace(path) ? "/" : path;
            Body = body;
            _headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
        }

        public string Method { get; }

        public string Path { get; }

        public string Body { get; }

        public IReadOnlyDictionary<string, string> Headers => _headers;

        public string? GetHeader(string name) => _headers.TryGetValue(name, out var value) ? value : null;
    }

    internal sealed class McpTransportResponse
    {
        public McpTransportResponse(int statusCode, IReadOnlyDictionary<string, string> headers, byte[] bodyBytes)
        {
            StatusCode = statusCode;
            Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            BodyBytes = bodyBytes;
        }

        public int StatusCode { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] BodyBytes { get; }

        public static McpTransportResponse Json(int statusCode, object body, IReadOnlyDictionary<string, string> headers)
        {
            var text = LitJson.Stringify(body);
            var merged = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json; charset=utf-8"
            };
            return new McpTransportResponse(statusCode, merged, Encoding.UTF8.GetBytes(text));
        }

        public static McpTransportResponse NoContent(int statusCode, IReadOnlyDictionary<string, string> headers)
        {
            var merged = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = "application/json; charset=utf-8"
            };
            return new McpTransportResponse(statusCode, merged, Array.Empty<byte>());
        }
    }

    internal readonly struct McpTransportStartResult
    {
        private McpTransportStartResult(bool success, string endpoint, string error)
        {
            Success = success;
            Endpoint = endpoint;
            Error = error;
        }

        public bool Success { get; }

        public string Endpoint { get; }

        public string Error { get; }

        public static McpTransportStartResult Started(string endpoint) => new(true, endpoint, string.Empty);

        public static McpTransportStartResult Failed(string error) => new(false, string.Empty, error);
    }

    internal static class McpTransportFactory
    {
        public static IMcpTransport Create()
        {
#if UNITY_WEBGL
            return new WebSocketMcpTransport();
#else
            return new TcpMcpTransport();
#endif
        }
    }

    internal static class McpTransportUtility
    {
        public static string NormalizeHost(string? host) => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

        public static string BuildEndpoint(string host, int port) => $"http://{FormatHttpHost(host)}:{port}/mcp";

        public static string FormatHttpHost(string host)
        {
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                return host;
            return host.Contains(":") ? "[" + host + "]" : host;
        }

        public static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            var normalized = host.Trim();
            if (normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal))
                normalized = normalized[1..^1];
            return string.Equals(normalized, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalized, "::1", StringComparison.OrdinalIgnoreCase);
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
