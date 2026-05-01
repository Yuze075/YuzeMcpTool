export const summary = "**project** - Project settings, profiler, and editor tool diagnostics.";

export const description = `
Editor-only. Use this module for project-level information that is not tied to one scene object or asset file.

Project and diagnostics:
- \`getProjectSettings()\` - Product/company/application id, tags, and layers.
- \`getProfilerState()\` - Basic profiler availability and recording flags.
- \`getToolState()\` - Active Editor tool type, pivot mode, pivot rotation, and current transform tool.

Use \`Editor/pipeline.mjs\` for Package Manager, tests, and builds.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function getProjectSettings() {
  return unwrap(await Mcp.invokeAsync("project.execute", { action: "getSettings" }));
}

export async function getProfilerState() {
  return unwrap(await Mcp.invokeAsync("project.execute", { action: "profilerState" }));
}

export async function getToolState() {
  return unwrap(await Mcp.invokeAsync("project.execute", { action: "toolState" }));
}
