#nullable enable
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace YuzeToolkit
{
    internal static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Actions = new();
        private static int _mainThreadId;
        private static bool _initialized;
        private static Runner? _runner;
#if UNITY_EDITOR
        private static bool _hasEditorUpdateThread;
#endif

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            Initialize();
            EditorApplication.update -= Drain;
            EditorApplication.update += Drain;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRuntime() => Initialize();

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
#if UNITY_EDITOR
            EditorApplication.update -= Drain;
            EditorApplication.update += Drain;
#else
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EnsureRunner();
#endif
        }

        public static bool IsMainThread
        {
            get
            {
#if UNITY_EDITOR
                return _hasEditorUpdateThread && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
#else
                return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
#endif
            }
        }

        public static Task RunAsync(Action action)
        {
            if (IsMainThread)
            {
                try
                {
                    action();
                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var completion = new TaskCompletionSource<bool>();
            Actions.Enqueue(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        public static Task RunAsync(Func<Task> action)
        {
            if (IsMainThread)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Enqueue(() =>
            {
                try
                {
                    _ = CompleteAsync(action(), completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        public static Task RunAsync(Func<ValueTask> action)
        {
            if (IsMainThread)
            {
                try
                {
                    var valueTask = action();
                    if (valueTask.IsCompleted)
                    {
                        valueTask.GetAwaiter().GetResult();
                        return Task.CompletedTask;
                    }

                    return valueTask.AsTask();
                }
                catch (Exception ex)
                {
                    return Task.FromException(ex);
                }
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Enqueue(() =>
            {
                try
                {
                    _ = CompleteAsync(action(), completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        public static Task<T> RunAsync<T>(Func<T> action)
        {
            if (IsMainThread)
            {
                try
                {
                    return Task.FromResult(action());
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var completion = new TaskCompletionSource<T>();
            Actions.Enqueue(() =>
            {
                try
                {
                    completion.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        public static Task<T> RunAsync<T>(Func<Task<T>> action)
        {
            if (IsMainThread)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Enqueue(() =>
            {
                try
                {
                    _ = CompleteAsync(action(), completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        public static Task<T> RunAsync<T>(Func<ValueTask<T>> action)
        {
            if (IsMainThread)
            {
                try
                {
                    var valueTask = action();
                    if (valueTask.IsCompleted)
                        return Task.FromResult(valueTask.GetAwaiter().GetResult());

                    return valueTask.AsTask();
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Enqueue(() =>
            {
                try
                {
                    _ = CompleteAsync(action(), completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            EnsureRunner();
            return completion.Task;
        }

        private static async Task CompleteAsync(Task task, TaskCompletionSource<bool> completion)
        {
            try
            {
                await task;
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private static async Task CompleteAsync(ValueTask task, TaskCompletionSource<bool> completion)
        {
            try
            {
                await task;
                completion.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private static async Task CompleteAsync<T>(Task<T> task, TaskCompletionSource<T> completion)
        {
            try
            {
                completion.TrySetResult(await task);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private static async Task CompleteAsync<T>(ValueTask<T> task, TaskCompletionSource<T> completion)
        {
            try
            {
                completion.TrySetResult(await task);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private static void EnsureRunner()
        {
#if !UNITY_EDITOR
            if (_runner != null) return;
            var go = new GameObject("[MainThreadDispatcher]");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
#endif
        }

        private static void Drain()
        {
#if UNITY_EDITOR
            _hasEditorUpdateThread = true;
#endif
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;

            while (Actions.TryDequeue(out var action))
                action();
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update() => Drain();
        }
    }
}
