#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace YuzeToolkit
{
    [UnityEngine.Scripting.Preserve]
    [EvalTool("diagnostics", "Read-only cameras, physics, graphics, UI, and loaded texture diagnostics.")]
    public sealed class DiagnosticsTool
    {
        [EvalFunction("List cameras.")]
        public Dictionary<string, object?> listCameras()
        {
            var cameras = Resources.FindObjectsOfTypeAll<Camera>()
                .Where(camera => camera != null && ToolUtilities.IsUsableSceneObject(camera.gameObject, true))
                .Select(camera => (object?)Summarize(camera))
                .ToList();
            return EvalData.Obj(("count", cameras.Count), ("cameras", cameras));
        }

        internal static object Summarize(Camera camera) =>
            EvalData.Obj(
                ("name", camera.name),
                ("instanceId", camera.GetInstanceID()),
                ("gameObject", ToolUtilities.SummarizeGameObject(camera.gameObject, false)),
                ("enabled", camera.enabled),
                ("tag", camera.tag),
                ("clearFlags", camera.clearFlags.ToString()),
                ("orthographic", camera.orthographic),
                ("orthographicSize", camera.orthographicSize),
                ("fieldOfView", camera.fieldOfView),
                ("nearClipPlane", camera.nearClipPlane),
                ("farClipPlane", camera.farClipPlane),
                ("depth", camera.depth));

        [EvalFunction("Read 2D/3D physics settings.")]
        public Dictionary<string, object?> getPhysicsState()
        {
            var colliders2D = Resources.FindObjectsOfTypeAll<Collider2D>()
                .Where(collider => collider != null && ToolUtilities.IsUsableSceneObject(collider.gameObject, true))
                .Select(collider => (object?)EvalData.Obj(
                    ("type", collider.GetType().FullName ?? collider.GetType().Name),
                    ("enabled", collider.enabled),
                    ("isTrigger", collider.isTrigger),
                    ("gameObject", ToolUtilities.SummarizeGameObject(collider.gameObject, false))))
                .ToList();

            return EvalData.Obj(
                ("gravity2D", ToolUtilities.Vector2ToObject(Physics2D.gravity)),
                ("queriesHitTriggers", Physics2D.queriesHitTriggers),
                ("collider2DCount", colliders2D.Count),
                ("colliders2D", colliders2D));
        }

        [EvalFunction("Read render pipeline and quality state.")]
        public Dictionary<string, object?> getGraphicsState()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return EvalData.Obj(
                ("renderPipeline", pipeline != null ? pipeline.GetType().FullName ?? pipeline.GetType().Name : "Built-in"),
                ("renderPipelineAsset", pipeline != null ? pipeline.name : string.Empty),
                ("activeColorSpace", QualitySettings.activeColorSpace.ToString()),
                ("qualityLevel", QualitySettings.GetQualityLevel()),
                ("qualityName", QualitySettings.names.Length > QualitySettings.GetQualityLevel() ? QualitySettings.names[QualitySettings.GetQualityLevel()] : string.Empty));
        }

        [EvalFunction("List UI canvases and EventSystems.")]
        public Dictionary<string, object?> listCanvases()
        {
            var canvases = Resources.FindObjectsOfTypeAll<Canvas>()
                .Where(canvas => canvas != null && ToolUtilities.IsUsableSceneObject(canvas.gameObject, true))
                .Select(canvas => (object?)EvalData.Obj(
                    ("name", canvas.name),
                    ("instanceId", canvas.GetInstanceID()),
                    ("renderMode", canvas.renderMode.ToString()),
                    ("sortingOrder", canvas.sortingOrder),
                    ("gameObject", ToolUtilities.SummarizeGameObject(canvas.gameObject, false))))
                .ToList();
            return EvalData.Obj(("canvasCount", canvases.Count), ("canvases", canvases));
        }

        [EvalFunction("List loaded textures.")]
        public Dictionary<string, object?> listLoadedTextures(int limit = 100)
        {
            limit = Math.Max(1, limit);
            var textures = Resources.FindObjectsOfTypeAll<Texture>()
                .Where(texture => texture != null)
                .Take(limit)
                .Select(texture => (object?)EvalData.Obj(
                    ("name", texture.name),
                    ("type", texture.GetType().FullName ?? texture.GetType().Name),
                    ("instanceId", texture.GetInstanceID()),
                    ("width", texture.width),
                    ("height", texture.height)))
                .ToList();
            return EvalData.Obj(("count", textures.Count), ("textures", textures));
        }
    }
}
