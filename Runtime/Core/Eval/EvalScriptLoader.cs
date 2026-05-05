#nullable enable
using System.IO;
using Puerts;
using UnityEngine;

namespace YuzeToolkit
{
    public sealed class EvalScriptLoader : ILoader, IModuleChecker
    {
        private static readonly DefaultLoader DefaultLoader = new();
        public bool FileExists(string filepath)
        {
            var modulePath = NormalizeModulePath(filepath);
            if (IsToolIndexPath(modulePath)) return true;
            if (TryGetToolModuleName(modulePath, out var toolName))
            {
                if (EvalToolCatalog.TryGetCSharpModuleSource(toolName, out _)) return true;
                return EvalToolCatalog.IsResourceToolEnabled(toolName) && TryLoadResourceText(modulePath, out _, out _);
            }

            return TryLoadResourceText(filepath, out _, out _) || DefaultLoader.FileExists(filepath);
        }

        public string ReadFile(string filepath, out string debugpath)
        {
            var modulePath = NormalizeModulePath(filepath);
            if (IsToolIndexPath(modulePath))
            {
                debugpath = "virtual://tools/index";
                return EvalToolCatalog.GenerateIndexModuleSource();
            }

            if (TryGetToolModuleName(modulePath, out var toolName) &&
                EvalToolCatalog.TryGetCSharpModuleSource(toolName, out var virtualSource))
            {
                debugpath = "virtual://tools/" + toolName;
                return virtualSource;
            }

            if (TryGetToolModuleName(modulePath, out var resourceToolName) &&
                !EvalToolCatalog.IsResourceToolEnabled(resourceToolName))
            {
                debugpath = filepath;
                return string.Empty;
            }

            if (TryLoadResourceText(filepath, out var resourceText, out var resourceDebugPath))
            {
                debugpath = resourceDebugPath;
                return resourceText;
            }

            if (DefaultLoader.FileExists(filepath))
                return DefaultLoader.ReadFile(filepath, out debugpath);

            debugpath = filepath;
            return string.Empty;
        }

        public bool IsESM(string filepath) =>
            NormalizeModulePath(filepath).StartsWith("tools/", System.StringComparison.OrdinalIgnoreCase) || DefaultLoader.IsESM(filepath);

        private static bool TryLoadResourceText(string filepath, out string text, out string debugpath)
        {
            text = string.Empty;
            debugpath = string.Empty;
            var path = NormalizeResourcePath(filepath);
            var asset = Resources.Load<TextAsset>(path);
            if (asset != null)
            {
                text = asset.text;
                debugpath = "Resources/" + path;
                return true;
            }

            if (Path.HasExtension(path))
            {
                asset = Resources.Load<TextAsset>(Path.ChangeExtension(path, null));
                if (asset != null)
                {
                    text = asset.text;
                    debugpath = "Resources/" + Path.ChangeExtension(path, null)?.Replace('\\', '/');
                    return true;
                }
            }

#if UNITY_EDITOR
            if (EvalToolCatalog.TryReadEditorResourceText(path, out text, out debugpath))
                return true;
#endif

            return false;
        }

        private static string NormalizeModulePath(string filepath)
        {
            var path = NormalizeResourcePath(filepath);
            if (Path.HasExtension(path))
                path = Path.ChangeExtension(path, null).Replace('\\', '/');
            return path;
        }

        private static bool IsToolIndexPath(string path) =>
            string.Equals(path, "tools/index", System.StringComparison.OrdinalIgnoreCase);

        private static bool TryGetToolModuleName(string path, out string toolName)
        {
            toolName = string.Empty;
            if (!path.StartsWith("tools/", System.StringComparison.OrdinalIgnoreCase)) return false;
            toolName = path.Substring("tools/".Length);
            return !string.IsNullOrWhiteSpace(toolName) &&
                   toolName.IndexOf('/') < 0 &&
                   !IsToolIndexPath(path);
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
