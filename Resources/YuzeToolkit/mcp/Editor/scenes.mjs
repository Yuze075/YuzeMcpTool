export const summary = "**scenes** - Scene file and open scene hierarchy operations.";

export const description = `
Editor-only. Use this module for scene files and open scene hierarchy.

Scene functions:
- \`listOpenScenes()\`, \`getSceneHierarchy(args = {})\`.
- \`openScene(path, mode = "Single")\`, \`createScene(args = {})\`.
- \`saveScene()\`, \`saveSceneAs(path)\`, \`setActiveScene(pathOrName)\`.

Use \`Editor/prefabs.mjs\` for Prefab workflows and \`Editor/serialized.mjs\` for Inspector fields.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function listOpenScenes() {
  return unwrap(await Mcp.invokeAsync("scene.execute", { action: "listOpen" }));
}

export async function getSceneHierarchy(args = {}) {
  return unwrap(await Mcp.invokeAsync("scene.execute", { ...args, action: "getHierarchy" }));
}

export async function openScene(path, mode = "Single") {
  return unwrap(await Mcp.invokeAsync("scene.execute", { action: "open", path, mode }));
}

export async function createScene(args = {}) {
  return unwrap(await Mcp.invokeAsync("scene.execute", { ...args, action: "create" }));
}

export async function saveScene() {
  return unwrap(await Mcp.invokeAsync("scene.execute", { action: "save" }));
}

export async function saveSceneAs(path) {
  return unwrap(await Mcp.invokeAsync("scene.execute", { action: "saveAs", path }));
}

export async function setActiveScene(path) {
  return unwrap(await Mcp.invokeAsync("scene.execute", { action: "setActive", path }));
}
