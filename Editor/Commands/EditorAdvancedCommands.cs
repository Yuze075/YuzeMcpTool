#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    internal sealed class EditorExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["getState"] = new EditorStateCommand(),
            ["getCompilationState"] = new EditorCompilationStateCommand(),
            ["requestScriptCompilation"] = new EditorRequestScriptCompilationCommand(),
            ["scheduleAssetRefresh"] = new EditorRequestAssetRefreshCommand(),
            ["getCompilerMessages"] = new EditorCompilerMessagesCommand(),
            ["setPlayMode"] = new EditorSetPlayModeCommand(),
            ["setPause"] = new EditorSetPauseCommand(),
            ["executeMenuItem"] = new EditorExecuteMenuItemCommand(),
            ["selectionGet"] = new SelectionGetCommand(),
            ["selectionSet"] = new SelectionSetCommand(),
            ["screenshotGameView"] = new ScreenshotGameViewCommand(),
        };

        public string Name => "editor.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class AssetExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["find"] = new AssetFindCommand(),
            ["getInfo"] = new AssetGetInfoCommand(),
            ["readText"] = new AssetReadTextCommand(),
            ["writeText"] = new AssetWriteTextCommand(),
            ["createFolder"] = new AssetCreateFolderCommand(),
            ["move"] = new AssetMoveCommand(),
            ["copy"] = new AssetCopyCommand(),
            ["delete"] = new AssetDeleteCommand(),
            ["refreshNow"] = new AssetRefreshCommand(),
            ["getDependencies"] = new AssetDependenciesCommand(),
            ["findReferences"] = new AssetFindReferencesCommand(),
            ["scriptCreate"] = new ScriptCreateCommand(),
            ["scriptApplyTextEdits"] = new ScriptApplyTextEditsCommand(),
            ["materialCreate"] = new MaterialCreateCommand(),
        };

        public string Name => "asset.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class SceneExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["listOpen"] = new SceneListOpenCommand(),
            ["getHierarchy"] = new SceneGetHierarchyCommand(),
            ["open"] = new SceneOpenCommand(),
            ["create"] = new SceneCreateCommand(),
            ["save"] = new SceneSaveCommand(),
            ["saveAs"] = new SceneSaveAsCommand(),
            ["setActive"] = new SceneSetActiveCommand(),
        };

        public string Name => "scene.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class PrefabExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["instantiate"] = new PrefabInstantiateCommand(),
            ["createFromObject"] = new PrefabCreateFromObjectCommand(),
            ["createVariant"] = new PrefabCreateVariantCommand(),
            ["openStage"] = new PrefabOpenStageCommand(),
            ["closeStage"] = new PrefabCloseStageCommand(),
            ["saveStage"] = new PrefabSaveStageCommand(),
            ["getOverrides"] = new PrefabGetOverridesCommand(),
            ["applyOverrides"] = new PrefabApplyOverridesCommand(),
            ["revertOverrides"] = new PrefabRevertOverridesCommand(),
            ["unpack"] = new PrefabUnpackCommand(),
        };

        public string Name => "prefab.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class SerializedExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["get"] = new SerializedGetCommand(),
            ["set"] = new SerializedSetCommand(),
            ["setMany"] = new SerializedSetManyCommand(),
            ["resizeArray"] = new SerializedResizeArrayCommand(),
            ["insertArrayElement"] = new SerializedInsertArrayElementCommand(),
            ["deleteArrayElement"] = new SerializedDeleteArrayElementCommand(),
        };

        public string Name => "serialized.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class SerializedGetCommand : IMcpCommand
    {
        public string Name => "serialized.get";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var serialized = new SerializedObject(obj);
            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                var props = new List<object?>();
                var iterator = serialized.GetIterator();
                var enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    props.Add(SummarizeProperty(iterator));
                }
                return Task.FromResult(McpBridge.Success(LitJson.Obj(("target", SummarizeObject(obj)), ("properties", props))));
            }
            var prop = serialized.FindProperty(propertyPath);
            return Task.FromResult(prop != null ? McpBridge.Success(SummarizeProperty(prop)) : McpBridge.Error($"Serialized property '{propertyPath}' was not found."));
        }

        internal static UnityEngine.Object? ResolveUnityObject(Dictionary<string, object?> args)
        {
            var assetPath = CommandUtilities.GetString(args, "assetPath");
            if (!string.IsNullOrWhiteSpace(assetPath)) return AssetDatabase.LoadMainAssetAtPath(assetPath);
            var guid = CommandUtilities.GetString(args, "guid");
            if (!string.IsNullOrWhiteSpace(guid)) return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
            var instanceId = LitJson.GetInt(args, "instanceId", 0);
            if (instanceId != 0) return EditorUtility.InstanceIDToObject(instanceId);
            return CommandUtilities.ResolveGameObject(args);
        }

        internal static object SummarizeObject(UnityEngine.Object obj) =>
            LitJson.Obj(("name", obj.name), ("type", obj.GetType().FullName ?? obj.GetType().Name), ("instanceId", obj.GetInstanceID()), ("assetPath", AssetDatabase.GetAssetPath(obj)));

        internal static object SummarizeProperty(SerializedProperty property)
        {
            return LitJson.Obj(
                ("propertyPath", property.propertyPath),
                ("displayName", property.displayName),
                ("type", property.propertyType.ToString()),
                ("isArray", property.isArray),
                ("value", GetPropertyValue(property)));
        }

        private static object? GetPropertyValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => CommandUtilities.ColorToObject(property.colorValue),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue != null ? SummarizeObject(property.objectReferenceValue) : null,
                SerializedPropertyType.Enum => property.enumDisplayNames.Length > property.enumValueIndex ? property.enumDisplayNames[property.enumValueIndex] : property.enumValueIndex,
                SerializedPropertyType.Vector2 => CommandUtilities.Vector2ToObject(property.vector2Value),
                SerializedPropertyType.Vector3 => CommandUtilities.Vector3ToObject(property.vector3Value),
                SerializedPropertyType.Vector2Int => LitJson.Obj(("x", property.vector2IntValue.x), ("y", property.vector2IntValue.y)),
                SerializedPropertyType.Vector3Int => LitJson.Obj(("x", property.vector3IntValue.x), ("y", property.vector3IntValue.y), ("z", property.vector3IntValue.z)),
                SerializedPropertyType.Rect => LitJson.Obj(("x", property.rectValue.x), ("y", property.rectValue.y), ("width", property.rectValue.width), ("height", property.rectValue.height)),
                SerializedPropertyType.Bounds => LitJson.Obj(("center", CommandUtilities.Vector3ToObject(property.boundsValue.center)), ("size", CommandUtilities.Vector3ToObject(property.boundsValue.size))),
                _ => property.hasVisibleChildren ? "(children)" : property.stringValue
            };
        }
    }

    internal sealed class SerializedSetCommand : IMcpCommand
    {
        public string Name => "serialized.set";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = SerializedGetCommand.ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null) return Task.FromResult(McpBridge.Error($"Serialized property '{propertyPath}' was not found."));
            args.TryGetValue("value", out var value);
            CommandUtilities.RecordUndo(obj, "MCP Set Serialized Property");
            SetPropertyValue(prop, value);
            serialized.ApplyModifiedProperties();
            CommandUtilities.MarkDirty(obj);
            return Task.FromResult(McpBridge.Success(SerializedGetCommand.SummarizeProperty(prop)));
        }

        internal static void SetPropertyValue(SerializedProperty property, object? value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = value is int i ? i : Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = value is bool b ? b : Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = CommandUtilities.ToFloat(value, property.floatValue);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = Convert.ToString(value) ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    if (LitJson.AsObject(value) is { } color)
                    {
                        property.colorValue = new Color(
                            LitJson.GetFloat(color, "r", property.colorValue.r),
                            LitJson.GetFloat(color, "g", property.colorValue.g),
                            LitJson.GetFloat(color, "b", property.colorValue.b),
                            LitJson.GetFloat(color, "a", property.colorValue.a));
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var vector = CommandUtilities.ToVector3(value, property.vector2Value);
                    property.vector2Value = new Vector2(vector.x, vector.y);
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = CommandUtilities.ToVector3(value, property.vector3Value);
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = ResolveObjectReference(value);
                    break;
                case SerializedPropertyType.Enum:
                    var text = Convert.ToString(value);
                    var index = Array.IndexOf(property.enumDisplayNames, text);
                    property.enumValueIndex = index >= 0 ? index : Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Vector2Int:
                    var vector2Int = CommandUtilities.ToVector3(value, new Vector3(property.vector2IntValue.x, property.vector2IntValue.y, 0));
                    property.vector2IntValue = new Vector2Int(Mathf.RoundToInt(vector2Int.x), Mathf.RoundToInt(vector2Int.y));
                    break;
                case SerializedPropertyType.Vector3Int:
                    var vector3Int = CommandUtilities.ToVector3(value, new Vector3(property.vector3IntValue.x, property.vector3IntValue.y, property.vector3IntValue.z));
                    property.vector3IntValue = new Vector3Int(Mathf.RoundToInt(vector3Int.x), Mathf.RoundToInt(vector3Int.y), Mathf.RoundToInt(vector3Int.z));
                    break;
                case SerializedPropertyType.Rect:
                    if (LitJson.AsObject(value) is { } rect)
                        property.rectValue = new Rect(
                            LitJson.GetFloat(rect, "x", property.rectValue.x),
                            LitJson.GetFloat(rect, "y", property.rectValue.y),
                            LitJson.GetFloat(rect, "width", property.rectValue.width),
                            LitJson.GetFloat(rect, "height", property.rectValue.height));
                    break;
                case SerializedPropertyType.Bounds:
                    if (LitJson.AsObject(value) is { } bounds)
                        property.boundsValue = new Bounds(
                            CommandUtilities.ToVector3(bounds.TryGetValue("center", out var center) ? center : null, property.boundsValue.center),
                            CommandUtilities.ToVector3(bounds.TryGetValue("size", out var size) ? size : null, property.boundsValue.size));
                    break;
                default:
                    throw new InvalidOperationException($"SerializedPropertyType '{property.propertyType}' is not supported by serialized.set yet.");
            }
        }

        private static UnityEngine.Object? ResolveObjectReference(object? value)
        {
            if (value == null) return null;
            if (value is int id) return EditorUtility.InstanceIDToObject(id);
            if (value is long longId) return EditorUtility.InstanceIDToObject(checked((int)longId));
            if (value is string path) return AssetDatabase.LoadMainAssetAtPath(path) ?? CommandUtilities.ResolveGameObject(path);
            if (LitJson.AsObject(value) is { } selector)
            {
                var assetPath = LitJson.GetString(selector, "assetPath") ?? LitJson.GetString(selector, "path");
                if (!string.IsNullOrWhiteSpace(assetPath))
                    return AssetDatabase.LoadMainAssetAtPath(assetPath);
                var guid = LitJson.GetString(selector, "guid");
                if (!string.IsNullOrWhiteSpace(guid))
                    return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var instanceId = LitJson.GetInt(selector, "instanceId", 0);
                if (instanceId != 0)
                    return EditorUtility.InstanceIDToObject(instanceId);
                return CommandUtilities.ResolveGameObject(selector);
            }
            return null;
        }
    }

    internal sealed class SerializedSetManyCommand : IMcpCommand
    {
        public string Name => "serialized.setMany";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = SerializedGetCommand.ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var changes = CommandUtilities.GetArray(args, "changes")
                .Select(LitJson.AsObject)
                .Where(change => change != null)
                .ToList();
            if (changes.Count == 0) return Task.FromResult(McpBridge.Error("Argument 'changes' must contain at least one property update."));

            var serialized = new SerializedObject(obj);
            CommandUtilities.RecordUndo(obj, "MCP Set Serialized Properties");
            var results = new List<object?>();
            foreach (var change in changes)
            {
                var propertyPath = CommandUtilities.GetString(change!, "propertyPath");
                var prop = serialized.FindProperty(propertyPath);
                if (prop == null) return Task.FromResult(McpBridge.Error($"Serialized property '{propertyPath}' was not found."));
                change!.TryGetValue("value", out var value);
                SerializedSetCommand.SetPropertyValue(prop, value);
                results.Add(SerializedGetCommand.SummarizeProperty(prop));
            }

            serialized.ApplyModifiedProperties();
            CommandUtilities.MarkDirty(obj);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("target", SerializedGetCommand.SummarizeObject(obj)), ("count", results.Count), ("properties", results))));
        }
    }

    internal sealed class SerializedResizeArrayCommand : IMcpCommand
    {
        public string Name => "serialized.resizeArray";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = SerializedGetCommand.ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var size = LitJson.GetInt(args, "size", -1);
            if (size < 0) return Task.FromResult(McpBridge.Error("Argument 'size' must be zero or greater."));
            var serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null || !prop.isArray) return Task.FromResult(McpBridge.Error($"Serialized array property '{propertyPath}' was not found."));
            CommandUtilities.RecordUndo(obj, "MCP Resize Serialized Array");
            prop.arraySize = size;
            serialized.ApplyModifiedProperties();
            CommandUtilities.MarkDirty(obj);
            return Task.FromResult(McpBridge.Success(SerializedGetCommand.SummarizeProperty(prop)));
        }
    }

    internal sealed class SerializedInsertArrayElementCommand : IMcpCommand
    {
        public string Name => "serialized.insertArrayElement";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = SerializedGetCommand.ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var index = LitJson.GetInt(args, "index", -1);
            var serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null || !prop.isArray) return Task.FromResult(McpBridge.Error($"Serialized array property '{propertyPath}' was not found."));
            if (index < 0 || index > prop.arraySize) index = prop.arraySize;
            CommandUtilities.RecordUndo(obj, "MCP Insert Serialized Array Element");
            prop.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            CommandUtilities.MarkDirty(obj);
            return Task.FromResult(McpBridge.Success(SerializedGetCommand.SummarizeProperty(prop)));
        }
    }

    internal sealed class SerializedDeleteArrayElementCommand : IMcpCommand
    {
        public string Name => "serialized.deleteArrayElement";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var obj = SerializedGetCommand.ResolveUnityObject(args);
            if (obj == null) return Task.FromResult(McpBridge.Error("Target UnityEngine.Object was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var index = LitJson.GetInt(args, "index", -1);
            var serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null || !prop.isArray) return Task.FromResult(McpBridge.Error($"Serialized array property '{propertyPath}' was not found."));
            if (index < 0 || index >= prop.arraySize) return Task.FromResult(McpBridge.Error($"Array index {index} is outside '{propertyPath}'."));
            CommandUtilities.RecordUndo(obj, "MCP Delete Serialized Array Element");
            prop.DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            CommandUtilities.MarkDirty(obj);
            return Task.FromResult(McpBridge.Success(SerializedGetCommand.SummarizeProperty(prop)));
        }
    }

    internal sealed class BuildGetSettingsCommand : IMcpCommand
    {
        public string Name => "build.getSettings";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var scenes = EditorBuildSettings.scenes
                .Select(scene => (object?)LitJson.Obj(("path", scene.path), ("enabled", scene.enabled), ("guid", scene.guid.ToString())))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString()),
                ("selectedBuildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString()),
                ("scenes", scenes))));
        }
    }

    internal sealed class ProjectSettingsCommand : IMcpCommand
    {
        public string Name => "project.getSettings";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("productName", PlayerSettings.productName),
                ("companyName", PlayerSettings.companyName),
                ("applicationIdentifier", PlayerSettings.applicationIdentifier),
                ("tags", UnityEditorInternal.InternalEditorUtility.tags.Cast<object?>().ToList()),
                ("layers", UnityEditorInternal.InternalEditorUtility.layers.Cast<object?>().ToList()))));
        }
    }

    internal sealed class MaterialCreateCommand : IMcpCommand
    {
        public string Name => "material.create";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (!CommandUtilities.TryResolveProjectPath(path, out _, out var projectPath, out var pathError))
                return Task.FromResult(McpBridge.Error(pathError));
            var shaderName = CommandUtilities.GetString(args, "shader", "Universal Render Pipeline/2D/Sprite-Lit-Default");
            var shader = Shader.Find(shaderName) ?? Shader.Find("Sprites/Default");
            if (shader == null) return Task.FromResult(McpBridge.Error($"Shader '{shaderName}' was not found."));
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, projectPath);
            AssetDatabase.SaveAssets();
            return Task.FromResult(McpBridge.Success(AssetFindCommand.SummarizeAsset(projectPath)));
        }
    }

    internal sealed class ScreenshotGameViewCommand : IMcpCommand
    {
        public string Name => "screenshot.gameView";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path", "Temp/YuzeMcpTool-GameView.png");
            if (!CommandUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                return Task.FromResult(McpBridge.Error(error));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            ScreenCapture.CaptureScreenshot(fullPath);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("path", projectPath), ("fullPath", fullPath), ("message", "Screenshot capture was requested. The file may be written after the current frame."))));
        }
    }
}
