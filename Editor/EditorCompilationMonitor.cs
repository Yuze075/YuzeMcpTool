#nullable enable
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class EditorCompilationMonitor
    {
        private const string SessionKeyDomainReloading = nameof(YuzeToolkit) + "." + nameof(EditorCompilationMonitor) + ".DomainReloading";
        private const string SessionKeyCompileStarted = nameof(YuzeToolkit) + "." + nameof(EditorCompilationMonitor) + ".CompileStartedUtc";
        private const string SessionKeyCompileFinished = nameof(YuzeToolkit) + "." + nameof(EditorCompilationMonitor) + ".CompileFinishedUtc";

        private static bool _wasCompiling;

        static EditorCompilationMonitor()
        {
            SessionState.SetBool(SessionKeyDomainReloading, false);
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= AfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += AfterAssemblyReload;
            UnityLogBuffer.Start();
        }

        public static Dictionary<string, object?> GetStateObject()
        {
            return McpData.Obj(
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode),
                ("isDomainReloading", SessionState.GetBool(SessionKeyDomainReloading, false)),
                ("lastCompilationStartedAtUtc", SessionState.GetString(SessionKeyCompileStarted, string.Empty)),
                ("lastCompilationFinishedAtUtc", SessionState.GetString(SessionKeyCompileFinished, string.Empty))
            );
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling && !_wasCompiling)
                SessionState.SetString(SessionKeyCompileStarted, DateTime.UtcNow.ToString("O"));

            if (!EditorApplication.isCompiling && _wasCompiling)
                SessionState.SetString(SessionKeyCompileFinished, DateTime.UtcNow.ToString("O"));

            _wasCompiling = EditorApplication.isCompiling;
        }

        private static void BeforeAssemblyReload()
        {
            SessionState.SetBool(SessionKeyDomainReloading, true);
            McpServer.Shared.Stop();
        }

        private static void AfterAssemblyReload()
        {
            SessionState.SetBool(SessionKeyDomainReloading, false);
            SessionState.SetString(SessionKeyCompileFinished, DateTime.UtcNow.ToString("O"));
        }
    }
}
#endif
