#nullable enable
using UnityEngine;

namespace YuzeToolkit
{
    public sealed class McpServerBehaviour : MonoBehaviour
    {
        [SerializeField, Tooltip("Whether to start the MCP server automatically when this component is enabled.")]
        private bool startOnEnable = true;

        [SerializeField, Tooltip("Local TCP port used by the MCP Streamable HTTP endpoint.")]
        private int port = 3100;

        private void OnEnable()
        {
            if (!startOnEnable) return;
            McpServer.Shared.Start(new McpServerOptions { Port = port });
        }

        private void OnDisable()
        {
            McpServer.Shared.Stop();
        }
    }
}
