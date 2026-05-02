#nullable enable
using System;
using System.Collections.Generic;
using Puerts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YuzeToolkit
{
    /// <summary>
    /// PuerTS generation configuration for the Unity MCP eval environment.
    /// Use Tools > PuerTS > Generate C# Static Wrappers for bindings, or Generate index.d.ts for typings.
    /// </summary>
    [Configure]
    public sealed class PuertsCfg
    {
        [Binding]
        private static IEnumerable<Type> Bindings
        {
            get
            {
                return new List<Type>
                {
                    typeof(McpToolRegistry),
                    typeof(McpValueFormatter),
                    typeof(GameObject),
                    typeof(Component),
                    typeof(Transform),
                    typeof(Camera),
                    typeof(UnityEngine.Object),
                    typeof(Vector2),
                    typeof(Vector3),
                    typeof(Quaternion),
                    typeof(Color),
                    typeof(Action<string>),
                    typeof(Action<Action<string>>),
                };
            }
        }

        [Typing]
        private static IEnumerable<Type> Typings
        {
            get
            {
                return new List<Type>
                {
                    typeof(Resources),
                    typeof(TextAsset),
                    typeof(Debug),
                    typeof(Application),
                    typeof(Time),
                    typeof(Screen),
                    typeof(Mathf),
                    typeof(System.Array),
                    typeof(Puerts.ScriptEnv),
                    typeof(AssetDatabase),
                    typeof(Selection),
                    typeof(EditorApplication),
                    typeof(EditorUtility),
                    typeof(Undo),
                    typeof(PrefabUtility),
                    typeof(EditorSceneManager),
                };
            }
        }
    }
}
