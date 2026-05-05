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
    internal static class EvalRuntimeToolsBootstrap
    {
#if UNITY_EDITOR
        static EvalRuntimeToolsBootstrap()
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
            EvalToolRegistry.TryRegister<RuntimeTool>();
            EvalToolRegistry.TryRegister<ObjectsTool>();
            EvalToolRegistry.TryRegister<ComponentsTool>();
            EvalToolRegistry.TryRegister<DiagnosticsTool>();
            EvalToolRegistry.TryRegister<ReflectionTool>();
            EvalToolRegistry.TryRegister<InspectTool>();
        }
    }
}
