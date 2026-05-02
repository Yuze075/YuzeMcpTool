#nullable enable
using UnityEngine;

namespace YuzeToolkit
{
    public sealed class McpServerBehaviour : MonoBehaviour
    {
        private string? _ownerId;

        [SerializeField, Tooltip("Whether to start the MCP server automatically when this component is enabled.")]
        private bool startOnEnable = true;

        [SerializeField, Tooltip("Host address to bind. Use 127.0.0.1 for local-only access or 0.0.0.0 to listen on all IPv4 interfaces.")]
        private string host = "127.0.0.1";

        [SerializeField, Tooltip("Local TCP port used by the MCP Streamable HTTP endpoint.")]
        private int port = 3100;

        [SerializeField, Tooltip("Whether loopback hosts should also bind localhost aliases for the TCP listener.")]
        private bool bindLocalhostAliases = true;

        private void OnEnable()
        {
            if (!startOnEnable) return;
            _ownerId = $"{nameof(McpServerBehaviour)}:{GetInstanceID()}";
            McpServer.Shared.StartWithOwner(_ownerId, new McpServerOptions
            {
                Host = host,
                Port = port,
                BindLocalhostAliases = bindLocalhostAliases
            });
        }

        private void OnDisable()
        {
            if (string.IsNullOrEmpty(_ownerId)) return;
            McpServer.Shared.StopOwner(_ownerId);
            _ownerId = null;
        }
    }
}
