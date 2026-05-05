#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YuzeToolkit
{
    public static class EvalToolCatalog
    {
        private const string ResourceFolder = "tools";
        private const string IndexModuleName = "index";
        private static readonly object SyncRoot = new();
        private static readonly object FunctionDescriptorSyncRoot = new();
        private static readonly Dictionary<Type, IReadOnlyList<EvalToolFunctionDescriptor>> ReflectedFunctionDescriptorCache = new();
        private static List<EvalToolDescriptor>? _cachedResourceTools;

        public static Dictionary<string, object?> GetIndex(bool refresh)
        {
            var tools = ListTools(refresh);
            return EvalData.Obj(
                ("resourcePath", ResourceFolder),
                ("tools", tools.Select(ToSummaryJson).Cast<object?>().ToList()),
                ("modules", tools.Select(ToSummaryJson).Cast<object?>().ToList()),
                ("description", BuildDescription(tools))
            );
        }

        public static Dictionary<string, object?> GetToolDetails(string name, bool refresh)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Tool name is required.");
            var tool = ListTools(refresh).FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tool == null)
                throw new InvalidOperationException($"Tool '{name}' was not found.");
            return ToJson(tool);
        }

        public static Dictionary<string, object?> GetCliCatalog(bool refresh)
        {
            var tools = ListTools(refresh);
            return EvalData.Obj(
                ("version", "1.0"),
                ("resourcePath", ResourceFolder),
                ("tools", tools.Select(ToCliJson).Cast<object?>().ToList()),
                ("commands", tools.SelectMany(ToCliCommands).Cast<object?>().ToList())
            );
        }

        public static IReadOnlyList<EvalToolDescriptor> ListTools(bool refreshResources = false)
        {
            var csharpTools = EvalToolRegistry.ListRegistered()
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
            if (!EvalToolRegistry.TryGetRegistered(moduleName, out var tool))
                return false;
            if (!EvalToolRegistry.IsEnabled(moduleName))
                return false;

            source = GenerateCSharpToolModule(ToDescriptor(tool));
            return true;
        }

        public static bool TryGetFunctionDescriptor(string toolName, string functionName, out EvalToolFunctionDescriptor descriptor)
        {
            descriptor = null!;
            if (!EvalToolRegistry.TryGetRegistered(toolName, out var tool))
                return false;

            var toolDescriptor = ToDescriptor(tool);
            descriptor = toolDescriptor.Functions.FirstOrDefault(function => string.Equals(function.MethodName, functionName, StringComparison.Ordinal))!;
            return descriptor != null;
        }

        public static bool IsResourceToolEnabled(string moduleName) => EvalToolSettings.IsEnabled(moduleName);

        public static string GenerateIndexModuleSource()
        {
            var json = LitJson.Stringify(GetIndex(false));
            return $@"let catalog = {json};

function readCatalog(refresh) {{
  const parsed = JSON.parse(CS.YuzeToolkit.EvalToolRegistry.GetToolCatalogJson(!!refresh));
  if (parsed && parsed.success === false) throw new Error(parsed.error || ""Eval tool catalog failed."");
  return parsed;
}}

function readToolDetails(name, refresh) {{
  const parsed = JSON.parse(CS.YuzeToolkit.EvalToolRegistry.GetToolDetailsJson(String(name || ''), !!refresh));
  if (parsed && parsed.success === false) throw new Error(parsed.error || ""Eval tool details failed."");
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

export function getToolDetails(name, refresh = false) {{
  return readToolDetails(name, refresh);
}}

export function describeTool(name, refresh = false) {{
  const details = readToolDetails(name, refresh);
  return {{
    name: details.name,
    path: details.path,
    description: details.description,
    editorOnly: details.editorOnly,
    enabled: details.enabled,
    source: details.source,
    functions: details.functions
  }};
}}
";
        }

        private static List<EvalToolDescriptor> GetResourceTools(bool refresh)
        {
            lock (SyncRoot)
            {
                if (refresh || _cachedResourceTools == null)
                    _cachedResourceTools = BuildResourceTools();
                return _cachedResourceTools;
            }
        }

        private static List<EvalToolDescriptor> BuildResourceTools()
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

        private static bool IsNotIndex(EvalToolDescriptor descriptor) =>
            !string.Equals(descriptor.Name, IndexModuleName, StringComparison.OrdinalIgnoreCase);

        private static EvalToolDescriptor ToResourceDescriptor(TextAsset asset)
        {
            var assetPath = GetAssetPath(asset);
            return new EvalToolDescriptor(
                asset.name,
                ExtractConstString(asset.text, "description") ?? $"JavaScript eval tool loaded from Resources/tools/{asset.name}.",
                IsEditorOnlyAssetPath(assetPath),
                EvalToolSettings.IsEnabled(asset.name),
                "js",
                ExtractJavaScriptFunctions(asset.text)
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
        private static EvalToolDescriptor ToEditorFileDescriptor(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var source = File.ReadAllText(path);
            return new EvalToolDescriptor(
                name,
                ExtractConstString(source, "description") ?? $"JavaScript eval tool loaded from {NormalizeEditorPath(path)}.",
                IsEditorOnlyAssetPath(path),
                EvalToolSettings.IsEnabled(name),
                "js",
                ExtractJavaScriptFunctions(source)
            );
        }
#endif

        private static EvalToolDescriptor ToDescriptor(EvalRegisteredTool tool)
        {
            return new EvalToolDescriptor(
                tool.Name,
                tool.Description,
                IsEditorOnlyAssembly(tool.ToolType),
                EvalToolRegistry.IsEnabled(tool.Name),
                "csharp",
                BuildFunctionDescriptors(tool)
            );
        }

        private static IReadOnlyList<EvalToolFunctionDescriptor> BuildFunctionDescriptors(EvalRegisteredTool tool)
        {
            if (tool.GeneratedFunctions is { } generatedFunctions)
                return generatedFunctions;

            return GetReflectedFunctionDescriptors(tool.ToolType);
        }

        private static bool IsEditorOnlyAssembly(Type toolType) =>
            toolType.Assembly.GetName().Name?.IndexOf(".Editor", StringComparison.OrdinalIgnoreCase) >= 0;

        private static IReadOnlyList<EvalToolFunctionDescriptor> GetReflectedFunctionDescriptors(Type toolType)
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

        private static IReadOnlyList<EvalToolFunctionDescriptor> ReflectFunctionDescriptors(Type toolType)
        {
            return GetPublicInstanceMethods(toolType)
                .Select(method => (Method: method, Attribute: method.GetCustomAttribute<EvalFunctionAttribute>(true)))
                .Where(entry => entry.Attribute != null)
                .OrderBy(entry => entry.Method.MetadataToken)
                .Select(entry => new EvalToolFunctionDescriptor(
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

        private static IReadOnlyList<EvalToolParameterDescriptor> GetParameters(MethodInfo method)
        {
            return method.GetParameters()
                .Select(parameter => new EvalToolParameterDescriptor(
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
            return value == DBNull.Value || value == System.Reflection.Missing.Value ? null : EvalValueFormatter.Format(value);
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

        private static Dictionary<string, object?> ToJson(EvalToolDescriptor descriptor) =>
            EvalData.Obj(
                ("name", descriptor.Name),
                ("path", $"{ResourceFolder}/{descriptor.Name}"),
                ("description", descriptor.Description),
                ("editorOnly", descriptor.EditorOnly),
                ("enabled", descriptor.Enabled),
                ("source", descriptor.Source),
                ("functions", descriptor.Functions.Select(function => (object?)EvalData.Obj(
                    ("name", function.MethodName),
                    ("methodName", function.MethodName),
                    ("description", function.Description),
                    ("parameterTypes", function.ParameterTypes.ToList()),
                    ("parameters", function.Parameters.Select(parameter => (object?)EvalData.Obj(
                        ("name", parameter.Name),
                        ("type", parameter.Type),
                        ("optional", parameter.Optional),
                        ("defaultValue", parameter.DefaultValue),
                        ("description", BuildParameterDescription(parameter))
                    )).ToList())
                )).ToList())
            );

        private static Dictionary<string, object?> ToSummaryJson(EvalToolDescriptor descriptor) =>
            EvalData.Obj(
                ("name", descriptor.Name),
                ("path", $"{ResourceFolder}/{descriptor.Name}"),
                ("description", descriptor.Description),
                ("editorOnly", descriptor.EditorOnly),
                ("enabled", descriptor.Enabled),
                ("source", descriptor.Source),
                ("functionCount", descriptor.Functions.Count)
            );

        private static Dictionary<string, object?> ToCliJson(EvalToolDescriptor descriptor) =>
            EvalData.Obj(
                ("name", descriptor.Name),
                ("path", $"{ResourceFolder}/{descriptor.Name}"),
                ("importPath", $"{ResourceFolder}/{descriptor.Name}"),
                ("description", descriptor.Description),
                ("editorOnly", descriptor.EditorOnly),
                ("enabled", descriptor.Enabled),
                ("source", descriptor.Source),
                ("functions", descriptor.Functions.Select(function => (object?)ToCliFunctionJson(descriptor, function)).ToList())
            );

        private static IEnumerable<object?> ToCliCommands(EvalToolDescriptor descriptor)
        {
            foreach (var function in descriptor.Functions)
                yield return ToCliFunctionJson(descriptor, function);
        }

        private static Dictionary<string, object?> ToCliFunctionJson(EvalToolDescriptor descriptor, EvalToolFunctionDescriptor function) =>
            EvalData.Obj(
                ("toolName", descriptor.Name),
                ("name", function.MethodName),
                ("methodName", function.MethodName),
                ("command", descriptor.Name),
                ("description", function.Description),
                ("usage", BuildCliUsage(descriptor, function)),
                ("importPath", $"{ResourceFolder}/{descriptor.Name}"),
                ("editorOnly", descriptor.EditorOnly),
                ("enabled", descriptor.Enabled),
                ("source", descriptor.Source),
                ("parameters", function.Parameters.Select(parameter => (object?)EvalData.Obj(
                    ("name", parameter.Name),
                    ("type", parameter.Type),
                    ("optional", parameter.Optional),
                    ("defaultValue", parameter.DefaultValue),
                    ("flags", BuildParameterFlags(parameter, function.Parameters)),
                    ("description", BuildParameterDescription(parameter))
                )).ToList()),
                ("parameterTypes", function.ParameterTypes.ToList())
            );

        private static string BuildCliUsage(EvalToolDescriptor descriptor, EvalToolFunctionDescriptor function)
        {
            var builder = new StringBuilder();
            builder.Append(descriptor.Name);
            builder.Append(' ');
            builder.Append(function.MethodName);
            foreach (var parameter in function.Parameters)
            {
                builder.Append(' ');
                builder.Append(parameter.Optional ? '[' : '<');
                builder.Append("--");
                builder.Append(ToKebabCase(parameter.Name));
                if (!IsBoolType(parameter.Type))
                {
                    builder.Append(' ');
                    builder.Append(parameter.Type);
                }
                builder.Append(parameter.Optional ? ']' : '>');
            }
            return builder.ToString();
        }

        private static bool IsBoolType(string type) =>
            string.Equals(type.TrimEnd('?'), "bool", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(type.TrimEnd('?'), "boolean", StringComparison.OrdinalIgnoreCase);

        private static string BuildParameterDescription(EvalToolParameterDescriptor parameter)
        {
            var name = parameter.Name;
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            return name switch
            {
                "target" => "GameObject, Component, UnityEngine.Object, instance id, name/path string, or selector object.",
                "value" => "Search value or value to assign. Check the function description for exact matching rules.",
                "by" => "Search mode. Common values are name, path, tag, or component.",
                "filter" => "Unity AssetDatabase search filter, such as t:Prefab, t:Scene, l:label, or a name fragment.",
                "path" => "Unity project path such as Assets/... or Packages/.... Absolute and parent traversal paths are rejected by project file IO helpers.",
                "from" => "Source Unity project asset path.",
                "to" => "Destination Unity project asset path.",
                "propertyPath" => "Unity SerializedProperty path, for example m_Script, m_Name, or fieldName.Array.data[0].",
                "method" => "C# method name. Use reflection.findMethods or component metadata to check available overloads first.",
                "member" => "C# field or property name.",
                "args" => "Positional argument array for the reflected method call.",
                "values" => "Object map of member names to values.",
                "changes" => "Array of change objects. Each change usually includes propertyPath and value.",
                "namespaceName" => "Exact namespace string returned by reflection.getNamespaces.",
                "fullName" => "Full C# type name including namespace.",
                "parent" => "Parent GameObject selector, or null for no parent.",
                "folders" => "Project folder path string or array of folder paths.",
                "confirm" => "Safety switch. Pass true, or use the CLI flag without a value.",
                "confirmOverwrite" => "Overwrite safety switch. Pass true, or use the CLI flag without a value.",
                "confirmDangerous" => "Required for non-public or static reflective operations. Pass true, or use the CLI flag without a value.",
                "mode" => "Formatting mode or operation mode accepted by this function.",
                "type" => "C# type name, component type name, or tool-specific type selector.",
                "limit" => "Maximum number of results. Use 0 only when the function documents it as unlimited.",
                "includeInactive" => "Whether inactive scene objects are included.",
                "includeNonPublic" => "Whether non-public members are included. Usually requires confirmDangerous.",
                "includeStatic" => "Whether static members are included. Usually requires confirmDangerous.",
                _ => name
            };
        }

        private static List<object?> BuildParameterFlags(
            EvalToolParameterDescriptor parameter,
            IReadOnlyList<EvalToolParameterDescriptor> allParameters)
        {
            var flags = new List<object?>();
            var parameterName = parameter.Name;
            if (!string.IsNullOrWhiteSpace(parameterName))
            {
                AddUnique(flags, "--" + ToKebabCase(parameterName));
                AddUnique(flags, "--" + parameterName);
                var shortFlag = "-" + char.ToLowerInvariant(parameterName[0]);
                var shortFlagIsUnique = allParameters.Count(other =>
                    !string.IsNullOrWhiteSpace(other.Name) &&
                    char.ToLowerInvariant(other.Name[0]) == char.ToLowerInvariant(parameterName[0])) == 1;
                if (shortFlagIsUnique && !IsReservedCliShortFlag(shortFlag))
                    AddUnique(flags, shortFlag);
            }
            return flags;
        }

        private static bool IsReservedCliShortFlag(string flag) =>
            string.Equals(flag, "-h", StringComparison.OrdinalIgnoreCase);

        private static void AddUnique(List<object?> flags, string flag)
        {
            if (!flags.Contains(flag))
                flags.Add(flag);
        }

        private static string ToKebabCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;
            var builder = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsUpper(c))
                {
                    if (i > 0) builder.Append('-');
                    builder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    builder.Append(c == '_' ? '-' : c);
                }
            }
            return builder.ToString();
        }

        private static IReadOnlyList<EvalToolFunctionDescriptor> ExtractJavaScriptFunctions(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return EvalToolFunctionDescriptor.Empty;

            var functions = new List<EvalToolFunctionDescriptor>();
            foreach (Match match in Regex.Matches(source, @"export\s+(?:async\s+)?function\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*\(([^)]*)\)"))
            {
                var name = match.Groups[1].Value;
                var parameters = ParseJavaScriptParameters(match.Groups[2].Value);
                functions.Add(new EvalToolFunctionDescriptor(
                    name,
                    $"JavaScript helper function `{name}`.",
                    parameters,
                    parameters.Select(parameter => parameter.Type).ToArray()));
            }

            return functions;
        }

        private static IReadOnlyList<EvalToolParameterDescriptor> ParseJavaScriptParameters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<EvalToolParameterDescriptor>();

            return text.Split(',')
                .Select(parameter => parameter.Trim())
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter))
                .Select(parameter =>
                {
                    var optional = parameter.Contains("=", StringComparison.Ordinal);
                    var name = parameter.Split('=')[0].Trim();
                    if (name.StartsWith("...", StringComparison.Ordinal)) name = name.Substring(3);
                    return new EvalToolParameterDescriptor(name, "object", optional, null);
                })
                .ToList();
        }

        private static string BuildDescription(IReadOnlyList<EvalToolDescriptor> tools)
        {
            var lines = tools.Select(tool =>
            {
                var tags = new List<string> { tool.Source };
                if (tool.EditorOnly) tags.Add("Editor-only");
                if (!tool.Enabled) tags.Add("disabled");
                return $"- {tool.Name}: `tools/{tool.Name}` [{string.Join(", ", tags)}] - {tool.Description}";
            });

            return @$"UnityEvalTool exposes one MCP tool: `evalJsCode`.

Inside `evalJsCode`, import Unity helper tools from `tools/<name>` without a `.mjs` suffix:

```javascript
async function execute() {{
  const index = await import('tools/index');
  const runtime = await import('tools/runtime');
  return {{ tools: index.listTools(), runtimeDescription: runtime.description }};
}}
```

Discovery:
- `tools/index.listTools()` returns concise tool summaries only.
- Call `tools/index.getToolDetails('toolName')` when you need that tool's method descriptions, parameter order, defaults, and safety flags.
- After importing a concrete tool such as `tools/assets`, its exported `functions` array also contains method metadata.

Available tools:
{string.Join("\n", lines)}

Call pattern: generated C# tools use positional arguments. Prefer helper tools for common workflows; use PuerTS `CS.*` interop only when no helper covers the task.
".Trim();
        }

        private static string GenerateCSharpToolModule(EvalToolDescriptor descriptor)
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
  return CS.YuzeToolkit.EvalToolRegistry.IsEnabled(descriptor.name);
}

function getInstance() {
  return CS.YuzeToolkit.EvalToolRegistry.GetRequiredInstance(descriptor.name);
}

function toSerializable(value) {
  return JSON.parse(CS.YuzeToolkit.EvalValueFormatter.ToJson(value));
}

function toToolArgument(value) {
  if (value === null || value === undefined) return value;
  const type = typeof value;
  if (type !== ""object"") return value;
  return CS.YuzeToolkit.EvalValueFormatter.FromJson(JSON.stringify(value));
}
");

            foreach (var function in descriptor.Functions)
            {
                if (!IsValidIdentifier(function.MethodName)) continue;
                var escapedMethodName = EscapeIdentifier(function.MethodName);
                var functionJson = LitJson.Stringify(EvalData.Obj(
                    ("description", function.Description),
                    ("parameterTypes", function.ParameterTypes.ToList()),
                    ("parameters", function.Parameters.Select(parameter => (object?)EvalData.Obj(
                        ("name", parameter.Name),
                        ("type", parameter.Type),
                        ("optional", parameter.Optional),
                        ("defaultValue", parameter.DefaultValue),
                        ("description", BuildParameterDescription(parameter))
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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawAssetPath in AssetDatabase.GetAllAssetPaths())
            {
                var assetPath = rawAssetPath.Replace('\\', '/');
                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    !assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var extension = Path.GetExtension(assetPath);
                if (!extension.Equals(".js", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                var marker = "/Resources/" + ResourceFolder + "/";
                var markerIndex = assetPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0) continue;
                var resourcesPath = assetPath.Substring(0, markerIndex + "/Resources".Length);
                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, resourcesPath));
                if (seen.Add(fullPath))
                    yield return fullPath;
            }
        }

        private static string NormalizeEditorPath(string path) =>
            path.Replace('\\', '/');
#endif
    }
}
