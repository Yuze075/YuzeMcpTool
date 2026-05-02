#nullable enable
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace YuzeToolkit
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal static class McpRuntimeToolsBootstrap
    {
#if UNITY_EDITOR
        static McpRuntimeToolsBootstrap()
        {
            RegisterTools();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterToolsOnLoad()
        {
            RegisterTools();
        }

        private static void RegisterTools()
        {
            McpToolRegistry.TryRegister<RuntimeTool>();
            McpToolRegistry.TryRegister<ObjectsTool>();
            McpToolRegistry.TryRegister<ComponentsTool>();
            McpToolRegistry.TryRegister<DiagnosticsTool>();
            McpToolRegistry.TryRegister<ReflectionTool>();
            McpToolRegistry.TryRegister<InspectTool>();
        }
    }
}
