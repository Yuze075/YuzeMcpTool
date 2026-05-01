#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    internal sealed class ProjectExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["getSettings"] = new ProjectSettingsCommand(),
            ["profilerState"] = new ProfilerGetStateCommand(),
            ["toolState"] = new ToolGetStateCommand(),
        };

        public string Name => "project.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ImporterExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["get"] = new ImporterGetCommand(),
            ["setProperty"] = new ImporterSetPropertyCommand(),
            ["setMany"] = new ImporterSetManyCommand(),
            ["reimport"] = new ImporterReimportCommand(),
        };

        public string Name => "importer.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ImporterGetCommand : IMcpCommand
    {
        public string Name => "importer.get";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return Task.FromResult(McpBridge.Error($"Importer for '{path}' was not found."));
            return Task.FromResult(McpBridge.Success(Summarize(importer, LitJson.GetBool(args, "includeProperties", false))));
        }

        internal static object Summarize(AssetImporter importer, bool includeProperties)
        {
            var result = LitJson.Obj(
                ("assetPath", importer.assetPath),
                ("type", importer.GetType().FullName ?? importer.GetType().Name),
                ("userData", importer.userData),
                ("assetBundleName", importer.assetBundleName),
                ("assetBundleVariant", importer.assetBundleVariant)
            );

            if (importer is TextureImporter texture)
            {
                result["texture"] = LitJson.Obj(
                    ("textureType", texture.textureType.ToString()),
                    ("spriteImportMode", texture.spriteImportMode.ToString()),
                    ("spritePixelsPerUnit", texture.spritePixelsPerUnit),
                    ("mipmapEnabled", texture.mipmapEnabled),
                    ("alphaIsTransparency", texture.alphaIsTransparency),
                    ("isReadable", texture.isReadable),
                    ("maxTextureSize", texture.maxTextureSize),
                    ("textureCompression", texture.textureCompression.ToString()),
                    ("filterMode", texture.filterMode.ToString())
                );
            }
            else if (importer is AudioImporter audio)
            {
                var settings = audio.defaultSampleSettings;
                result["audio"] = LitJson.Obj(
                    ("loadType", settings.loadType.ToString()),
                    ("compressionFormat", settings.compressionFormat.ToString()),
                    ("quality", settings.quality),
                    ("sampleRateSetting", settings.sampleRateSetting.ToString())
                );
            }
            else if (importer is ModelImporter model)
            {
                result["model"] = LitJson.Obj(
                    ("importAnimation", model.importAnimation),
                    ("importCameras", model.importCameras),
                    ("importLights", model.importLights),
                    ("globalScale", model.globalScale)
                );
            }

            if (includeProperties)
            {
                var serialized = new SerializedObject(importer);
                var props = new List<object?>();
                var iterator = serialized.GetIterator();
                var enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    props.Add(SerializedGetCommand.SummarizeProperty(iterator));
                }
                result["properties"] = props;
            }

            return result;
        }
    }

    internal sealed class ImporterSetPropertyCommand : IMcpCommand
    {
        public string Name => "importer.setProperty";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return Task.FromResult(McpBridge.Error($"Importer for '{path}' was not found."));
            var propertyPath = CommandUtilities.GetString(args, "propertyPath");
            var serialized = new SerializedObject(importer);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null) return Task.FromResult(McpBridge.Error($"Importer property '{propertyPath}' was not found."));
            args.TryGetValue("value", out var value);
            SerializedSetCommand.SetPropertyValue(prop, value);
            serialized.ApplyModifiedProperties();
            if (LitJson.GetBool(args, "saveAndReimport", false))
                importer.SaveAndReimport();
            else
                EditorUtility.SetDirty(importer);
            return Task.FromResult(McpBridge.Success(ImporterGetCommand.Summarize(importer, false)));
        }
    }

    internal sealed class ImporterSetManyCommand : IMcpCommand
    {
        public string Name => "importer.setMany";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) return Task.FromResult(McpBridge.Error($"Importer for '{path}' was not found."));
            var changes = CommandUtilities.GetArray(args, "changes")
                .Select(LitJson.AsObject)
                .Where(change => change != null)
                .ToList();
            if (changes.Count == 0) return Task.FromResult(McpBridge.Error("Argument 'changes' must contain at least one importer property update."));

            var serialized = new SerializedObject(importer);
            var results = new List<object?>();
            foreach (var change in changes)
            {
                var propertyPath = CommandUtilities.GetString(change!, "propertyPath");
                var prop = serialized.FindProperty(propertyPath);
                if (prop == null) return Task.FromResult(McpBridge.Error($"Importer property '{propertyPath}' was not found."));
                change!.TryGetValue("value", out var value);
                SerializedSetCommand.SetPropertyValue(prop, value);
                results.Add(SerializedGetCommand.SummarizeProperty(prop));
            }

            serialized.ApplyModifiedProperties();
            if (LitJson.GetBool(args, "saveAndReimport", false))
                importer.SaveAndReimport();
            else
                EditorUtility.SetDirty(importer);
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("importer", ImporterGetCommand.Summarize(importer, false)), ("count", results.Count), ("properties", results))));
        }
    }

    internal sealed class ImporterReimportCommand : IMcpCommand
    {
        public string Name => "importer.reimport";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var path = CommandUtilities.GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(McpBridge.Error("Argument 'path' is required."));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path);
            return Task.FromResult(importer != null ? McpBridge.Success(ImporterGetCommand.Summarize(importer, false)) : McpBridge.Error($"Importer for '{path}' was not found after reimport."));
        }
    }

    internal sealed class PipelineExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["listPackages"] = new PipelinePackageListCommand(),
            ["addPackage"] = new PipelinePackageAddCommand(),
            ["removePackage"] = new PipelinePackageRemoveCommand(),
            ["searchPackages"] = new PipelinePackageSearchCommand(),
            ["getPackageRequest"] = new PipelinePackageRequestStatusCommand(),
            ["runTests"] = new PipelineTestRunCommand(),
            ["getTestRun"] = new PipelineTestStatusCommand(),
            ["getBuildSettings"] = new BuildGetSettingsCommand(),
            ["buildPlayer"] = new PipelineBuildPlayerCommand(),
            ["getBuild"] = new PipelineBuildStatusCommand(),
        };

        public string Name => "pipeline.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal static class PipelineRequestStore
    {
        public const string TestFrameworkUnavailableMessage = "Unity Test Framework support is unavailable. Install com.unity.test-framework 1.4.0 or newer so YUZE_USE_UNITY_TEST_FRAMEWORK is defined.";

        private static readonly Dictionary<string, TrackedPackageRequest> PackageRequests = new(StringComparer.Ordinal);
#if YUZE_USE_UNITY_TEST_FRAMEWORK
        private static readonly Dictionary<string, TrackedTestRun> TestRuns = new(StringComparer.Ordinal);
#endif
        private static readonly Dictionary<string, object> Builds = new(StringComparer.Ordinal);
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

        public static object GetPackageRequest(string id)
        {
            lock (SyncRoot)
            {
                if (!PackageRequests.TryGetValue(id, out var tracked))
                    return LitJson.Obj(("found", false), ("id", id));
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

        public static object GetTestRun(string id)
        {
            lock (SyncRoot)
            {
                if (!TestRuns.TryGetValue(id, out var tracked))
                    return LitJson.Obj(("found", false), ("id", id));
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

        public static string TrackBuild(object summary)
        {
            var id = Guid.NewGuid().ToString("N");
            lock (SyncRoot)
                Builds[id] = LitJson.Obj(("found", true), ("id", id), ("finishedAtUtc", DateTime.UtcNow.ToString("O")), ("summary", summary));
            return id;
        }

        public static object GetBuild(string id)
        {
            lock (SyncRoot)
                return Builds.TryGetValue(id, out var build) ? build : LitJson.Obj(("found", false), ("id", id));
        }

        private static object SummarizePackageRequest(TrackedPackageRequest tracked)
        {
            var request = tracked.Request;
            object? result = null;
            if (request.IsCompleted)
            {
                var resultProperty = request.GetType().GetProperty("Result");
                if (resultProperty != null)
                    result = SummarizePackageResult(resultProperty.GetValue(request));
            }

            return LitJson.Obj(
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

        public static object SummarizePackage(PackageInfo package) =>
            LitJson.Obj(
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

            public object ToObject() =>
                LitJson.Obj(
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

    internal sealed class PipelinePackageListCommand : IMcpCommand
    {
        public string Name => "pipeline.packageList";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var packages = PackageInfo.GetAllRegisteredPackages()
                .Select(package => (object?)PipelineRequestStore.SummarizePackage(package))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", packages.Count), ("packages", packages))));
        }
    }

    internal sealed class PipelinePackageAddCommand : IMcpCommand
    {
        public string Name => "pipeline.packageAdd";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false)) return Task.FromResult(McpBridge.Error("Package add requires confirm: true."));
            var packageId = CommandUtilities.GetString(args, "packageId");
            if (string.IsNullOrWhiteSpace(packageId)) return Task.FromResult(McpBridge.Error("Argument 'packageId' is required."));
            var request = Client.Add(packageId);
            var id = PipelineRequestStore.TrackPackageRequest("add", packageId, request);
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetPackageRequest(id)));
        }
    }

    internal sealed class PipelinePackageRemoveCommand : IMcpCommand
    {
        public string Name => "pipeline.packageRemove";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false)) return Task.FromResult(McpBridge.Error("Package removal requires confirm: true."));
            var packageName = CommandUtilities.GetString(args, "packageName");
            if (string.IsNullOrWhiteSpace(packageName)) return Task.FromResult(McpBridge.Error("Argument 'packageName' is required."));
            var request = Client.Remove(packageName);
            var id = PipelineRequestStore.TrackPackageRequest("remove", packageName, request);
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetPackageRequest(id)));
        }
    }

    internal sealed class PipelinePackageSearchCommand : IMcpCommand
    {
        public string Name => "pipeline.packageSearch";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var packageName = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "packageName");
            var request = Client.Search(packageName);
            var id = PipelineRequestStore.TrackPackageRequest("search", packageName, request);
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetPackageRequest(id)));
        }
    }

    internal sealed class PipelinePackageRequestStatusCommand : IMcpCommand
    {
        public string Name => "pipeline.packageRequestStatus";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var id = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "id");
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetPackageRequest(id)));
        }
    }

    internal sealed class PipelineTestRunCommand : IMcpCommand
    {
        public string Name => "pipeline.testRun";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var modeText = CommandUtilities.GetString(args, "mode", "EditMode");
            var mode = modeText.Equals("PlayMode", StringComparison.OrdinalIgnoreCase) ? TestMode.PlayMode : TestMode.EditMode;
            var filter = new Filter { testMode = mode };
            var executionId = PipelineRequestStore.TestRunner.Execute(new ExecutionSettings(filter));
            PipelineRequestStore.TrackTestRun(mode.ToString(), executionId);
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetTestRun(executionId)));
#else
            return Task.FromResult(McpBridge.Error(PipelineRequestStore.TestFrameworkUnavailableMessage));
