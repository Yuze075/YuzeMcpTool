#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace YuzeToolkit
{
    internal sealed class EditorSetPlayModeCommand : IMcpCommand
    {
        public string Name => "editor.setPlayMode";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            EditorApplication.isPlaying = LitJson.GetBool(args, "isPlaying", EditorApplication.isPlaying);
            return Task.FromResult(McpBridge.Success(EditorStatusProvider.GetStateObject()));
        }
    }

    internal sealed class EditorSetPauseCommand : IMcpCommand
    {
        public string Name => "editor.setPause";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            EditorApplication.isPaused = LitJson.GetBool(args, "isPaused", EditorApplication.isPaused);
            return Task.FromResult(McpBridge.Success(EditorStatusProvider.GetStateObject()));
        }
    }

    internal sealed class EditorExecuteMenuItemCommand : IMcpCommand
    {
        public string Name => "editor.executeMenuItem";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            if (!path.StartsWith("YuzeToolkit/MCP/", StringComparison.Ordinal) && !LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Executing arbitrary menu items requires confirm: true."));
            var ok = EditorApplication.ExecuteMenuItem(path);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("path", path), ("executed", ok))));
        }
    }

    internal sealed class SelectionGetCommand : IMcpCommand
    {
        public string Name => "selection.get";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var items = Selection.objects
                .Where(obj => obj != null)
                .Select(obj => (object?)LitJson.Obj(
                    ("name", obj.name),
                    ("type", obj.GetType().FullName ?? obj.GetType().Name),
                    ("instanceId", obj.GetInstanceID()),
                    ("assetPath", AssetDatabase.GetAssetPath(obj))))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("count", items.Count),
                ("activeInstanceId", Selection.activeObject != null ? Selection.activeObject.GetInstanceID() : 0),
                ("items", items))));
        }
    }

    internal sealed class SelectionSetCommand : IMcpCommand
    {
        public string Name => "selection.set";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var objects = new List<UnityEngine.Object>();
            foreach (var item in CommandUtilities.GetArray(args, "items"))
            {
                UnityEngine.Object? obj = null;
                if (item is int id) obj = EditorUtility.InstanceIDToObject(id);
                if (item is long longId) obj = EditorUtility.InstanceIDToObject(checked((int)longId));
                if (item is string path) obj = AssetDatabase.LoadMainAssetAtPath(path) ?? CommandUtilities.ResolveGameObject(path);
                if (LitJson.AsObject(item) is { } selector)
                {
                    var assetPath = LitJson.GetString(selector, "assetPath");
                    obj = !string.IsNullOrWhiteSpace(assetPath) ? AssetDatabase.LoadMainAssetAtPath(assetPath) : CommandUtilities.ResolveGameObject(selector);
                }
                if (obj != null) objects.Add(obj);
            }
            Selection.objects = objects.ToArray();
            return new SelectionGetCommand().ExecuteAsync(context);
        }
    }

    internal sealed class AssetFindCommand : IMcpCommand
    {
        public string Name => "asset.find";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var filter = CommandUtilities.GetString(args, "filter");
            if (string.IsNullOrWhiteSpace(filter)) return Task.FromResult(McpBridge.Error("Argument 'filter' is required."));
            var folders = CommandUtilities.GetArray(args, "folders").OfType<string>().ToArray();
            var limit = LitJson.GetInt(args, "limit", 0);
            var guids = folders.Length > 0 ? AssetDatabase.FindAssets(filter, folders) : AssetDatabase.FindAssets(filter);
            var guidQuery = limit > 0 ? guids.Take(limit) : guids.AsEnumerable();
            var assets = guidQuery.Select(guid => (object?)SummarizeAsset(AssetDatabase.GUIDToAssetPath(guid), guid)).ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", assets.Count), ("assets", assets))));
        }

        internal static object SummarizeAsset(string path, string? guid = null)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return LitJson.Obj(
                ("guid", string.IsNullOrEmpty(guid) ? AssetDatabase.AssetPathToGUID(path) : guid),
                ("path", path),
                ("name", asset != null ? asset.name : Path.GetFileNameWithoutExtension(path)),
                ("type", asset != null ? asset.GetType().FullName ?? asset.GetType().Name : string.Empty),
                ("exists", asset != null || File.Exists(path) || Directory.Exists(path)));
        }
    }

    internal sealed class AssetGetInfoCommand : IMcpCommand
    {
        public string Name => "asset.getInfo";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var path = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            return Task.FromResult(McpBridge.Success(AssetFindCommand.SummarizeAsset(path)));
        }
    }

    internal sealed class AssetReadTextCommand : IMcpCommand
    {
        public string Name => "asset.readText";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var path = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            if (!CommandUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                return Task.FromResult(McpBridge.Error(error));
            if (!File.Exists(fullPath)) return Task.FromResult(McpBridge.Error($"File '{projectPath}' was not found."));
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("path", projectPath), ("text", File.ReadAllText(fullPath)))));
        }
    }

    internal sealed class AssetWriteTextCommand : IMcpCommand
    {
        public string Name => "asset.writeText";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            var text = CommandUtilities.GetString(args, "text");
            if (!CommandUtilities.TryResolveProjectPath(path, out var full, out var projectPath, out var error))
                return Task.FromResult(McpBridge.Error(error));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            if (LitJson.GetBool(args, "refresh", false)) AssetDatabase.Refresh();
            return Task.FromResult(McpBridge.Success(AssetFindCommand.SummarizeAsset(projectPath)));
        }
    }

    internal sealed class AssetCreateFolderCommand : IMcpCommand
    {
        public string Name => "asset.createFolder";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var parent = CommandUtilities.GetString(args, "parent", "Assets");
            var name = CommandUtilities.GetString(args, "name");
            if (string.IsNullOrWhiteSpace(name)) return Task.FromResult(McpBridge.Error("Argument 'name' is required."));
            var guid = AssetDatabase.CreateFolder(parent, name);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("guid", guid), ("path", AssetDatabase.GUIDToAssetPath(guid)))));
        }
    }

    internal sealed class AssetMoveCommand : IMcpCommand
    {
        public string Name => "asset.move";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false)) return Task.FromResult(McpBridge.Error("Asset move requires confirm: true."));
            var from = CommandUtilities.GetString(args, "from");
            var to = CommandUtilities.GetString(args, "to");
            var error = AssetDatabase.MoveAsset(from, to);
            return Task.FromResult(error.Length == 0 ? McpBridge.Success(AssetFindCommand.SummarizeAsset(to)) : McpBridge.Error(error));
        }
    }

    internal sealed class AssetCopyCommand : IMcpCommand
    {
        public string Name => "asset.copy";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var from = CommandUtilities.GetString(args, "from");
            var to = CommandUtilities.GetString(args, "to");
            var ok = AssetDatabase.CopyAsset(from, to);
            return Task.FromResult(ok ? McpBridge.Success(AssetFindCommand.SummarizeAsset(to)) : McpBridge.Error($"Failed to copy '{from}' to '{to}'."));
        }
    }

    internal sealed class AssetDeleteCommand : IMcpCommand
    {
        public string Name => "asset.delete";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false)) return Task.FromResult(McpBridge.Error("Asset delete requires confirm: true."));
            var path = CommandUtilities.GetString(args, "path");
            var ok = AssetDatabase.DeleteAsset(path);
            return Task.FromResult(ok ? McpBridge.Success(LitJson.Obj(("deleted", path))) : McpBridge.Error($"Failed to delete asset '{path}'."));
        }
    }

    internal sealed class AssetRefreshCommand : IMcpCommand
    {
        public string Name => "asset.refreshNow";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            AssetDatabase.Refresh();
            return Task.FromResult(McpBridge.Success(EditorCompilationMonitor.GetStateObject()));
        }
    }

    internal sealed class AssetDependenciesCommand : IMcpCommand
    {
        public string Name => "asset.getDependencies";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            var recursive = LitJson.GetBool(args, "recursive", true);
            var deps = AssetDatabase.GetDependencies(path, recursive).Select(p => (object?)AssetFindCommand.SummarizeAsset(p)).ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("path", path), ("count", deps.Count), ("dependencies", deps))));
        }
    }

    internal sealed class AssetFindReferencesCommand : IMcpCommand
    {
        public string Name => "asset.findReferences";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var target = CommandUtilities.GetString(args, "path");
            var folders = CommandUtilities.GetArray(args, "folders").OfType<string>().ToArray();
            var limit = LitJson.GetInt(args, "limit", 0);
            var scope = folders.Length > 0 ? folders : new[] { "Assets" };
            var results = new List<object?>();
            foreach (var guid in AssetDatabase.FindAssets("", scope))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == target) continue;
                if (AssetDatabase.GetDependencies(path, true).Contains(target))
                    results.Add(AssetFindCommand.SummarizeAsset(path, guid));
                if (limit > 0 && results.Count >= limit) break;
            }
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("target", target), ("count", results.Count), ("references", results))));
        }
    }
}
