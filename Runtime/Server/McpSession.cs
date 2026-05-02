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

        public int ActiveToolFunctionCount { get; internal set; }

        public string CurrentToolFunctionName { get; internal set; } = string.Empty;

        public DateTime CurrentToolFunctionStartedAtUtc { get; internal set; }

        public double CurrentToolFunctionElapsedMs { get; internal set; }

        public bool HasLastToolFunction { get; internal set; }

        public string LastToolFunctionName { get; internal set; } = string.Empty;

        public bool LastToolFunctionSucceeded { get; internal set; }

        public DateTime LastToolFunctionFinishedAtUtc { get; internal set; }

        public double LastToolFunctionDurationMs { get; internal set; }

        public string LastToolFunctionError { get; internal set; } = string.Empty;

        public int TotalToolFunctionCount { get; internal set; }

        public int FailedToolFunctionCount { get; internal set; }
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
        private int _activeToolFunctionCount;
        private string _currentToolFunctionName = string.Empty;
        private DateTime _currentToolFunctionStartedAtUtc;
        private bool _hasLastToolFunction;
        private string _lastToolFunctionName = string.Empty;
        private bool _lastToolFunctionSucceeded;
        private DateTime _lastToolFunctionFinishedAtUtc;
        private double _lastToolFunctionDurationMs;
        private string _lastToolFunctionError = string.Empty;
        private int _totalToolFunctionCount;
        private int _failedToolFunctionCount;
        private bool _disposeWhenEvalCompletes;

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

        public bool IsEvalRunning
        {
            get
            {
                lock (_syncRoot) return _evalStatus == McpLogicExecutionStatus.Running;
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

            DisposePendingEvalSession();
        }

        public void BeginToolFunction(string toolFunctionName)
        {
            lock (_syncRoot)
            {
                _activeToolFunctionCount++;
                _currentToolFunctionName = toolFunctionName;
                _currentToolFunctionStartedAtUtc = DateTime.UtcNow;
            }
        }

        public void CompleteToolFunction(string toolFunctionName, bool success, string error)
        {
            lock (_syncRoot)
            {
                var now = DateTime.UtcNow;
                var startedAt = _currentToolFunctionStartedAtUtc == default ? now : _currentToolFunctionStartedAtUtc;
                _hasLastToolFunction = true;
                _lastToolFunctionName = toolFunctionName;
                _lastToolFunctionSucceeded = success;
                _lastToolFunctionFinishedAtUtc = now;
                _lastToolFunctionDurationMs = Math.Max(0, (now - startedAt).TotalMilliseconds);
                _lastToolFunctionError = success ? string.Empty : error;
                _totalToolFunctionCount++;
                if (!success) _failedToolFunctionCount++;

                _activeToolFunctionCount = Math.Max(0, _activeToolFunctionCount - 1);
                if (_activeToolFunctionCount == 0)
                {
                    _currentToolFunctionName = string.Empty;
                    _currentToolFunctionStartedAtUtc = default;
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
                    ActiveToolFunctionCount = _activeToolFunctionCount,
                    CurrentToolFunctionName = _currentToolFunctionName,
                    CurrentToolFunctionStartedAtUtc = _currentToolFunctionStartedAtUtc,
                    CurrentToolFunctionElapsedMs = _currentToolFunctionStartedAtUtc == default ? 0 : Math.Max(0, (now - _currentToolFunctionStartedAtUtc).TotalMilliseconds),
                    HasLastToolFunction = _hasLastToolFunction,
                    LastToolFunctionName = _lastToolFunctionName,
                    LastToolFunctionSucceeded = _lastToolFunctionSucceeded,
                    LastToolFunctionFinishedAtUtc = _lastToolFunctionFinishedAtUtc,
                    LastToolFunctionDurationMs = _lastToolFunctionDurationMs,
                    LastToolFunctionError = _lastToolFunctionError,
                    TotalToolFunctionCount = _totalToolFunctionCount,
                    FailedToolFunctionCount = _failedToolFunctionCount,
                };
            }
        }

        public void Dispose()
        {
            if (IsEvalRunning)
            {
                lock (_syncRoot)
                    _disposeWhenEvalCompletes = true;
                return;
            }

            EvalSession?.Dispose();
            EvalSession = null;
        }

        private void DisposePendingEvalSession()
        {
            var shouldDispose = false;
            lock (_syncRoot)
            {
                if (_disposeWhenEvalCompletes && _evalStatus != McpLogicExecutionStatus.Running)
                {
                    _disposeWhenEvalCompletes = false;
                    shouldDispose = true;
                }
            }

            if (!shouldDispose) return;
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
