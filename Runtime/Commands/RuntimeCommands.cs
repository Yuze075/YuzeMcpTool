#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using static YuzeToolkit.ReflectionCommandUtility;

namespace YuzeToolkit
{
    internal sealed class RuntimeStateCommand : IMcpCommand
    {
        public string Name => "runtime.getState";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var scene = SceneManager.GetActiveScene();
            var result = LitJson.Obj(
                ("environment", CommandUtilities.GetEnvironmentObject()),
                ("unityVersion", Application.unityVersion),
                ("platform", Application.platform.ToString()),
                ("isEditor", Application.isEditor),
                ("isRuntime", !Application.isEditor),
                ("isPlaying", Application.isPlaying),
                ("dataPath", Application.dataPath),
                ("persistentDataPath", Application.persistentDataPath),
                ("activeScene", LitJson.Obj(
                    ("name", scene.name),
                    ("path", scene.path),
                    ("isLoaded", scene.isLoaded),
                    ("rootCount", scene.rootCount)
                )),
                ("registeredCommands", McpCommandRegistry.ListSummaries())
            );
            return Task.FromResult(McpBridge.Success(result));
        }
    }

    internal sealed class UnityLogGetCommand : IMcpCommand
    {
        public string Name => "unityLog.getRecent";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = LitJson.AsObject(LitJson.Parse(context.ArgumentsJson)) ?? new Dictionary<string, object?>();
            var count = LitJson.GetInt(args, "count", 50);
            var type = LitJson.GetString(args, "type") ?? "all";
            return Task.FromResult(McpBridge.Success(UnityLogBuffer.GetRecent(count, type)));
        }
    }

    internal sealed class UnityLogClearCommand : IMcpCommand
    {
        public string Name => "unityLog.clear";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            UnityLogBuffer.Clear();
            return Task.FromResult(McpBridge.Success("Unity MCP log buffer cleared."));
        }
    }

    internal sealed class ReflectionNamespacesCommand : IMcpCommand
    {
        public string Name => "reflection.getNamespaces";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var namespaces = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(GetTypesSafe)
                .Where(type => type is { IsPublic: true })
                .Select(type => type.Namespace ?? "(global)")
                .Distinct()
                .OrderBy(ns => ns, StringComparer.Ordinal)
                .Cast<object?>()
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("count", namespaces.Count),
                ("namespaces", namespaces)
            )));
        }
    }

    internal sealed class ReflectionTypesCommand : IMcpCommand
    {
        public string Name => "reflection.getTypes";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = LitJson.AsObject(LitJson.Parse(context.ArgumentsJson)) ?? new Dictionary<string, object?>();
            var ns = LitJson.GetString(args, "namespace") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(ns))
                return Task.FromResult(McpBridge.Error("Argument 'namespace' is required."));

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .SelectMany(GetTypesSafe)
                .Where(type => type is { IsPublic: true } && (type.Namespace ?? "(global)") == ns)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .Select(type => (object?)LitJson.Obj(
                    ("fullName", type.FullName),
                    ("name", type.Name),
                    ("kind", GetKind(type))
                ))
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("namespace", ns),
                ("count", types.Count),
                ("types", types)
            )));
        }
    }

    internal sealed class ReflectionTypeDetailsCommand : IMcpCommand
    {
        public string Name => "reflection.getTypeDetails";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = LitJson.AsObject(LitJson.Parse(context.ArgumentsJson)) ?? new Dictionary<string, object?>();
            var fullName = LitJson.GetString(args, "fullName") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
                return Task.FromResult(McpBridge.Error("Argument 'fullName' is required."));

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => !assembly.IsDynamic)
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(t => t != null);

            if (type == null)
                return Task.FromResult(McpBridge.Error($"Type '{fullName}' was not found."));

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => (object?)FormatMethod(method))
                .ToList();

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(field => (object?)$"{FormatType(field.FieldType)} {field.Name}")
                .ToList();

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(prop => (object?)$"{FormatType(prop.PropertyType)} {prop.Name}")
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("fullName", type.FullName),
                ("name", type.Name),
                ("namespace", type.Namespace ?? "(global)"),
                ("kind", GetKind(type)),
                ("baseType", type.BaseType?.FullName ?? string.Empty),
                ("methods", methods),
                ("fields", fields),
                ("properties", properties)
            )));
        }
    }

    internal static class ReflectionCommandUtility
    {
        public static IEnumerable<Type> GetTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes().Where(type => type != null);
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null)!;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        public static string GetKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type.IsInterface) return "interface";
            if (typeof(Delegate).IsAssignableFrom(type)) return "delegate";
            if (type.IsValueType) return "struct";
            return "class";
        }

        public static string FormatType(Type type)
        {
            if (type == typeof(void)) return "void";
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(bool)) return "bool";
            if (type.IsArray) return FormatType(type.GetElementType()!) + "[]";
            return type.FullName ?? type.Name;
        }

        public static string FormatMethod(MethodInfo method)
        {
            var parameters = method.GetParameters()
                .Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}");
            return $"{FormatType(method.ReturnType)} {method.Name}({string.Join(", ", parameters)})";
        }
    }
}
