#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal static class CompactCommandDispatcher
    {
        public static Task<string> ExecuteAsync(
            McpCommandContext context,
            IReadOnlyDictionary<string, IMcpCommand> actions,
            string commandName)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var action = CommandUtilities.GetString(args, "action");
            if (string.IsNullOrWhiteSpace(action))
                return Task.FromResult(McpBridge.Error($"Command '{commandName}' requires an 'action' argument."));
            if (!actions.TryGetValue(action, out var command))
                return Task.FromResult(McpBridge.Error($"Unknown action '{action}' for command '{commandName}'."));
            return command.ExecuteAsync(context);
        }
    }

    internal sealed class LogExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["getRecent"] = new UnityLogGetCommand(),
            ["clear"] = new UnityLogClearCommand(),
        };

        public string Name => "log.execute";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ObjectExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["find"] = new ObjectFindCommand(),
            ["get"] = new ObjectGetCommand(),
            ["create"] = new ObjectCreateCommand(),
            ["destroy"] = new ObjectDestroyCommand(),
            ["duplicate"] = new ObjectDuplicateCommand(),
            ["setParent"] = new ObjectSetParentCommand(),
            ["setTransform"] = new ObjectSetTransformCommand(),
            ["setActive"] = new ObjectSetActiveCommand(),
            ["setNameLayerTag"] = new ObjectSetNameLayerTagCommand(),
        };

        public string Name => "object.execute";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ComponentExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["list"] = new ComponentListCommand(),
            ["get"] = new ComponentGetCommand(),
            ["add"] = new ComponentAddCommand(),
            ["remove"] = new ComponentRemoveCommand(),
            ["setProperty"] = new ComponentSetPropertyCommand(),
            ["setProperties"] = new ComponentSetPropertiesCommand(),
            ["callMethod"] = new ComponentCallMethodCommand(),
            ["listTypes"] = new ComponentListTypesCommand(),
        };

        public string Name => "component.execute";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class RuntimeDiagnosticsCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["cameraList"] = new CameraListCommand(),
            ["physicsState"] = new PhysicsGetStateCommand(),
            ["graphicsState"] = new GraphicsGetStateCommand(),
            ["uiList"] = new UiListCommand(),
            ["textureList"] = new TextureListLoadedCommand(),
        };

        public string Name => "runtime.diagnostics";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ReflectionExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["getNamespaces"] = new ReflectionNamespacesCommand(),
            ["getTypes"] = new ReflectionTypesCommand(),
            ["getTypeDetails"] = new ReflectionTypeDetailsCommand(),
            ["findMethods"] = new ReflectionFindMethodsCommand(),
            ["callStaticMethod"] = new ReflectionCallStaticMethodCommand(),
        };

        public string Name => "reflection.execute";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ReflectionFindMethodsCommand : IMcpCommand
    {
        public string Name => "reflection.findMethods";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var query = CommandUtilities.GetString(args, "query");
            var typeName = CommandUtilities.GetString(args, "type");
            var includeNonPublic = LitJson.GetBool(args, "includeNonPublic", false);
            if (includeNonPublic && !LitJson.GetBool(args, "confirmDangerous", false))
                return Task.FromResult(McpBridge.Error("Searching non-public methods requires confirmDangerous: true."));
            var limit = Math.Max(1, LitJson.GetInt(args, "limit", 100));
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;

            var types = string.IsNullOrWhiteSpace(typeName)
                ? AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).SelectMany(ReflectionCommandUtility.GetTypesSafe)
                : new[] { CommandUtilities.FindType(typeName) }.Where(t => t != null)!;

            var methods = types
                .SelectMany(type => type!.GetMethods(flags).Select(method => (Type: type!, Method: method)))
                .Where(item => !item.Method.IsSpecialName)
                .Where(item => string.IsNullOrWhiteSpace(query) ||
                               item.Method.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (item.Type.FullName ?? item.Type.Name).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(item => item.Type.FullName ?? item.Type.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Method.Name, StringComparer.Ordinal)
                .Take(limit)
                .Select(item => (object?)LitJson.Obj(
                    ("declaringType", item.Type.FullName),
                    ("name", item.Method.Name),
                    ("isStatic", item.Method.IsStatic),
                    ("signature", ReflectionCommandUtility.FormatMethod(item.Method))))
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", methods.Count), ("methods", methods))));
        }
    }

    internal sealed class ReflectionCallStaticMethodCommand : IMcpCommand
    {
        public string Name => "reflection.callStaticMethod";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var typeName = CommandUtilities.GetString(args, "type");
            var methodName = CommandUtilities.GetString(args, "method");
            var type = CommandUtilities.FindType(typeName);
            if (type == null) return Task.FromResult(McpBridge.Error($"Type '{typeName}' was not found."));
            var values = CommandUtilities.GetArray(args, "args");
            var includeNonPublic = LitJson.GetBool(args, "includeNonPublic", false);
            if (includeNonPublic && !LitJson.GetBool(args, "confirmDangerous", false))
                return Task.FromResult(McpBridge.Error("Calling non-public static methods requires confirmDangerous: true."));
            var flags = BindingFlags.Public | BindingFlags.Static;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            var candidates = type.GetMethods(flags)
                .Where(method => method.Name == methodName && method.GetParameters().Length == values.Count)
                .ToList();
            foreach (var method in candidates)
            {
                try
                {
                    var parameters = method.GetParameters();
                    var converted = new object?[parameters.Length];
                    for (var i = 0; i < parameters.Length; i++)
                        converted[i] = CommandUtilities.ConvertToType(values[i], parameters[i].ParameterType);
                    var result = method.Invoke(null, converted);
                    return Task.FromResult(McpBridge.Success(LitJson.Obj(("method", ReflectionCommandUtility.FormatMethod(method)), ("result", CommandUtilities.ToJsonFriendly(result)))));
                }
                catch
                {
                    // Try the next overload.
                }
            }

            return Task.FromResult(McpBridge.Error($"No callable static overload '{methodName}' with {values.Count} argument(s) was found on '{typeName}'."));
        }
    }

    internal sealed class BatchExecuteCommand : IMcpCommand
    {
        public string Name => "batch.execute";
        public bool EditorOnly => false;

        public async Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var stopOnError = LitJson.GetBool(args, "stopOnError", true);
            var commands = CommandUtilities.GetArray(args, "commands");
            var results = new List<object?>();

            foreach (var item in commands)
            {
                var commandObject = LitJson.AsObject(item);
                if (commandObject == null)
                {
                    var invalid = LitJson.Obj(("success", false), ("error", "Batch command item must be an object."));
                    results.Add(invalid);
                    if (stopOnError) break;
                    continue;
                }

                var name = LitJson.GetString(commandObject, "name") ?? string.Empty;
                var commandArgs = commandObject.TryGetValue("args", out var rawArgs) ? rawArgs : LitJson.Obj();
                if (!McpCommandRegistry.TryGet(name, out var command))
                {
                    var unknown = LitJson.Obj(("success", false), ("error", $"Unknown command '{name}'."));
                    results.Add(unknown);
                    if (stopOnError) break;
                    continue;
                }

                var nested = new McpCommandContext(context.SessionId, context.RequestId, LitJson.Stringify(commandArgs), context.CancellationToken);
                var responseText = await command.ExecuteAsync(nested);
                var response = LitJson.AsObject(LitJson.Parse(responseText)) ?? LitJson.Obj(("success", false), ("error", "Command returned invalid JSON."));
                results.Add(response);
                if (stopOnError && !LitJson.GetBool(response, "success", false)) break;
            }

            return McpBridge.Success(LitJson.Obj(("count", results.Count), ("results", results)));
        }
    }
}
