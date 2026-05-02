#nullable enable
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("objects", "Scene GameObject, hierarchy, and Transform query/edit operations.")]
    public sealed class ObjectsTool
    {
        [McpFunction("Find scene objects by value, by?, includeInactive?, limit?.")]
        public List<GameObject> find(string value, string by = "name", bool includeInactive = true, int limit = 100)
        {
            return ToolUtilities.FindGameObjects(by, value, includeInactive, Math.Max(1, limit));
        }

        [McpFunction("Find one scene object by value, by?, includeInactive?.")]
        public GameObject? findOne(string value, string by = "name", bool includeInactive = true)
        {
            return find(value, by, includeInactive, 2).FirstOrDefault();
        }

        [McpFunction("Get one object summary.")]
        public GameObject get(object target)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            return go;
        }

        [McpFunction("Create a GameObject.")]
        public Dictionary<string, object?> create(string name = "GameObject", string primitive = "", object? parent = null, object? localPosition = null, object? position = null, object? localScale = null)
        {
            GameObject go;
            if (string.IsNullOrWhiteSpace(primitive) || primitive.Equals("empty", StringComparison.OrdinalIgnoreCase))
                go = new GameObject(name);
            else if (Enum.TryParse<PrimitiveType>(primitive, true, out var primitiveType))
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }
            else
                throw new InvalidOperationException($"Unknown primitive type '{primitive}'.");
            ToolUtilities.RegisterCreatedObjectUndo(go, "MCP Create GameObject");

            if (parent != null)
            {
                var parentObject = ToolUtilities.ResolveGameObject(parent);
                if (parentObject == null)
                {
                    ToolUtilities.DestroyObject(go);
                    throw new InvalidOperationException("Parent GameObject was not found or is ambiguous.");
                }
                ToolUtilities.RecordUndo(go.transform, "MCP Set Parent");
                go.transform.SetParent(parentObject.transform, false);
            }

            ToolUtilities.RecordUndo(go.transform, "MCP Set Transform");
            if (localPosition != null) go.transform.localPosition = ToolUtilities.ToVector3(localPosition, go.transform.localPosition);
            if (position != null) go.transform.position = ToolUtilities.ToVector3(position, go.transform.position);
            if (localScale != null) go.transform.localScale = ToolUtilities.ToVector3(localScale, go.transform.localScale);
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeGameObject(go);
        }

        [McpFunction("Destroy a GameObject.")]
        public Dictionary<string, object?> destroy(object target, bool confirm = false)
        {
            if (!confirm)
                throw new InvalidOperationException("Destroy requires confirm: true.");

            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            var summary = ToolUtilities.SummarizeGameObject(go, false);
            ToolUtilities.DestroyObject(go);
            return McpData.Obj(("destroyed", summary));
        }

        [McpFunction("Duplicate a GameObject.")]
        public Dictionary<string, object?> duplicate(object target, string name = "")
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            var clone = UnityEngine.Object.Instantiate(go, go.transform.parent);
            clone.name = string.IsNullOrWhiteSpace(name) ? go.name + " Copy" : name;
            ToolUtilities.RegisterCreatedObjectUndo(clone, "MCP Duplicate GameObject");
            ToolUtilities.MarkDirty(clone);
            return ToolUtilities.SummarizeGameObject(clone);
        }

        [McpFunction("Change parent.")]
        public Dictionary<string, object?> setParent(object target, object? parent = null, bool worldPositionStays = true)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            Transform? parentTransform = null;
            if (parent != null)
            {
                var parentObject = ToolUtilities.ResolveGameObject(parent);
                if (parentObject == null) throw new InvalidOperationException("Parent GameObject was not found or is ambiguous.");
                parentTransform = parentObject.transform;
            }
            ToolUtilities.RecordUndo(go.transform, "MCP Set Parent");
            go.transform.SetParent(parentTransform, worldPositionStays);
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeGameObject(go);
        }

        [McpFunction("Set transform values.")]
        public Dictionary<string, object?> setTransform(object target, object? position = null, object? localPosition = null, object? rotationEuler = null, object? localRotationEuler = null, object? localScale = null)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            ToolUtilities.RecordUndo(go.transform, "MCP Set Transform");
            if (position != null) go.transform.position = ToolUtilities.ToVector3(position, go.transform.position);
            if (localPosition != null) go.transform.localPosition = ToolUtilities.ToVector3(localPosition, go.transform.localPosition);
            if (rotationEuler != null) go.transform.eulerAngles = ToolUtilities.ToVector3(rotationEuler, go.transform.eulerAngles);
            if (localRotationEuler != null) go.transform.localEulerAngles = ToolUtilities.ToVector3(localRotationEuler, go.transform.localEulerAngles);
            if (localScale != null) go.transform.localScale = ToolUtilities.ToVector3(localScale, go.transform.localScale);
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeGameObject(go);
        }

        [McpFunction("Set active state.")]
        public Dictionary<string, object?> setActive(object target, bool active)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            ToolUtilities.RecordUndo(go, "MCP Set Active");
            go.SetActive(active);
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeGameObject(go);
        }

        [McpFunction("Set name, layer, or tag.")]
        public Dictionary<string, object?> setNameLayerTag(object target, string name = "", int layer = int.MinValue, string tag = "")
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            ToolUtilities.RecordUndo(go, "MCP Set Name Layer Tag");
            if (!string.IsNullOrEmpty(name)) go.name = name;
            if (!string.IsNullOrEmpty(tag)) go.tag = tag;
            if (layer != int.MinValue) go.layer = layer;
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeGameObject(go);
        }
    }
}
