#nullable enable
using System;
using System.Collections.Generic;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("inspect", "Format C#/Unity object references into AI-readable data.")]
    public sealed class InspectTool
    {
        [EvalFunction("Return a default summary DTO.")]
        public Dictionary<string, object?> describe(object? value, int depth = 4) => EvalValueFormatter.Describe(value, depth);

        [EvalFunction("Format a value with mode: default, summary, name, path, text, json, yaml.")]
        public Dictionary<string, object?> format(object? value, string mode = "default", int depth = 4) => EvalValueFormatter.Describe(EvalValueFormatter.Format(value, mode, depth), depth);

        [EvalFunction("Return a Unity/C# object's name.")]
        public string toName(object? value) => EvalValueFormatter.Format(value, "name") as string ?? string.Empty;

        [EvalFunction("Return a scene hierarchy path or asset path.")]
        public string toPath(object? value) => EvalValueFormatter.Format(value, "path") as string ?? string.Empty;

        [EvalFunction("Return a JSON string for a formatted value.")]
        public string toJson(object? value, string mode = "json", int depth = 4) => EvalValueFormatter.ToJson(value, mode, depth);

        [EvalFunction("Return a YAML string for a formatted value.")]
        public string toYaml(object? value, int depth = 4) => EvalValueFormatter.Format(value, "yaml", depth) as string ?? string.Empty;
    }
}
