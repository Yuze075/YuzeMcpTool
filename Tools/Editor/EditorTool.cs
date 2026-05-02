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
    [McpTool("editor", "Editor state, compilation, selection, menu commands, play mode, and screenshots.")]
    public sealed class EditorTool
    {
        [McpFunction("Return Editor state.")]
        public Dictionary<string, object?> getState()
        {
            var scene = SceneManager.GetActiveScene();
            var selection = new List<object?>();
            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                selection.Add(McpData.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID())
                ));
            }

            return McpData.Obj(
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("isPlaying", EditorApplication.isPlaying),
                ("isPaused", EditorApplication.isPaused),
                ("isCompiling", EditorApplication.isCompiling),
                ("isUpdating", EditorApplication.isUpdating),
                ("isPlayingOrWillChangePlaymode", EditorApplication.isPlayingOrWillChangePlaymode),
                ("applicationPath", Application.dataPath + "/.."),
                ("dataPath", Application.dataPath),
                ("unityVersion", Application.unityVersion),
                ("activeScene", McpData.Obj(
                    ("name", scene.name),
                    ("path", scene.path),
                    ("isDirty", scene.isDirty),
                    ("isLoaded", scene.isLoaded),
                    ("rootCount", scene.rootCount)
                )),
                ("selection", McpData.Obj(
                    ("count", selection.Count),
                    ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                    ("activeObjectName", Selection.activeObject != null ? Selection.activeObject.name : string.Empty),
                    ("items", selection)
                ))
            );
        }

        [McpFunction("Return compilation state.")]
        public Dictionary<string, object?> getCompilationState() => EditorCompilationMonitor.GetStateObject();

        [McpFunction("Request script compilation.")]
        public Dictionary<string, object?> requestScriptCompilation()
        {
            EditorApplication.delayCall += CompilationPipeline.RequestScriptCompilation;
            return McpData.Obj(
                ("requested", "scriptCompilation"),
                ("message", "Script compilation was scheduled on the next editor tick. Unity may briefly disconnect during domain reload.")
            );
        }

        [McpFunction("Schedule AssetDatabase refresh.")]
        public Dictionary<string, object?> scheduleAssetRefresh()
        {
            EditorApplication.delayCall += AssetDatabase.Refresh;
            return McpData.Obj(
                ("requested", "assetRefresh"),
                ("message", "AssetDatabase.Refresh was scheduled on the next editor tick. Unity may briefly disconnect during asset refresh or domain reload.")
            );
        }

        [McpFunction("Read compiler messages.")]
        public List<object?> getCompilerMessages(int count = 50) => UnityLogBuffer.GetCompilerLikeMessages(count);

        [McpFunction("Enter or exit play mode.")]
        public Dictionary<string, object?> setPlayMode(bool isPlaying)
        {
            EditorApplication.isPlaying = isPlaying;
            return EditorStatusProvider.GetStateObject();
        }

        [McpFunction("Set pause state.")]
        public Dictionary<string, object?> setPause(bool isPaused)
        {
            EditorApplication.isPaused = isPaused;
            return EditorStatusProvider.GetStateObject();
        }

        [McpFunction("Execute an Editor menu item.")]
        public Dictionary<string, object?> executeMenuItem(string path, bool confirm = false)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Argument 'path' is required.");
            if (!path.StartsWith("YuzeToolkit/MCP/", StringComparison.Ordinal) && !confirm)
                throw new InvalidOperationException("Executing arbitrary menu items requires confirm: true.");
            var ok = EditorApplication.ExecuteMenuItem(path);
            return McpData.Obj(("path", path), ("executed", ok));
        }

        [McpFunction("Read current selection.")]
        public Dictionary<string, object?> getSelection()
        {
            var items = Selection.objects
                .Where(obj => obj != null)
                .Select(obj => (object?)McpData.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID()),
                    ("assetPath", AssetDatabase.GetAssetPath(obj))))
                .ToList();
            return McpData.Obj(
                ("count", items.Count),
                ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                ("items", items));
        }

        [McpFunction("Set selection.")]
        public Dictionary<string, object?> setSelection(object items)
        {
            var objects = new List<UnityEngine.Object>();
            foreach (var item in McpData.AsArray(items) ?? new List<object?>())
            {
                UnityEngine.Object? obj = null;
                if (item is int id) obj = EditorUtility.InstanceIDToObject(id);
                if (item is long longId) obj = EditorUtility.InstanceIDToObject(checked((int)longId));
                if (item is string path) obj = AssetDatabase.LoadMainAssetAtPath(path) ?? ToolUtilities.ResolveGameObject(path);
                if (McpData.AsObject(item) is { } selector)
                {
                    var assetPath = McpData.GetString(selector, "assetPath");
                    obj = !string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.LoadMainAssetAtPath(assetPath) : ToolUtilities.ResolveGameObject(selector);
                }
                if (obj != null) objects.Add(obj);
            }
            Selection.objects = objects.ToArray();
            return getSelection();
        }

        [McpFunction("Capture Game View screenshot.")]
        public Dictionary<string, object?> screenshotGameView(string path = "Temp/YuzeMcpTool-GameView.png")
        {
            if (!ToolUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            ScreenCapture.CaptureScreenshot(fullPath);
            return McpData.Obj(("path", projectPath), ("fullPath", fullPath), ("message", "Screenshot capture was requested. The file may be written after the current frame."));
        }
    }
}
