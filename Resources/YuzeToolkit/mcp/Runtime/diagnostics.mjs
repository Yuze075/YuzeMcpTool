export const summary = "**diagnostics** - Read-only cameras, physics, graphics, UI, and loaded texture diagnostics.";

export const description = `
Use this module for read-only runtime diagnostics.

Functions:
- \`listCameras()\` - List scene cameras and common camera settings.
- \`getPhysicsState()\` - Return Physics2D gravity, query trigger setting, and Collider2D summaries.
- \`getGraphicsState()\` - Return render pipeline and quality/color-space summary.
- \`listCanvases()\` - List scene Canvas objects and render settings.
- \`listLoadedTextures(limit = 100)\` - List loaded Texture objects with size and type.

This module does not mutate scene state. Use \`Runtime/components.mjs\` or \`Editor/serialized.mjs\` for edits.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function listCameras() {
  return unwrap(await Mcp.invokeAsync("runtime.diagnostics", { action: "cameraList" }));
}

export async function getPhysicsState() {
  return unwrap(await Mcp.invokeAsync("runtime.diagnostics", { action: "physicsState" }));
}

export async function getGraphicsState() {
  return unwrap(await Mcp.invokeAsync("runtime.diagnostics", { action: "graphicsState" }));
}

export async function listCanvases() {
  return unwrap(await Mcp.invokeAsync("runtime.diagnostics", { action: "uiList" }));
}

export async function listLoadedTextures(limit = 100) {
  return unwrap(await Mcp.invokeAsync("runtime.diagnostics", { action: "textureList", limit }));
}
