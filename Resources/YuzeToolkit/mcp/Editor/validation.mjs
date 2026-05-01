export const summary = "**validation** - Project health checks for missing scripts, references, and conventions.";

export const description = `
Editor-only. Use this module for project validation before or after automated changes.

Functions:
- \`run(args = {})\` - Run all validation checks.
- \`missingScripts(args = {})\` - Find scene and Prefab GameObjects with missing MonoBehaviour scripts.
- \`missingReferences(args = {})\` - Find broken serialized object references in loaded scenes.
- \`serializedFieldTooltips(args = {})\` - Check C# files for \`[SerializeField]\` fields without nearby \`[Tooltip]\`.

The tooltip check is source-based and may report formatting edge cases; use it as a project convention guard.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function run(args = {}) {
  return unwrap(await Mcp.invokeAsync("validation.execute", { ...args, action: "run" }));
}

export async function missingScripts(args = {}) {
  return unwrap(await Mcp.invokeAsync("validation.execute", { ...args, action: "missingScripts" }));
}

export async function missingReferences(args = {}) {
  return unwrap(await Mcp.invokeAsync("validation.execute", { ...args, action: "missingReferences" }));
}

export async function serializedFieldTooltips(args = {}) {
  return unwrap(await Mcp.invokeAsync("validation.execute", { ...args, action: "serializedFieldTooltips" }));
}
