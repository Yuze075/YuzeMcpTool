#nullable enable
using UnityEditor;

namespace YuzeToolkit
{
    internal static class McpToolEditorSettings
    {
        private const string PrefPrefix = nameof(YuzeToolkit) + ".McpTool.Enabled.";

        public static void ApplyPersistedStates()
        {
            foreach (var tool in EvalToolCatalog.ListTools(true))
            {
                if (!EditorPrefs.HasKey(PrefPrefix + tool.Name)) continue;
                EvalToolSettings.SetEnabled(tool.Name, EditorPrefs.GetBool(PrefPrefix + tool.Name, true));
            }
        }

        public static void SetEnabled(string name, bool enabled)
        {
            EditorPrefs.SetBool(PrefPrefix + name, enabled);
            EvalToolSettings.SetEnabled(name, enabled);
        }
    }
}
