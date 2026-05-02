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
using UnityEngine;

namespace YuzeToolkit
{
    internal sealed class TcpMcpTransport : IMcpTransport
    {
        private const int MaxHeaderBytes = 32 * 1024;
        private readonly List<TcpListener> _listeners = new();
        private Func<McpTransportRequest, CancellationToken, Task<McpTransportResponse>>? _requestHandler;
        private CancellationToken _cancellationToken;
        private int _maxRequestBodyBytes;

        public string Name => "Tcp";

        public string Endpoint { get; private set; } = string.Empty;

        public bool IsRunning => _listeners.Count > 0;

        public McpTransportStartResult Start(
            McpServerOptions options,
            Func<McpTransportRequest, CancellationToken, Task<McpTransportResponse>> requestHandler,
            CancellationToken cancellationToken)
        {
            if (IsRunning) return McpTransportStartResult.Started(Endpoint);

            _requestHandler = requestHandler;
            _cancellationToken = cancellationToken;
            _maxRequestBodyBytes = options.MaxRequestBodyBytes;

            try
            {
                var errors = new List<string>();
                foreach (var address in BuildBindAddresses(options))
                {
                    try
                    {
                        var listener = new TcpListener(address, options.Port);
                        listener.Start();
                        _listeners.Add(listener);
                        _ = Task.Run(() => AcceptLoopAsync(listener), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{address}: {ex.Message}");
                    }
                }

                if (_listeners.Count == 0)
                {
                    Stop();
                    return McpTransportStartResult.Failed(errors.Count == 0
                        ? "No TCP bind addresses were available."
                        : string.Join("; ", errors));
                }

                if (errors.Count > 0)
                    Debug.LogWarning($"[YuzeMcpTool] Some MCP TCP bind addresses failed: {string.Join("; ", errors)}");

                Endpoint = McpTransportUtility.BuildEndpoint(McpTransportUtility.NormalizeHost(options.Host), options.Port);
                return McpTransportStartResult.Started(Endpoint);
            }
            catch (Exception ex)
            {
                Stop();
                return McpTransportStartResult.Failed(ex.Message);
            }
        }

        public void Stop()
        {
            foreach (var listener in _listeners)
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                    // Listener shutdown is best effort.
                }
            }

            _listeners.Clear();
            _requestHandler = null;
            Endpoint = string.Empty;
        }

        public void Dispose() => Stop();

