#nullable enable
using System.Threading;

namespace YuzeToolkit
{
    internal sealed class McpBridgeContext
    {
        public McpBridgeContext(McpSession session, string requestId, CancellationToken cancellationToken)
        {
            Session = session;
            RequestId = requestId;
            CancellationToken = cancellationToken;
        }

        public McpSession Session { get; }

        public string SessionId => Session.Id;

        public string RequestId { get; }

        public CancellationToken CancellationToken { get; }
    }
}
