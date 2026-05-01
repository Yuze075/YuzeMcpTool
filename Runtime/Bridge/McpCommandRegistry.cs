#nullable enable
using System;
using System.Collections.Generic;

namespace YuzeToolkit
{
    internal static class McpCommandRegistry
    {
        private static readonly Dictionary<string, IMcpCommand> Commands = new(StringComparer.Ordinal);
        private static bool _registeredDefaults;

        public static void EnsureDefaultCommands()
        {
            if (_registeredDefaults) return;
            _registeredDefaults = true;

            Register(new RuntimeStateCommand());
            Register(new LogExecuteCommand());
            Register(new ObjectExecuteCommand());
            Register(new ComponentExecuteCommand());
            Register(new RuntimeDiagnosticsCommand());
            Register(new ReflectionExecuteCommand());
            Register(new BatchExecuteCommand());
        }

        public static void Register(IMcpCommand command)
        {
            if (Commands.ContainsKey(command.Name))
                throw new InvalidOperationException($"MCP command '{command.Name}' is already registered.");
            Commands.Add(command.Name, command);
        }

        public static bool TryGet(string name, out IMcpCommand command)
        {
            EnsureDefaultCommands();
            return Commands.TryGetValue(name, out command!);
        }

        public static List<object?> ListSummaries()
        {
            EnsureDefaultCommands();
            var result = new List<object?>();
            foreach (var command in Commands.Values)
            {
                result.Add(LitJson.Obj(
                    ("name", command.Name),
                    ("editorOnly", command.EditorOnly)
                ));
            }
            return result;
        }
    }
}
