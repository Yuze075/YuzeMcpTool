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
    function toText(value, images) {
        if (typeof value === 'undefined') return '(no return value)';
        if (value === null) return 'null';
        if (typeof value === 'object') {
            collectImages(value, images, []);
            try { return JSON.stringify(value, null, 2); } catch (_) { return String(value); }
        }
        return String(value);
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
            var text = toText(result, images);
            var content = [{ type: 'text', text: text }];
            for (var i = 0; i < images.length; i++) content.push(images[i]);
            onFinish.Invoke(JSON.stringify({ success: true, content: content }));
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
                McpBridge.CurrentContext = new McpBridgeContext(_session, requestId, cancellationToken);

                try
                {
                    var runner = _scriptEnv!.Eval<Action<Action<string>>>(BuildRunnerCode(code), "evalJsCode.runner");
                    runner(result => completion.TrySetResult(result));
                }
                catch (Exception ex)
                {
                    completion.TrySetResult(LitJson.Stringify(LitJson.Obj(
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

            await MainThreadDispatcher.RunAsync(() => McpBridge.CurrentContext = null);

            try
            {
                var parsed = LitJson.AsObject(LitJson.Parse(completion.Task.Result));
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
    backend: '" + PuerTsBackendFactory.SelectedBackendName + @"',
    invokeRaw: function(name, args) {
        return CS.YuzeToolkit.McpBridge.InvokeSync(String(name), JSON.stringify(args || {}));
    },
    invoke: function(name, args) {
        return JSON.parse(CS.YuzeToolkit.McpBridge.InvokeSync(String(name), JSON.stringify(args || {})));
    },
    invokeAsyncRaw: function(name, args) {
        return new Promise(function(resolve) {
            CS.YuzeToolkit.McpBridge.InvokeAsync(String(name), JSON.stringify(args || {}), function(result) {
                resolve(result);
            });
        });
    },
    invokeAsync: function(name, args) {
        return new Promise(function(resolve) {
            CS.YuzeToolkit.McpBridge.InvokeAsync(String(name), JSON.stringify(args || {}), function(result) {
                resolve(JSON.parse(result));
            });
        });
    }
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
            McpBridge.CurrentContext = null;
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
            LitJson.Obj(("success", false), ("error", message), ("stack", string.Empty));

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
