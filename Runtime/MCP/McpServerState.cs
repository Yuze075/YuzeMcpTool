#nullable enable
using System;
using System.Collections.Generic;

namespace YuzeToolkit
{
    public sealed class McpServerState
    {
        public bool IsRunning { get; internal set; }

        public string Endpoint { get; internal set; } = string.Empty;

        public string LastError { get; internal set; } = string.Empty;

        public DateTime StartedAtUtc { get; internal set; }

        public int ActiveSessionCount { get; internal set; }

        public bool RequireToken { get; internal set; }

        public string Token { get; internal set; } = string.Empty;

        public double UptimeSeconds { get; internal set; }

        public IReadOnlyList<EvalSessionSnapshot> Sessions { get; internal set; } = Array.Empty<EvalSessionSnapshot>();

        public string Status => IsRunning ? "running" : "stopped";

        public string Environment => ToolUtilities.GetEnvironmentName();
    }
}
