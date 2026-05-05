#nullable enable
using System.Collections.Generic;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("cli", "Unity CLI bridge control and state access.")]
    public sealed class CliBridgeTool
    {
        [EvalFunction("Start the Unity CLI bridge. Args: host?, port?, token?, requireToken?. Omit token to generate one when token auth is enabled. If already running this returns the current state; call stopCliBridge first to apply different options.")]
        public Dictionary<string, object?> startCliBridge(string host = "127.0.0.1", int port = 0, string token = "", bool requireToken = true)
        {
            CliBridgeServer.Shared.Start(new CliBridgeOptions
            {
                Host = host,
                Port = port,
                Token = token,
                RequireToken = requireToken
            });
            return getCliBridgeState();
        }

        [EvalFunction("Stop the Unity CLI bridge.")]
        public Dictionary<string, object?> stopCliBridge()
        {
            CliBridgeServer.Shared.Stop();
            return getCliBridgeState();
        }

        [EvalFunction("Return Unity CLI bridge state.")]
        public Dictionary<string, object?> getCliBridgeState()
        {
            var state = CliBridgeServer.Shared.State;
            return EvalData.Obj(
                ("isRunning", state.IsRunning),
                ("host", state.Host),
                ("port", state.Port),
                ("endpoint", state.Endpoint),
                ("transport", state.Transport),
                ("activeSessionCount", state.ActiveSessionCount),
                ("lastError", state.LastError),
                ("requireToken", state.RequireToken),
                ("token", state.Token)
            );
        }
    }
}
