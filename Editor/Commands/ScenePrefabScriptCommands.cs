#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YuzeToolkit
{
    internal sealed class SceneListOpenCommand : IMcpCommand
    {
        public string Name => "scene.listOpen";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var scenes = new List<object?>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                scenes.Add(SummarizeScene(scene, false));
            }
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", scenes.Count), ("scenes", scenes))));
        }

        internal static object SummarizeScene(Scene scene, bool includeRoots)
        {
            return LitJson.Obj(
                ("name", scene.name),
                ("path", scene.path),
                ("handle", scene.handle),
                ("isLoaded", scene.isLoaded),
                ("isDirty", scene.isDirty),
                ("isValid", scene.IsValid()),
                ("rootCount", scene.IsValid() && scene.isLoaded ? scene.rootCount : 0),
                ("roots", includeRoots ? CommandUtilities.GetRootSummaries(scene) : new List<object?>()));
        }
    }

    internal sealed class SceneGetHierarchyCommand : IMcpCommand
    {
        public string Name => "scene.getHierarchy";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var scene = SceneManager.GetActiveScene();
            var depth = Math.Max(0, LitJson.GetInt(args, "depth", 2));
            var includeComponents = LitJson.GetBool(args, "includeComponents", false);
            var limit = Math.Max(0, LitJson.GetInt(args, "limit", 200));
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("name", scene.name),
                ("path", scene.path),
                ("handle", scene.handle),
                ("isLoaded", scene.isLoaded),
                ("isDirty", scene.isDirty),
                ("isValid", scene.IsValid()),
                ("rootCount", scene.IsValid() && scene.isLoaded ? scene.rootCount : 0),
                ("depth", depth),
                ("includeComponents", includeComponents),
                ("roots", CommandUtilities.GetHierarchySummaries(scene, depth, includeComponents, limit))
            )));
        }
    }

    internal sealed class SceneOpenCommand : IMcpCommand
    {
        public string Name => "scene.open";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            var modeText = CommandUtilities.GetString(args, "mode", "Single");
            var mode = Enum.TryParse<OpenSceneMode>(modeText, true, out var parsed) ? parsed : OpenSceneMode.Single;
            var scene = EditorSceneManager.OpenScene(path, mode);
            return Task.FromResult(McpBridge.Success(SceneListOpenCommand.SummarizeScene(scene, true)));
        }
    }

    internal sealed class SceneCreateCommand : IMcpCommand
    {
        public string Name => "scene.create";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var setupText = CommandUtilities.GetString(args, "setup", "DefaultGameObjects");
            var modeText = CommandUtilities.GetString(args, "mode", "Single");
            var setup = Enum.TryParse<NewSceneSetup>(setupText, true, out var parsedSetup) ? parsedSetup : NewSceneSetup.DefaultGameObjects;
            var mode = Enum.TryParse<NewSceneMode>(modeText, true, out var parsedMode) ? parsedMode : NewSceneMode.Single;
            var scene = EditorSceneManager.NewScene(setup, mode);
            var path = CommandUtilities.GetString(args, "path");
            if (!string.IsNullOrWhiteSpace(path))
                EditorSceneManager.SaveScene(scene, path);
            return Task.FromResult(McpBridge.Success(SceneListOpenCommand.SummarizeScene(scene, true)));
        }
    }

    internal sealed class SceneSaveCommand : IMcpCommand
    {
        public string Name => "scene.save";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var scene = SceneManager.GetActiveScene();
            var ok = EditorSceneManager.SaveScene(scene);
            return Task.FromResult(ok ? McpBridge.Success(SceneListOpenCommand.SummarizeScene(scene, false)) : McpBridge.Error("Failed to save active scene."));
        }
    }

    internal sealed class SceneSaveAsCommand : IMcpCommand
    {
        public string Name => "scene.saveAs";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var path = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "path");
            var scene = SceneManager.GetActiveScene();
            var ok = EditorSceneManager.SaveScene(scene, path);
            return Task.FromResult(ok ? McpBridge.Success(SceneListOpenCommand.SummarizeScene(scene, false)) : McpBridge.Error($"Failed to save scene as '{path}'."));
        }
    }

    internal sealed class SceneSetActiveCommand : IMcpCommand
    {
        public string Name => "scene.setActive";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var path = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "path");
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.path != path && scene.name != path) continue;
                SceneManager.SetActiveScene(scene);
                return Task.FromResult(McpBridge.Success(SceneListOpenCommand.SummarizeScene(scene, false)));
            }
            return Task.FromResult(McpBridge.Error($"Open scene '{path}' was not found."));
        }
    }

    internal sealed class PrefabInstantiateCommand : IMcpCommand
    {
        public string Name => "prefab.instantiate";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return Task.FromResult(McpBridge.Error($"Prefab '{path}' was not found."));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (args.TryGetValue("parent", out var parentValue))
            {
                var parent = CommandUtilities.ResolveGameObject(parentValue);
                if (parent != null) instance.transform.SetParent(parent.transform, false);
            }
            instance.transform.position = CommandUtilities.GetVector3(args, "position", instance.transform.position);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(instance)));
        }
    }

    internal sealed class PrefabCreateFromObjectCommand : IMcpCommand
    {
        public string Name => "prefab.createFromObject";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var path = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            return Task.FromResult(prefab != null ? McpBridge.Success(AssetFindCommand.SummarizeAsset(path)) : McpBridge.Error($"Failed to create prefab '{path}'."));
        }
    }

    internal sealed class PrefabCreateVariantCommand : IMcpCommand
    {
        public string Name => "prefab.createVariant";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var basePath = CommandUtilities.GetString(args, "basePath");
            var variantPath = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(basePath)) return Task.FromResult(McpBridge.Error("Argument 'basePath' is required."));
            if (string.IsNullOrWhiteSpace(variantPath)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            if (prefab == null) return Task.FromResult(McpBridge.Error($"Prefab '{basePath}' was not found."));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(instance, variantPath);
                return Task.FromResult(saved != null ? McpBridge.Success(AssetFindCommand.SummarizeAsset(variantPath)) : McpBridge.Error($"Failed to create prefab variant '{variantPath}'."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }

    internal sealed class PrefabOpenStageCommand : IMcpCommand
    {
        public string Name => "prefab.openStage";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var path = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "path");
            var stage = PrefabStageUtility.OpenPrefab(path);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("assetPath", stage.assetPath), ("scenePath", stage.scene.path))));
        }
    }

    internal sealed class PrefabCloseStageCommand : IMcpCommand
    {
        public string Name => "prefab.closeStage";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            StageUtility.GoBackToPreviousStage();
            return Task.FromResult(McpBridge.Success("Prefab stage close requested."));
        }
    }

    internal sealed class PrefabSaveStageCommand : IMcpCommand
    {
        public string Name => "prefab.saveStage";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null) return Task.FromResult(McpBridge.Error("No prefab stage is open."));

            var savePrefab = stage.GetType().GetMethod("SavePrefab", BindingFlags.Instance | BindingFlags.NonPublic);
            if (savePrefab == null) return Task.FromResult(McpBridge.Error("PrefabStage.SavePrefab was not found in this Unity version."));

            try
            {
                savePrefab.Invoke(stage, Array.Empty<object>());
            }
            catch (TargetInvocationException ex)
            {
                return Task.FromResult(McpBridge.Error(ex.InnerException?.Message ?? ex.Message));
            }

            AssetDatabase.SaveAssets();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("assetPath", stage.assetPath), ("saved", true))));
        }
    }

    internal sealed class PrefabGetOverridesCommand : IMcpCommand
    {
        public string Name => "prefab.getOverrides";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var go = CommandUtilities.ResolveGameObject(CommandUtilities.ParseArgs(context.ArgumentsJson));
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var overrides = PrefabUtility.GetObjectOverrides(go)
                .Select(ov => (object?)LitJson.Obj(
                    ("instanceObject", ov.instanceObject != null ? ov.instanceObject.name : string.Empty),
                    ("instanceId", ov.instanceObject != null ? ov.instanceObject.GetInstanceID() : 0)))
                .ToList();
            var modifications = PrefabUtility.GetPropertyModifications(go)?
                .Select(mod => (object?)LitJson.Obj(
                    ("target", mod.target != null ? mod.target.name : string.Empty),
                    ("targetInstanceId", mod.target != null ? mod.target.GetInstanceID() : 0),
                    ("propertyPath", mod.propertyPath),
                    ("value", mod.value),
                    ("objectReference", mod.objectReference != null ? SerializedGetCommand.SummarizeObject(mod.objectReference) : null)))
                .ToList() ?? new List<object?>();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("count", overrides.Count),
                ("overrides", overrides),
                ("propertyModificationCount", modifications.Count),
                ("propertyModifications", modifications)
            )));
        }
    }

    internal sealed class PrefabApplyOverridesCommand : IMcpCommand
    {
        public string Name => "prefab.applyOverrides";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Applying prefab overrides requires confirm: true."));
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return Task.FromResult(McpBridge.Error("Target is not part of a prefab instance."));
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(root, false)));
        }
    }

    internal sealed class PrefabRevertOverridesCommand : IMcpCommand
    {
        public string Name => "prefab.revertOverrides";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Reverting prefab overrides requires confirm: true."));
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return Task.FromResult(McpBridge.Error("Target is not part of a prefab instance."));
            PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(root, false)));
        }
    }

    internal sealed class PrefabUnpackCommand : IMcpCommand
    {
        public string Name => "prefab.unpack";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Unpacking a prefab instance requires confirm: true."));
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
            if (root == null) return Task.FromResult(McpBridge.Error("Target is not part of a prefab instance."));
            var modeText = CommandUtilities.GetString(args, "mode", "OutermostRoot");
            var mode = Enum.TryParse<PrefabUnpackMode>(modeText, true, out var parsed) ? parsed : PrefabUnpackMode.OutermostRoot;
            PrefabUtility.UnpackPrefabInstance(root, mode, InteractionMode.AutomatedAction);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(root, false)));
        }
    }

    internal sealed class ScriptCreateCommand : IMcpCommand
    {
        public string Name => "script.create";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (!CommandUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                return Task.FromResult(McpBridge.Error(error));
            var className = Path.GetFileNameWithoutExtension(fullPath);
            var ns = CommandUtilities.GetString(args, "namespace");
            var body = string.IsNullOrWhiteSpace(ns)
                ? $"using UnityEngine;\n\npublic class {className} : MonoBehaviour\n{{\n}}\n"
                : $"using UnityEngine;\n\nnamespace {ns}\n{{\n    public class {className} : MonoBehaviour\n    {{\n    }}\n}}\n";
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, body);
            AssetDatabase.Refresh();
            return Task.FromResult(McpBridge.Success(AssetFindCommand.SummarizeAsset(projectPath)));
        }
    }

    internal sealed class ScriptApplyTextEditsCommand : IMcpCommand
    {
        public string Name => "script.applyTextEdits";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (!CommandUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                return Task.FromResult(McpBridge.Error(error));
            if (!File.Exists(fullPath)) return Task.FromResult(McpBridge.Error($"File '{projectPath}' was not found."));
            var text = File.ReadAllText(fullPath);
            var edits = CommandUtilities.GetArray(args, "edits")
                .Select(LitJson.AsObject)
                .Where(edit => edit != null)
                .Select(edit => (Start: LitJson.GetInt(edit!, "start", -1), Length: LitJson.GetInt(edit!, "length", 0), Text: CommandUtilities.GetString(edit!, "text")))
                .OrderByDescending(edit => edit.Start)
                .ToList();
            foreach (var edit in edits)
            {
                if (edit.Start < 0 || edit.Start > text.Length) return Task.FromResult(McpBridge.Error($"Invalid edit start {edit.Start}."));
                if (edit.Length < 0) return Task.FromResult(McpBridge.Error($"Invalid edit length {edit.Length}."));
                text = text.Remove(edit.Start, Math.Min(edit.Length, text.Length - edit.Start)).Insert(edit.Start, edit.Text);
            }
            File.WriteAllText(fullPath, text);
            if (LitJson.GetBool(args, "refresh", true)) AssetDatabase.Refresh();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("path", projectPath), ("editCount", edits.Count))));
        }
    }
}
