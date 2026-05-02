#nullable enable
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [McpTool("importers", "AssetImporter inspection, serialized edits, and reimport.")]
    public sealed class ImportersTool
    {
        [McpFunction("Read importer summary.")]
        public Dictionary<string, object?> get(string path, bool includeProperties = false)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Importer for '{path}' was not found.");
            return SummarizeImporter(importer, includeProperties);
        }

        private static Dictionary<string, object?> SummarizeImporter(AssetImporter importer, bool includeProperties)
        {
            var result = McpData.Obj(
                ("assetPath", importer.assetPath),
                ("type", importer.GetType().FullName ?? importer.GetType().Name),
                ("userData", importer.userData),
                ("assetBundleName", importer.assetBundleName),
                ("assetBundleVariant", importer.assetBundleVariant)
            );

            if (importer is TextureImporter texture)
            {
                result["texture"] = McpData.Obj(
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
                result["audio"] = McpData.Obj(
                    ("loadType", settings.loadType.ToString()),
                    ("compressionFormat", settings.compressionFormat.ToString()),
                    ("quality", settings.quality),
                    ("sampleRateSetting", settings.sampleRateSetting.ToString())
                );
            }
            else if (importer is ModelImporter model)
            {
                result["model"] = McpData.Obj(
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
                    props.Add(SerializedTool.SummarizeProperty(iterator));
                }
                result["properties"] = props;
            }

            return result;
        }

        [McpFunction("Set one importer property.")]
        public Dictionary<string, object?> setProperty(string path, string propertyPath, object? value, bool saveAndReimport = false, bool confirm = false)
        {
            if (!confirm) throw new System.InvalidOperationException("Importer property edits require confirm: true.");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Importer for '{path}' was not found.");
            var serialized = new SerializedObject(importer);
            var prop = serialized.FindProperty(propertyPath);
            if (prop == null) throw new System.InvalidOperationException($"Importer property '{propertyPath}' was not found.");
            SerializedTool.SetPropertyValue(prop, value);
            serialized.ApplyModifiedProperties();
            if (saveAndReimport) importer.SaveAndReimport();
            else EditorUtility.SetDirty(importer);
            return SummarizeImporter(importer, false);
        }

        [McpFunction("Set multiple importer properties.")]
        public Dictionary<string, object?> setMany(string path, object changes, bool saveAndReimport = false, bool confirm = false)
        {
            if (!confirm) throw new System.InvalidOperationException("Importer property edits require confirm: true.");
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Importer for '{path}' was not found.");
            var changeList = (McpData.AsArray(changes) ?? new List<object?>())
                .Select(McpData.AsObject)
                .Where(change => change != null)
                .ToList();
            if (changeList.Count == 0) throw new System.InvalidOperationException("Argument 'changes' must contain at least one importer property update.");

            var serialized = new SerializedObject(importer);
            var results = new List<object?>();
            foreach (var change in changeList)
            {
                var propertyPath = ToolUtilities.GetString(change!, "propertyPath");
                var prop = serialized.FindProperty(propertyPath);
                if (prop == null) throw new System.InvalidOperationException($"Importer property '{propertyPath}' was not found.");
                change!.TryGetValue("value", out var value);
                SerializedTool.SetPropertyValue(prop, value);
                results.Add(SerializedTool.SummarizeProperty(prop));
            }

            serialized.ApplyModifiedProperties();
            if (saveAndReimport) importer.SaveAndReimport();
            else EditorUtility.SetDirty(importer);
            return McpData.Obj(("importer", SummarizeImporter(importer, false)), ("count", results.Count), ("properties", results));
        }

        [McpFunction("Force reimport.")]
        public Dictionary<string, object?> reimport(string path, bool confirm = false)
        {
            if (!confirm) throw new System.InvalidOperationException("Asset reimport requires confirm: true.");
            if (string.IsNullOrWhiteSpace(path)) throw new System.InvalidOperationException("Argument 'path' is required.");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.InvalidOperationException($"Importer for '{path}' was not found after reimport.");
            return SummarizeImporter(importer, false);
        }
    }
}
