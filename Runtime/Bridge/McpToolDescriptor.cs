#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace YuzeToolkit
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class McpToolAttribute : Attribute
    {
        public McpToolAttribute(string name, string description)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? throw new ArgumentNullException(nameof(description));
        }

        public string Name { get; }

        public string Description { get; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class McpFunctionAttribute : Attribute
    {
        public McpFunctionAttribute(string description)
        {
            Description = description;
        }

        public string Description { get; }
    }

    public sealed class McpToolFunctionDescriptor
    {
        public static readonly IReadOnlyList<McpToolFunctionDescriptor>
            Empty = Array.Empty<McpToolFunctionDescriptor>();

        public McpToolFunctionDescriptor(string methodName, string description, params string[] parameterTypes)
            : this(methodName, description, Array.Empty<McpToolParameterDescriptor>(), parameterTypes)
        {
        }

        public McpToolFunctionDescriptor(
            string methodName,
            string description,
            IReadOnlyList<McpToolParameterDescriptor>? parameters,
            params string[] parameterTypes)
        {
            MethodName = methodName;
            Description = description;
            Parameters = parameters ?? Array.Empty<McpToolParameterDescriptor>();
            ParameterTypes = parameterTypes is { Length: > 0 }
                ? parameterTypes
                : Parameters.Select(parameter => parameter.Type).ToArray();
        }

        public string MethodName { get; }

        public string Description { get; }

        public IReadOnlyList<string> ParameterTypes { get; }

        public IReadOnlyList<McpToolParameterDescriptor> Parameters { get; }
    }

    public sealed class McpToolParameterDescriptor
    {
        public McpToolParameterDescriptor(string name, string type, bool optional, object? defaultValue)
        {
            Name = name;
            Type = type;
            Optional = optional;
            DefaultValue = defaultValue;
        }

        public string Name { get; }

        public string Type { get; }

        public bool Optional { get; }

        public object? DefaultValue { get; }
    }

    public sealed class McpRegisteredTool
    {
        public McpRegisteredTool(string name, string description, object instance, IReadOnlyList<McpToolFunctionDescriptor>? generatedFunctions)
        {
            Name = name;
            Description = description;
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            GeneratedFunctions = generatedFunctions;
        }

        public string Name { get; }

        public string Description { get; }

        public object Instance { get; }

        public Type ToolType => Instance.GetType();

        public IReadOnlyList<McpToolFunctionDescriptor>? GeneratedFunctions { get; }
    }

    public sealed class McpToolDescriptor
    {
        public McpToolDescriptor(
            string name,
            string description,
            bool editorOnly,
            bool enabled,
            string source,
            IReadOnlyList<McpToolFunctionDescriptor> functions)
        {
            Name = name;
            Description = description;
            EditorOnly = editorOnly;
            Enabled = enabled;
            Source = source;
            Functions = functions;
        }

        public string Name { get; }

        public string Description { get; }

        public bool EditorOnly { get; }

        public bool Enabled { get; }

        public string Source { get; }

        public IReadOnlyList<McpToolFunctionDescriptor> Functions { get; }
    }
}
