export const summary = "**pipeline** - Package Manager, Test Runner, and BuildPipeline workflows.";

export const description = `
Editor-only. Use this module for long-running or project-wide pipeline operations.

Package Manager:
- \`listPackages()\`
- \`addPackage(packageId, confirm = false)\`, \`removePackage(packageName, confirm = false)\`, \`searchPackages(packageName)\`
- \`getPackageRequest(id)\` - Poll add/remove/search request state.

Testing and build:
- \`runTests(mode = "EditMode")\`, \`getTestRun(id)\`
- \`getBuildSettings()\`
- \`buildPlayer(locationPathName, confirm = false)\`, \`getBuild(id)\`

Package add/remove and builds can change the project and require explicit confirmation.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function listPackages() {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "listPackages" }));
}

export async function addPackage(packageId, confirm = false) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "addPackage", packageId, confirm }));
}

export async function removePackage(packageName, confirm = false) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "removePackage", packageName, confirm }));
}

export async function searchPackages(packageName) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "searchPackages", packageName }));
}

export async function getPackageRequest(id) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "getPackageRequest", id }));
}

export async function runTests(mode = "EditMode") {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "runTests", mode }));
}

export async function getTestRun(id) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "getTestRun", id }));
}

export async function getBuildSettings() {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "getBuildSettings" }));
}

export async function buildPlayer(locationPathName, confirm = false) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "buildPlayer", locationPathName, confirm }));
}

export async function getBuild(id) {
  return unwrap(await Mcp.invokeAsync("pipeline.execute", { action: "getBuild", id }));
}
