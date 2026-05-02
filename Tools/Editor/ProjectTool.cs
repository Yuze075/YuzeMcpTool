#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("project", "Project settings, profiler, and editor tool diagnostics.")]
    public sealed class ProjectTool
    {
        [McpFunction("Read project settings.")]
        public Dictionary<string, object?> getProjectSettings()
        {
            return McpData.Obj(
                ("productName", PlayerSettings.productName),
                ("companyName", PlayerSettings.companyName),
                ("applicationIdentifier", PlayerSettings.applicationIdentifier),
                ("tags", UnityEditorInternal.InternalEditorUtility.tags.Cast<object?>().ToList()),
                ("layers", UnityEditorInternal.InternalEditorUtility.layers.Cast<object?>().ToList()));
        }

        [McpFunction("Read profiler state.")]
        public Dictionary<string, object?> getProfilerState()
        {
            var driver = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditorInternal.ProfilerDriver");
            object? isEnabled = null;
            object? isRecording = null;
            if (driver != null)
            {
                isEnabled = driver.GetProperty("enabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                isRecording = driver.GetProperty("profileEditor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            }

            return McpData.Obj(
                ("profilerDriverAvailable", driver != null),
                ("enabled", isEnabled),
                ("profileEditor", isRecording));
        }

        [McpFunction("Read MCP/editor tool state.")]
        public Dictionary<string, object?> getToolState()
        {
            var toolManager = typeof(Editor).Assembly.GetType("UnityEditor.EditorTools.ToolManager");
            var activeToolType = toolManager?.GetProperty("activeToolType", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Type;
            return McpData.Obj(
                ("activeToolType", activeToolType != null ? activeToolType.FullName ?? activeToolType.Name : string.Empty),
                ("pivotMode", Tools.pivotMode.ToString()),
                ("pivotRotation", Tools.pivotRotation.ToString()),
                ("current", Tools.current.ToString()));
        }
    }
}
