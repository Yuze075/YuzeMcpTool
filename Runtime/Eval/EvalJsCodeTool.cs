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
            var code = LitJson.GetString(arguments, "code") ?? string.Empty;
            var timeout = LitJson.GetInt(arguments, "timeout", _options.DefaultEvalTimeoutSeconds);
            var resetSession = LitJson.GetBool(arguments, "resetSession", false);
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

                if (resetSession)
                {
                    session.EvalSession?.Dispose();
                    session.EvalSession = null;
                }

                session.EvalSession ??= new PuerTsEvalSession(session);

                await GlobalEvalLock.WaitAsync(cancellationToken);
                try
                {
                    BeginEval();
                    var result = await session.EvalSession.ExecuteAsync(requestId, code, timeout, cancellationToken);
                    if (result.TryGetValue("success", out var successValue) && successValue is bool success && success)
                    {
                        CompleteEval(true, string.Empty);
                        if (result.TryGetValue("content", out var content) && content is List<object?> contentList)
                            return LitJson.Obj(("content", contentList));

                        return LitJson.Obj(("content", LitJson.Arr(LitJson.Obj(("type", "text"), ("text", "(no return value)")))));
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
            return LitJson.Obj(
                ("name", "evalJsCode"),
                ("description", Description),
                ("inputSchema", LitJson.Obj(
                    ("type", "object"),
                    ("properties", LitJson.Obj(
                        ("code", LitJson.Obj(
                            ("type", "string"),
                            ("description", "An async function declaration named execute. Example: async function execute() { return await Mcp.invokeAsync('runtime.getState', {}); }")
                        )),
                        ("timeout", LitJson.Obj(
                            ("type", "number"),
                            ("description", "Execution timeout in seconds. Default is 30.")
                        )),
                        ("resetSession", LitJson.Obj(
                            ("type", "boolean"),
                            ("description", "Reset this MCP session's persistent JavaScript VM before executing.")
                        ))
                    )),
                    ("required", LitJson.Arr("code"))
                ))
            );
        }

        private static Dictionary<string, object?> ToolError(string text)
        {
            return LitJson.Obj(
                ("content", LitJson.Arr(LitJson.Obj(("type", "text"), ("text", "Error: " + text)))),
                ("isError", true)
            );
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
            "Execute JavaScript code inside Unity through a persistent PuerTS VM owned by the current MCP session. " +
            "This MCP server intentionally exposes only one MCP tool: evalJsCode. Use it as the entry point for Unity operations. " +
            "The submitted code must define `async function execute() { ... }`; return a concise serializable value from that function. " +
            "The `execute` function is isolated per call, while the underlying VM persists for module cache and deliberate `globalThis` state. " +
            "Start with `const index = await import('YuzeToolkit/mcp/index.mjs'); return index.description;` to discover the compact helper modules. " +
            "Load helper modules from `YuzeToolkit/mcp/Runtime/*.mjs` or `YuzeToolkit/mcp/Editor/*.mjs` only when needed, and read a module's `description` before first use. " +
            "Runtime helpers cover environment state, logs, batching, scene GameObjects, Components, read-only diagnostics, and C# reflection. " +
            "Editor helpers cover Editor state, compilation, selection, menu commands, screenshots, AssetDatabase operations, importers, scripts, materials, scenes, prefabs, SerializedObject, project settings, packages, tests, builds, and validation. " +
            "Editor helper modules and Editor-only bridge commands require the Unity Editor and fail in Runtime/Player; query `runtime.getState`, `Runtime/runtime.mjs#getState()`, or `/health` to check the current environment. " +
            "Use `Mcp.invoke(name, args)` for synchronous C# bridge commands and `await Mcp.invokeAsync(name, args)` for asynchronous bridge commands. " +
            "When a helper does not cover a project-specific custom class, custom editor utility, or special Unity API, write custom JavaScript inside `execute()` and combine helper calls, direct bridge calls, reflection, or any PuerTS-accessible C# API available in this Unity project. " +
            "For direct PuerTS C# interop, use the `CS` global, for example `CS.UnityEngine.GameObject.Find('Main Camera')` or `CS.UnityEditor.AssetDatabase.FindAssets('t:Prefab')` in the Editor. " +
            "When a Unity API expects a `System.Type`, use `puer.$typeof(CS.Namespace.Type)` or `puerts.$typeof(CS.Namespace.Type)`, for example `go.GetComponent(puer.$typeof(CS.UnityEngine.Camera))`. " +
            "Direct JavaScript runs in PuerTS, not Node.js; prefer bridge/helper file APIs for project file IO and return plain serializable objects instead of raw UnityEngine.Object graphs. " +
            "Destructive operations require explicit confirmation where documented, such as `confirm: true`; non-public reflection or dangerous writes require `confirmDangerous: true` where documented. " +
            "During Unity compilation or asset refresh this tool returns a busy error; Domain Reload may briefly disconnect the MCP session, so reinitialize/retry after Unity becomes idle. " +
            "Do not trigger script compilation or AssetDatabase refresh and then wait for completion in the same eval call.";
    }
}
