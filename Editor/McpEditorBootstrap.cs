#nullable enable
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class McpEditorBootstrap
    {
        private const string PrefKeyAutoStart = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".AutoStart";
        private const string PrefKeyHost = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Host";
        private const string PrefKeyPort = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Port";
        private const string PrefKeyBindLocalhostAliases = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".BindLocalhostAliases";

        static McpEditorBootstrap()
        {
            EditorApplication.delayCall += McpToolEditorSettings.ApplyPersistedStates;
            EditorApplication.delayCall += StartIfNeeded;
        }

        [MenuItem(nameof(YuzeToolkit) + "/MCP/Start Server")]
        private static void StartServerMenu()
        {
            StartServer();
        }

        internal static void StartServer()
        {
            var options = LoadOptions();
            McpServer.Shared.Start(options);
            EditorPrefs.SetBool(PrefKeyAutoStart, true);
        }

        [MenuItem(nameof(YuzeToolkit) + "/MCP/Stop Server")]
        private static void StopServerMenu()
        {
            StopServer();
        }

        internal static void StopServer()
        {
            McpServer.Shared.Stop();
            EditorPrefs.SetBool(PrefKeyAutoStart, false);
        }

        [MenuItem(nameof(YuzeToolkit) + "/MCP/Log Status")]
        private static void LogStatus()
        {
            var state = McpServer.Shared.State;
            Debug.Log($"[YuzeMcpTool] status={state.Status}, environment={state.Environment}, endpoint={state.Endpoint}, sessions={state.ActiveSessionCount}, lastError={state.LastError}");
        }

        private static void StartIfNeeded()
        {
            if (!EditorPrefs.GetBool(PrefKeyAutoStart, false)) return;
            McpServer.Shared.Start(LoadOptions());
        }

        internal static McpServerOptions LoadOptions()
        {
            return new McpServerOptions
            {
                Host = EditorPrefs.GetString(PrefKeyHost, "127.0.0.1"),
                Port = EditorPrefs.GetInt(PrefKeyPort, 3100),
                BindLocalhostAliases = EditorPrefs.GetBool(PrefKeyBindLocalhostAliases, true)
            };
        }
    }
}
