#nullable enable
using UnityEditor;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class McpEditorToolsBootstrap
    {
        static McpEditorToolsBootstrap()
        {
            McpToolRegistry.TryRegister<EditorTool>();
            McpToolRegistry.TryRegister<AssetsTool>();
            McpToolRegistry.TryRegister<ImportersTool>();
            McpToolRegistry.TryRegister<ScenesTool>();
            McpToolRegistry.TryRegister<PrefabsTool>();
            McpToolRegistry.TryRegister<SerializedTool>();
            McpToolRegistry.TryRegister<ProjectTool>();
            McpToolRegistry.TryRegister<PipelineTool>();
            McpToolRegistry.TryRegister<ValidationTool>();
        }
    }
}