#endif
        }
    }

    internal sealed class PipelineTestStatusCommand : IMcpCommand
    {
        public string Name => "pipeline.testStatus";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
#if YUZE_USE_UNITY_TEST_FRAMEWORK
            var id = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "id");
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetTestRun(id)));
#else
            return Task.FromResult(McpBridge.Error(PipelineRequestStore.TestFrameworkUnavailableMessage));
#endif
        }
    }

    internal sealed class PipelineBuildPlayerCommand : IMcpCommand
    {
        public string Name => "pipeline.buildPlayer";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            if (!LitJson.GetBool(args, "confirm", false)) return Task.FromResult(McpBridge.Error("Build requires confirm: true."));
            var locationPathName = CommandUtilities.GetString(args, "locationPathName");
            if (string.IsNullOrWhiteSpace(locationPathName)) return Task.FromResult(McpBridge.Error("Argument 'locationPathName' is required."));
            var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(), locationPathName, EditorUserBuildSettings.activeBuildTarget, BuildOptions.None);
            var summary = LitJson.Obj(
                ("result", report.summary.result.ToString()),
                ("totalErrors", report.summary.totalErrors),
                ("totalWarnings", report.summary.totalWarnings),
                ("outputPath", report.summary.outputPath)
            );
            var id = PipelineRequestStore.TrackBuild(summary);
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetBuild(id)));
        }
    }

    internal sealed class PipelineBuildStatusCommand : IMcpCommand
    {
        public string Name => "pipeline.buildStatus";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var id = CommandUtilities.GetString(CommandUtilities.ParseArgs(context.ArgumentsJson), "id");
            return Task.FromResult(McpBridge.Success(PipelineRequestStore.GetBuild(id)));
        }
    }

