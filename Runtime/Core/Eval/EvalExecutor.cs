#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace YuzeToolkit
{
    public sealed class EvalExecutor
    {
        private readonly EvalOptions _options;

        public EvalExecutor(EvalOptions options) => _options = options;

        public async Task<Dictionary<string, object?>> ExecuteAsync(
            EvalSession session,
            string requestId,
            Dictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            var code = EvalData.GetString(arguments, "code") ?? string.Empty;
            var timeout = EvalData.GetInt(arguments, "timeout", _options.DefaultEvalTimeoutSeconds);
            var resetSession = EvalData.GetBool(arguments, "resetSession", false);
            var evalStarted = false;
            var evalCompleted = false;
            var evalTurnAcquired = false;

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
                evalTurnAcquired = await session.TryEnterEvalTurnAsync(cancellationToken);
                if (!evalTurnAcquired)
                {
                    const string closedMessage = "Eval session is closing or has been disposed.";
                    return ToolError(closedMessage);
                }

                var busyReason = await MainThreadDispatcher.RunAsync(GetUnityBusyReason);
                if (!string.IsNullOrEmpty(busyReason))
                {
                    var busyMessage = $"{busyReason}. Retry after Unity becomes idle.";
                    CompleteEval(false, busyMessage);
                    return ToolError(busyMessage);
                }

                if (resetSession && session.VmSession != null)
                {
                    var previousSession = session.VmSession;
                    await MainThreadDispatcher.RunAsync(previousSession.Dispose);
                    session.VmSession = null;
                }

                session.VmSession ??= new EvalVmSession(session);

                BeginEval();
                var result = await session.VmSession.ExecuteAsync(requestId, code, timeout, cancellationToken);
                if (result.TryGetValue("success", out var successValue) && successValue is bool success && success)
                {
                    CompleteEval(true, string.Empty);
                    return EvalData.Obj(("content", BuildContent(result)));
                }

                var error = result.TryGetValue("error", out var errorValue) ? Convert.ToString(errorValue) : "evalJsCode failed.";
                var stack = result.TryGetValue("stack", out var stackValue) ? Convert.ToString(stackValue) : string.Empty;
                var resultMessage = string.IsNullOrEmpty(stack) ? error ?? string.Empty : $"{error}\nStack: {stack}";
                CompleteEval(false, resultMessage);
                return ToolError(resultMessage);
            }
            catch (OperationCanceledException)
            {
                const string cancelMessage = "evalJsCode was canceled.";
                CompleteEval(false, cancelMessage);
                return ToolError(cancelMessage);
            }
            catch (Exception ex)
            {
                CompleteEval(false, ex.Message);
                throw;
            }
            finally
            {
                if (evalTurnAcquired)
                    session.ReleaseEvalTurn();
            }
        }

        public static Dictionary<string, object?> ToolDefinition()
        {
            return EvalData.Obj(
                ("name", "evalJsCode"),
                ("description", Description),
                ("inputSchema", EvalData.Obj(
                    ("type", "object"),
                    ("properties", EvalData.Obj(
                        ("code", EvalData.Obj(
                            ("type", "string"),
                            ("description", "An async function declaration named execute. Example: async function execute() { const runtime = await import('tools/runtime'); return runtime.getState(); }")
                        )),
                        ("timeout", EvalData.Obj(
                            ("type", "number"),
                            ("description", "Execution timeout in seconds. Default is 30.")
                        )),
                        ("resetSession", EvalData.Obj(
                            ("type", "boolean"),
                            ("description", "Reset this eval session's persistent JavaScript VM before executing.")
                        ))
                    )),
                    ("required", EvalData.Arr("code"))
                ))
            );
        }

        private static Dictionary<string, object?> ToolError(string text)
        {
            return EvalData.Obj(
                ("content", EvalData.Arr(EvalData.Obj(("type", "text"), ("text", "Error: " + text)))),
                ("isError", true)
            );
        }

        private static List<object?> BuildContent(Dictionary<string, object?> evalResult)
        {
            var content = new List<object?>();
            var hasValue = evalResult.TryGetValue("hasValue", out var hasValueRaw) && hasValueRaw is bool valueExists && valueExists;
            var text = hasValue && evalResult.TryGetValue("result", out var value)
                ? EvalValueFormatter.ToEvalText(value)
                : "(no return value)";
            content.Add(EvalData.Obj(("type", "text"), ("text", text)));

            if (EvalData.AsArray(evalResult.TryGetValue("images", out var imagesRaw) ? imagesRaw : null) is { } images)
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
            "Execute JavaScript inside Unity through the current eval session's persistent PuerTS VM. " +
            "Your code must define `async function execute() { ... }` and return concise serializable data. " +
            "First discover tools with `const index = await import('tools/index'); return index.description;`; this lists evalJsCode usage and concise tool summaries only. " +
            "Import a tool with `const runtime = await import('tools/runtime');` after choosing it from the tool summaries. " +
            "Use `tools/index.getToolDetails('toolName')` or an imported tool's `functions` array when you need method descriptions and parameters. " +
            "Generated C# tool functions use positional arguments. " +
            "Prefer helper tools for common workflows; use PuerTS `CS.*` interop only when no helper covers the task. " +
            "Return primitives, lists, or dictionaries. Editor-only tools require the Unity Editor. Destructive operations require explicit confirmation flags.";
    }
}
