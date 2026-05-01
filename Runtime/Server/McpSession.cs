#nullable enable
using System;

namespace YuzeToolkit
{
    public enum McpLogicExecutionStatus
    {
        Idle,
        Running,
        Succeeded,
        Failed
    }

    public sealed class McpSessionSnapshot
    {
        public string Id { get; internal set; } = string.Empty;

        public string ProtocolVersion { get; internal set; } = string.Empty;

        public string ClientName { get; internal set; } = string.Empty;

        public DateTime CreatedAtUtc { get; internal set; }

        public DateTime LastSeenUtc { get; internal set; }

        public bool HasEvalSession { get; internal set; }

        public McpLogicExecutionStatus EvalStatus { get; internal set; }

        public string CurrentRequestId { get; internal set; } = string.Empty;

        public string CurrentCodeSummary { get; internal set; } = string.Empty;

        public DateTime CurrentEvalStartedAtUtc { get; internal set; }

        public double CurrentEvalElapsedMs { get; internal set; }

        public int CurrentTimeoutSeconds { get; internal set; }

        public bool CurrentResetSession { get; internal set; }

        public bool HasLastEval { get; internal set; }

        public string LastRequestId { get; internal set; } = string.Empty;

        public McpLogicExecutionStatus LastEvalStatus { get; internal set; }

        public DateTime LastEvalStartedAtUtc { get; internal set; }

        public DateTime LastEvalFinishedAtUtc { get; internal set; }

        public double LastEvalDurationMs { get; internal set; }

        public string LastEvalError { get; internal set; } = string.Empty;

        public int TotalEvalCount { get; internal set; }

        public int FailedEvalCount { get; internal set; }

        public int ActiveCommandCount { get; internal set; }

        public string CurrentCommandName { get; internal set; } = string.Empty;

        public DateTime CurrentCommandStartedAtUtc { get; internal set; }

        public double CurrentCommandElapsedMs { get; internal set; }

        public bool HasLastCommand { get; internal set; }

        public string LastCommandName { get; internal set; } = string.Empty;

        public bool LastCommandSucceeded { get; internal set; }

        public DateTime LastCommandFinishedAtUtc { get; internal set; }

        public double LastCommandDurationMs { get; internal set; }

        public string LastCommandError { get; internal set; } = string.Empty;

        public int TotalCommandCount { get; internal set; }

        public int FailedCommandCount { get; internal set; }
    }

    internal sealed class McpSession : IDisposable
    {
        private const int CodeSummaryMaxLength = 180;
        private readonly object _syncRoot = new();
        private DateTime _lastSeenUtc;
        private McpLogicExecutionStatus _evalStatus = McpLogicExecutionStatus.Idle;
        private string _currentRequestId = string.Empty;
        private string _currentCodeSummary = string.Empty;
        private DateTime _currentEvalStartedAtUtc;
        private int _currentTimeoutSeconds;
        private bool _currentResetSession;
        private bool _hasLastEval;
        private string _lastRequestId = string.Empty;
        private McpLogicExecutionStatus _lastEvalStatus = McpLogicExecutionStatus.Idle;
        private DateTime _lastEvalStartedAtUtc;
        private DateTime _lastEvalFinishedAtUtc;
        private double _lastEvalDurationMs;
        private string _lastEvalError = string.Empty;
        private int _totalEvalCount;
        private int _failedEvalCount;
        private int _activeCommandCount;
        private string _currentCommandName = string.Empty;
        private DateTime _currentCommandStartedAtUtc;
        private bool _hasLastCommand;
        private string _lastCommandName = string.Empty;
        private bool _lastCommandSucceeded;
        private DateTime _lastCommandFinishedAtUtc;
        private double _lastCommandDurationMs;
        private string _lastCommandError = string.Empty;
        private int _totalCommandCount;
        private int _failedCommandCount;

        public McpSession(string id, string protocolVersion, string clientName)
        {
            Id = id;
            ProtocolVersion = protocolVersion;
            ClientName = clientName;
            CreatedAtUtc = DateTime.UtcNow;
            _lastSeenUtc = CreatedAtUtc;
        }

        public string Id { get; }

        public string ProtocolVersion { get; }

        public string ClientName { get; }

        public DateTime CreatedAtUtc { get; }

        public DateTime LastSeenUtc
        {
            get
            {
                lock (_syncRoot) return _lastSeenUtc;
            }
        }

        public PuerTsEvalSession? EvalSession { get; set; }

        public void Touch()
        {
            lock (_syncRoot)
                _lastSeenUtc = DateTime.UtcNow;
        }

        public void BeginEval(string requestId, string code, int timeoutSeconds, bool resetSession)
        {
            lock (_syncRoot)
            {
                _evalStatus = McpLogicExecutionStatus.Running;
                _currentRequestId = requestId;
                _currentCodeSummary = SummarizeCode(code);
                _currentEvalStartedAtUtc = DateTime.UtcNow;
                _currentTimeoutSeconds = timeoutSeconds;
                _currentResetSession = resetSession;
            }
        }

