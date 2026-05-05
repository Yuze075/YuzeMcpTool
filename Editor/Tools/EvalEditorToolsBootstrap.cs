#nullable enable
using UnityEditor;

namespace YuzeToolkit
{
    [InitializeOnLoad]
    internal static class EvalEditorToolsBootstrap
    {
        static EvalEditorToolsBootstrap()
        {
            EvalToolRegistry.TryRegister<EditorTool>();
            EvalToolRegistry.TryRegister<AssetsTool>();
            EvalToolRegistry.TryRegister<ImportersTool>();
            EvalToolRegistry.TryRegister<ScenesTool>();
            EvalToolRegistry.TryRegister<PrefabsTool>();
            EvalToolRegistry.TryRegister<SerializedTool>();
            EvalToolRegistry.TryRegister<ProjectTool>();
            EvalToolRegistry.TryRegister<PipelineTool>();
            EvalToolRegistry.TryRegister<ValidationTool>();
        }
    }
}
