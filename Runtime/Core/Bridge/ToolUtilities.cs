#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace YuzeToolkit
{
    public static class ToolUtilities
    {
        public static Dictionary<string, object?> ParseArgs(string json) =>
            EvalData.AsObject(LitJson.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json)) ?? new Dictionary<string, object?>();

        public static string GetString(Dictionary<string, object?> args, string key, string defaultValue = "") =>
            EvalData.GetString(args, key) ?? defaultValue;

        public static List<object?> GetArray(Dictionary<string, object?> args, string key)
        {
            return args.TryGetValue(key, out var value) ? EvalData.AsArray(value) ?? new List<object?>() : new List<object?>();
        }

        public static string GetEnvironmentName() => Application.isEditor ? "Editor" : "Runtime";

        public static object GetEnvironmentObject() =>
            EvalData.Obj(
                ("name", GetEnvironmentName()),
                ("isEditor", Application.isEditor),
                ("isRuntime", !Application.isEditor));

        public static string GetProjectRoot()
        {
            return TrimTrailingSeparators(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
        }

        public static bool TryResolveProjectPath(string path, out string fullPath, out string projectRelativePath, out string error)
        {
            fullPath = string.Empty;
            projectRelativePath = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is required.";
                return false;
            }

            var root = GetProjectRoot();
            var candidate = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(root, path));
            candidate = TrimTrailingSeparators(candidate);

            var rootWithSeparator = EnsureTrailingSeparator(root);
            if (!candidate.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                !candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                error = "Path must stay inside the Unity project.";
                return false;
            }

            fullPath = candidate;
            projectRelativePath = candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : candidate.Substring(rootWithSeparator.Length)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
            return true;
        }

        public static void RecordUndo(UnityEngine.Object? obj, string name)
        {
#if UNITY_EDITOR
            if (obj == null || Application.isPlaying) return;
            Undo.RecordObject(obj, name);
#endif
        }

        public static void RegisterCreatedObjectUndo(GameObject? go, string name)
        {
#if UNITY_EDITOR
            if (go == null || Application.isPlaying) return;
            Undo.RegisterCreatedObjectUndo(go, name);
#endif
        }

        public static void MarkDirty(UnityEngine.Object? obj)
        {
#if UNITY_EDITOR
            if (obj == null || Application.isPlaying) return;
            EditorUtility.SetDirty(obj);
            if (obj is GameObject go && go.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(go.scene);
            else if (obj is Component component && component.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
#endif
        }

        public static void DestroyObject(UnityEngine.Object obj)
        {
#if UNITY_EDITOR
            if (Application.isEditor && !Application.isPlaying)
            {
                Undo.DestroyObjectImmediate(obj);
                return;
            }
#endif
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;
            return path + Path.DirectorySeparatorChar;
        }

        private static string TrimTrailingSeparators(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static Type? FindType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var normalized = typeName.Trim();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
            {
                var exact = assembly.GetType(normalized, false);
                if (exact != null) return exact;

                foreach (var type in GetTypesSafe(assembly))
                {
                    if (type.FullName == normalized || type.Name == normalized)
                        return type;
                }
            }

            return null;
        }

        public static IEnumerable<Type> GetTypesSafe(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes().Where(type => type != null)!;
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null)!;
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }

        public static GameObject? ResolveGameObject(Dictionary<string, object?> args, bool defaultIncludeInactive = true)
        {
            if (args.TryGetValue("target", out var target))
                return ResolveGameObject(target, defaultIncludeInactive);

            return ResolveGameObjectSelector(args, defaultIncludeInactive);
        }

        public static GameObject? ResolveGameObject(object? selectorValue, bool defaultIncludeInactive = true)
        {
            if (selectorValue is GameObject go)
                return go;
            if (selectorValue is Component component)
                return component.gameObject;
            if (selectorValue is UnityEngine.Object unityObject)
                return FindGameObjectByInstanceId(unityObject.GetInstanceID(), defaultIncludeInactive);
            if (selectorValue is string text)
                return ResolveGameObject(text, defaultIncludeInactive);
            if (selectorValue is int id)
                return FindGameObjectByInstanceId(id, defaultIncludeInactive);
            if (selectorValue is long longId)
                return FindGameObjectByInstanceId(checked((int)longId), defaultIncludeInactive);
            if (EvalData.AsObject(selectorValue) is { } obj)
                return ResolveGameObjectSelector(obj, defaultIncludeInactive);
            return null;
        }

        public static GameObject? ResolveGameObject(string nameOrPath, bool includeInactive)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath)) return null;
            var matches = FindGameObjects(nameOrPath.Contains('/') ? "path" : "name", nameOrPath, includeInactive, 2);
            return matches.Count == 1 ? matches[0] : null;
        }

        private static GameObject? ResolveGameObjectSelector(Dictionary<string, object?> selector, bool defaultIncludeInactive = true)
        {
            var includeInactive = EvalData.GetBool(selector, "includeInactive", defaultIncludeInactive);
            var instanceId = EvalData.GetInt(selector, "instanceId", 0);
            if (instanceId != 0)
                return FindGameObjectByInstanceId(instanceId, includeInactive);

            var path = EvalData.GetString(selector, "path");
            if (!string.IsNullOrWhiteSpace(path))
                return FindGameObjects("path", path!, includeInactive, 2).SingleOrDefault();

            var name = EvalData.GetString(selector, "name");
            if (!string.IsNullOrWhiteSpace(name))
                return FindGameObjects("name", name!, includeInactive, 2).SingleOrDefault();

            return null;
        }

        public static List<GameObject> FindGameObjects(string by, string value, bool includeInactive, int limit = 100)
        {
            var results = new List<GameObject>();
            if (string.IsNullOrWhiteSpace(value)) return results;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!IsUsableSceneObject(go, includeInactive)) continue;
                var matched = by switch
                {
                    "path" => GetPath(go) == value,
                    "tag" => go.CompareTag(value),
                    "component" => FindType(value) is { } componentType && go.GetComponent(componentType) != null,
                    _ => go.name == value
                };

                if (!matched) continue;
                results.Add(go);
                if (results.Count >= limit) break;
            }

            return results;
        }

        public static bool IsUsableSceneObject(GameObject go, bool includeInactive)
        {
            if (go == null) return false;
            var scene = go.scene;
            if (!scene.IsValid() || !scene.isLoaded) return false;
            if ((go.hideFlags & HideFlags.HideAndDontSave) != 0) return false;
            return includeInactive || go.activeInHierarchy;
        }

        public static GameObject? FindGameObjectByInstanceId(int instanceId, bool includeInactive)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.GetInstanceID() == instanceId && IsUsableSceneObject(go, includeInactive))
                    return go;
            }

            foreach (var component in Resources.FindObjectsOfTypeAll<Component>())
            {
                if (component == null || component.GetInstanceID() != instanceId) continue;
                var go = component.gameObject;
                if (IsUsableSceneObject(go, includeInactive))
                    return go;
            }

            return null;
        }

        public static string GetPath(GameObject go)
        {
            var names = new Stack<string>();
            var current = go.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        public static Dictionary<string, object?> SummarizeGameObject(GameObject go, bool includeComponents = true)
        {
            return EvalData.Obj(
                ("name", go.name),
                ("instanceId", go.GetInstanceID()),
                ("path", GetPath(go)),
                ("tag", go.tag),
                ("layer", go.layer),
                ("activeSelf", go.activeSelf),
                ("activeInHierarchy", go.activeInHierarchy),
                ("scene", EvalData.Obj(
                    ("name", go.scene.name),
                    ("path", go.scene.path),
                    ("handle", go.scene.handle)
                )),
                ("transform", EvalData.Obj(
                    ("position", Vector3ToObject(go.transform.position)),
                    ("localPosition", Vector3ToObject(go.transform.localPosition)),
                    ("rotationEuler", Vector3ToObject(go.transform.eulerAngles)),
                    ("localRotationEuler", Vector3ToObject(go.transform.localEulerAngles)),
                    ("localScale", Vector3ToObject(go.transform.localScale))
                )),
                ("components", includeComponents
                    ? go.GetComponents<Component>().Select((component, index) => (object?)SummarizeComponent(component, index)).ToList()
                    : new List<object?>())
            );
        }

        public static Dictionary<string, object?> SummarizeComponent(Component? component, int index = -1)
        {
            if (component == null)
                return EvalData.Obj(("missing", true), ("index", index));

            var type = component.GetType();
            return EvalData.Obj(
                ("type", type.FullName ?? type.Name),
                ("name", type.Name),
                ("instanceId", component.GetInstanceID()),
                ("index", index),
                ("enabled", component is Behaviour behaviour ? behaviour.enabled : null)
            );
        }

        public static Dictionary<string, object?> Vector3ToObject(Vector3 value) =>
            EvalData.Obj(("x", value.x), ("y", value.y), ("z", value.z));

        public static Dictionary<string, object?> Vector2ToObject(Vector2 value) =>
            EvalData.Obj(("x", value.x), ("y", value.y));

        public static Dictionary<string, object?> ColorToObject(Color value) =>
            EvalData.Obj(("r", value.r), ("g", value.g), ("b", value.b), ("a", value.a));

        public static Vector3 GetVector3(Dictionary<string, object?> args, string key, Vector3 defaultValue)
        {
            if (!args.TryGetValue(key, out var value)) return defaultValue;
            return ToVector3(value, defaultValue);
        }

        public static Vector3 ToVector3(object? value, Vector3 defaultValue)
        {
            if (EvalData.AsObject(value) is { } obj)
                return new Vector3(
                    EvalData.GetFloat(obj, "x", defaultValue.x),
                    EvalData.GetFloat(obj, "y", defaultValue.y),
                    EvalData.GetFloat(obj, "z", defaultValue.z));

            if (EvalData.AsArray(value) is { Count: >= 2 } arr)
                return new Vector3(ToFloat(arr[0], defaultValue.x), ToFloat(arr[1], defaultValue.y), arr.Count > 2 ? ToFloat(arr[2], defaultValue.z) : defaultValue.z);

            return defaultValue;
        }

        public static object? ConvertToType(object? value, Type targetType)
        {
            if (value == null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            var nullable = Nullable.GetUnderlyingType(targetType);
            if (nullable != null)
                return ConvertToType(value, nullable);

            if (targetType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool))
                return value is bool b ? b : bool.Parse(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "false");
            if (targetType == typeof(int))
                return checked((int)ToDouble(value, 0));
            if (targetType == typeof(long))
                return checked((long)ToDouble(value, 0));
            if (targetType == typeof(float))
                return ToFloat(value, 0f);
            if (targetType == typeof(double))
                return ToDouble(value, 0d);
            if (targetType.IsEnum)
                return value is string s ? Enum.Parse(targetType, s, true) : Enum.ToObject(targetType, checked((int)ToDouble(value, 0)));
            if (targetType == typeof(Vector2))
            {
                var vector = ToVector3(value, Vector3.zero);
                return new Vector2(vector.x, vector.y);
            }
            if (targetType == typeof(Vector3))
                return ToVector3(value, Vector3.zero);
            if (targetType == typeof(Color) && EvalData.AsObject(value) is { } color)
                return new Color(
                    EvalData.GetFloat(color, "r", 1f),
                    EvalData.GetFloat(color, "g", 1f),
                    EvalData.GetFloat(color, "b", 1f),
                    EvalData.GetFloat(color, "a", 1f));

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType))
            {
                if (value is int id)
                    return Resources.FindObjectsOfTypeAll(targetType).Cast<UnityEngine.Object>().FirstOrDefault(obj => obj.GetInstanceID() == id);
                if (EvalData.AsObject(value) is { } selector)
                {
                    var go = ResolveGameObject(selector);
                    if (go == null) return null;
                    return targetType == typeof(GameObject) ? go : go.GetComponent(targetType);
                }
            }

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        public static object? ToJsonFriendly(object? value, int depth = 0)
        {
            return EvalValueFormatter.Format(value, "json", Math.Max(0, 4 - depth));
        }

        public static float ToFloat(object? value, float defaultValue)
        {
            return value switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                long l => l,
                string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => defaultValue
            };
        }

        private static double ToDouble(object? value, double defaultValue)
        {
            return value switch
            {
                double d => d,
                float f => f,
                int i => i,
                long l => l,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => defaultValue
            };
        }

        public static MemberInfo? FindMember(Type type, string name, bool includeNonPublic = false, bool includeStatic = false)
        {
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            if (includeStatic) flags |= BindingFlags.Static;
            return (MemberInfo?)type.GetField(name, flags) ?? type.GetProperty(name, flags);
        }

        public static bool TrySetMember(object target, string memberName, object? value, out string error, bool includeNonPublic = false, bool includeStatic = false)
        {
            error = string.Empty;
            var type = target.GetType();
            var flags = BindingFlags.Public | BindingFlags.Instance;
            if (includeNonPublic) flags |= BindingFlags.NonPublic;
            if (includeStatic) flags |= BindingFlags.Static;

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(target, ConvertToType(value, field.FieldType));
                return true;
            }

            var property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, ConvertToType(value, property.PropertyType));
                return true;
            }

            error = $"Writable field or property '{memberName}' was not found on {type.FullName}.";
            return false;
        }

        public static List<object?> GetRootSummaries(Scene scene)
        {
            var roots = new List<object?>();
            if (!scene.IsValid() || !scene.isLoaded) return roots;
            foreach (var root in scene.GetRootGameObjects())
                roots.Add(SummarizeGameObject(root, false));
            return roots;
        }

        public static List<object?> GetHierarchySummaries(Scene scene, int depth, bool includeComponents, int limit)
        {
            var roots = new List<object?>();
            if (!scene.IsValid() || !scene.isLoaded) return roots;

            var remaining = limit <= 0 ? int.MaxValue : limit;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (remaining <= 0) break;
                roots.Add(SummarizeHierarchy(root, Math.Max(0, depth), includeComponents, ref remaining));
            }

            return roots;
        }

        private static object SummarizeHierarchy(GameObject go, int depth, bool includeComponents, ref int remaining)
        {
            remaining--;
            var children = new List<object?>();
            if (depth > 0)
            {
                for (var i = 0; i < go.transform.childCount && remaining > 0; i++)
                    children.Add(SummarizeHierarchy(go.transform.GetChild(i).gameObject, depth - 1, includeComponents, ref remaining));
            }

            return EvalData.Obj(
                ("name", go.name),
                ("instanceId", go.GetInstanceID()),
                ("path", GetPath(go)),
                ("tag", go.tag),
                ("layer", go.layer),
                ("activeSelf", go.activeSelf),
                ("activeInHierarchy", go.activeInHierarchy),
                ("transform", EvalData.Obj(
                    ("position", Vector3ToObject(go.transform.position)),
                    ("localPosition", Vector3ToObject(go.transform.localPosition)),
                    ("rotationEuler", Vector3ToObject(go.transform.eulerAngles)),
                    ("localRotationEuler", Vector3ToObject(go.transform.localEulerAngles)),
                    ("localScale", Vector3ToObject(go.transform.localScale))
                )),
                ("components", includeComponents
                    ? go.GetComponents<Component>().Select((component, index) => (object?)SummarizeComponent(component, index)).ToList()
                    : new List<object?>()),
                ("children", children)
            );
        }
    }
}
