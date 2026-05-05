#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("runtime", "Environment state and Unity log buffer access.")]
    public sealed class RuntimeTool
    {
        [EvalFunction("Return environment, Unity version, platform, play state, paths, active scene, and registered tools.")]
        public Dictionary<string, object?> getState()
        {
            var scene = SceneManager.GetActiveScene();
            var registeredTools = EvalToolRegistry.ListSummaries();
            return EvalData.Obj(
                ("environment", ToolUtilities.GetEnvironmentObject()),
                ("unityVersion", Application.unityVersion),
                ("platform", Application.platform.ToString()),
                ("isEditor", Application.isEditor),
                ("isRuntime", !Application.isEditor),
                ("isPlaying", Application.isPlaying),
                ("dataPath", Application.dataPath),
                ("persistentDataPath", Application.persistentDataPath),
                ("activeScene", EvalData.Obj(
                    ("name", scene.name),
                    ("path", scene.path),
                    ("isLoaded", scene.isLoaded),
                    ("rootCount", scene.rootCount)
                )),
                ("registeredToolCount", registeredTools.Count),
                ("registeredTools", registeredTools)
            );
        }

        [EvalFunction("Return captured Unity logs. Args: count?, type?.")]
        public List<object?> getRecentLogs(int count = 50, string type = "all")
        {
            return UnityLogBuffer.GetRecent(count, type);
        }

        [EvalFunction("Clear only the eval tool log buffer.")]
        public string clearLogs()
        {
            UnityLogBuffer.Clear();
            return "Unity eval tool log buffer cleared.";
        }

    }
}
