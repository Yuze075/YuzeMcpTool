#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("serialized", "SerializedObject and Inspector property reads/writes.")]
    public sealed class SerializedTool
    {
        [McpFunction("Read serialized properties.")]
        public Dictionary<string, object?> get(object target, string propertyPath = "")
        {
            var obj = ResolveUnityObject(target);
            if (obj == null) throw new InvalidOperationException("Target UnityEngine.Object was not found.");
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
                return McpData.Obj(("target", SummarizeObject(obj)), ("properties", props));
            }

            var prop = serialized.FindProperty(propertyPath);
            if (prop == null) throw new InvalidOperationException($"Serialized property '{propertyPath}' was not found.");
            return SummarizeProperty(prop);
        }

        internal static UnityEngine.Object? ResolveUnityObject(object? target)
        {
            if (target is UnityEngine.Object unityObject) return unityObject;
            if (target is string path) return AssetDatabase.LoadMainAssetAtPath(path) ?? ToolUtilities.ResolveGameObject(path);
            if (target is int id) return EditorUtility.InstanceIDToObject(id);
            if (target is long longId) return EditorUtility.InstanceIDToObject(checked((int)longId));
            if (McpData.AsObject(target) is { } selector)
            {
                var assetPath = McpData.GetString(selector, "assetPath");
                if (!string.IsNullOrWhiteSpace(assetPath)) return AssetDatabase.LoadMainAssetAtPath(assetPath);
                var guid = McpData.GetString(selector, "guid");
                if (!string.IsNullOrWhiteSpace(guid)) return AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                var instanceId = McpData.GetInt(selector, "instanceId", 0);
                if (instanceId != 0) return EditorUtility.InstanceIDToObject(instanceId);
                return ToolUtilities.ResolveGameObject(selector);
            }
            return ToolUtilities.ResolveGameObject(target);
        }

        internal static Dictionary<string, object?> SummarizeObject(UnityEngine.Object obj) =>
            McpData.Obj(("name", obj.name), ("type", obj.GetType().FullName ?? obj.GetType().Name), ("instanceId", obj.GetInstanceID()), ("assetPath", AssetDatabase.GetAssetPath(obj)));

        internal static Dictionary<string, object?> SummarizeProperty(SerializedProperty property)
        {
            return McpData.Obj(
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
                SerializedPropertyType.Color => ToolUtilities.ColorToObject(property.colorValue),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue != null ? SummarizeObject(property.objectReferenceValue) : null,
                SerializedPropertyType.Enum => property.enumDisplayNames.Length > property.enumValueIndex ? property.enumDisplayNames[property.enumValueIndex] : property.enumValueIndex,
                SerializedPropertyType.Vector2 => ToolUtilities.Vector2ToObject(property.vector2Value),
                SerializedPropertyType.Vector3 => ToolUtilities.Vector3ToObject(property.vector3Value),
                SerializedPropertyType.Vector2Int => McpData.Obj(("x", property.vector2IntValue.x), ("y", property.vector2IntValue.y)),
                SerializedPropertyType.Vector3Int => McpData.Obj(("x", property.vector3IntValue.x), ("y", property.vector3IntValue.y), ("z", property.vector3IntValue.z)),
                SerializedPropertyType.Rect => McpData.Obj(("x", property.rectValue.x), ("y", property.rectValue.y), ("width", property.rectValue.width), ("height", property.rectValue.height)),
                SerializedPropertyType.Bounds => McpData.Obj(("center", ToolUtilities.Vector3ToObject(property.boundsValue.center)), ("size", ToolUtilities.Vector3ToObject(property.boundsValue.size))),
                _ => property.hasVisibleChildren ? "(children)" : property.stringValue
            };
        }

        [McpFunction("Set one serialized property.")]
        public Dictionary<string, object?> set(object target, string propertyPath, object? value, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Serialized property writes require confirm: true.");
            var obj = ResolveUnityObject(target);
            if (obj == null) throw new InvalidOperationException("Target UnityEngine.Object was not found.");
            var serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null) throw new InvalidOperationException($"Serialized property '{propertyPath}' was not found.");
            ToolUtilities.RecordUndo(obj, "MCP Set Serialized Property");
            SetPropertyValue(prop, value);
            serialized.ApplyModifiedProperties();
            ToolUtilities.MarkDirty(obj);
            return SummarizeProperty(prop);
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
                    property.floatValue = ToolUtilities.ToFloat(value, property.floatValue);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = Convert.ToString(value) ?? string.Empty;
                    break;
                case SerializedPropertyType.Color:
                    if (McpData.AsObject(value) is { } color)
                    {
                        property.colorValue = new Color(
                            McpData.GetFloat(color, "r", property.colorValue.r),
                            McpData.GetFloat(color, "g", property.colorValue.g),
                            McpData.GetFloat(color, "b", property.colorValue.b),
                            McpData.GetFloat(color, "a", property.colorValue.a));
                    }
                    break;
                case SerializedPropertyType.Vector2:
                    var vector = ToolUtilities.ToVector3(value, property.vector2Value);
                    property.vector2Value = new Vector2(vector.x, vector.y);
                    break;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = ToolUtilities.ToVector3(value, property.vector3Value);
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
                    var vector2Int = ToolUtilities.ToVector3(value, new Vector3(property.vector2IntValue.x, property.vector2IntValue.y, 0));
                    property.vector2IntValue = new Vector2Int(Mathf.RoundToInt(vector2Int.x), Mathf.RoundToInt(vector2Int.y));
                    break;
                case SerializedPropertyType.Vector3Int:
                    var vector3Int = ToolUtilities.ToVector3(value, new Vector3(property.vector3IntValue.x, property.vector3IntValue.y, property.vector3IntValue.z));
                    property.vector3IntValue = new Vector3Int(Mathf.RoundToInt(vector3Int.x), Mathf.RoundToInt(vector3Int.y), Mathf.RoundToInt(vector3Int.z));
                    break;
                case SerializedPropertyType.Rect:
                    if (McpData.AsObject(value) is { } rect)
                        property.rectValue = new Rect(
                            McpData.GetFloat(rect, "x", property.rectValue.x),
                            McpData.GetFloat(rect, "y", property.rectValue.y),
                            McpData.GetFloat(rect, "width", property.rectValue.width),
                            McpData.GetFloat(rect, "height", property.rectValue.height));
                    break;
                case SerializedPropertyType.Bounds:
                    if (McpData.AsObject(value) is { } bounds)
                        property.boundsValue = new Bounds(
                            ToolUtilities.ToVector3(bounds.TryGetValue("center", out var center) ? center : null, property.boundsValue.center),
                            ToolUtilities.ToVector3(bounds.TryGetValue("size", out var size) ? size : null, property.boundsValue.size));
                    break;
                default:
                    throw new InvalidOperationException($"SerializedPropertyType '{property.propertyType}' is not supported by serialized.set yet.");
            }
        }

        private static UnityEngine.Object? ResolveObjectReference(object? value)
        {
            if (value == null) return null;
            if (value is UnityEngine.Object obj) return obj;
            if (value is int id) return EditorUtility.InstanceIDToObject(id);
            if (value is long longId) return EditorUtility.InstanceIDToObject(checked((int)longId));
            if (value is string path) return AssetDatabase.LoadMainAssetAtPath(path) ?? ToolUtilities.ResolveGameObject(path);
            return ResolveUnityObject(value);
        }

        [McpFunction("Set multiple serialized properties.")]
        public Dictionary<string, object?> setMany(object target, object changes, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Serialized property writes require confirm: true.");
            var obj = ResolveUnityObject(target);
            if (obj == null) throw new InvalidOperationException("Target UnityEngine.Object was not found.");
            var changeList = (McpData.AsArray(changes) ?? new List<object?>())
                .Select(McpData.AsObject)
                .Where(change => change != null)
                .ToList();
            if (changeList.Count == 0) throw new InvalidOperationException("Argument 'changes' must contain at least one property update.");

            var serialized = new SerializedObject(obj);
            ToolUtilities.RecordUndo(obj, "MCP Set Serialized Properties");
            var results = new List<object?>();
            foreach (var change in changeList)
            {
                var propertyPath = ToolUtilities.GetString(change!, "propertyPath");
                var prop = serialized.FindProperty(propertyPath);
                if (prop == null) throw new InvalidOperationException($"Serialized property '{propertyPath}' was not found.");
                change!.TryGetValue("value", out var value);
                SetPropertyValue(prop, value);
                results.Add(SummarizeProperty(prop));
            }

            serialized.ApplyModifiedProperties();
            ToolUtilities.MarkDirty(obj);
            return McpData.Obj(("target", SummarizeObject(obj)), ("count", results.Count), ("properties", results));
        }

        [McpFunction("Resize array property.")]
        public Dictionary<string, object?> resizeArray(object target, string propertyPath, int size, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Serialized array writes require confirm: true.");
            if (size < 0) throw new InvalidOperationException("Argument 'size' must be zero or greater.");
            var prop = ResolveArrayProperty(target, propertyPath, out var serialized, out var obj);
            ToolUtilities.RecordUndo(obj, "MCP Resize Serialized Array");
            prop.arraySize = size;
            serialized.ApplyModifiedProperties();
            ToolUtilities.MarkDirty(obj);
            return SummarizeProperty(prop);
        }

        [McpFunction("Insert array element.")]
        public Dictionary<string, object?> insertArrayElement(object target, string propertyPath, int index = -1, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Serialized array writes require confirm: true.");
            var prop = ResolveArrayProperty(target, propertyPath, out var serialized, out var obj);
            if (index < 0 || index > prop.arraySize) index = prop.arraySize;
            ToolUtilities.RecordUndo(obj, "MCP Insert Serialized Array Element");
            prop.InsertArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            ToolUtilities.MarkDirty(obj);
            return SummarizeProperty(prop);
        }

        [McpFunction("Delete array element.")]
        public Dictionary<string, object?> deleteArrayElement(object target, string propertyPath, int index, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Serialized array writes require confirm: true.");
            var prop = ResolveArrayProperty(target, propertyPath, out var serialized, out var obj);
            if (index < 0 || index >= prop.arraySize) throw new InvalidOperationException($"Array index {index} is outside '{propertyPath}'.");
            ToolUtilities.RecordUndo(obj, "MCP Delete Serialized Array Element");
            prop.DeleteArrayElementAtIndex(index);
            serialized.ApplyModifiedProperties();
            ToolUtilities.MarkDirty(obj);
            return SummarizeProperty(prop);
        }

        private static SerializedProperty ResolveArrayProperty(object target, string propertyPath, out SerializedObject serialized, out UnityEngine.Object obj)
        {
            obj = ResolveUnityObject(target) ?? throw new InvalidOperationException("Target UnityEngine.Object was not found.");
            serialized = new SerializedObject(obj);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null || !prop.isArray) throw new InvalidOperationException($"Serialized array property '{propertyPath}' was not found.");
            return prop;
        }
    }
}
