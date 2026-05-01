#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    internal sealed class ObjectFindCommand : IMcpCommand
    {
        public string Name => "object.find";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var by = CommandUtilities.GetString(args, "by", "name");
            var value = CommandUtilities.GetString(args, "value");
            var includeInactive = LitJson.GetBool(args, "includeInactive", true);
            var limit = Math.Max(1, LitJson.GetInt(args, "limit", 100));
            var results = CommandUtilities.FindGameObjects(by, value, includeInactive, limit)
                .Select(go => (object?)CommandUtilities.SummarizeGameObject(go))
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", results.Count), ("items", results))));
        }
    }

    internal sealed class ObjectGetCommand : IMcpCommand
    {
        public string Name => "object.get";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }

    internal sealed class ObjectCreateCommand : IMcpCommand
    {
        public string Name => "object.create";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var name = CommandUtilities.GetString(args, "name", "GameObject");
            var primitive = CommandUtilities.GetString(args, "primitive");
            GameObject go;
            if (string.IsNullOrWhiteSpace(primitive) || primitive.Equals("empty", StringComparison.OrdinalIgnoreCase))
                go = new GameObject(name);
            else if (Enum.TryParse<PrimitiveType>(primitive, true, out var primitiveType))
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }
            else
                return Task.FromResult(McpBridge.Error($"Unknown primitive type '{primitive}'."));
            CommandUtilities.RegisterCreatedObjectUndo(go, "MCP Create GameObject");

            if (args.TryGetValue("parent", out var parentValue))
            {
                var parent = CommandUtilities.ResolveGameObject(parentValue);
                if (parent == null)
                {
                    CommandUtilities.DestroyObject(go);
                    return Task.FromResult(McpBridge.Error("Parent GameObject was not found or is ambiguous."));
                }
                CommandUtilities.RecordUndo(go.transform, "MCP Set Parent");
                go.transform.SetParent(parent.transform, false);
            }

            CommandUtilities.RecordUndo(go.transform, "MCP Set Transform");
            go.transform.localPosition = CommandUtilities.GetVector3(args, "localPosition", go.transform.localPosition);
            go.transform.position = CommandUtilities.GetVector3(args, "position", go.transform.position);
            go.transform.localScale = CommandUtilities.GetVector3(args, "localScale", go.transform.localScale);
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }

    internal sealed class ObjectDestroyCommand : IMcpCommand
    {
        public string Name => "object.destroy";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Destroy requires confirm: true."));

            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var summary = CommandUtilities.SummarizeGameObject(go, false);
            CommandUtilities.DestroyObject(go);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("destroyed", summary))));
        }
    }

    internal sealed class ObjectDuplicateCommand : IMcpCommand
    {
        public string Name => "object.duplicate";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var clone = UnityEngine.Object.Instantiate(go, go.transform.parent);
            clone.name = CommandUtilities.GetString(args, "name", go.name + " Copy");
            CommandUtilities.RegisterCreatedObjectUndo(clone, "MCP Duplicate GameObject");
            CommandUtilities.MarkDirty(clone);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(clone)));
        }
    }

    internal sealed class ObjectSetParentCommand : IMcpCommand
    {
        public string Name => "object.setParent";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var worldPositionStays = LitJson.GetBool(args, "worldPositionStays", true);
            Transform? parentTransform = null;
            if (args.TryGetValue("parent", out var parentValue) && parentValue != null)
            {
                var parent = CommandUtilities.ResolveGameObject(parentValue);
                if (parent == null) return Task.FromResult(McpBridge.Error("Parent GameObject was not found or is ambiguous."));
                parentTransform = parent.transform;
            }
            CommandUtilities.RecordUndo(go.transform, "MCP Set Parent");
            go.transform.SetParent(parentTransform, worldPositionStays);
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }

    internal sealed class ObjectSetTransformCommand : IMcpCommand
    {
        public string Name => "object.setTransform";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            CommandUtilities.RecordUndo(go.transform, "MCP Set Transform");
            if (args.ContainsKey("position")) go.transform.position = CommandUtilities.GetVector3(args, "position", go.transform.position);
            if (args.ContainsKey("localPosition")) go.transform.localPosition = CommandUtilities.GetVector3(args, "localPosition", go.transform.localPosition);
            if (args.ContainsKey("rotationEuler")) go.transform.eulerAngles = CommandUtilities.GetVector3(args, "rotationEuler", go.transform.eulerAngles);
            if (args.ContainsKey("localRotationEuler")) go.transform.localEulerAngles = CommandUtilities.GetVector3(args, "localRotationEuler", go.transform.localEulerAngles);
            if (args.ContainsKey("localScale")) go.transform.localScale = CommandUtilities.GetVector3(args, "localScale", go.transform.localScale);
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }

    internal sealed class ObjectSetActiveCommand : IMcpCommand
    {
        public string Name => "object.setActive";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            CommandUtilities.RecordUndo(go, "MCP Set Active");
            go.SetActive(LitJson.GetBool(args, "active", go.activeSelf));
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }

    internal sealed class ObjectSetNameLayerTagCommand : IMcpCommand
    {
        public string Name => "object.setNameLayerTag";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            CommandUtilities.RecordUndo(go, "MCP Set Name Layer Tag");
            var name = LitJson.GetString(args, "name");
            if (!string.IsNullOrEmpty(name)) go.name = name!;
            var tag = LitJson.GetString(args, "tag");
            if (!string.IsNullOrEmpty(tag)) go.tag = tag!;
            if (args.ContainsKey("layer")) go.layer = LitJson.GetInt(args, "layer", go.layer);
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeGameObject(go)));
        }
    }
}
