#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("components", "Component query, edit, and instance method operations for live Unity objects.")]
    public sealed class ComponentsTool
    {
        [McpFunction("List components on a GameObject.")]
        public Dictionary<string, object?> list(object target)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            var components = go.GetComponents<Component>().Select((component, index) => (object?)ToolUtilities.SummarizeComponent(component, index)).ToList();
            return McpData.Obj(("object", ToolUtilities.SummarizeGameObject(go, false)), ("components", components));
        }

        [McpFunction("Get component data.")]
        public Dictionary<string, object?> get(object target, string type = "", int index = -1)
        {
            var component = ResolveComponent(target, type, index, out var error);
            if (component == null) throw new InvalidOperationException(error);
            return McpData.Obj(
                ("summary", ToolUtilities.SummarizeComponent(component)),
                ("members", GetReadableMembers(component))
            );
        }

        internal static Component? ResolveComponent(object target, string typeName, int index, out string error)
        {
            error = string.Empty;
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null)
            {
                error = "Target GameObject was not found or is ambiguous.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                var components = go.GetComponents<Component>();
                if (index >= 0 && index < components.Length)
                    return components[index];
                error = "Argument 'type' or valid 'index' is required.";
                return null;
            }

            var type = ToolUtilities.FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                error = $"Component type '{typeName}' was not found.";
                return null;
            }

            var all = go.GetComponents(type);
            var requestedIndex = index < 0 ? 0 : index;
            if (requestedIndex >= 0 && requestedIndex < all.Length)
                return (Component)all[requestedIndex];

            error = $"Component '{typeName}' index {requestedIndex} was not found on '{go.name}'.";
            return null;
        }

        private static List<object?> GetReadableMembers(Component component)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            var type = component.GetType();
            var result = new List<object?>();

            foreach (var field in type.GetFields(flags).Where(f => !f.IsStatic))
            {
                object? value;
                try { value = field.GetValue(component); }
                catch { continue; }
                result.Add(McpData.Obj(("kind", "field"), ("name", field.Name), ("type", field.FieldType.FullName ?? field.FieldType.Name), ("value", ToolUtilities.ToJsonFriendly(value))));
            }

            foreach (var property in type.GetProperties(flags).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            {
                object? value;
                try { value = property.GetValue(component); }
                catch { continue; }
                result.Add(McpData.Obj(("kind", "property"), ("name", property.Name), ("type", property.PropertyType.FullName ?? property.PropertyType.Name), ("canWrite", property.CanWrite), ("value", ToolUtilities.ToJsonFriendly(value))));
            }

            return result;
        }

        [McpFunction("Add a component.")]
        public Dictionary<string, object?> add(object target, string type)
        {
            var go = ToolUtilities.ResolveGameObject(target);
            if (go == null) throw new InvalidOperationException("Target GameObject was not found or is ambiguous.");
            var componentType = ToolUtilities.FindType(type);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                throw new InvalidOperationException($"Component type '{type}' was not found.");
#if UNITY_EDITOR
            var component = !Application.isPlaying
                ? UnityEditor.Undo.AddComponent(go, componentType)
                : go.AddComponent(componentType);
#else
            var component = go.AddComponent(componentType);
#endif
            ToolUtilities.MarkDirty(go);
            return ToolUtilities.SummarizeComponent(component);
        }

        [McpFunction("Remove a component.")]
        public Dictionary<string, object?> remove(object target, string type = "", int index = -1, bool confirm = false)
        {
            if (!confirm)
                throw new InvalidOperationException("Component removal requires confirm: true.");
            var component = ResolveComponent(target, type, index, out var error);
            if (component == null) throw new InvalidOperationException(error);
            var summary = ToolUtilities.SummarizeComponent(component);
            ToolUtilities.DestroyObject(component);
            return McpData.Obj(("removed", summary));
        }

        [McpFunction("Set one component property or field.")]
        public Dictionary<string, object?> setProperty(object target, string type, string member, object? value, int index = 0, bool includeNonPublic = false, bool includeStatic = false, bool confirmDangerous = false)
        {
            var component = ResolveComponent(target, type, index, out var error);
            if (component == null) throw new InvalidOperationException(error);
            if (string.IsNullOrWhiteSpace(member))
                throw new InvalidOperationException("Argument 'member' is required.");
            if ((includeNonPublic || includeStatic) && !confirmDangerous)
                throw new InvalidOperationException("Setting non-public or static members requires confirmDangerous: true.");
            ToolUtilities.RecordUndo(component, "MCP Set Component Property");
            if (!ToolUtilities.TrySetMember(component, member, value, out error, includeNonPublic, includeStatic))
                throw new InvalidOperationException(error);
            ToolUtilities.MarkDirty(component);
            return McpData.Obj(("component", ToolUtilities.SummarizeComponent(component)), ("member", member), ("value", ToolUtilities.ToJsonFriendly(value)));
        }

        [McpFunction("Set multiple component properties or fields.")]
        public Dictionary<string, object?> setProperties(object target, string type, object values, int index = 0, bool includeNonPublic = false, bool includeStatic = false, bool confirmDangerous = false)
        {
            var component = ResolveComponent(target, type, index, out var error);
            if (component == null) throw new InvalidOperationException(error);
            if ((includeNonPublic || includeStatic) && !confirmDangerous)
                throw new InvalidOperationException("Setting non-public or static members requires confirmDangerous: true.");

            var valueMap = McpData.AsObject(values);
            if (valueMap == null || valueMap.Count == 0)
                throw new InvalidOperationException("Argument 'values' must contain at least one member update.");

            ToolUtilities.RecordUndo(component, "MCP Set Component Properties");
            var results = new List<object?>();
            foreach (var pair in valueMap)
            {
                if (!ToolUtilities.TrySetMember(component, pair.Key, pair.Value, out error, includeNonPublic, includeStatic))
                    throw new InvalidOperationException(error);
                results.Add(McpData.Obj(
                    ("member", pair.Key),
                    ("value", ToolUtilities.ToJsonFriendly(pair.Value))
                ));
            }

            ToolUtilities.MarkDirty(component);
            return McpData.Obj(
                ("component", ToolUtilities.SummarizeComponent(component)),
                ("count", results.Count),
                ("changes", results)
            );
        }

        [McpFunction("Call a component instance method by type name, method name, and positional args.")]
        public Dictionary<string, object?> callMethod(object target, string type, string method, object? args = null, int index = 0, bool includeNonPublic = false, bool confirmDangerous = false)
        {
            var component = ResolveComponent(target, type, index, out var error);
            if (component == null) throw new InvalidOperationException(error);
            if (string.IsNullOrWhiteSpace(method))
                throw new InvalidOperationException("Argument 'method' is required.");
            if (includeNonPublic && !confirmDangerous)
                throw new InvalidOperationException("Calling non-public instance methods requires confirmDangerous: true.");

            var values = McpData.AsArray(args) ?? new List<object?>();
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;

            var candidates = component.GetType()
                .GetMethods(flags)
                .Where(candidate => candidate.Name == method && !candidate.IsSpecialName && candidate.GetParameters().Length == values.Count)
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    var parameters = candidate.GetParameters();
                    var converted = new object?[parameters.Length];
                    for (var i = 0; i < parameters.Length; i++)
                        converted[i] = ToolUtilities.ConvertToType(values[i], parameters[i].ParameterType);

                    var result = candidate.Invoke(component, converted);
                    ToolUtilities.MarkDirty(component);
                    return McpData.Obj(
                        ("component", ToolUtilities.SummarizeComponent(component)),
                        ("method", ReflectionTool.FormatMethod(candidate)),
                        ("result", ToolUtilities.ToJsonFriendly(result))
                    );
                }
                catch
                {
                    // Try the next overload.
                }
            }

            throw new InvalidOperationException($"No callable instance overload '{method}' with {values.Count} argument(s) was found on '{component.GetType().FullName}'.");
        }

        [McpFunction("List available component types.")]
        public Dictionary<string, object?> listTypes(string query = "", int limit = 200)
        {
            limit = Math.Max(1, limit);
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(ToolUtilities.GetTypesSafe)
                .Where(type => type is { IsAbstract: false } && typeof(Component).IsAssignableFrom(type))
                .Where(type => string.IsNullOrWhiteSpace(query) || (type.FullName ?? type.Name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
                .Take(limit)
                .Select(type => (object?)McpData.Obj(
                    ("fullName", type.FullName ?? type.Name),
                    ("name", type.Name),
                    ("assembly", type.Assembly.GetName().Name ?? string.Empty)))
                .ToList();
            return McpData.Obj(("count", types.Count), ("types", types));
        }
    }
}
