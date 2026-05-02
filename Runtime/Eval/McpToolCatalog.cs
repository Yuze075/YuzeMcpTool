#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YuzeToolkit
{
    internal static class McpToolCatalog
    {
        private const string ResourceFolder = "tools";
        private const string IndexModuleName = "index";
        private static readonly object SyncRoot = new();
        private static readonly object FunctionDescriptorSyncRoot = new();
        private static readonly Dictionary<Type, IReadOnlyList<McpToolFunctionDescriptor>> ReflectedFunctionDescriptorCache = new();
        private static List<McpToolDescriptor>? _cachedResourceTools;

        public static Dictionary<string, object?> GetIndex(bool refresh)
        {
            var tools = ListTools(refresh);
            return McpData.Obj(
                ("resourcePath", ResourceFolder),
                ("tools", tools.Select(ToJson).Cast<object?>().ToList()),
                ("modules", tools.Select(ToJson).Cast<object?>().ToList()),
                ("description", BuildDescription(tools))
            );
        }

        public static IReadOnlyList<McpToolDescriptor> ListTools(bool refreshResources = false)
        {
            var csharpTools = McpToolRegistry.ListRegistered()
                .Select(ToDescriptor)
                .ToList();

            var csharpNames = new HashSet<string>(csharpTools.Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
            var resourceTools = GetResourceTools(refreshResources)
                .Where(tool => !csharpNames.Contains(tool.Name))
                .ToList();

            return csharpTools
                .Concat(resourceTools)
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
        }

        public static bool TryGetCSharpModuleSource(string moduleName, out string source)
        {
            source = string.Empty;
            if (!McpToolRegistry.TryGetRegistered(moduleName, out var tool))
                return false;
            if (!McpToolRegistry.IsEnabled(moduleName))
                return false;

            source = GenerateCSharpToolModule(ToDescriptor(tool));
            return true;
        }

        public static bool TryGetFunctionDescriptor(string toolName, string functionName, out McpToolFunctionDescriptor descriptor)
        {
            descriptor = null!;
            if (!McpToolRegistry.TryGetRegistered(toolName, out var tool))
                return false;

            var toolDescriptor = ToDescriptor(tool);
            descriptor = toolDescriptor.Functions.FirstOrDefault(function => string.Equals(function.MethodName, functionName, StringComparison.Ordinal))!;
            return descriptor != null;
        }

        public static bool IsResourceToolEnabled(string moduleName) => McpToolSettings.IsEnabled(moduleName);

        public static string GenerateIndexModuleSource()
        {
            var json = LitJson.Stringify(GetIndex(false));
            return $@"let catalog = {json};

function readCatalog(refresh) {{
  const parsed = JSON.parse(CS.YuzeToolkit.McpToolRegistry.GetToolCatalogJson(!!refresh));
  if (parsed && parsed.success === false) throw new Error(parsed.error || ""MCP tool catalog failed."");
  return parsed;
}}

export let description = catalog.description;
export let tools = catalog.tools;
export let modules = catalog.modules;

export function listTools() {{
  return tools;
}}

export function listModules() {{
  return modules;
}}

export function refreshTools() {{
  catalog = readCatalog(true);
  description = catalog.description;
  tools = catalog.tools;
  modules = catalog.modules;
  return {{ tools, modules, description }};
}}
";
        }

        private static List<McpToolDescriptor> GetResourceTools(bool refresh)
        {
            lock (SyncRoot)
            {
                if (refresh || _cachedResourceTools == null)
                    _cachedResourceTools = BuildResourceTools();
                return _cachedResourceTools;
            }
        }

        private static List<McpToolDescriptor> BuildResourceTools()
        {
            var textAssetDescriptors = Resources.LoadAll<TextAsset>(ResourceFolder)
                .Where(asset => asset != null)
                .Where(IsCurrentTextAsset)
                .Select(ToResourceDescriptor)
                .Where(IsNotIndex)
                .ToList();

#if UNITY_EDITOR
            textAssetDescriptors.AddRange(FindEditorResourceToolFiles()
                .Select(ToEditorFileDescriptor)
                .Where(IsNotIndex));
#endif

            return textAssetDescriptors
                .GroupBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(descriptor => descriptor.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsNotIndex(McpToolDescriptor descriptor) =>
            !string.Equals(descriptor.Name, IndexModuleName, StringComparison.OrdinalIgnoreCase);

        private static McpToolDescriptor ToResourceDescriptor(TextAsset asset)
        {
            var assetPath = GetAssetPath(asset);
            return new McpToolDescriptor(
                asset.name,
                ExtractConstString(asset.text, "description") ?? $"JavaScript MCP tool loaded from Resources/tools/{asset.name}.",
                IsEditorOnlyAssetPath(assetPath),
                McpToolSettings.IsEnabled(asset.name),
                "js",
                McpToolFunctionDescriptor.Empty
            );
        }

        private static bool IsCurrentTextAsset(TextAsset asset)
        {
#if UNITY_EDITOR
            var assetPath = GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath)) return true;
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return true;
            return File.Exists(Path.Combine(projectRoot, assetPath));
#else
            return true;
#endif
        }

#if UNITY_EDITOR
        private static McpToolDescriptor ToEditorFileDescriptor(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = File.ReadAllText(path);
            return new McpToolDescriptor(
                name,
                ExtractConstString(source, "description") ?? $"JavaScript MCP tool loaded from {NormalizeEditorPath(path)}.",
                IsEditorOnlyAssetPath(path),
                McpToolSettings.IsEnabled(name),
                "js",
                McpToolFunctionDescriptor.Empty
            );
        }
#endif

        private static McpToolDescriptor ToDescriptor(McpRegisteredTool tool)
        {
            return new McpToolDescriptor(
                tool.Name,
                tool.Description,
                IsEditorOnlyAssembly(tool.ToolType),
                McpToolRegistry.IsEnabled(tool.Name),
                "csharp",
                BuildFunctionDescriptors(tool)
            );
        }

        private static IReadOnlyList<McpToolFunctionDescriptor> BuildFunctionDescriptors(McpRegisteredTool tool)
        {
            if (tool.GeneratedFunctions is { } generatedFunctions)
                return generatedFunctions;

            return GetReflectedFunctionDescriptors(tool.ToolType);
        }

        private static bool IsEditorOnlyAssembly(Type toolType) =>
            toolType.Assembly.GetName().Name?.IndexOf(".Editor", StringComparison.OrdinalIgnoreCase) >= 0;

        private static IReadOnlyList<McpToolFunctionDescriptor> GetReflectedFunctionDescriptors(Type toolType)
        {
            lock (FunctionDescriptorSyncRoot)
            {
                if (!ReflectedFunctionDescriptorCache.TryGetValue(toolType, out var descriptors))
                {
                    descriptors = ReflectFunctionDescriptors(toolType);
                    ReflectedFunctionDescriptorCache[toolType] = descriptors;
                }

                return descriptors;
            }
        }

        private static IReadOnlyList<McpToolFunctionDescriptor> ReflectFunctionDescriptors(Type toolType)
        {
            return GetPublicInstanceMethods(toolType)
                .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpFunctionAttribute>(true)))
                .Where(entry => entry.Attribute != null)
                .OrderBy(entry => entry.Method.MetadataToken)
                .Select(entry => new McpToolFunctionDescriptor(
                    entry.Method.Name,
                    entry.Attribute!.Description,
                    GetParameters(entry.Method),
                    GetParameterTypes(entry.Method)))
                .ToList();
        }

        private static IEnumerable<MethodInfo> GetPublicInstanceMethods(Type toolType)
        {
            return toolType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => !method.IsSpecialName)
                .Where(method => method.DeclaringType != typeof(object));
        }

        private static string[] GetParameterTypes(MethodInfo method)
        {
            return method.GetParameters()
                .Select(parameter => FormatParameterType(parameter.ParameterType))
                .ToArray();
        }

        private static IReadOnlyList<McpToolParameterDescriptor> GetParameters(MethodInfo method)
        {
            return method.GetParameters()
                .Select(parameter => new McpToolParameterDescriptor(
                    parameter.Name ?? string.Empty,
                    FormatParameterType(parameter.ParameterType),
                    parameter.IsOptional,
                    GetDefaultValue(parameter)))
                .ToList();
        }

        private static object? GetDefaultValue(ParameterInfo parameter)
        {
            if (!parameter.HasDefaultValue) return null;
            var value = parameter.DefaultValue;
            return value == DBNull.Value || value == System.Reflection.Missing.Value ? null : McpValueFormatter.Format(value);
        }

        private static string FormatParameterType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(short)) return "short";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(object)) return "object";
            if (type.IsArray) return $"{FormatParameterType(type.GetElementType()!)}[]";

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return $"{FormatParameterType(nullable)}?";

            if (!type.IsGenericType) return type.Name;

            var name = type.Name;
            var tickIndex = name.IndexOf('`');
            if (tickIndex >= 0) name = name[..tickIndex];
            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatParameterType))}>";
        }

        private static Dictionary<string, object?> ToJson(McpToolDescriptor descriptor) =>
            McpData.Obj(
                ("name", descriptor.Name),
                ("path", $"{ResourceFolder}/{descriptor.Name}"),
                ("description", descriptor.Description),
                ("editorOnly", descriptor.EditorOnly),
                ("enabled", descriptor.Enabled),
                ("source", descriptor.Source),
                ("functions", descriptor.Functions.Select(function => (object?)McpData.Obj(
                    ("name", function.MethodName),
                    ("methodName", function.MethodName),
                    ("description", function.Description),
                    ("parameterTypes", function.ParameterTypes.ToList()),
                    ("parameters", function.Parameters.Select(parameter => (object?)McpData.Obj(
                        ("name", parameter.Name),
                        ("type", parameter.Type),
                        ("optional", parameter.Optional),
                        ("defaultValue", parameter.DefaultValue)
                    )).ToList())
                )).ToList())
            );

        private static string BuildDescription(IReadOnlyList<McpToolDescriptor> tools)
        {
            var lines = tools.Select(tool =>
            {
                var tags = new List<string> { tool.Source };
                if (tool.EditorOnly) tags.Add("Editor-only");
                if (!tool.Enabled) tags.Add("disabled");
                return $"- {tool.Name}: `tools/{tool.Name}` [{string.Join(", ", tags)}] - {tool.Description}";
            });

            return @$"YuzeMcpTool exposes one external MCP tool: `evalJsCode`.

Inside `evalJsCode`, import MCP sub-tools from `tools/<name>` without a `.mjs` suffix:

```javascript
async function execute() {{
  const index = await import('tools/index');
  const runtime = await import('tools/runtime');
  return await runtime.getState();
}}
```

Discovery:
- `tools/index` is a virtual module generated by the Unity loader.
- C# tools are classes registered through `McpToolRegistry.Register<TTool>()` with `[McpTool(name, description)]`. A generated JavaScript module exports small semantic functions that call public C# instance methods.
- JavaScript extension tools are loaded from every Unity `Resources/tools` folder.
- Editor-only JavaScript tools should live under `Editor/Resources/tools`.
- Add or remove JS files, then call `tools/index.refreshTools()` or use the Editor window refresh button.

Available tools:
{string.Join("\n", lines)}

Call pattern: generated C# tools use positional arguments such as `assets.findPaths('t:Prefab', 50)`. Function metadata includes `description`, ordered `parameters`, and legacy `parameterTypes`.
Prefer helper modules for common workflows because they return stable structured data. When helpers do not cover the task, use PuerTS interop directly through `CS.*` to run Unity/C# APIs inside the same VM; repeated workflows should be promoted into a C# or JavaScript helper.
For C# tools, generated functions validate that the tool is enabled, call the C# public instance methods, and format return values into JSON-friendly data.
For JavaScript tools, the file content is loaded as-is from Resources.
".Trim();
        }

        private static string GenerateCSharpToolModule(McpToolDescriptor descriptor)
        {
            var json = LitJson.Stringify(ToJson(descriptor));
            var builder = new StringBuilder();
            builder.AppendLine($"const descriptor = {json};");
            builder.AppendLine(@"
export const name = descriptor.name;
export const description = descriptor.description;
export const editorOnly = descriptor.editorOnly;
export const functions = descriptor.functions || [];

export function isEnabled() {
  return CS.YuzeToolkit.McpToolRegistry.IsEnabled(descriptor.name);
}

function getInstance() {
  return CS.YuzeToolkit.McpToolRegistry.GetRequiredInstance(descriptor.name);
}

function toSerializable(value) {
  return JSON.parse(CS.YuzeToolkit.McpValueFormatter.ToJson(value));
}

function toToolArgument(value) {
  if (value === null || value === undefined) return value;
  const type = typeof value;
  if (type !== ""object"") return value;
  return CS.YuzeToolkit.McpValueFormatter.FromJson(JSON.stringify(value));
}
");

            foreach (var function in descriptor.Functions)
            {
                if (!IsValidIdentifier(function.MethodName)) continue;
                var escapedMethodName = EscapeIdentifier(function.MethodName);
                var functionJson = LitJson.Stringify(McpData.Obj(
                    ("description", function.Description),
                    ("parameterTypes", function.ParameterTypes.ToList()),
                    ("parameters", function.Parameters.Select(parameter => (object?)McpData.Obj(
                        ("name", parameter.Name),
                        ("type", parameter.Type),
                        ("optional", parameter.Optional),
                        ("defaultValue", parameter.DefaultValue)
                    )).ToList())));
                builder.AppendLine($"export function {escapedMethodName}(...args) {{");
                builder.AppendLine($"  return toSerializable(getInstance().{escapedMethodName}(...args.map(toToolArgument)));");
                builder.AppendLine("}");
                builder.AppendLine($"{escapedMethodName}.description = {functionJson}.description;");
                builder.AppendLine($"{escapedMethodName}.parameterTypes = {functionJson}.parameterTypes;");
                builder.AppendLine($"{escapedMethodName}.parameters = {functionJson}.parameters;");
            }

            return builder.ToString();
        }

        private static bool IsValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (!(char.IsLetter(value[0]) || value[0] == '_' || value[0] == '$')) return false;
            for (var i = 1; i < value.Length; i++)
            {
                var c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '$')) return false;
            }
            return true;
        }

        private static string EscapeString(string value) =>
            value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        private static string EscapeIdentifier(string value) =>
            IsValidIdentifier(value) ? value : throw new InvalidOperationException($"Invalid C# method export name '{value}'.");

        private static string? ExtractConstString(string text, string exportName)
        {
            var token = "export const " + exportName;
            var tokenIndex = text.IndexOf(token, StringComparison.Ordinal);
            if (tokenIndex < 0) return null;

            var equalsIndex = text.IndexOf('=', tokenIndex + token.Length);
            if (equalsIndex < 0) return null;

            var valueStart = equalsIndex + 1;
            while (valueStart < text.Length && char.IsWhiteSpace(text[valueStart]))
                valueStart++;

            if (valueStart >= text.Length) return null;
            var quote = text[valueStart];
            if (quote != '"' && quote != '\'' && quote != '`') return null;

            var valueEnd = valueStart + 1;
            var escaped = false;
            for (; valueEnd < text.Length; valueEnd++)
            {
                var current = text[valueEnd];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                    break;
            }

            if (valueEnd >= text.Length) return null;
            var raw = text.Substring(valueStart + 1, valueEnd - valueStart - 1).Trim();
            return quote == '`' ? raw : UnescapeStringLiteral(raw);
        }

        private static string UnescapeStringLiteral(string value) =>
            value
                .Replace("\\\"", "\"")
                .Replace("\\'", "'")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\\", "\\");

        private static string GetAssetPath(TextAsset asset)
        {
#if UNITY_EDITOR
            return AssetDatabase.GetAssetPath(asset) ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        private static bool IsEditorOnlyAssetPath(string assetPath) =>
            assetPath.Replace('\\', '/').IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;

#if UNITY_EDITOR
        internal static bool TryReadEditorResourceText(string resourcePath, out string text, out string debugPath)
        {
            text = string.Empty;
            debugPath = string.Empty;

            foreach (var path in GetEditorResourceCandidates(resourcePath))
            {
                if (!File.Exists(path)) continue;
                text = File.ReadAllText(path);
                debugPath = NormalizeEditorPath(path);
                return true;
            }

            return false;
        }

        private static IEnumerable<string> GetEditorResourceCandidates(string resourcePath)
        {
            var normalized = resourcePath.Replace('\\', '/').TrimStart('/');
            var candidates = Path.HasExtension(normalized)
                ? new[] { normalized }
                : new[] { normalized + ".mjs", normalized + ".js" };

            foreach (var resourcesDirectory in FindEditorResourceDirectories())
            {
                foreach (var candidate in candidates)
                    yield return Path.Combine(resourcesDirectory, candidate.Replace('/', Path.DirectorySeparatorChar));
            }
        }

        private static IEnumerable<string> FindEditorResourceToolFiles()
        {
            foreach (var resourcesDirectory in FindEditorResourceDirectories())
            {
                var toolsDirectory = Path.Combine(resourcesDirectory, ResourceFolder);
                if (!Directory.Exists(toolsDirectory)) continue;

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(toolsDirectory, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(path =>
                        {
                            var extension = Path.GetExtension(path);
                            return extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                                   extension.Equals(".js", StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in files)
                    yield return file;
            }
        }

        private static IEnumerable<string> FindEditorResourceDirectories()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) yield break;

            foreach (var rootName in new[] { "Assets", "Packages" })
            {
                var root = Path.Combine(projectRoot, rootName);
                if (!Directory.Exists(root)) continue;

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(root, ResourceFolder, SearchOption.AllDirectories)
                        .Select(Path.GetDirectoryName)
                        .Where(parent => parent != null &&
                                         string.Equals(Path.GetFileName(parent), "Resources", StringComparison.OrdinalIgnoreCase))
                        .Cast<string>()
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var directory in directories)
                    yield return directory;
            }
        }

        private static string NormalizeEditorPath(string path) =>
            path.Replace('\\', '/');
#endif
    }
}
