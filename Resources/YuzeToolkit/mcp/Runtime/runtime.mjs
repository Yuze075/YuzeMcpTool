export const summary = "**runtime** - Environment state, Unity logs, and bridge command batching.";

export const description = `
Use this module for lightweight runtime-safe operations before choosing more specific Unity helpers.

Functions:
- \`getState()\` - Return environment, Unity version, platform, play state, paths, active scene, and registered bridge commands.
- \`getRecentLogs(count = 50, type = "all")\` - Return MCP-captured Unity logs. Type can be "all", "Log", "Warning", "Error", "Exception", or "Assert".
- \`clearLogs()\` - Clear only the MCP log buffer. This does not clear the Unity Console window.
- \`executeBatch({ commands, stopOnError? })\` - Run multiple bridge commands in order. Each item is \`{ name, args }\`.

Use \`getState()\` or \`/health\` first when you are unsure whether Unity is running in Editor or Runtime/Player.
Batch only commands that do not need a Domain Reload between steps.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function getState() {
  return unwrap(await Mcp.invokeAsync("runtime.getState", {}));
}

export async function getRecentLogs(count = 50, type = "all") {
  return unwrap(await Mcp.invokeAsync("log.execute", { action: "getRecent", count, type }));
}

export async function clearLogs() {
  return unwrap(await Mcp.invokeAsync("log.execute", { action: "clear" }));
}

export async function executeBatch(args) {
  return unwrap(await Mcp.invokeAsync("batch.execute", args));
}
