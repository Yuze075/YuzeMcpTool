#nullable enable
using System.Collections.Generic;
using System.IO;
using Puerts;
using UnityEngine;

namespace YuzeToolkit
{
    internal sealed class McpScriptLoader : ILoader, IModuleChecker
    {
        private static readonly DefaultLoader DefaultLoader = new();
        private readonly List<string> _searchPaths = new();

        public McpScriptLoader()
        {
#if UNITY_EDITOR
            _searchPaths.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "com.yuzetoolkit.mcptool", "Resources")));
#endif
        }

        public bool FileExists(string filepath)
        {
            foreach (var searchPath in _searchPaths)
            {
                if (File.Exists(Path.Combine(searchPath, filepath)))
                    return true;
            }

            return LoadResource(filepath) != null || DefaultLoader.FileExists(filepath);
        }

        public string ReadFile(string filepath, out string debugpath)
        {
            foreach (var searchPath in _searchPaths)
            {
                var fullPath = Path.Combine(searchPath, filepath);
                if (!File.Exists(fullPath)) continue;

                debugpath = fullPath;
                return File.ReadAllText(fullPath);
            }

            var resource = LoadResource(filepath);
            if (resource != null)
            {
                debugpath = "Resources/" + NormalizeResourcePath(filepath);
                return resource.text;
            }

            if (DefaultLoader.FileExists(filepath))
                return DefaultLoader.ReadFile(filepath, out debugpath);

            debugpath = filepath;
            return string.Empty;
        }

        public bool IsESM(string filepath) => DefaultLoader.IsESM(filepath);

        private static TextAsset? LoadResource(string filepath)
        {
            var path = NormalizeResourcePath(filepath);
            var asset = Resources.Load<TextAsset>(path);
            if (asset != null) return asset;

            if (Path.HasExtension(path))
                asset = Resources.Load<TextAsset>(Path.ChangeExtension(path, null));

            return asset;
        }

        private static string NormalizeResourcePath(string filepath)
        {
            var path = filepath.Replace('\\', '/');
            while (path.StartsWith("./"))
                path = path.Substring(2);
            if (path.StartsWith("/"))
                path = path.Substring(1);
            return path;
        }
    }
}
