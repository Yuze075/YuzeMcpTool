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
    public static class CliBridgeBootstrap
    {
#if UNITY_EDITOR
        static CliBridgeBootstrap()
        {
            RegisterTools();
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterTools()
        {
            EvalToolRegistry.TryRegister<CliBridgeTool>();
        }
    }
}
