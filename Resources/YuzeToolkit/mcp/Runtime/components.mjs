export const summary = "**components** - Component query, edit, and instance method operations.";

export const description = `
Use this module for Component work on live scene GameObjects.

Functions:
- \`list(target)\` - List components on a GameObject.
- \`get(args)\` - Read one component. Args include target plus type, index, or componentIndex.
- \`add(target, type)\` - Add a Component type.
- \`remove(args)\` - Remove a component. Requires \`confirm: true\`.
- \`setProperty(args)\` - Set one public instance field/property.
- \`setProperties(args)\` - Set multiple fields/properties. Use \`values: { member: value }\` or \`changes: [{ member, value }]\`.
- \`callMethod(args)\` - Call a public instance method. Non-public calls require \`includeNonPublic: true\` and \`confirmDangerous: true\`.
- \`listTypes({ query?, limit? })\` - Search available Component types.

Non-public or static member writes require \`confirmDangerous: true\`.
Use \`Editor/serialized.mjs\` for Inspector serialized fields when Unity serialization semantics matter.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function list(target) {
  const args = typeof target === "object" ? target : { target };
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "list" }));
}

export async function get(args) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "get" }));
}

export async function add(target, type) {
  return unwrap(await Mcp.invokeAsync("component.execute", { action: "add", target, type }));
}

export async function remove(args) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "remove" }));
}

export async function setProperty(args) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "setProperty" }));
}

export async function setProperties(args) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "setProperties" }));
}

export async function callMethod(args) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "callMethod" }));
}

export async function listTypes(args = {}) {
  return unwrap(await Mcp.invokeAsync("component.execute", { ...args, action: "listTypes" }));
}
