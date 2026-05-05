#nullable enable
using UnityEditor;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class CliBridgeReloadHandler
    {
        static CliBridgeReloadHandler()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload;
        }

        private static void BeforeAssemblyReload()
        {
            CliBridgeServer.Shared.Stop();
        }
    }
}
