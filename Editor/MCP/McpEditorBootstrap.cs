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
        private const string PrefKeyRequireToken = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".RequireToken";
        private const string PrefKeyToken = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Token";

        static McpEditorBootstrap()
        {
            EditorApplication.delayCall += McpToolEditorSettings.ApplyPersistedStates;
            EditorApplication.delayCall += StartIfNeeded;
        }

        internal static void StartServer()
        {
            var options = LoadOptions();
            McpServer.Shared.Start(options);
            SaveGeneratedToken(options);
            EditorPrefs.SetBool(PrefKeyAutoStart, McpServer.Shared.State.IsRunning);
        }

        internal static void StopServer()
        {
            McpServer.Shared.Stop();
            EditorPrefs.SetBool(PrefKeyAutoStart, false);
        }

        private static void StartIfNeeded()
        {
            if (!EditorPrefs.GetBool(PrefKeyAutoStart, false)) return;
            var options = LoadOptions();
            McpServer.Shared.Start(options);
            SaveGeneratedToken(options);
        }

        private static void SaveGeneratedToken(McpServerOptions options)
        {
            var state = McpServer.Shared.State;
            if (!state.IsRunning || !options.RequireToken || !string.IsNullOrWhiteSpace(options.Token)) return;
            options.Token = state.Token;
            SaveOptions(options);
        }

        internal static McpServerOptions LoadOptions()
        {
            return new McpServerOptions
            {
                Host = EditorPrefs.GetString(PrefKeyHost, "127.0.0.1"),
                Port = EditorPrefs.GetInt(PrefKeyPort, 3100),
                BindLocalhostAliases = EditorPrefs.GetBool(PrefKeyBindLocalhostAliases, true),
                RequireToken = EditorPrefs.GetBool(PrefKeyRequireToken, false),
                Token = EditorPrefs.GetString(PrefKeyToken, string.Empty)
            };
        }

        internal static void SaveOptions(McpServerOptions options)
        {
            EditorPrefs.SetString(PrefKeyHost, options.Host);
            EditorPrefs.SetInt(PrefKeyPort, options.Port);
            EditorPrefs.SetBool(PrefKeyBindLocalhostAliases, options.BindLocalhostAliases);
            EditorPrefs.SetBool(PrefKeyRequireToken, options.RequireToken);
            EditorPrefs.SetString(PrefKeyToken, options.Token);
        }
    }
}
