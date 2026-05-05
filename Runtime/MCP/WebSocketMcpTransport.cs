#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal sealed class WebSocketMcpTransport : IMcpTransport
    {
        public string Name => "WebSocket";

        public string Endpoint => string.Empty;

        public bool IsRunning => false;

        public McpTransportStartResult Start(
            McpServerOptions options,
            Func<McpTransportRequest, CancellationToken, Task<McpTransportResponse>> requestHandler,
            CancellationToken cancellationToken)
        {
            return McpTransportStartResult.Failed("WebGL WebSocket MCP transport is not implemented. WebGL cannot listen for inbound TCP/HTTP; use a WebSocket relay transport.");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
