#nullable enable
using System.Threading;

namespace YuzeToolkit
{
    internal sealed class McpCommandContext
    {
        public McpCommandContext(string sessionId, string requestId, string argumentsJson, CancellationToken cancellationToken)
        {
            SessionId = sessionId;
            RequestId = requestId;
            ArgumentsJson = argumentsJson;
            CancellationToken = cancellationToken;
        }

        public string SessionId { get; }

        public string RequestId { get; }

        public string ArgumentsJson { get; }

        public CancellationToken CancellationToken { get; }
    }
}
