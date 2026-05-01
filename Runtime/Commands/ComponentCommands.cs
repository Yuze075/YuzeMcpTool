#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    internal sealed class ComponentListCommand : IMcpCommand
    {
        public string Name => "component.list";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var components = go.GetComponents<Component>().Select((component, index) => (object?)CommandUtilities.SummarizeComponent(component, index)).ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("object", CommandUtilities.SummarizeGameObject(go, false)), ("components", components))));
        }
    }

    internal sealed class ComponentGetCommand : IMcpCommand
    {
        public string Name => "component.get";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var component = ResolveComponent(args, out var error);
            if (component == null) return Task.FromResult(McpBridge.Error(error));
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("summary", CommandUtilities.SummarizeComponent(component)),
                ("members", GetReadableMembers(component))
            )));
        }

        internal static Component? ResolveComponent(Dictionary<string, object?> args, out string error)
        {
            error = string.Empty;
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null)
            {
                error = "Target GameObject was not found or is ambiguous.";
                return null;
            }

            var typeName = CommandUtilities.GetString(args, "type");
            if (string.IsNullOrWhiteSpace(typeName))
            {
                var index = LitJson.GetInt(args, "index", -1);
                var components = go.GetComponents<Component>();
                if (index >= 0 && index < components.Length)
                    return components[index];
                error = "Argument 'type' or valid 'index' is required.";
                return null;
            }

            var type = CommandUtilities.FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                error = $"Component type '{typeName}' was not found.";
                return null;
            }

            var all = go.GetComponents(type);
            var requestedIndex = LitJson.GetInt(args, "componentIndex", 0);
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
                result.Add(LitJson.Obj(("kind", "field"), ("name", field.Name), ("type", field.FieldType.FullName ?? field.FieldType.Name), ("value", CommandUtilities.ToJsonFriendly(value))));
            }

            foreach (var property in type.GetProperties(flags).Where(p => p.CanRead && p.GetIndexParameters().Length == 0))
            {
                object? value;
                try { value = property.GetValue(component); }
                catch { continue; }
                result.Add(LitJson.Obj(("kind", "property"), ("name", property.Name), ("type", property.PropertyType.FullName ?? property.PropertyType.Name), ("canWrite", property.CanWrite), ("value", CommandUtilities.ToJsonFriendly(value))));
            }

            return result;
        }
    }

    internal sealed class ComponentAddCommand : IMcpCommand
    {
        public string Name => "component.add";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var go = CommandUtilities.ResolveGameObject(args);
            if (go == null) return Task.FromResult(McpBridge.Error("Target GameObject was not found or is ambiguous."));
            var typeName = CommandUtilities.GetString(args, "type");
            var type = CommandUtilities.FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                return Task.FromResult(McpBridge.Error($"Component type '{typeName}' was not found."));
            var component = go.AddComponent(type);
            CommandUtilities.RecordUndo(go, "MCP Add Component");
            CommandUtilities.MarkDirty(go);
            return Task.FromResult(McpBridge.Success(CommandUtilities.SummarizeComponent(component)));
        }
    }

    internal sealed class ComponentRemoveCommand : IMcpCommand
    {
        public string Name => "component.remove";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false))
                return Task.FromResult(McpBridge.Error("Component removal requires confirm: true."));
            var component = ComponentGetCommand.ResolveComponent(args, out var error);
            if (component == null) return Task.FromResult(McpBridge.Error(error));
            var summary = CommandUtilities.SummarizeComponent(component);
            CommandUtilities.DestroyObject(component);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("removed", summary))));
        }
    }

    internal sealed class ComponentSetPropertyCommand : IMcpCommand
    {
        public string Name => "component.setProperty";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var component = ComponentGetCommand.ResolveComponent(args, out var error);
            if (component == null) return Task.FromResult(McpBridge.Error(error));
            var member = CommandUtilities.GetString(args, "member");
            if (string.IsNullOrWhiteSpace(member))
                return Task.FromResult(McpBridge.Error("Argument 'member' is required."));
            var includeNonPublic = LitJson.GetBool(args, "includeNonPublic", false);
            var includeStatic = LitJson.GetBool(args, "includeStatic", false);
            if ((includeNonPublic || includeStatic) && !LitJson.GetBool(args, "confirmDangerous", false))
                return Task.FromResult(McpBridge.Error("Setting non-public or static members requires confirmDangerous: true."));
            args.TryGetValue("value", out var value);
            CommandUtilities.RecordUndo(component, "MCP Set Component Property");
            if (!CommandUtilities.TrySetMember(component, member, value, out error, includeNonPublic, includeStatic))
                return Task.FromResult(McpBridge.Error(error));
            CommandUtilities.MarkDirty(component);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("component", CommandUtilities.SummarizeComponent(component)), ("member", member), ("value", CommandUtilities.ToJsonFriendly(value)))));
        }
    }

    internal sealed class ComponentSetPropertiesCommand : IMcpCommand
    {
        public string Name => "component.setProperties";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var component = ComponentGetCommand.ResolveComponent(args, out var error);
            if (component == null) return Task.FromResult(McpBridge.Error(error));

            var includeNonPublic = LitJson.GetBool(args, "includeNonPublic", false);
            var includeStatic = LitJson.GetBool(args, "includeStatic", false);
            if ((includeNonPublic || includeStatic) && !LitJson.GetBool(args, "confirmDangerous", false))
                return Task.FromResult(McpBridge.Error("Setting non-public or static members requires confirmDangerous: true."));

            var changes = new List<(string Member, object? Value)>();
            if (LitJson.AsObject(args.TryGetValue("values", out var rawValues) ? rawValues : null) is { } values)
            {
                foreach (var pair in values)
                    changes.Add((pair.Key, pair.Value));
            }

            foreach (var item in CommandUtilities.GetArray(args, "changes"))
            {
                var change = LitJson.AsObject(item);
                if (change == null) continue;
                var member = CommandUtilities.GetString(change, "member");
                if (string.IsNullOrWhiteSpace(member)) continue;
                change.TryGetValue("value", out var value);
                changes.Add((member, value));
            }

            if (changes.Count == 0)
                return Task.FromResult(McpBridge.Error("Argument 'values' or 'changes' must contain at least one member update."));

            CommandUtilities.RecordUndo(component, "MCP Set Component Properties");
            var results = new List<object?>();
            foreach (var change in changes)
            {
                if (!CommandUtilities.TrySetMember(component, change.Member, change.Value, out error, includeNonPublic, includeStatic))
                    return Task.FromResult(McpBridge.Error(error));
                results.Add(LitJson.Obj(
                    ("member", change.Member),
                    ("value", CommandUtilities.ToJsonFriendly(change.Value))
                ));
            }

            CommandUtilities.MarkDirty(component);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("component", CommandUtilities.SummarizeComponent(component)),
                ("count", results.Count),
                ("changes", results)
            )));
        }
    }

    internal sealed class ComponentCallMethodCommand : IMcpCommand
    {
        public string Name => "component.callMethod";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var component = ComponentGetCommand.ResolveComponent(args, out var error);
            if (component == null) return Task.FromResult(McpBridge.Error(error));

            var methodName = CommandUtilities.GetString(args, "method");
            if (string.IsNullOrWhiteSpace(methodName))
                return Task.FromResult(McpBridge.Error("Argument 'method' is required."));

            var includeNonPublic = LitJson.GetBool(args, "includeNonPublic", false);
            if (includeNonPublic && !LitJson.GetBool(args, "confirmDangerous", false))
                return Task.FromResult(McpBridge.Error("Calling non-public instance methods requires confirmDangerous: true."));

            var values = CommandUtilities.GetArray(args, "args");
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;

            var candidates = component.GetType()
                .GetMethods(flags)
                .Where(method => method.Name == methodName && !method.IsSpecialName && method.GetParameters().Length == values.Count)
                .ToList();

            foreach (var method in candidates)
            {
                try
                {
                    var parameters = method.GetParameters();
                    var converted = new object?[parameters.Length];
                    for (var i = 0; i < parameters.Length; i++)
                        converted[i] = CommandUtilities.ConvertToType(values[i], parameters[i].ParameterType);

                    var result = method.Invoke(component, converted);
                    CommandUtilities.MarkDirty(component);
                    return Task.FromResult(McpBridge.Success(LitJson.Obj(
                        ("component", CommandUtilities.SummarizeComponent(component)),
                        ("method", ReflectionCommandUtility.FormatMethod(method)),
                        ("result", CommandUtilities.ToJsonFriendly(result))
                    )));
                }
                catch
                {
                    // Try the next overload.
                }
            }

            return Task.FromResult(McpBridge.Error($"No callable instance overload '{methodName}' with {values.Count} argument(s) was found on '{component.GetType().FullName}'."));
        }
    }

    internal sealed class ComponentListTypesCommand : IMcpCommand
    {
        public string Name => "component.listTypes";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var query = CommandUtilities.GetString(args, "query");
            var limit = Math.Max(1, LitJson.GetInt(args, "limit", 200));
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(ReflectionCommandUtility.GetTypesSafe)
                .Where(type => type is { IsAbstract: false } && typeof(Component).IsAssignableFrom(type))
                .Where(type => string.IsNullOrWhiteSpace(query) || (type.FullName ?? type.Name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(type => type.FullName ?? type.Name, StringComparer.Ordinal)
                .Take(limit)
                .Select(type => (object?)LitJson.Obj(("fullName", type.FullName), ("name", type.Name), ("assembly", type.Assembly.GetName().Name)))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", types.Count), ("types", types))));
        }
    }
}