#if YUZE_USE_UNITY_TEST_FRAMEWORK
    internal sealed class PipelineTestCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }

        public void RunFinished(ITestResultAdaptor result) => PipelineRequestStore.CompleteRunningTests(result);

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }
    }
#endif

    internal sealed class ValidationExecuteCommand : IMcpCommand
    {
        private static readonly IReadOnlyDictionary<string, IMcpCommand> Actions = new Dictionary<string, IMcpCommand>(StringComparer.Ordinal)
        {
            ["run"] = new ValidationRunCommand(),
            ["missingScripts"] = new ValidationMissingScriptsCommand(),
            ["missingReferences"] = new ValidationMissingReferencesCommand(),
            ["serializedFieldTooltips"] = new ValidationSerializedFieldTooltipsCommand(),
        };

        public string Name => "validation.execute";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            CompactCommandDispatcher.ExecuteAsync(context, Actions, Name);
    }

    internal sealed class ValidationRunCommand : IMcpCommand
    {
        public string Name => "validation.run";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var argsJson = context.ArgumentsJson;
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("missingScripts", ValidationMissingScriptsCommand.GetResult(argsJson)),
                ("missingReferences", ValidationMissingReferencesCommand.GetResult(argsJson)),
                ("serializedFieldTooltips", ValidationSerializedFieldTooltipsCommand.GetResult(argsJson))
            )));
        }
    }

    internal sealed class ValidationMissingScriptsCommand : IMcpCommand
    {
        public string Name => "validation.missingScripts";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            Task.FromResult(McpBridge.Success(GetResult(context.ArgumentsJson)));

        internal static object GetResult(string argumentsJson)
        {
            var issues = new List<object?>();
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go != null && CommandUtilities.IsUsableSceneObject(go, true)))
            {
                var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (count <= 0) continue;
                issues.Add(LitJson.Obj(("scope", "scene"), ("count", count), ("gameObject", CommandUtilities.SummarizeGameObject(go, false))));
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    issues.Add(LitJson.Obj(("scope", "prefab"), ("path", path), ("error", "Prefab asset could not be loaded.")));
                    continue;
                }
                var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab);
                if (count > 0)
                    issues.Add(LitJson.Obj(("scope", "prefab"), ("path", path), ("count", count)));
            }

            return LitJson.Obj(("count", issues.Count), ("issues", issues));
        }
    }

    internal sealed class ValidationMissingReferencesCommand : IMcpCommand
    {
        public string Name => "validation.missingReferences";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            Task.FromResult(McpBridge.Success(GetResult(context.ArgumentsJson)));

        internal static object GetResult(string argumentsJson)
        {
            var args = CommandUtilities.ParseArgs(argumentsJson);
            var limit = Math.Max(1, LitJson.GetInt(args, "limit", 200));
            var issues = new List<object?>();

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>().Where(go => go != null && CommandUtilities.IsUsableSceneObject(go, true)))
            {
                CollectMissingReferences(go, CommandUtilities.GetPath(go), issues, limit);
                if (issues.Count >= limit) break;
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    CollectMissingReferences(component, CommandUtilities.GetPath(go) + "/" + component.GetType().Name, issues, limit);
                    if (issues.Count >= limit) break;
                }
                if (issues.Count >= limit) break;
            }

            return LitJson.Obj(("count", issues.Count), ("issues", issues));
        }

        private static void CollectMissingReferences(UnityEngine.Object obj, string owner, List<object?> issues, int limit)
        {
            try
            {
                var serialized = new SerializedObject(obj);
                var iterator = serialized.GetIterator();
                var enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                        iterator.objectReferenceValue == null &&
                        iterator.objectReferenceInstanceIDValue != 0)
                    {
                        issues.Add(LitJson.Obj(
                            ("owner", owner),
                            ("type", obj.GetType().FullName ?? obj.GetType().Name),
                            ("propertyPath", iterator.propertyPath)
                        ));
                        if (issues.Count >= limit) return;
                    }
                }
            }
            catch
            {
                // Some editor-only objects cannot be serialized safely; skip them.
            }
        }
    }

    internal sealed class ValidationSerializedFieldTooltipsCommand : IMcpCommand
    {
        public string Name => "validation.serializedFieldTooltips";
        public bool EditorOnly => true;

        public Task<string> ExecuteAsync(McpCommandContext context) =>
            Task.FromResult(McpBridge.Success(GetResult(context.ArgumentsJson)));

        internal static object GetResult(string argumentsJson)
        {
            var args = CommandUtilities.ParseArgs(argumentsJson);
            var folders = CommandUtilities.GetArray(args, "folders").OfType<string>().ToList();
            var limit = Math.Max(0, LitJson.GetInt(args, "limit", 0));
            if (folders.Count == 0)
            {
                folders.Add("Assets");
                folders.Add("Packages/com.yuzetoolkit.mcptool");
            }

            var root = CommandUtilities.GetProjectRoot();
            var issues = new List<object?>();
            foreach (var folder in folders)
            {
                if (!CommandUtilities.TryResolveProjectPath(folder, out var fullFolder, out _, out _)) continue;
                if (!Directory.Exists(fullFolder)) continue;
                foreach (var file in Directory.GetFiles(fullFolder, "*.cs", SearchOption.AllDirectories))
                {
                    ScanFile(root, file, issues);
                    if (limit > 0 && issues.Count >= limit)
                        return LitJson.Obj(("count", issues.Count), ("issues", issues));
                }
            }

            return LitJson.Obj(("count", issues.Count), ("issues", issues));
        }

        private static void ScanFile(string root, string file, List<object?> issues)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.Contains("SerializeField", StringComparison.Ordinal)) continue;
                if (HasNearbyTooltip(lines, i)) continue;
                issues.Add(LitJson.Obj(
                    ("path", file.Substring(root.Length + 1).Replace(Path.DirectorySeparatorChar, '/')),
                    ("line", i + 1),
                    ("text", trimmed)
                ));
            }
        }

        private static bool HasNearbyTooltip(string[] lines, int index)
        {
            for (var i = index; i >= 0 && index - i <= 8; i--)
            {
                var text = lines[i].Trim();
                if (text.Contains("Tooltip(", StringComparison.Ordinal)) return true;
                if (i < index && text.Length > 0 && !text.StartsWith("[", StringComparison.Ordinal)) break;
            }
            return false;
        }
    }

#if YUZE_USE_UNITY_TEST_FRAMEWORK
    internal static class TestResultSummaryUtility
    {
        public static object Summarize(ITestResultAdaptor result, int depth)
        {
            var children = new List<object?>();
            if (depth > 0 && result.Children != null)
            {
                foreach (var child in result.Children)
                    children.Add(Summarize(child, depth - 1));
            }

            return LitJson.Obj(
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
