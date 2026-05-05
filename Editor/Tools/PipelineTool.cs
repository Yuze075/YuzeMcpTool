#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
#if YUZE_USE_UNITY_TEST_FRAMEWORK
using UnityEditor.TestTools.TestRunner.Api;
#endif
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("pipeline", "Package Manager, Test Runner, and BuildPipeline workflows.")]
    public sealed class PipelineTool
    {
        [EvalFunction("List packages.")]
        public Dictionary<string, object?> listPackages()
        {
            var packages = PackageInfo.GetAllRegisteredPackages()
                .Select(package => (object?)PipelineRequestStore.SummarizePackage(package))
                .ToList();
            return EvalData.Obj(("count", packages.Count), ("packages", packages));
        }

        [EvalFunction("Add package.")]
        public Dictionary<string, object?> addPackage(string packageId, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Package add requires confirm: true.");
            if (string.IsNullOrWhiteSpace(packageId)) throw new InvalidOperationException("Argument 'packageId' is required.");
            var request = Client.Add(packageId);
            var id = PipelineRequestStore.TrackPackageRequest("add", packageId, request);
            return PipelineRequestStore.GetPackageRequest(id);
        }

        [EvalFunction("Remove package.")]
        public Dictionary<string, object?> removePackage(string packageName, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Package removal requires confirm: true.");
            if (string.IsNullOrWhiteSpace(packageName)) throw new InvalidOperationException("Argument 'packageName' is required.");
            var request = Client.Remove(packageName);
            var id = PipelineRequestStore.TrackPackageRequest("remove", packageName, request);
            return PipelineRequestStore.GetPackageRequest(id);
        }

        [EvalFunction("Search packages.")]
        public Dictionary<string, object?> searchPackages(string packageName = "")
        {
            var request = Client.Search(packageName);
            var id = PipelineRequestStore.TrackPackageRequest("search", packageName, request);
            return PipelineRequestStore.GetPackageRequest(id);
        }

        [EvalFunction("Read package request status.")]
        public Dictionary<string, object?> getPackageRequest(string id) => PipelineRequestStore.GetPackageRequest(id);

        [EvalFunction("Start tests.")]
        public Dictionary<string, object?> runTests(string mode = "EditMode", string testName = "")
        {
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            var testMode = mode.Equals("PlayMode", StringComparison.OrdinalIgnoreCase) ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = testMode };
            if (!string.IsNullOrWhiteSpace(testName))
                filter.testNames = new[] { testName };
            var executionId = PipelineRequestStore.TestRunner.Execute(new ExecutionSettings(filter));
            PipelineRequestStore.TrackTestRun(testMode.ToString(), executionId);
            return PipelineRequestStore.GetTestRun(executionId);
#else
            throw new InvalidOperationException(PipelineRequestStore.TestFrameworkUnavailableMessage);
#endif
        }

        [EvalFunction("Read test run status.")]
        public Dictionary<string, object?> getTestRun(string id)
        {
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            return PipelineRequestStore.GetTestRun(id);
#else
            throw new InvalidOperationException(PipelineRequestStore.TestFrameworkUnavailableMessage);
#endif
        }

        [EvalFunction("Read build settings.")]
        public Dictionary<string, object?> getBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Select(scene => (object?)EvalData.Obj(("path", scene.path), ("enabled", scene.enabled), ("guid", scene.guid.ToString())))
                .ToList();
            return EvalData.Obj(
                ("activeBuildTarget", EditorUserBuildSettings.activeBuildTarget.ToString()),
                ("selectedBuildTargetGroup", EditorUserBuildSettings.selectedBuildTargetGroup.ToString()),
                ("scenes", scenes));
        }

        [EvalFunction("Build player.")]
        public Dictionary<string, object?> buildPlayer(string locationPathName, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Build requires confirm: true.");
            if (string.IsNullOrWhiteSpace(locationPathName)) throw new InvalidOperationException("Argument 'locationPathName' is required.");
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), locationPathName, EditorUserBuildSettings.activeBuildTarget, BuildOptions.None);
            var summary = EvalData.Obj(
                ("result", report.summary.result.ToString()),
                ("totalErrors", report.summary.totalErrors),
                ("totalWarnings", report.summary.totalWarnings),
                ("outputPath", report.summary.outputPath)
            );
            var id = PipelineRequestStore.TrackBuild(summary);
            return PipelineRequestStore.GetBuild(id);
        }

        [EvalFunction("Read build result.")]
        public Dictionary<string, object?> getBuild(string id) => PipelineRequestStore.GetBuild(id);

        private static class PipelineRequestStore
        {
            public const string TestFrameworkUnavailableMessage = "Unity Test Framework support is unavailable. Install com.unity.test-framework 1.4.0 or newer so YUZE_USE_UNITY_TEST_FRAMEWORK is defined.";

            private static readonly Dictionary<string, TrackedPackageRequest> PackageRequests = new(StringComparer.Ordinal);
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            private static readonly Dictionary<string, TrackedTestRun> TestRuns = new(StringComparer.Ordinal);
#endif
            private static readonly Dictionary<string, Dictionary<string, object?>> Builds = new(StringComparer.Ordinal);
            private static readonly object SyncRoot = new();
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            private static TestRunnerApi? _testRunner;
            private static bool _callbacksRegistered;
#endif

            public static string TrackPackageRequest(string kind, string label, Request request)
            {
                var id = Guid.NewGuid().ToString("N");
                lock (SyncRoot)
                    PackageRequests[id] = new TrackedPackageRequest(id, kind, label, request, DateTime.UtcNow);
                return id;
            }

            public static Dictionary<string, object?> GetPackageRequest(string id)
            {
                lock (SyncRoot)
                {
                    if (!PackageRequests.TryGetValue(id, out var tracked))
                        return EvalData.Obj(("found", false), ("id", id));
                    return SummarizePackageRequest(tracked);
                }
            }

#if YUZE_USE_UNITY_TEST_FRAMEWORK
            public static string TrackTestRun(string mode, string executionId)
            {
                lock (SyncRoot)
                    TestRuns[executionId] = new TrackedTestRun(executionId, mode, DateTime.UtcNow);
                return executionId;
            }

            public static Dictionary<string, object?> GetTestRun(string id)
            {
                lock (SyncRoot)
                {
                    if (!TestRuns.TryGetValue(id, out var tracked))
                        return EvalData.Obj(("found", false), ("id", id));
                    return tracked.ToObject();
                }
            }

            public static void EnsureTestCallbacks()
            {
                if (_callbacksRegistered) return;
                _testRunner = ScriptableObject.CreateInstance<TestRunnerApi>();
                _testRunner.RegisterCallbacks(new PipelineTestCallbacks());
                _callbacksRegistered = true;
            }

            public static TestRunnerApi TestRunner
            {
                get
                {
                    EnsureTestCallbacks();
                    return _testRunner!;
                }
            }

            public static void CompleteRunningTests(ITestResultAdaptor result)
            {
                lock (SyncRoot)
                {
                    foreach (var tracked in TestRuns.Values.Where(run => run.Status == "Running"))
                        tracked.Complete(result);
                }
            }
#endif

            public static string TrackBuild(Dictionary<string, object?> summary)
            {
                var id = Guid.NewGuid().ToString("N");
                lock (SyncRoot)
                    Builds[id] = EvalData.Obj(("found", true), ("id", id), ("finishedAtUtc", DateTime.UtcNow.ToString("O")), ("summary", summary));
                return id;
            }

            public static Dictionary<string, object?> GetBuild(string id)
            {
                lock (SyncRoot)
                    return Builds.TryGetValue(id, out var build) ? build : EvalData.Obj(("found", false), ("id", id));
            }

            private static Dictionary<string, object?> SummarizePackageRequest(TrackedPackageRequest tracked)
            {
                var request = tracked.Request;
                object? result = null;
                if (request.IsCompleted)
                {
                    var resultProperty = request.GetType().GetProperty("Result");
                    if (resultProperty != null)
                        result = SummarizePackageResult(resultProperty.GetValue(request));
                }

                return EvalData.Obj(
                    ("found", true),
                    ("id", tracked.Id),
                    ("kind", tracked.Kind),
                    ("label", tracked.Label),
                    ("startedAtUtc", tracked.StartedAtUtc.ToString("O")),
                    ("isCompleted", request.IsCompleted),
                    ("status", request.Status.ToString()),
                    ("error", request.Error != null ? request.Error.message : string.Empty),
                    ("result", result)
                );
            }

            private static object? SummarizePackageResult(object? result)
            {
                if (result == null) return null;
                if (result is PackageInfo package) return SummarizePackage(package);
                if (result is IEnumerable<PackageInfo> packages)
                    return packages.Select(package => (object?)SummarizePackage(package)).ToList();
                return result.ToString();
            }

            public static Dictionary<string, object?> SummarizePackage(PackageInfo package) =>
                EvalData.Obj(
                    ("name", package.name),
                    ("displayName", package.displayName),
                    ("version", package.version),
                    ("source", package.source.ToString()),
                    ("assetPath", package.assetPath)
                );

            private sealed class TrackedPackageRequest
            {
                public TrackedPackageRequest(string id, string kind, string label, Request request, DateTime startedAtUtc)
                {
                    Id = id;
                    Kind = kind;
                    Label = label;
                    Request = request;
                    StartedAtUtc = startedAtUtc;
                }

                public string Id { get; }
                public string Kind { get; }
                public string Label { get; }
                public Request Request { get; }
                public DateTime StartedAtUtc { get; }
            }

#if YUZE_USE_UNITY_TEST_FRAMEWORK
            private sealed class TrackedTestRun
            {
                private object? _result;

                public TrackedTestRun(string id, string mode, DateTime startedAtUtc)
                {
                    Id = id;
                    Mode = mode;
                    StartedAtUtc = startedAtUtc;
                    Status = "Running";
                }

                public string Id { get; }
                public string Mode { get; }
                public DateTime StartedAtUtc { get; }
                public DateTime FinishedAtUtc { get; private set; }
                public string Status { get; private set; }

                public void Complete(ITestResultAdaptor result)
                {
                    Status = result.TestStatus.ToString();
                    FinishedAtUtc = DateTime.UtcNow;
                    _result = TestResultSummaryUtility.Summarize(result, 2);
                }

                public Dictionary<string, object?> ToObject() =>
                    EvalData.Obj(
                        ("found", true),
                        ("id", Id),
                        ("mode", Mode),
                        ("status", Status),
                        ("startedAtUtc", StartedAtUtc.ToString("O")),
                        ("finishedAtUtc", FinishedAtUtc == default ? string.Empty : FinishedAtUtc.ToString("O")),
                        ("result", _result)
                    );
            }
#endif
        }

#if YUZE_USE_UNITY_TEST_FRAMEWORK
        private sealed class PipelineTestCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result) => PipelineRequestStore.CompleteRunningTests(result);

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result) { }
        }

        private static class TestResultSummaryUtility
        {
            public static object Summarize(ITestResultAdaptor result, int depth)
            {
                var children = new List<object?>();
                if (depth > 0 && result.Children != null)
                {
                    foreach (var child in result.Children)
                        children.Add(Summarize(child, depth - 1));
                }

                return EvalData.Obj(
                    ("name", result.Name),
                    ("fullName", result.FullName),
                    ("testStatus", result.TestStatus.ToString()),
                    ("resultState", result.ResultState),
                    ("duration", result.Duration),
                    ("passCount", result.PassCount),
                    ("failCount", result.FailCount),
                    ("skipCount", result.SkipCount),
                    ("message", result.Message),
                    ("stackTrace", result.StackTrace),
                    ("children", children)
                );
            }
        }
#endif
    }
}
