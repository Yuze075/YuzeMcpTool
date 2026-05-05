#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    public static class EvalToolRegistry
    {
        private static readonly Dictionary<string, EvalRegisteredTool> Tools = new(StringComparer.Ordinal);
        private static readonly object SyncRoot = new();

        public static event Action? Changed;

        public static void Register<TTool>() where TTool : class, new()
        {
            var instance = new TTool();
            var metadata = GetToolMetadata(typeof(TTool), instance);
            ValidateName(metadata.Name);

            lock (SyncRoot)
            {
                if (Tools.ContainsKey(metadata.Name))
                    throw new InvalidOperationException($"Eval tool '{metadata.Name}' is already registered.");

                Tools.Add(metadata.Name, new EvalRegisteredTool(metadata.Name, metadata.Description, instance, metadata.Functions));
                EvalToolSettings.EnsureKnown(metadata.Name);
            }
            Changed?.Invoke();
        }

        public static bool TryRegister<TTool>() where TTool : class, new()
        {
            var instance = new TTool();
            var metadata = GetToolMetadata(typeof(TTool), instance);
            ValidateName(metadata.Name);

            lock (SyncRoot)
            {
                if (Tools.ContainsKey(metadata.Name)) return false;

                Tools.Add(metadata.Name, new EvalRegisteredTool(metadata.Name, metadata.Description, instance, metadata.Functions));
                EvalToolSettings.EnsureKnown(metadata.Name);
            }
            Changed?.Invoke();
            return true;
        }

        public static bool Unregister(string name)
        {
            lock (SyncRoot)
            {
                if (!Tools.Remove(name)) return false;
            }
            Changed?.Invoke();
            return true;
        }

        public static bool TryGet(string name, out EvalRegisteredTool tool)
        {
            lock (SyncRoot)
            {
                if (!Tools.TryGetValue(name, out tool!))
                    return false;
                return EvalToolSettings.IsEnabled(name);
            }
        }

        public static bool TryGetRegistered(string name, out EvalRegisteredTool tool)
        {
            lock (SyncRoot)
                return Tools.TryGetValue(name, out tool!);
        }

        [UnityEngine.Scripting.Preserve]
        public static object GetRequiredInstance(string name)
        {
            if (!TryGet(name, out var tool))
                throw new InvalidOperationException($"Eval tool '{name}' is unknown or disabled.");
            return tool.Instance;
        }

        public static IReadOnlyList<EvalRegisteredTool> ListRegistered()
        {
            lock (SyncRoot)
                return Tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToList();
        }

        public static bool IsEnabled(string name) => EvalToolSettings.IsEnabled(name);

        public static void SetEnabled(string name, bool enabled)
        {
            lock (SyncRoot)
                EvalToolSettings.SetEnabled(name, enabled);
            Changed?.Invoke();
        }

        public static List<object?> ListSummaries()
        {
            var result = new List<object?>();
            IReadOnlyList<EvalRegisteredTool> tools;
            lock (SyncRoot)
                tools = Tools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToList();
            foreach (var tool in tools)
            {
                result.Add(EvalData.Obj(
                    ("name", tool.Name),
                    ("enabled", EvalToolSettings.IsEnabled(tool.Name))
                ));
            }
            return result;
        }

        [UnityEngine.Scripting.Preserve]
        public static string GetToolCatalogJson(bool refresh)
        {
            try
            {
                var result = EvalToolCatalog.GetIndex(refresh);
                return LitJson.Stringify(result);
            }
            catch (Exception ex)
            {
                return LitJson.Stringify(EvalData.Obj(("success", false), ("error", ex.Message)));
            }
        }

        [UnityEngine.Scripting.Preserve]
        public static string GetToolDetailsJson(string name, bool refresh)
        {
            try
            {
                var result = EvalToolCatalog.GetToolDetails(name, refresh);
                return LitJson.Stringify(result);
            }
            catch (Exception ex)
            {
                return LitJson.Stringify(EvalData.Obj(("success", false), ("error", ex.Message)));
            }
        }

        private static ToolMetadata GetToolMetadata(Type toolType, object instance)
        {
            if (instance is IEvalTool generatedTool)
            {
                if (string.IsNullOrWhiteSpace(generatedTool.Description))
                    throw new InvalidOperationException($"Generated eval tool type '{toolType.FullName}' must define a non-empty Description.");
                if (generatedTool.Functions == null)
                    throw new InvalidOperationException($"Generated eval tool type '{toolType.FullName}' must define non-null Functions.");

                return new ToolMetadata(generatedTool.Name, generatedTool.Description, generatedTool.Functions);
            }

            var attribute = toolType.GetCustomAttribute<EvalToolAttribute>(false)
                ?? throw new InvalidOperationException($"Eval tool type '{toolType.FullName}' must define EvalToolAttribute or implement IEvalTool.");
            return new ToolMetadata(attribute.Name, attribute.Description, null);
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Eval tool name cannot be empty.", nameof(name));
            if (name.Equals("index", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Eval tool name 'index' is reserved.", nameof(name));
            if (name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new ArgumentException($"Eval tool name '{name}' contains invalid path characters.", nameof(name));
        }

        private readonly struct ToolMetadata
        {
            public ToolMetadata(string name, string description, IReadOnlyList<EvalToolFunctionDescriptor>? functions)
            {
                Name = name;
                Description = description;
                Functions = functions;
            }

            public string Name { get; }

            public string Description { get; }

            public IReadOnlyList<EvalToolFunctionDescriptor>? Functions { get; }
        }
    }

    public static class EvalToolSettings
    {
        private static readonly Dictionary<string, bool> EnabledOverrides = new(StringComparer.Ordinal);
        private static readonly object SyncRoot = new();

        public static bool IsEnabled(string name) =>
            !TryGetOverride(name, out var enabled) || enabled;

        public static void SetEnabled(string name, bool enabled)
        {
            lock (SyncRoot)
                EnabledOverrides[name] = enabled;
        }

        public static void EnsureKnown(string name)
        {
            lock (SyncRoot)
            {
                if (!EnabledOverrides.ContainsKey(name))
                    EnabledOverrides[name] = true;
            }
        }

        private static bool TryGetOverride(string name, out bool enabled)
        {
            lock (SyncRoot)
                return EnabledOverrides.TryGetValue(name, out enabled);
        }
    }
}
