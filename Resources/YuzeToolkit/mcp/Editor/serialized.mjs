export const summary = "**serialized** - SerializedObject and Inspector property reads/writes.";

export const description = `
Editor-only. Use this module when Unity serialization semantics matter.

Functions:
- \`get(args)\` - Read all visible serialized properties or one \`propertyPath\`.
- \`set(args)\` - Set one supported SerializedProperty value.
- \`setMany(args)\` - Set multiple properties with \`changes: [{ propertyPath, value }]\`.
- \`resizeArray(args)\` - Set a serialized array size.
- \`insertArrayElement(args)\`, \`deleteArrayElement(args)\`.

Targets can be \`assetPath\`, \`guid\`, \`instanceId\`, or a GameObject selector.
Object references can be set with instanceId, assetPath, guid, or selector objects.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function get(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "get" }));
}

export async function set(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "set" }));
}

export async function setMany(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "setMany" }));
}

export async function resizeArray(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "resizeArray" }));
}

export async function insertArrayElement(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "insertArrayElement" }));
}

export async function deleteArrayElement(args) {
  return unwrap(await Mcp.invokeAsync("serialized.execute", { ...args, action: "deleteArrayElement" }));
}
