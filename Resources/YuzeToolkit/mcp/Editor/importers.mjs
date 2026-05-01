export const summary = "**importers** - AssetImporter inspection, serialized edits, and reimport.";

export const description = `
Editor-only. Use this module for Texture/Sprite, Audio, Model, and generic AssetImporter workflows.

Functions:
- \`get(path, includeProperties = false)\` - Read importer summary and optionally visible serialized importer properties.
- \`setProperty(args)\` - Set one importer SerializedProperty. Args: path, propertyPath, value, saveAndReimport?.
- \`setMany(args)\` - Set multiple importer properties with \`changes: [{ propertyPath, value }]\`.
- \`reimport(path)\` - Force reimport an asset.

For TextureImporter and AudioImporter this returns a compact domain summary. Use \`includeProperties: true\` to discover exact property paths.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function get(path, includeProperties = false) {
  return unwrap(await Mcp.invokeAsync("importer.execute", { action: "get", path, includeProperties }));
}

export async function setProperty(args) {
  return unwrap(await Mcp.invokeAsync("importer.execute", { ...args, action: "setProperty" }));
}

export async function setMany(args) {
  return unwrap(await Mcp.invokeAsync("importer.execute", { ...args, action: "setMany" }));
}

export async function reimport(path) {
  return unwrap(await Mcp.invokeAsync("importer.execute", { action: "reimport", path }));
}