        private async Task AcceptLoopAsync(TcpListener listener)
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    if (_cancellationToken.IsCancellationRequested) return;
                    continue;
                }

                _ = Task.Run(() => ProcessClientAsync(client), _cancellationToken);
            }
        }

        private async Task ProcessClientAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var parsed = await ReadHttpRequestAsync(stream);
                    if (parsed.ErrorResponse != null)
                    {
                        await WriteResponseAsync(stream, parsed.ErrorResponse);
                        return;
                    }

                    if (_requestHandler == null || parsed.Request == null)
                    {
                        await WriteResponseAsync(stream, McpTransportResponse.NoContent(503, EmptyHeaders()));
                        return;
                    }

                    var response = await _requestHandler(parsed.Request, _cancellationToken);
                    await WriteResponseAsync(stream, response);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    try
                    {
                        var stream = client.GetStream();
                        await WriteResponseAsync(stream, JsonError(500, "Transport error: " + ex.Message));
                    }
                    catch (Exception)
                    {
                        // The connection may already be closed.
                    }
                }
            }
        }

        private async Task<ParsedHttpRequest> ReadHttpRequestAsync(NetworkStream stream)
        {
            // Minimal HTTP/1.1 parsing for MCP requests only; this is not intended as a general web server.
            var buffer = new List<byte>(1024);
            var temp = new byte[1024];
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(temp, 0, temp.Length);
                if (read <= 0)
                    return ParsedHttpRequest.WithError(JsonError(400, "Bad Request"));

                buffer.AddRange(Slice(temp, read));
                if (buffer.Count > MaxHeaderBytes)
                    return ParsedHttpRequest.WithError(JsonError(431, "Request headers are too large."));

                headerEnd = FindHeaderEnd(buffer);
            }

            var headerBytes = buffer.GetRange(0, headerEnd).ToArray();
            var headerText = Encoding.UTF8.GetString(headerBytes);
            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
                return ParsedHttpRequest.WithError(JsonError(400, "Bad Request"));

            var requestLine = lines[0].Split(' ');
            if (requestLine.Length < 2)
                return ParsedHttpRequest.WithError(JsonError(400, "Bad Request"));

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                headers[name] = value;
            }

            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var contentLengthText)
                && (!int.TryParse(contentLengthText, out contentLength) || contentLength < 0))
            {
                return ParsedHttpRequest.WithError(JsonError(400, "Invalid Content-Length."));
            }

            if (contentLength > _maxRequestBodyBytes)
                return ParsedHttpRequest.WithError(JsonError(413, $"MCP request body exceeds {_maxRequestBodyBytes} bytes."));

            var bodyStart = headerEnd + 4;
            var bodyBytes = new byte[contentLength];
            var bufferedBodyLength = Math.Min(contentLength, buffer.Count - bodyStart);
            if (bufferedBodyLength > 0)
                buffer.CopyTo(bodyStart, bodyBytes, 0, bufferedBodyLength);

            var offset = bufferedBodyLength;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(bodyBytes, offset, contentLength - offset);
                if (read <= 0)
                    return ParsedHttpRequest.WithError(JsonError(400, "Unexpected end of request body."));
                offset += read;
            }

            var target = requestLine[1];
            var queryIndex = target.IndexOf('?');
            var path = queryIndex >= 0 ? target.Substring(0, queryIndex) : target;
            var request = new McpTransportRequest(requestLine[0], path, headers, Encoding.UTF8.GetString(bodyBytes));
            return ParsedHttpRequest.WithRequest(request);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, McpTransportResponse response)
        {
            var headers = new Dictionary<string, string>(response.Headers, StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Length"] = response.BodyBytes.Length.ToString(),
                ["Connection"] = "close"
            };

            if (!headers.ContainsKey("Content-Type"))
                headers["Content-Type"] = "application/json; charset=utf-8";

            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ");
            builder.Append(response.StatusCode);
            builder.Append(' ');
            builder.Append(GetReasonPhrase(response.StatusCode));
            builder.Append("\r\n");
            foreach (var pair in headers)
            {
                builder.Append(pair.Key);
                builder.Append(": ");
                builder.Append(pair.Value);
                builder.Append("\r\n");
            }
            builder.Append("\r\n");

            var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
            if (response.BodyBytes.Length > 0)
                await stream.WriteAsync(response.BodyBytes, 0, response.BodyBytes.Length);
        }

        private static List<IPAddress> BuildBindAddresses(McpServerOptions options)
        {
            var host = McpTransportUtility.NormalizeHost(options.Host);
            var addresses = new List<IPAddress>();

            if (McpTransportUtility.IsAnyHost(host))
            {
                addresses.Add(host.Contains(":") ? IPAddress.IPv6Any : IPAddress.Any);
                return addresses;
            }

            if (McpTransportUtility.IsLoopbackHost(host) && options.BindLocalhostAliases)
            {
                addresses.Add(IPAddress.Loopback);
                addresses.Add(IPAddress.IPv6Loopback);
                return addresses;
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                addresses.Add(IPAddress.Loopback);
                return addresses;
            }

            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host[1..^1];

            if (IPAddress.TryParse(host, out var parsed))
            {
                addresses.Add(parsed);
                return addresses;
            }

            throw new InvalidOperationException($"Unsupported TCP bind host '{options.Host}'. Use an IP address, localhost, or 0.0.0.0.");
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' && bytes[i - 2] == '\n' && bytes[i - 1] == '\r' && bytes[i] == '\n')
                    return i - 3;
            }
            return -1;
        }

        private static byte[] Slice(byte[] source, int length)
        {
            var result = new byte[length];
            Array.Copy(source, result, length);
            return result;
        }

        private static Dictionary<string, string> EmptyHeaders() => new(StringComparer.OrdinalIgnoreCase);

        private static McpTransportResponse JsonError(int statusCode, string message) =>
            McpTransportResponse.Json(statusCode, McpData.Obj(("error", message)), EmptyHeaders());

        private static string GetReasonPhrase(int statusCode)
        {
            return statusCode switch
            {
                200 => "OK",
                202 => "Accepted",
                204 => "No Content",
                400 => "Bad Request",
                403 => "Forbidden",
                404 => "Not Found",
                405 => "Method Not Allowed",
                413 => "Payload Too Large",
                431 => "Request Header Fields Too Large",
                500 => "Internal Server Error",
                503 => "Service Unavailable",
                _ => "OK"
            };
        }

        private readonly struct ParsedHttpRequest
        {
            private ParsedHttpRequest(McpTransportRequest? request, McpTransportResponse? errorResponse)
            {
                Request = request;
                ErrorResponse = errorResponse;
            }

            public McpTransportRequest? Request { get; }

            public McpTransportResponse? ErrorResponse { get; }

            public static ParsedHttpRequest WithRequest(McpTransportRequest request) => new(request, null);

            public static ParsedHttpRequest WithError(McpTransportResponse response) => new(null, response);
        }
    }
}
#endif
