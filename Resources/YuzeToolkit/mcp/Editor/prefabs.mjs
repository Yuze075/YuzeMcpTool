export const summary = "**prefabs** - Prefab instance, asset, stage, override, and unpack operations.";

export const description = `
Editor-only. Use this module for Prefab workflows.

Functions:
- \`instantiate(args)\` - Instantiate a Prefab asset into the active scene.
- \`createFromObject(args)\` - Save a scene GameObject as a Prefab asset.
- \`createVariant(args)\` - Create a Prefab variant from \`basePath\` at \`path\`.
- \`openStage(path)\`, \`closeStage()\`, \`saveStage()\`.
- \`getOverrides(target)\` - Return object overrides and property modifications.
- \`applyOverrides(target, confirm = false)\` - Apply all overrides on the nearest Prefab instance root. Requires \`confirm: true\`.
- \`revertOverrides(target, confirm = false)\` - Revert all overrides on the nearest Prefab instance root. Requires \`confirm: true\`.
- \`unpack(target, mode = "OutermostRoot", confirm = false)\` - Unpack a Prefab instance. Requires \`confirm: true\`.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function instantiate(args) {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "instantiate" }));
}

export async function createFromObject(args) {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "createFromObject" }));
}

export async function createVariant(args) {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "createVariant" }));
}

export async function openStage(path) {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { action: "openStage", path }));
}

export async function closeStage() {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { action: "closeStage" }));
}

export async function saveStage() {
  return unwrap(await Mcp.invokeAsync("prefab.execute", { action: "saveStage" }));
}

export async function getOverrides(target) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "getOverrides" }));
}

export async function applyOverrides(target, confirm = false) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "applyOverrides", confirm }));
}

export async function revertOverrides(target, confirm = false) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "revertOverrides", confirm }));
}

export async function unpack(target, mode = "OutermostRoot", confirm = false) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("prefab.execute", { ...args, action: "unpack", mode, confirm }));
}
