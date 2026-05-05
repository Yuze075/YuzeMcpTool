#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    public static class CliBridgeEditorBootstrap
    {
        private const string PrefKeyHost = nameof(YuzeToolkit) + ".CliBridge.Host";
        private const string PrefKeyPort = nameof(YuzeToolkit) + ".CliBridge.Port";
        private const string PrefKeyToken = nameof(YuzeToolkit) + ".CliBridge.Token";
        private const string PrefKeyRequireToken = nameof(YuzeToolkit) + ".CliBridge.RequireToken";
        private const string PrefKeyAutoStart = nameof(YuzeToolkit) + ".CliBridge.AutoStart";
        private const string PrefKeyCliPath = nameof(YuzeToolkit) + ".CliBridge.CliPath";

        static CliBridgeEditorBootstrap()
        {
            EditorApplication.delayCall += StartIfNeeded;
        }

        public static void CopyConnectCommand()
        {
            var command = BuildConnectCommand();
            if (string.IsNullOrEmpty(command)) return;
            EditorGUIUtility.systemCopyBuffer = command;
            UnityEngine.Debug.Log($"[UnityEvalTool] CLI command copied: {RedactToken(command)}");
        }

        public static void LaunchCli()
        {
            StartBridge();
            var state = CliBridgeServer.Shared.State;
            if (!state.IsRunning) return;

            var cliPath = ResolveCliPath();
            if (string.IsNullOrWhiteSpace(cliPath) || !File.Exists(cliPath))
            {
                UnityEngine.Debug.LogWarning($"[UnityEvalTool] unity executable was not found. Build Game/UnityCLI first or set EditorPrefs key {PrefKeyCliPath}.");
                CopyConnectCommand();
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = state.RequireToken
                    ? $"--host {Quote(state.Host)} --port {state.Port} --token {Quote(state.Token)}"
                    : $"--host {Quote(state.Host)} --port {state.Port}",
                UseShellExecute = true,
                WorkingDirectory = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath
            };
            Process.Start(startInfo);
        }

        public static void StartBridge()
        {
            var options = LoadOptions();
            CliBridgeServer.Shared.Start(options);
            var state = CliBridgeServer.Shared.State;
            if (!state.IsRunning)
            {
                UnityEngine.Debug.LogWarning($"[UnityEvalTool] CLI bridge did not start: {state.LastError}");
                return;
            }

            EditorPrefs.SetString(PrefKeyHost, state.Host);
            EditorPrefs.SetInt(PrefKeyPort, state.Port);
            EditorPrefs.SetString(PrefKeyToken, state.Token);
            EditorPrefs.SetBool(PrefKeyRequireToken, state.RequireToken);
            EditorPrefs.SetBool(PrefKeyAutoStart, true);
        }

        public static void StopBridge()
        {
            CliBridgeServer.Shared.Stop();
            EditorPrefs.SetBool(PrefKeyAutoStart, false);
        }

        public static CliBridgeOptions LoadOptions()
        {
            return new CliBridgeOptions
            {
                Host = EditorPrefs.GetString(PrefKeyHost, "127.0.0.1"),
                Port = EditorPrefs.GetInt(PrefKeyPort, 0),
                RequireToken = EditorPrefs.GetBool(PrefKeyRequireToken, true),
                Token = EditorPrefs.GetString(PrefKeyToken, string.Empty)
            };
        }

        public static void SaveOptions(CliBridgeOptions options)
        {
            EditorPrefs.SetString(PrefKeyHost, options.Host);
            EditorPrefs.SetInt(PrefKeyPort, options.Port);
            EditorPrefs.SetBool(PrefKeyRequireToken, options.RequireToken);
            EditorPrefs.SetString(PrefKeyToken, options.Token);
        }

        public static string BuildConnectCommand()
        {
            if (!CliBridgeServer.Shared.State.IsRunning)
                StartBridge();
            var state = CliBridgeServer.Shared.State;
            if (!state.IsRunning)
            {
                UnityEngine.Debug.LogWarning($"[UnityEvalTool] Cannot build CLI connect command because the bridge is not running: {state.LastError}");
                return string.Empty;
            }

            return state.RequireToken
                ? $"unity --host {state.Host} --port {state.Port} --token {Quote(state.Token)}"
                : $"unity --host {state.Host} --port {state.Port}";
        }

        private static void StartIfNeeded()
        {
            if (!EditorPrefs.GetBool(PrefKeyAutoStart, false)) return;
            StartBridge();
        }

        private static string ResolveCliPath()
        {
            var configured = EditorPrefs.GetString(PrefKeyCliPath, string.Empty);
            if (!string.IsNullOrWhiteSpace(configured)) return configured;

            var root = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;

            var exe = Path.Combine(root, "Game", "UnityCLI", "bin", "Debug", "net10.0", "unity.exe");
            return exe;
        }

        private static string Quote(string value) =>
            string.IsNullOrEmpty(value) || value.Contains(' ') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;

        private static string RedactToken(string command)
        {
            const string tokenFlag = "--token ";
            var index = command.IndexOf(tokenFlag, StringComparison.Ordinal);
            if (index < 0) return command;
            var valueStart = index + tokenFlag.Length;
            var valueEnd = command.Length;
            if (valueStart < command.Length && command[valueStart] == '"')
            {
                for (var i = valueStart + 1; i < command.Length; i++)
                {
                    if (command[i] != '"' || command[i - 1] == '\\') continue;
                    valueEnd = i + 1;
                    break;
                }
            }
            else
            {
                var nextSpace = command.IndexOf(' ', valueStart);
                if (nextSpace >= 0) valueEnd = nextSpace;
            }
            return command.Substring(0, valueStart) + "<redacted>" + command.Substring(valueEnd);
        }
    }
}
