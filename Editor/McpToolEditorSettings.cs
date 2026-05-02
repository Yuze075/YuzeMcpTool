#nullable enable
using UnityEditor;

namespace YuzeToolkit
{
    internal static class McpToolEditorSettings
    {
        private const string PrefPrefix = nameof(YuzeToolkit) + ".McpTool.Enabled.";

        public static void ApplyPersistedStates()
        {
            foreach (var tool in McpToolCatalog.ListTools(true))
            {
                if (!EditorPrefs.HasKey(PrefPrefix + tool.Name)) continue;
                McpToolSettings.SetEnabled(tool.Name, EditorPrefs.GetBool(PrefPrefix + tool.Name, true));
            }
        }

        public static void SetEnabled(string name, bool enabled)
        {
            EditorPrefs.SetBool(PrefPrefix + name, enabled);
            McpToolSettings.SetEnabled(name, enabled);
        }
    }
}
