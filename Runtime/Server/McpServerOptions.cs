#nullable enable

namespace YuzeToolkit
{
    public sealed class McpServerOptions
    {
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 3100;

        public bool BindLocalhostAliases { get; set; } = true;

        public int MaxRequestBodyBytes { get; set; } = 1024 * 1024;

        public int DefaultEvalTimeoutSeconds { get; set; } = 30;

        public int MaxSessions { get; set; } = 16;

        public int SessionIdleTimeoutSeconds { get; set; } = 30 * 60;
    }
}
