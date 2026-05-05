#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal sealed class WebSocketCliBridgeTransport : ICliBridgeTransport
    {
        public string Name => "WebSocket";

        public string Endpoint => string.Empty;

        public bool IsRunning => false;

        public CliBridgeTransportStartResult Start(
            CliBridgeOptions options,
            Func<ICliBridgeTransportConnection, CancellationToken, Task> connectionHandler,
            CancellationToken cancellationToken)
        {
            return CliBridgeTransportStartResult.Failed("WebGL CLI bridge transport is not implemented. WebGL cannot listen for inbound TCP; use a relay transport.");
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