        public void CompleteEval(string requestId, bool success, string error)
        {
            lock (_syncRoot)
            {
                var now = DateTime.UtcNow;
                var startedAt = _currentEvalStartedAtUtc == default ? now : _currentEvalStartedAtUtc;
                _hasLastEval = true;
                _lastRequestId = string.IsNullOrWhiteSpace(requestId) ? _currentRequestId : requestId;
                _lastEvalStatus = success ? McpLogicExecutionStatus.Succeeded : McpLogicExecutionStatus.Failed;
                _lastEvalStartedAtUtc = startedAt;
                _lastEvalFinishedAtUtc = now;
                _lastEvalDurationMs = Math.Max(0, (now - startedAt).TotalMilliseconds);
                _lastEvalError = success ? string.Empty : error;
                _totalEvalCount++;
                if (!success) _failedEvalCount++;

                _evalStatus = McpLogicExecutionStatus.Idle;
                _currentRequestId = string.Empty;
                _currentCodeSummary = string.Empty;
                _currentEvalStartedAtUtc = default;
                _currentTimeoutSeconds = 0;
                _currentResetSession = false;
            }
        }

        public void BeginCommand(string commandName)
        {
            lock (_syncRoot)
            {
                _activeCommandCount++;
                _currentCommandName = commandName;
                _currentCommandStartedAtUtc = DateTime.UtcNow;
            }
        }

        public void CompleteCommand(string commandName, bool success, string error)
        {
            lock (_syncRoot)
            {
                var now = DateTime.UtcNow;
                var startedAt = _currentCommandStartedAtUtc == default ? now : _currentCommandStartedAtUtc;
                _hasLastCommand = true;
                _lastCommandName = commandName;
                _lastCommandSucceeded = success;
                _lastCommandFinishedAtUtc = now;
                _lastCommandDurationMs = Math.Max(0, (now - startedAt).TotalMilliseconds);
                _lastCommandError = success ? string.Empty : error;
                _totalCommandCount++;
                if (!success) _failedCommandCount++;

                _activeCommandCount = Math.Max(0, _activeCommandCount - 1);
                if (_activeCommandCount == 0)
                {
                    _currentCommandName = string.Empty;
                    _currentCommandStartedAtUtc = default;
                }
            }
        }

        public McpSessionSnapshot ToSnapshot()
        {
            lock (_syncRoot)
            {
                var now = DateTime.UtcNow;
                return new McpSessionSnapshot
                {
                    Id = Id,
                    ProtocolVersion = ProtocolVersion,
                    ClientName = ClientName,
                    CreatedAtUtc = CreatedAtUtc,
                    LastSeenUtc = _lastSeenUtc,
                    HasEvalSession = EvalSession != null,
                    EvalStatus = _evalStatus,
                    CurrentRequestId = _currentRequestId,
                    CurrentCodeSummary = _currentCodeSummary,
                    CurrentEvalStartedAtUtc = _currentEvalStartedAtUtc,
                    CurrentEvalElapsedMs = _currentEvalStartedAtUtc == default ? 0 : Math.Max(0, (now - _currentEvalStartedAtUtc).TotalMilliseconds),
                    CurrentTimeoutSeconds = _currentTimeoutSeconds,
                    CurrentResetSession = _currentResetSession,
                    HasLastEval = _hasLastEval,
                    LastRequestId = _lastRequestId,
                    LastEvalStatus = _lastEvalStatus,
                    LastEvalStartedAtUtc = _lastEvalStartedAtUtc,
                    LastEvalFinishedAtUtc = _lastEvalFinishedAtUtc,
                    LastEvalDurationMs = _lastEvalDurationMs,
                    LastEvalError = _lastEvalError,
                    TotalEvalCount = _totalEvalCount,
                    FailedEvalCount = _failedEvalCount,
                    ActiveCommandCount = _activeCommandCount,
                    CurrentCommandName = _currentCommandName,
                    CurrentCommandStartedAtUtc = _currentCommandStartedAtUtc,
                    CurrentCommandElapsedMs = _currentCommandStartedAtUtc == default ? 0 : Math.Max(0, (now - _currentCommandStartedAtUtc).TotalMilliseconds),
                    HasLastCommand = _hasLastCommand,
                    LastCommandName = _lastCommandName,
                    LastCommandSucceeded = _lastCommandSucceeded,
                    LastCommandFinishedAtUtc = _lastCommandFinishedAtUtc,
                    LastCommandDurationMs = _lastCommandDurationMs,
                    LastCommandError = _lastCommandError,
                    TotalCommandCount = _totalCommandCount,
                    FailedCommandCount = _failedCommandCount,
                };
            }
        }

        public void Dispose()
        {
            EvalSession?.Dispose();
            EvalSession = null;
        }

        private static string SummarizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "(empty)";
            var summary = code.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            while (summary.Contains("  "))
                summary = summary.Replace("  ", " ");
            return summary.Length <= CodeSummaryMaxLength ? summary : summary[..CodeSummaryMaxLength] + "...";
        }
    }
}
