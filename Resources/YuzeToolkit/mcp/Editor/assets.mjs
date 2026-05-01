export const summary = "**assets** - AssetDatabase search, text IO, asset moves, scripts, materials, dependencies, and refresh.";

export const description = `
Editor-only. Use this module for project assets and files inside the Unity project root.

Generic AssetDatabase functions:
- \`find(args)\` - AssetDatabase search using Unity filters, for example \`{ filter: "t:AnimationClip", folders: ["Assets"], limit: 50 }\`.
- \`getInfo(path)\`, \`readText(path)\`, \`writeText(args)\`.
- \`createFolder(parent, name)\`, \`copy(from, to)\`, \`move(from, to, confirm = false)\`, \`deleteAsset(path, confirm = false)\`.
- \`refreshNow()\`, \`getDependencies(path, recursive = true)\`, \`findReferences(path, folders?, limit = 0)\`.

Specialized creation/editing functions:
- \`createScript(args)\` - Create a default MonoBehaviour script. Args: path, namespace?.
- \`applyScriptTextEdits({ path, edits, refresh? })\` - Apply character start/length/text edits to a script file.
- \`createMaterial(args)\` - Create a Material asset. Args: path, shader?.

Use \`find({ filter: "t:TypeName" })\` instead of adding new list helpers for asset types. Move/delete/build-affecting operations require explicit confirmation where documented.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function find(args) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { ...args, action: "find" }));
}

export async function getInfo(path) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "getInfo", path }));
}

export async function readText(path) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "readText", path }));
}

export async function writeText(args) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { ...args, action: "writeText" }));
}

export async function createFolder(parent, name) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "createFolder", parent, name }));
}

export async function copy(from, to) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "copy", from, to }));
}

export async function move(from, to, confirm = false) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "move", from, to, confirm }));
}

export async function deleteAsset(path, confirm = false) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "delete", path, confirm }));
}

export async function refreshNow() {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "refreshNow" }));
}

export async function getDependencies(path, recursive = true) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "getDependencies", path, recursive }));
}

export async function findReferences(path, folders, limit = 0) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { action: "findReferences", path, folders, limit }));
}

export async function createScript(args) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { ...args, action: "scriptCreate" }));
}

export async function applyScriptTextEdits(args) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { ...args, action: "scriptApplyTextEdits" }));
}

export async function createMaterial(args) {
  return unwrap(await Mcp.invokeAsync("asset.execute", { ...args, action: "materialCreate" }));
}
