#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    public static class McpToolRegistry
    {
        private static readonly Dictionary<string, McpRegisteredTool> Tools = new(StringComparer.Ordinal);

        public static event Action? Changed;

        public static void Register<TTool>() where TTool : class, new()
        {
            var instance = new TTool();
            var metadata = GetToolMetadata(typeof(TTool), instance);
            ValidateName(metadata.Name);

            if (Tools.ContainsKey(metadata.Name))
                throw new InvalidOperationException($"MCP tool '{metadata.Name}' is already registered.");

            Tools.Add(metadata.Name, new McpRegisteredTool(metadata.Name, metadata.Description, instance, metadata.Functions));
            McpToolSettings.EnsureKnown(metadata.Name);
            Changed?.Invoke();
        }

        public static bool TryRegister<TTool>() where TTool : class, new()
        {
            var instance = new TTool();
            var metadata = GetToolMetadata(typeof(TTool), instance);
            ValidateName(metadata.Name);

            if (TryGetRegistered(metadata.Name, out _)) return false;

            Tools.Add(metadata.Name, new McpRegisteredTool(metadata.Name, metadata.Description, instance, metadata.Functions));
            McpToolSettings.EnsureKnown(metadata.Name);
            Changed?.Invoke();
            return true;
        }

        public static bool Unregister(string name)
        {
            if (!Tools.Remove(name)) return false;
            Changed?.Invoke();
            return true;
        }

        public static bool TryGet(string name, out McpRegisteredTool tool)
        {
            if (!Tools.TryGetValue(name, out tool!))
                return false;
            return McpToolSettings.IsEnabled(name);
        }

        public static bool TryGetRegistered(string name, out McpRegisteredTool tool)
        {
            return Tools.TryGetValue(name, out tool!);
        }

        [UnityEngine.Scripting.Preserve]
        public static object GetRequiredInstance(string name)
        {
            if (!TryGet(name, out var tool))
                throw new InvalidOperationException($"MCP tool '{name}' is unknown or disabled.");
            return tool.Instance;
        }

        public static IReadOnlyList<McpRegisteredTool> ListRegistered()
        {
            return Tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToList();
        }

        public static bool IsEnabled(string name) => McpToolSettings.IsEnabled(name);

        public static void SetEnabled(string name, bool enabled)
        {
            McpToolSettings.SetEnabled(name, enabled);
            Changed?.Invoke();
        }

        public static List<object?> ListSummaries()
        {
            var result = new List<object?>();
            foreach (var tool in Tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal))
            {
                result.Add(McpData.Obj(
                    ("name", tool.Name),
                    ("enabled", McpToolSettings.IsEnabled(tool.Name))
                ));
            }
            return result;
        }

        [UnityEngine.Scripting.Preserve]
        public static string GetToolCatalogJson(bool refresh)
        {
            try
            {
                var result = McpToolCatalog.GetIndex(refresh);
                return LitJson.Stringify(result);
            }
            catch (Exception ex)
            {
                return LitJson.Stringify(McpData.Obj(("success", false), ("error", ex.Message)));
            }
        }

        private static ToolMetadata GetToolMetadata(Type toolType, object instance)
        {
            if (instance is IMcpTool generatedTool)
            {
                if (string.IsNullOrWhiteSpace(generatedTool.Description))
                    throw new InvalidOperationException($"MCP generated tool type '{toolType.FullName}' must define a non-empty Description.");
                if (generatedTool.Functions == null)
                    throw new InvalidOperationException($"MCP generated tool type '{toolType.FullName}' must define non-null Functions.");

                return new ToolMetadata(generatedTool.Name, generatedTool.Description, generatedTool.Functions);
            }

            var attribute = toolType.GetCustomAttribute<McpToolAttribute>(false)
                ?? throw new InvalidOperationException($"MCP tool type '{toolType.FullName}' must define McpToolAttribute or implement IMcpTool.");
            return new ToolMetadata(attribute.Name, attribute.Description, null);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("MCP tool name cannot be empty.", nameof(name));
            if (name.Equals("index", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("MCP tool name 'index' is reserved.", nameof(name));
            if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new ArgumentException($"MCP tool name '{name}' contains invalid path characters.", nameof(name));
        }

        private readonly struct ToolMetadata
        {
            public ToolMetadata(string name, string description, IReadOnlyList<McpToolFunctionDescriptor>? functions)
            {
                Name = name;
                Description = description;
                Functions = functions;
            }

            public string Name { get; }

            public string Description { get; }

            public IReadOnlyList<McpToolFunctionDescriptor>? Functions { get; }
        }
    }

    public static class McpToolSettings
    {
        private static readonly Dictionary<string, bool> EnabledOverrides = new(StringComparer.Ordinal);

        public static bool IsEnabled(string name) =>
            !EnabledOverrides.TryGetValue(name, out var enabled) || enabled;

        public static void SetEnabled(string name, bool enabled) =>
            EnabledOverrides[name] = enabled;

        public static void EnsureKnown(string name)
        {
            if (!EnabledOverrides.ContainsKey(name))
                EnabledOverrides[name] = true;
        }
    }
}
