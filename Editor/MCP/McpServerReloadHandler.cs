#nullable enable
#if UNITY_EDITOR
using UnityEditor;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class McpServerReloadHandler
    {
        static McpServerReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private static void BeforeAssemblyReload()
        {
            McpServer.Shared.Stop();
        }
    }
}
#endif
