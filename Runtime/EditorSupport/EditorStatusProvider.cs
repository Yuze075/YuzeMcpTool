#nullable enable
#if UNITY_EDITOR
using UnityEditor;

namespace YuzeToolkit
{
    internal static class EditorStatusProvider
    {
        public static object GetStateObject()
        {
            return LitJson.Obj(
                ("environment", CommandUtilities.GetEnvironmentObject()),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode)
            );
        }
    }
}
#endif
