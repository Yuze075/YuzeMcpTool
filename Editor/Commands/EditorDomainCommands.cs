#nullable enable
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;

namespace YuzeToolkit
{
    internal sealed class ProfilerGetStateCommand : IMcpCommand
    {
        public string Name => "profiler.getState";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var driver = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditorInternal.ProfilerDriver");
            object? isEnabled = null;
            object? isRecording = null;
            if (driver != null)
            {
                isEnabled = driver.GetProperty("enabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                isRecording = driver.GetProperty("profileEditor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            }

            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("profilerDriverAvailable", driver != null),
                ("enabled", isEnabled),
                ("profileEditor", isRecording))));
        }
    }

    internal sealed class ToolGetStateCommand : IMcpCommand
    {
        public string Name => "tool.getState";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var toolManager = typeof(Editor).Assembly.GetType("UnityEditor.EditorTools.ToolManager");
            var activeToolType = toolManager?.GetProperty("activeToolType", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Type;
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("activeToolType", activeToolType != null ? activeToolType.FullName ?? activeToolType.Name : string.Empty),
                ("pivotMode", Tools.pivotMode.ToString()),
                ("pivotRotation", Tools.pivotRotation.ToString()),
                ("current", Tools.current.ToString()))));
        }
    }
}
