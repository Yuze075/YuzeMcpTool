#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("assets", "AssetDatabase search, text IO, asset moves, scripts, materials, dependencies, and refresh.")]
    public sealed class AssetsTool
    {
        [McpFunction("AssetDatabase search returning asset summaries.")]
        public Dictionary<string, object?> find(string filter, int limit = 0, object? folders = null)
        {
            if (string.IsNullOrWhiteSpace(filter)) throw new InvalidOperationException("Argument 'filter' is required.");
            var guids = FindGuids(filter, folders, limit);
            var assets = guids.Select(guid => (object?)SummarizeAsset(AssetDatabase.GUIDToAssetPath(guid), guid)).ToList();
            return McpData.Obj(("count", assets.Count), ("assets", assets));
        }

        [McpFunction("AssetDatabase search returning asset paths.")]
        public List<string> findPaths(string filter, int limit = 0, object? folders = null)
        {
            if (string.IsNullOrWhiteSpace(filter)) throw new InvalidOperationException("Argument 'filter' is required.");
            return FindGuids(filter, folders, limit).Select(AssetDatabase.GUIDToAssetPath).ToList();
        }

        [McpFunction("AssetDatabase search returning asset names.")]
        public List<string> findNames(string filter, int limit = 0, object? folders = null)
        {
            if (string.IsNullOrWhiteSpace(filter)) throw new InvalidOperationException("Argument 'filter' is required.");
            return FindGuids(filter, folders, limit).Select(guid =>
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                return asset != null ? asset.name : Path.GetFileNameWithoutExtension(path);
            }).ToList();
        }

        private static IEnumerable<string> FindGuids(string filter, object? folders, int limit)
        {
            var folderArray = ToStringArray(folders);
            var guids = folderArray.Length > 0 ? AssetDatabase.FindAssets(filter, folderArray) : AssetDatabase.FindAssets(filter);
            return limit > 0 ? guids.Take(limit) : guids.AsEnumerable();
        }

        internal static Dictionary<string, object?> SummarizeAsset(string path, string? guid = null)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return McpData.Obj(
                ("guid", string.IsNullOrEmpty(guid) ? AssetDatabase.AssetPathToGUID(path) : guid),
                ("path", path),
                ("name", asset != null ? asset.name : Path.GetFileNameWithoutExtension(path)),
                ("type", asset != null ? asset.GetType().FullName ?? asset.GetType().Name : string.Empty),
                ("exists", asset != null || File.Exists(path) || Directory.Exists(path)));
        }

        [McpFunction("Get asset info.")]
        public Dictionary<string, object?> getInfo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Argument 'path' is required.");
            return SummarizeAsset(path);
        }

        [McpFunction("Read a project text asset.")]
        public Dictionary<string, object?> readText(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Argument 'path' is required.");
            if (!ToolUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            if (!File.Exists(fullPath)) throw new InvalidOperationException($"File '{projectPath}' was not found.");
            return McpData.Obj(("path", projectPath), ("text", File.ReadAllText(fullPath)));
        }

        [McpFunction("Write a project text asset.")]
        public Dictionary<string, object?> writeText(string path, string text, bool refresh = false, bool confirmOverwrite = false)
        {
            if (!ToolUtilities.TryResolveProjectPath(path, out var full, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            if (File.Exists(full) && !confirmOverwrite)
                throw new InvalidOperationException("Writing an existing file requires confirmOverwrite: true.");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text ?? string.Empty);
            if (refresh) AssetDatabase.Refresh();
            return SummarizeAsset(projectPath);
        }

        [McpFunction("Create a folder.")]
        public Dictionary<string, object?> createFolder(string parent = "Assets", string name = "")
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Argument 'name' is required.");
            var guid = AssetDatabase.CreateFolder(parent, name);
            return McpData.Obj(("guid", guid), ("path", AssetDatabase.GUIDToAssetPath(guid)));
        }

        [McpFunction("Move an asset.")]
        public Dictionary<string, object?> move(string from, string to, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Asset move requires confirm: true.");
            var error = AssetDatabase.MoveAsset(from, to);
            if (error.Length != 0) throw new InvalidOperationException(error);
            return SummarizeAsset(to);
        }

        [McpFunction("Copy an asset.")]
        public Dictionary<string, object?> copy(string from, string to, bool confirmOverwrite = false)
        {
            if (AssetDatabase.LoadMainAssetAtPath(to) != null && !confirmOverwrite)
                throw new InvalidOperationException("Copying over an existing asset requires confirmOverwrite: true.");
            var ok = AssetDatabase.CopyAsset(from, to);
            if (!ok) throw new InvalidOperationException($"Failed to copy '{from}' to '{to}'.");
            return SummarizeAsset(to);
        }

        [McpFunction("Delete an asset.")]
        public Dictionary<string, object?> deleteAsset(string path, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Asset delete requires confirm: true.");
            var ok = AssetDatabase.DeleteAsset(path);
            if (!ok) throw new InvalidOperationException($"Failed to delete asset '{path}'.");
            return McpData.Obj(("deleted", path));
        }

        [McpFunction("Refresh AssetDatabase immediately.")]
        public Dictionary<string, object?> refreshNow()
        {
            AssetDatabase.Refresh();
            return EditorCompilationMonitor.GetStateObject();
        }

        [McpFunction("Get asset dependencies.")]
        public Dictionary<string, object?> getDependencies(string path, bool recursive = true)
        {
            var deps = AssetDatabase.GetDependencies(path, recursive).Select(p => (object?)SummarizeAsset(p)).ToList();
            return McpData.Obj(("path", path), ("count", deps.Count), ("dependencies", deps));
        }

        [McpFunction("Find asset references.")]
        public Dictionary<string, object?> findReferences(string path, object? folders = null, int limit = 0)
        {
            var scope = ToStringArray(folders);
            if (scope.Length == 0) scope = new[] { "Assets" };
            var results = new List<object?>();
            foreach (var guid in AssetDatabase.FindAssets("", scope))
            {
                var candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (candidate == path) continue;
                if (AssetDatabase.GetDependencies(candidate, true).Contains(path))
                    results.Add(SummarizeAsset(candidate, guid));
                if (limit > 0 && results.Count >= limit) break;
            }
            return McpData.Obj(("target", path), ("count", results.Count), ("references", results));
        }

        [McpFunction("Create a MonoBehaviour script.")]
        public Dictionary<string, object?> createScript(string path, string className = "", string namespaceName = "", bool confirmOverwrite = false)
        {
            if (!ToolUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            if (File.Exists(fullPath) && !confirmOverwrite)
                throw new InvalidOperationException("Creating over an existing script requires confirmOverwrite: true.");
            className = string.IsNullOrWhiteSpace(className) ? Path.GetFileNameWithoutExtension(fullPath) : className;
            var body = string.IsNullOrWhiteSpace(namespaceName)
                ? $"using UnityEngine;\n\npublic class {className} : MonoBehaviour\n{{\n}}\n"
                : $"using UnityEngine;\n\nnamespace {namespaceName}\n{{\n    public class {className} : MonoBehaviour\n    {{\n    }}\n}}\n";
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, body);
            AssetDatabase.Refresh();
            return SummarizeAsset(projectPath);
        }

        [McpFunction("Apply character-based script text edits.")]
        public Dictionary<string, object?> applyScriptTextEdits(string path, object edits, bool refresh = true, bool confirm = false)
        {
            if (!confirm) throw new InvalidOperationException("Script text edits require confirm: true.");
            if (!ToolUtilities.TryResolveProjectPath(path, out var fullPath, out var projectPath, out var error))
                throw new InvalidOperationException(error);
            if (!File.Exists(fullPath)) throw new InvalidOperationException($"File '{projectPath}' was not found.");
            var text = File.ReadAllText(fullPath);
            var editList = (McpData.AsArray(edits) ?? new List<object?>())
                .Select(McpData.AsObject)
                .Where(edit => edit != null)
                .Select(edit => (Start: McpData.GetInt(edit!, "start", -1), Length: McpData.GetInt(edit!, "length", 0), Text: ToolUtilities.GetString(edit!, "text")))
                .OrderByDescending(edit => edit.Start)
                .ToList();
            foreach (var edit in editList)
            {
                if (edit.Start < 0 || edit.Start > text.Length) throw new InvalidOperationException($"Invalid edit start {edit.Start}.");
                if (edit.Length < 0) throw new InvalidOperationException($"Invalid edit length {edit.Length}.");
                text = text.Remove(edit.Start, Math.Min(edit.Length, text.Length - edit.Start)).Insert(edit.Start, edit.Text);
            }
            File.WriteAllText(fullPath, text);
            if (refresh) AssetDatabase.Refresh();
            return McpData.Obj(("path", projectPath), ("editCount", editList.Count));
        }

        [McpFunction("Create a Material asset.")]
        public Dictionary<string, object?> createMaterial(string path, string shaderName = "Universal Render Pipeline/2D/Sprite-Lit-Default", object? properties = null, bool confirmOverwrite = false)
        {
            if (!ToolUtilities.TryResolveProjectPath(path, out _, out var projectPath, out var pathError))
                throw new InvalidOperationException(pathError);
            if (AssetDatabase.LoadMainAssetAtPath(projectPath) != null && !confirmOverwrite)
                throw new InvalidOperationException("Creating over an existing material requires confirmOverwrite: true.");
            var shader = Shader.Find(shaderName) ?? Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException($"Shader '{shaderName}' was not found.");
            var material = new Material(shader);
            ApplyMaterialProperties(material, properties);
            AssetDatabase.CreateAsset(material, projectPath);
            AssetDatabase.SaveAssets();
            return SummarizeAsset(projectPath);
        }

        private static void ApplyMaterialProperties(Material material, object? properties)
        {
            var map = McpData.AsObject(properties);
            if (map == null) return;
            foreach (var pair in map)
            {
                if (!material.HasProperty(pair.Key)) continue;
                if (pair.Value is int or long or float or double)
                    material.SetFloat(pair.Key, ToolUtilities.ToFloat(pair.Value, 0f));
                else if (McpData.AsObject(pair.Value) is { } color)
                    material.SetColor(pair.Key, new Color(
                        McpData.GetFloat(color, "r", 1f),
                        McpData.GetFloat(color, "g", 1f),
                        McpData.GetFloat(color, "b", 1f),
                        McpData.GetFloat(color, "a", 1f)));
                else if (pair.Value is string assetPath)
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(assetPath);
                    if (texture != null) material.SetTexture(pair.Key, texture);
                }
            }
        }

        private static string[] ToStringArray(object? value)
        {
            if (value == null) return Array.Empty<string>();
            if (value is string single) return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
            return (McpData.AsArray(value) ?? new List<object?>())
                .Select(Convert.ToString)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray();
        }
    }
}
