#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("editor", "Editor state, compilation, selection, menu commands, play mode, and screenshots.")]
    public sealed class EditorTool
    {
        [EvalFunction("Return Editor state.")]
        public Dictionary<string, object?> getState()
        {
            var scene = SceneManager.GetActiveScene();
            var selection = new List<object?>();
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                selection.Add(EvalData.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID())
                ));
            }

            return EvalData.Obj(
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("isPlaying", EditorApplication.isPlaying),
                ("isPaused", EditorApplication.isPaused),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode),
                ("applicationPath", Application.dataPath + "/.."),
                ("dataPath", Application.dataPath),
                ("unityVersion", Application.unityVersion),
                ("activeScene", EvalData.Obj(
                    ("name", scene.name),
                    ("path", scene.path),
                    ("isDirty", scene.isDirty),
                    ("isLoaded", scene.isLoaded),
                    ("rootCount", scene.rootCount)
                )),
                ("selection", EvalData.Obj(
                    ("count", selection.Count),
                    ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                    ("activeObjectName", Selection.activeObject != null ? Selection.activeObject.name : string.Empty),
                    ("items", selection)
                ))
            );
        }

        [EvalFunction("Return compilation state.")]
        public Dictionary<string, object?> getCompilationState() => EditorCompilationMonitor.GetStateObject();

        [EvalFunction("Request script compilation.")]
        public Dictionary<string, object?> requestScriptCompilation()
        {
            EditorApplication.delayCall += CompilationPipeline.RequestScriptCompilation;
            return EvalData.Obj(
                ("requested", "scriptCompilation"),
                ("message", "Script compilation was scheduled on the next editor tick. Unity may briefly disconnect during domain reload.")
            );
        }

        [EvalFunction("Schedule AssetDatabase refresh.")]
        public Dictionary<string, object?> scheduleAssetRefresh()
        {
            EditorApplication.delayCall += AssetDatabase.Refresh;
            return EvalData.Obj(
                ("requested", "assetRefresh"),
                ("message", "AssetDatabase.Refresh was scheduled on the next editor tick. Unity may briefly disconnect during asset refresh or domain reload.")
            );
        }

        [EvalFunction("Read compiler messages.")]
        public List<object?> getCompilerMessages(int count = 50) => UnityLogBuffer.GetCompilerLikeMessages(count);

        [EvalFunction("Enter or exit play mode.")]
        public Dictionary<string, object?> setPlayMode(bool isPlaying)
        {
            EditorApplication.isPlaying = isPlaying;
            return EditorStatusProvider.GetStateObject();
        }

        [EvalFunction("Set pause state.")]
        public Dictionary<string, object?> setPause(bool isPaused)
        {
            EditorApplication.isPaused = isPaused;
            return EditorStatusProvider.GetStateObject();
        }

        [EvalFunction("Execute an Editor menu item.")]
        public Dictionary<string, object?> executeMenuItem(string path, bool confirm = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Argument 'path' is required.");
            if (!path.StartsWith("UnityEvalTool/", StringComparison.Ordinal) && !confirm)
                throw new InvalidOperationException("Executing arbitrary menu items requires confirm: true.");
            var ok = EditorApplication.ExecuteMenuItem(path);
            return EvalData.Obj(("path", path), ("executed", ok));
        }

        [EvalFunction("Read current selection.")]
        public Dictionary<string, object?> getSelection()
        {
            var items = Selection.objects
                .Where(obj => obj != null)
                .Select(obj => (object?)EvalData.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID()),
                    ("assetPath", AssetDatabase.GetAssetPath(obj))))
                .ToList();
            return EvalData.Obj(
                ("count", items.Count),
                ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                ("items", items));
        }

        [EvalFunction("Set selection.")]
        public Dictionary<string, object?> setSelection(object items)
        {
            var objects = new List<UnityEngine.Object>();
            foreach (var item in EvalData.AsArray(items) ?? new List<object?>())
            {
                UnityEngine.Object? obj = null;
                if (item is int id) obj = EditorUtility.InstanceIDToObject(id);
                if (item is long longId) obj = EditorUtility.InstanceIDToObject(checked((int)longId));
                if (item is string path) obj = AssetDatabase.LoadMainAssetAtPath(path) ?? ToolUtilities.ResolveGameObject(path);
                if (EvalData.AsObject(item) is { } selector)
                {
                    var assetPath = EvalData.GetString(selector, "assetPath");
                    obj = !string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.LoadMainAssetAtPath(assetPath) : ToolUtilities.ResolveGameObject(selector);
                }
                if (obj != null) objects.Add(obj);
            }
            Selection.objects = objects.ToArray();
            return getSelection();
        }

        [EvalFunction("Capture Game View screenshot.")]
        public Dictionary<string, object?> screenshotGameView(string path = "Temp/UnityEvalTool-GameView.png")
        {
            if (!ToolUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            ScreenCapture.CaptureScreenshot(fullPath);
            return EvalData.Obj(("path", projectPath), ("fullPath", fullPath), ("message", "Screenshot capture was requested. The file may be written after the current frame."));
        }
    }
}
