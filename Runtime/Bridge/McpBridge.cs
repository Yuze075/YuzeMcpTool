#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace YuzeToolkit
{
    public static class McpBridge
    {
        internal static McpBridgeContext? CurrentContext { get; set; }

        [UnityEngine.Scripting.Preserve]
        public static string InvokeSync(string commandName, string argumentsJson)
        {
            try
            {
                var task = InvokeCommandAsync(commandName, argumentsJson);
                if (!task.IsCompleted)
                    return Error($"Command '{commandName}' is asynchronous. Use Mcp.invokeAsync() from JavaScript.");

                return task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        [UnityEngine.Scripting.Preserve]
        public static void InvokeAsync(string commandName, string argumentsJson, Action<string> onComplete)
        {
            _ = InvokeAsyncCore(commandName, argumentsJson, onComplete);
        }

        private static async Task InvokeAsyncCore(string commandName, string argumentsJson, Action<string> onComplete)
        {
            var result = await InvokeCommandAsync(commandName, argumentsJson);
            onComplete?.Invoke(result);
        }

        private static async Task<string> InvokeCommandAsync(string commandName, string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return Error("Command name is empty.");

            var bridgeContext = CurrentContext;
            if (bridgeContext == null)
                return Error("No active evalJsCode context is available.");

            bridgeContext.Session.BeginCommand(commandName);
            try
            {
                if (!McpCommandRegistry.TryGet(commandName, out var command))
                    return CompleteMonitoredCommand(bridgeContext.Session, commandName, Error($"Unknown command '{commandName}'."));

                if (command.EditorOnly && !Application.isEditor)
                    return CompleteMonitoredCommand(bridgeContext.Session, commandName, Error($"Command '{commandName}' is Editor-only and cannot be called in Runtime/Player. Current environment: {CommandUtilities.GetEnvironmentName()}."));

                var context = new McpCommandContext(
                    bridgeContext.SessionId,
                    bridgeContext.RequestId,
                    string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson,
                    bridgeContext.CancellationToken
                );

                return CompleteMonitoredCommand(bridgeContext.Session, commandName, await command.ExecuteAsync(context));
            }
            catch (Exception ex)
            {
                var response = Error(ex.Message);
                bridgeContext.Session.CompleteCommand(commandName, false, ex.Message);
                return response;
            }
        }

        private static string CompleteMonitoredCommand(McpSession session, string commandName, string responseText)
        {
            session.CompleteCommand(commandName, IsSuccessResponse(responseText, out var error), error);
            return responseText;
        }

        private static bool IsSuccessResponse(string responseText, out string error)
        {
            error = string.Empty;
            try
            {
                var response = LitJson.AsObject(LitJson.Parse(responseText));
                if (response == null)
                {
                    error = "Command returned invalid JSON.";
                    return false;
                }

                var success = LitJson.GetBool(response, "success", false);
                if (!success)
                    error = LitJson.GetString(response, "error") ?? "Command returned success=false.";
                return success;
            }
            catch (Exception ex)
            {
                error = $"Command returned invalid JSON: {ex.Message}";
                return false;
            }
        }

        internal static string Success(object? result) => LitJson.Stringify(LitJson.Obj(("success", true), ("result", result)));

        internal static string Error(string message) => LitJson.Stringify(LitJson.Obj(("success", false), ("error", message)));
    }
}
