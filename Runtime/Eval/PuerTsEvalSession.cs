#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Puerts;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YuzeToolkit
{
    internal sealed class PuerTsEvalSession : IDisposable
    {
        private static string BuildRunnerCode(string userCode) => @"(function(onFinish) {
    function collectImages(value, images, visited) {
        if (!value || typeof value !== 'object') return;
        if (visited.indexOf(value) >= 0) return;
        visited.push(value);
        if (value.__image && value.__image.base64) {
            images.push({
                type: 'image',
                data: String(value.__image.base64),
                mimeType: String(value.__image.mimeType || value.__image.mediaType || 'image/png')
            });
            delete value.__image;
        }
        for (var key in value) {
            if (Object.prototype.hasOwnProperty.call(value, key)) {
                collectImages(value[key], images, visited);
            }
        }
    }
    function normalizeResult(value, images) {
        if (typeof value === 'undefined') return { hasValue: false, value: null };
        if (typeof value === 'object') {
            collectImages(value, images, []);
            try {
                JSON.stringify(value);
                return { hasValue: true, value: value };
            } catch (_) {
                return { hasValue: true, value: String(value), fallback: 'string' };
            }
        }
        return { hasValue: true, value: value };
    }
    Promise.resolve()
        .then(function() {
            var execute = (function(execute) {
" + userCode + @"
                if (typeof execute !== 'function') return null;
                return execute;
            })(undefined);
            if (typeof execute !== 'function') throw new Error('evalJsCode requires async function execute() { ... }.');
            return execute();
        })
        .then(function(result) {
            var images = [];
            var normalized = normalizeResult(result, images);
            onFinish.Invoke(JSON.stringify({
                success: true,
                hasValue: normalized.hasValue,
                result: normalized.value,
                fallback: normalized.fallback || '',
                images: images
            }));
        })
        .catch(function(err) {
            onFinish.Invoke(JSON.stringify({
                success: false,
                error: String((err && err.message) || err),
                stack: String((err && err.stack) || '')
            }));
        });
})";

        private readonly McpSession _session;
        private readonly string _sessionId;
        private readonly object _syncRoot = new();
        private ScriptEnv? _scriptEnv;
        private bool _isTicking;
        private bool _disposed;

        public PuerTsEvalSession(McpSession session)
        {
            _session = session;
            _sessionId = session.Id;
        }

        public async Task<Dictionary<string, object?>> ExecuteAsync(
            string requestId,
            string code,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(code))
                return Error("Code is empty.");

            timeoutSeconds = Mathf.Clamp(timeoutSeconds <= 0 ? 30 : timeoutSeconds, 1, 600);
            var completion = new TaskCompletionSource<string>();

            await MainThreadDispatcher.RunAsync(() =>
            {
                EnsureEnv();

                try
                {
                    var runner = _scriptEnv!.Eval<Action<Action<string>>>(BuildRunnerCode(code), "evalJsCode.runner");
                    runner(result => completion.TrySetResult(result));
                }
                catch (Exception ex)
                {
                    completion.TrySetResult(LitJson.Stringify(McpData.Obj(
                        ("success", false),
                        ("error", ex.Message),
                        ("stack", ex.StackTrace ?? string.Empty)
                    )));
                }
            });

            var delayTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            var completedTask = await Task.WhenAny(completion.Task, delayTask);
            if (completedTask != completion.Task)
            {
                await MainThreadDispatcher.RunAsync(() => DisposeEnv("evalJsCode timeout"));
                return Error($"Execution timed out after {timeoutSeconds}s. The eval VM for this MCP session was reset.");
            }

            try
            {
                var parsed = McpData.AsObject(LitJson.Parse(completion.Task.Result));
                return parsed ?? Error("evalJsCode returned invalid JSON.");
            }
            catch (Exception ex)
            {
                return Error($"Failed to parse evalJsCode result: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (MainThreadDispatcher.IsMainThread)
                DisposeEnv("session disposed");
            else
                _ = MainThreadDispatcher.RunAsync(() => DisposeEnv("session disposed"));
        }

        private void EnsureEnv()
        {
            if (_scriptEnv != null) return;

            var loader = new McpScriptLoader();
            _scriptEnv = PuerTsBackendFactory.Create(loader);
            StartTicking();
            InstallBootstrap();
        }

        private void InstallBootstrap()
        {
            _scriptEnv!.Eval(@"
globalThis.Mcp = {
    backend: '" + PuerTsBackendFactory.SelectedBackendName + @"'
};
", "Mcp.bootstrap");
        }

        private void StartTicking()
        {
            if (_isTicking) return;
            _isTicking = true;
#if UNITY_EDITOR
            EditorApplication.update += Tick;
#else
            RuntimeTicker.Ensure(Tick);
#endif
        }

        private void StopTicking()
        {
            if (!_isTicking) return;
            _isTicking = false;
#if UNITY_EDITOR
            EditorApplication.update -= Tick;
#else
            RuntimeTicker.Remove(Tick);
#endif
        }

        private void Tick()
        {
            lock (_syncRoot)
            {
                if (_scriptEnv == null) return;
                try
                {
                    _scriptEnv.Tick();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[YuzeMcpTool] PuerTS tick error in session {_sessionId}: {ex.Message}");
                }
            }
        }

        private void DisposeEnv(string reason)
        {
            StopTicking();
            if (_scriptEnv == null) return;

            try
            {
                _scriptEnv.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[YuzeMcpTool] Failed to dispose eval VM ({reason}): {ex.Message}");
            }
            finally
            {
                _scriptEnv = null;
            }
        }

        private static Dictionary<string, object?> Error(string message) =>
            McpData.Obj(("success", false), ("error", message), ("stack", string.Empty));

#if !UNITY_EDITOR
        private sealed class RuntimeTicker : MonoBehaviour
        {
            private static RuntimeTicker? _instance;
            private readonly List<Action> _ticks = new();

            public static void Ensure(Action tick)
            {
                if (_instance == null)
                {
                    var go = new GameObject("[McpPuerTsTicker]");
                    go.hideFlags = HideFlags.HideAndDontSave;
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<RuntimeTicker>();
                }

                if (!_instance._ticks.Contains(tick))
                    _instance._ticks.Add(tick);
            }

            public static void Remove(Action tick)
            {
                if (_instance == null) return;
                _instance._ticks.Remove(tick);
            }

            private void Update()
            {
                for (var i = 0; i < _ticks.Count; i++)
                    _ticks[i]?.Invoke();
            }
        }
#endif
    }
}
