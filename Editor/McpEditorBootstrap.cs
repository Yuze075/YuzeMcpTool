#nullable enable
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class McpEditorBootstrap
    {
        private const string PrefKeyAutoStart = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".AutoStart";
        private const string PrefKeyPort = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Port";

        static McpEditorBootstrap()
        {
            RegisterEditorCommands();
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
            if (!EditorPrefs.GetBool(PrefKeyAutoStart, true)) return;
            McpServer.Shared.Start(LoadOptions());
        }

        internal static McpServerOptions LoadOptions()
        {
            return new McpServerOptions
            {
                Port = EditorPrefs.GetInt(PrefKeyPort, 3100)
            };
        }

        private static void RegisterEditorCommands()
        {
            McpCommandRegistry.EnsureDefaultCommands();
            TryRegister(new EditorExecuteCommand());
            TryRegister(new AssetExecuteCommand());
            TryRegister(new ImporterExecuteCommand());
            TryRegister(new SceneExecuteCommand());
            TryRegister(new PrefabExecuteCommand());
            TryRegister(new SerializedExecuteCommand());
            TryRegister(new ProjectExecuteCommand());
            TryRegister(new PipelineExecuteCommand());
            TryRegister(new ValidationExecuteCommand());
        }

        private static void TryRegister(IMcpCommand command)
        {
            if (McpCommandRegistry.TryGet(command.Name, out _)) return;
            McpCommandRegistry.Register(command);
        }
    }
}
