#nullable enable
using System;
using System.Collections.Generic;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("inspect", "Format C#/Unity object references into AI-readable data.")]
    public sealed class InspectTool
    {
        [McpFunction("Return a default summary DTO.")]
        public Dictionary<string, object?> describe(object? value, int depth = 4) => McpValueFormatter.Describe(value, depth);

        [McpFunction("Format a value with mode: default, summary, name, path, text, json, yaml.")]
        public Dictionary<string, object?> format(object? value, string mode = "default", int depth = 4) => McpValueFormatter.Describe(McpValueFormatter.Format(value, mode, depth), depth);

        [McpFunction("Return a Unity/C# object's name.")]
        public string toName(object? value) => McpValueFormatter.Format(value, "name") as string ?? string.Empty;

        [McpFunction("Return a scene hierarchy path or asset path.")]
        public string toPath(object? value) => McpValueFormatter.Format(value, "path") as string ?? string.Empty;

        [McpFunction("Return a JSON string for a formatted value.")]
        public string toJson(object? value, string mode = "json", int depth = 4) => McpValueFormatter.ToJson(value, mode, depth);

        [McpFunction("Return a YAML string for a formatted value.")]
        public string toYaml(object? value, int depth = 4) => McpValueFormatter.Format(value, "yaml", depth) as string ?? string.Empty;
    }
}
