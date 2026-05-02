#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    internal sealed class EvalJsCodeTool
    {
        private static readonly SemaphoreSlim GlobalEvalLock = new(1, 1);
        private readonly McpServerOptions _options;

        public EvalJsCodeTool(McpServerOptions options) => _options = options;

        public async Task<Dictionary<string, object?>> ExecuteAsync(
            McpSession session,
            string requestId,
            Dictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            var code = McpData.GetString(arguments, "code") ?? string.Empty;
            var timeout = McpData.GetInt(arguments, "timeout", _options.DefaultEvalTimeoutSeconds);
            var resetSession = McpData.GetBool(arguments, "resetSession", false);
            var evalStarted = false;
            var evalCompleted = false;

            void BeginEval()
            {
                if (evalStarted) return;
                session.BeginEval(requestId, code, timeout, resetSession);
                evalStarted = true;
            }

            void CompleteEval(bool success, string error)
            {
                if (evalCompleted) return;
                BeginEval();
                session.CompleteEval(requestId, success, error);
                evalCompleted = true;
            }

            try
            {
                var busyReason = await MainThreadDispatcher.RunAsync(GetUnityBusyReason);
                if (!string.IsNullOrEmpty(busyReason))
                {
                    var message = $"{busyReason}. Retry after Unity becomes idle.";
                    CompleteEval(false, message);
                    return ToolError(message);
                }

                await GlobalEvalLock.WaitAsync(cancellationToken);
                try
                {
                    if (resetSession && session.EvalSession != null)
                    {
                        var previousSession = session.EvalSession;
                        await MainThreadDispatcher.RunAsync(previousSession.Dispose);
                        session.EvalSession = null;
                    }

                    session.EvalSession ??= new PuerTsEvalSession(session);

                    BeginEval();
                    var result = await session.EvalSession.ExecuteAsync(requestId, code, timeout, cancellationToken);
                    if (result.TryGetValue("success", out var successValue) && successValue is bool success && success)
                    {
                        CompleteEval(true, string.Empty);
                        return McpData.Obj(("content", BuildContent(result)));
                    }

                    var error = result.TryGetValue("error", out var errorValue) ? Convert.ToString(errorValue) : "evalJsCode failed.";
                    var stack = result.TryGetValue("stack", out var stackValue) ? Convert.ToString(stackValue) : string.Empty;
                    var message = string.IsNullOrEmpty(stack) ? error ?? string.Empty : $"{error}\nStack: {stack}";
                    CompleteEval(false, message);
                    return ToolError(message);
                }
                finally
                {
                    GlobalEvalLock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                const string message = "evalJsCode was canceled.";
                CompleteEval(false, message);
                return ToolError(message);
            }
            catch (Exception ex)
            {
                CompleteEval(false, ex.Message);
                throw;
            }
        }

        public static Dictionary<string, object?> ToolDefinition()
        {
            return McpData.Obj(
                ("name", "evalJsCode"),
                ("description", Description),
                ("inputSchema", McpData.Obj(
                    ("type", "object"),
                    ("properties", McpData.Obj(
                        ("code", McpData.Obj(
                            ("type", "string"),
                            ("description", "An async function declaration named execute. Example: async function execute() { const runtime = await import('tools/runtime'); return runtime.getState(); }")
                        )),
                        ("timeout", McpData.Obj(
                            ("type", "number"),
                            ("description", "Execution timeout in seconds. Default is 30.")
                        )),
                        ("resetSession", McpData.Obj(
                            ("type", "boolean"),
                            ("description", "Reset this MCP session's persistent JavaScript VM before executing.")
                        ))
                    )),
                    ("required", McpData.Arr("code"))
                ))
            );
        }

        private static Dictionary<string, object?> ToolError(string text)
        {
            return McpData.Obj(
                ("content", McpData.Arr(McpData.Obj(("type", "text"), ("text", "Error: " + text)))),
                ("isError", true)
            );
        }

        private static List<object?> BuildContent(Dictionary<string, object?> evalResult)
        {
            var content = new List<object?>();
            var hasValue = evalResult.TryGetValue("hasValue", out var hasValueRaw) && hasValueRaw is bool valueExists && valueExists;
            var text = hasValue && evalResult.TryGetValue("result", out var value)
                ? McpValueFormatter.ToMcpText(value)
                : "(no return value)";
            content.Add(McpData.Obj(("type", "text"), ("text", text)));

            if (McpData.AsArray(evalResult.TryGetValue("images", out var imagesRaw) ? imagesRaw : null) is { } images)
                content.AddRange(images);

            return content;
        }

        private static string? GetUnityBusyReason()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isCompiling)
                return "Unity Editor is compiling scripts";
            if (UnityEditor.EditorApplication.isUpdating)
                return "Unity Editor is updating assets";
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return "Unity Editor is changing play mode";
#endif
            return null;
        }

        private const string Description =
            "Execute JavaScript inside Unity through the current MCP session's persistent PuerTS VM. " +
            "Your code must define `async function execute() { ... }` and return concise serializable data. " +
            "First discover tools with `const index = await import('tools/index'); return index.description;`. " +
            "Import C# tools from `tools/<name>` and call exported functions directly, for example `const runtime = await import('tools/runtime'); return runtime.getState();`. " +
            "Use helper modules first for common workflows because they return stable structured data; when helpers do not cover the task, use PuerTS `CS.*` interop directly to run Unity/C# APIs in this VM. " +
            "Generated C# tool functions accept positional arguments such as `assets.findPaths('t:Prefab', 50)`. Function metadata includes `description`, ordered `parameters`, and legacy `parameterTypes`. " +
            "Prefer returning primitives, lists, and dictionaries; the server serializes them as JSON text content. UnityEngine.Object and custom C# objects are summarized at the server boundary. " +
            "Editor-only tools require the Unity Editor; runtime tools work in Editor and Player. " +
            "Destructive operations require explicit flags such as `confirm: true`, `confirmOverwrite: true`, or `confirmDangerous: true`. " +
            "The timeout only bounds async completion after control returns to Unity; it cannot interrupt an infinite synchronous JS loop or a long blocking Unity call. " +
            "Do not trigger script compilation or AssetDatabase refresh and then wait for completion in the same eval call.";
    }
}
