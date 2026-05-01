export const summary = "**editor** - Editor state, compilation, selection, menu commands, play mode, and screenshots.";

export const description = `
Editor-only. Use this module for Unity Editor workflow state and control.

Functions:
- \`getState()\` - Return play/compile/update state, paths, active scene, and selection summary.
- \`getCompilationState()\` - Return current compiler/asset refresh state.
- \`requestScriptCompilation()\` - Schedule script compilation on the next editor tick.
- \`scheduleAssetRefresh()\` - Schedule AssetDatabase.Refresh() on the next editor tick.
- \`getCompilerMessages(count = 50)\` - Return recent compiler-like error/warning logs from the MCP log buffer.
- \`getSelection()\`, \`setSelection(items)\` - Read or set Editor selection by asset path, instanceId, or selector object.
- \`executeMenuItem(path, confirm = false)\` - Execute an Editor menu item. Non-YuzeToolkit/MCP menu items require \`confirm: true\`.
- \`setPlayMode(isPlaying)\`, \`setPause(isPaused)\` - Change Editor play/pause state.
- \`screenshotGameView(path = "Temp/YuzeMcpTool-GameView.png")\` - Request a Game View screenshot and return its target path.

Domain Reload rule: do not trigger compilation/refresh and wait for completion in the same evalJsCode call. Trigger it, reconnect if needed, then query state/logs in a later call.
`.trim();

function unwrap(response) {
  if (!response || response.success !== true) throw new Error(response?.error || "MCP bridge command failed.");
  return response.result;
}

export async function getState() {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "getState" }));
}

export async function getCompilationState() {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "getCompilationState" }));
}

export async function requestScriptCompilation() {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "requestScriptCompilation" }));
}

export async function scheduleAssetRefresh() {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "scheduleAssetRefresh" }));
}

export async function getCompilerMessages(count = 50) {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "getCompilerMessages", count }));
}

export async function getSelection() {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "selectionGet" }));
}

export async function setSelection(items) {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "selectionSet", items }));
}

export async function executeMenuItem(path, confirm = false) {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "executeMenuItem", path, confirm }));
}

export async function setPlayMode(isPlaying) {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "setPlayMode", isPlaying }));
}

export async function setPause(isPaused) {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "setPause", isPaused }));
}

export async function screenshotGameView(path = "Temp/YuzeMcpTool-GameView.png") {
  return unwrap(await Mcp.invokeAsync("editor.execute", { action: "screenshotGameView", path }));
}
