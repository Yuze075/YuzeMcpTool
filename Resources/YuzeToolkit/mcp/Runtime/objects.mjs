export const summary = "**objects** - Scene GameObject, hierarchy, and Transform query/edit operations.";

export const description = `
Use this module for scene GameObjects, transforms, hierarchy, activation, tags, and layers.

GameObject functions:
- \`find({ by?, value, includeInactive?, limit? })\` - Find GameObjects by "name", "path", "tag", or "component".
- \`get(target)\` - Inspect one GameObject. Target can be name, hierarchy path, instanceId, or selector object.
- \`create(args)\` - Create a GameObject. Args: name, primitive?, parent?, position?, localPosition?, localScale?.
- \`destroy(target, confirm = false)\` - Destroy a GameObject. Requires \`confirm: true\`.
- \`duplicate(target, name?)\`, \`setParent(target, parent?, worldPositionStays?)\`.
- \`setTransform(args)\` - Set position/localPosition/rotationEuler/localRotationEuler/localScale.
- \`setActive(target, active)\`, \`setNameLayerTag(args)\`.

Selectors are accepted consistently: string name/path, instanceId number, or an object such as \`{ path, includeInactive }\`.
Use \`Runtime/components.mjs\` for Component reads/writes and method calls.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function find(args) {
  return unwrap(await Mcp.invokeAsync("object.execute", { ...args, action: "find" }));
}

export async function get(target) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("object.execute", { ...args, action: "get" }));
}

export async function create(args) {
  return unwrap(await Mcp.invokeAsync("object.execute", { ...args, action: "create" }));
}

export async function destroy(target, confirm = false) {
  return unwrap(await Mcp.invokeAsync("object.execute", { action: "destroy", target, confirm }));
}

export async function duplicate(target, name) {
  return unwrap(await Mcp.invokeAsync("object.execute", { action: "duplicate", target, name }));
}

export async function setParent(target, parent = null, worldPositionStays = true) {
  return unwrap(await Mcp.invokeAsync("object.execute", { action: "setParent", target, parent, worldPositionStays }));
}

export async function setTransform(args) {
  return unwrap(await Mcp.invokeAsync("object.execute", { ...args, action: "setTransform" }));
}

export async function setActive(target, active) {
  return unwrap(await Mcp.invokeAsync("object.execute", { action: "setActive", target, active }));
}

export async function setNameLayerTag(args) {
  return unwrap(await Mcp.invokeAsync("object.execute", { ...args, action: "setNameLayerTag" }));
}
