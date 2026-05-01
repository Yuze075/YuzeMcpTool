#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace YuzeToolkit
{
    internal sealed class CameraListCommand : IMcpCommand
    {
        public string Name => "camera.list";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var cameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(camera => camera != null && CommandUtilities.IsUsableSceneObject(camera.gameObject, true))
                .Select(camera => (object?)Summarize(camera))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", cameras.Count), ("cameras", cameras))));
        }

        internal static object Summarize(Camera camera) =>
            LitJson.Obj(
                ("name", camera.name),
                ("instanceId", camera.GetInstanceID()),
                ("gameObject", CommandUtilities.SummarizeGameObject(camera.gameObject, false)),
                ("enabled", camera.enabled),
                ("tag", camera.tag),
                ("clearFlags", camera.clearFlags.ToString()),
                ("orthographic", camera.orthographic),
                ("orthographicSize", camera.orthographicSize),
                ("fieldOfView", camera.fieldOfView),
                ("nearClipPlane", camera.nearClipPlane),
                ("farClipPlane", camera.farClipPlane),
                ("depth", camera.depth));
    }

    internal sealed class PhysicsGetStateCommand : IMcpCommand
    {
        public string Name => "physics.getState";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var colliders2D = Resources.FindObjectsOfTypeAll<Collider2D>()
                .Where(collider => collider != null && CommandUtilities.IsUsableSceneObject(collider.gameObject, true))
                .Select(collider => (object?)LitJson.Obj(
                    ("type", collider.GetType().FullName ?? collider.GetType().Name),
                    ("enabled", collider.enabled),
                    ("isTrigger", collider.isTrigger),
                    ("gameObject", CommandUtilities.SummarizeGameObject(collider.gameObject, false))))
                .ToList();

            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("gravity2D", CommandUtilities.Vector2ToObject(Physics2D.gravity)),
                ("queriesHitTriggers", Physics2D.queriesHitTriggers),
                ("collider2DCount", colliders2D.Count),
                ("colliders2D", colliders2D))));
        }
    }

    internal sealed class GraphicsGetStateCommand : IMcpCommand
    {
        public string Name => "graphics.getState";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return Task.FromResult(McpBridge.Success(LitJson.Obj(
                ("renderPipeline", pipeline != null ? pipeline.GetType().FullName ?? pipeline.GetType().Name : "Built-in"),
                ("renderPipelineAsset", pipeline != null ? pipeline.name : string.Empty),
                ("activeColorSpace", QualitySettings.activeColorSpace.ToString()),
                ("qualityLevel", QualitySettings.GetQualityLevel()),
                ("qualityName", QualitySettings.names.Length > QualitySettings.GetQualityLevel() ? QualitySettings.names[QualitySettings.GetQualityLevel()] : string.Empty))));
        }
    }

    internal sealed class UiListCommand : IMcpCommand
    {
        public string Name => "ui.list";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(canvas => canvas != null && CommandUtilities.IsUsableSceneObject(canvas.gameObject, true))
                .Select(canvas => (object?)LitJson.Obj(
                    ("name", canvas.name),
                    ("instanceId", canvas.GetInstanceID()),
                    ("renderMode", canvas.renderMode.ToString()),
                    ("sortingOrder", canvas.sortingOrder),
                    ("gameObject", CommandUtilities.SummarizeGameObject(canvas.gameObject, false))))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("canvasCount", canvases.Count), ("canvases", canvases))));
        }
    }

    internal sealed class TextureListLoadedCommand : IMcpCommand
    {
        public string Name => "texture.listLoaded";
        public bool EditorOnly => false;

        public Task<string> ExecuteAsync(McpCommandContext context)
        {
            var args = CommandUtilities.ParseArgs(context.ArgumentsJson);
            var limit = Math.Max(1, LitJson.GetInt(args, "limit", 100));
            var textures = Resources.FindObjectsOfTypeAll<Texture>()
                .Where(texture => texture != null)
                .Take(limit)
                .Select(texture => (object?)LitJson.Obj(
                    ("name", texture.name),
                    ("type", texture.GetType().FullName ?? texture.GetType().Name),
                    ("instanceId", texture.GetInstanceID()),
                    ("width", texture.width),
                    ("height", texture.height)))
                .ToList();
            return Task.FromResult(McpBridge.Success(LitJson.Obj(("count", textures.Count), ("textures", textures))));
        }
    }
}
