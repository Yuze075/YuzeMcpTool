#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YuzeToolkit
{
    internal sealed class EditorStateCommand : IMcpCommand
    {
        public string Name => "editor.getState";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var scene = SceneManager.GetActiveScene();
            var selection = new List<object?>();
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                selection.Add(LitJson.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID())
                ));
            }

            var result = LitJson.Obj(
                ("environment", CommandUtilities.GetEnvironmentObject()),
                ("isPlaying", EditorApplication.isPlaying),
                ("isPaused", EditorApplication.isPaused),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode),
                ("applicationPath", Application.dataPath + "/.."),
                ("dataPath", Application.dataPath),
                ("unityVersion", Application.unityVersion),
                ("activeScene", LitJson.Obj(
                    ("name", scene.name),
                    ("path", scene.path),
                    ("isDirty", scene.isDirty),
                    ("isLoaded", scene.isLoaded),
                    ("rootCount", scene.rootCount)
                )),
                ("selection", LitJson.Obj(
                    ("count", selection.Count),
                    ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                    ("activeObjectName", Selection.activeObject != null ? Selection.activeObject.name : string.Empty),
                    ("items", selection)
                ))
            );

            return Task.FromResult(McpBridge.Success(result));
        }
    }

    internal sealed class EditorCompilationStateCommand : IMcpCommand
    {
        public string Name => "editor.getCompilationState";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            return Task.FromResult(McpBridge.Success(EditorCompilationMonitor.GetStateObject()));
        }
    }

    internal sealed class EditorRequestScriptCompilationCommand : IMcpCommand
    {
        public string Name => "editor.requestScriptCompilation";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            EditorApplication.delayCall += CompilationPipeline.RequestScriptCompilation;
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("requested", "scriptCompilation"),
                ("message", "Script compilation was scheduled on the next editor tick. Unity may briefly disconnect during domain reload.")
            )));
        }
    }

    internal sealed class EditorRequestAssetRefreshCommand : IMcpCommand
    {
        public string Name => "editor.scheduleAssetRefresh";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            EditorApplication.delayCall += AssetDatabase.Refresh;
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("requested", "assetRefresh"),
                ("message", "AssetDatabase.Refresh was scheduled on the next editor tick. Unity may briefly disconnect during asset refresh or domain reload.")
            )));
        }
    }

    internal sealed class EditorCompilerMessagesCommand : IMcpCommand
    {
        public string Name => "editor.getCompilerMessages";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = LitJson.AsObject(LitJson.Parse(context.ArgumentsJson)) ?? new Dictionary<string, object?>();
            var count = LitJson.GetInt(args, "count", 50);
            return Task.FromResult(McpBridge.Success(UnityLogBuffer.GetCompilerLikeMessages(count)));
        }
    }
}
