#nullable enable
#if UNITY_EDITOR
using UnityEditor;
using System.Collections.Generic;

namespace YuzeToolkit
{
    public static class EditorStatusProvider
    {
        public static Dictionary<string, object?> GetStateObject()
        {
            return EvalData.Obj(
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode)
            );
        }
    }
}
#endif
