# Advanced Notes

[README](../README.md) | [中文](ADVANCED_USAGE_zh.md) | [Client setup](CLIENT_SETUP.md) | [Helper reference](HELPER_MODULES.md) | [Project design](PROJECT_DESIGN.md)

[![Bridge](https://img.shields.io/badge/Bridge-Commands-9b59b6)](#bridge-commands)
[![PuerTS](https://img.shields.io/badge/PuerTS-C%23%20Interop-00a8ff)](#puerts-c-interop)
[![Safety](https://img.shields.io/badge/Safety-confirm%20required-e67e22)](#safety-flags)

This page is for agents and maintainers. It covers low-level bridge commands, direct PuerTS C# interop, safety flags, Domain Reload rules, old helper migration, and troubleshooting.

## Bridge Commands

Helper modules are the preferred API. Use direct bridge commands only when a helper does not cover the task or when debugging the helper layer.

Response shape:

```json
{ "success": true, "result": {} }
```

or:

```json
{ "success": false, "error": "message" }
```

Direct call:

```javascript
async function execute() {
  return await Mcp.invokeAsync("asset.execute", {
    action: "find",
    filter: "t:Prefab",
    folders: ["Assets"],
    limit: 20
  });
}
```

## Runtime Commands

| Command | Actions |
|---|---|
| `runtime.getState` | No action argument. |
| `log.execute` | `getRecent`, `clear` |
| `object.execute` | `find`, `get`, `create`, `destroy`, `duplicate`, `setParent`, `setTransform`, `setActive`, `setNameLayerTag` |
| `component.execute` | `list`, `get`, `add`, `remove`, `setProperty`, `setProperties`, `callMethod`, `listTypes` |
| `runtime.diagnostics` | `cameraList`, `physicsState`, `graphicsState`, `uiList`, `textureList` |
| `reflection.execute` | `getNamespaces`, `getTypes`, `getTypeDetails`, `findMethods`, `callStaticMethod` |
| `batch.execute` | Args are `{ commands, stopOnError? }`. |

## Editor Commands

| Command | Actions |
|---|---|
| `editor.execute` | `getState`, `getCompilationState`, `requestScriptCompilation`, `scheduleAssetRefresh`, `getCompilerMessages`, `setPlayMode`, `setPause`, `executeMenuItem`, `selectionGet`, `selectionSet`, `screenshotGameView` |
| `asset.execute` | `find`, `getInfo`, `readText`, `writeText`, `createFolder`, `move`, `copy`, `delete`, `refreshNow`, `getDependencies`, `findReferences`, `scriptCreate`, `scriptApplyTextEdits`, `materialCreate` |
| `importer.execute` | `get`, `setProperty`, `setMany`, `reimport` |
| `scene.execute` | `listOpen`, `getHierarchy`, `open`, `create`, `save`, `saveAs`, `setActive` |
| `prefab.execute` | `instantiate`, `createFromObject`, `createVariant`, `openStage`, `closeStage`, `saveStage`, `getOverrides`, `applyOverrides`, `revertOverrides`, `unpack` |
| `serialized.execute` | `get`, `set`, `setMany`, `resizeArray`, `insertArrayElement`, `deleteArrayElement` |
| `project.execute` | `getSettings`, `profilerState`, `toolState` |
| `pipeline.execute` | `listPackages`, `addPackage`, `removePackage`, `searchPackages`, `getPackageRequest`, `runTests`, `getTestRun`, `getBuildSettings`, `buildPlayer`, `getBuild` |
| `validation.execute` | `run`, `missingScripts`, `missingReferences`, `serializedFieldTooltips` |

Editor commands require the Unity Editor. Runtime/Player calls return an Editor-only error.

## Batch Commands

Use `Runtime/runtime.mjs#executeBatch()` or call `batch.execute` directly for short command sequences that do not require Unity reloads between steps.

```javascript
async function execute() {
  const runtime = await import('YuzeToolkit/mcp/Runtime/runtime.mjs');
  return await runtime.executeBatch({
    stopOnError: true,
    commands: [
      { name: "object.execute", args: { action: "find", by: "name", value: "Player", limit: 5 } },
      { name: "runtime.diagnostics", args: { action: "graphicsState" } }
    ]
  });
}
```

Do not batch script compilation, AssetDatabase refresh, package add/remove, builds, or test polling that must happen after Unity updates its state.

## PuerTS C# Interop

Direct JavaScript runs in PuerTS, not Node.js.

Runtime API example:

```javascript
async function execute() {
  const go = CS.UnityEngine.GameObject.Find("Main Camera");
  if (!go) return { found: false };
  const camera = go.GetComponent(puer.$typeof(CS.UnityEngine.Camera));
  return {
    found: true,
    name: go.name,
    instanceId: go.GetInstanceID(),
    orthographic: camera ? camera.orthographic : null
  };
}
```

Rules:

- Use `CS.UnityEngine.*` for runtime Unity APIs.
- Use `CS.UnityEditor.*` only in the Unity Editor.
- Use `puer.$typeof(...)` or `puerts.$typeof(...)` when Unity expects `System.Type`.
- Do not assume Node.js modules such as `fs` are available.
- Return JSON-friendly summaries, not raw `UnityEngine.Object` graphs.

## Project-Specific C# APIs

Use `Runtime/reflection.mjs` for project static methods:

```javascript
async function execute() {
  const reflection = await import('YuzeToolkit/mcp/Runtime/reflection.mjs');
  return await reflection.callStaticMethod({
    type: "MyGame.EditorTools.AssetReport",
    method: "CreateReport",
    args: ["Assets"]
  });
}
```

Non-public calls require:

```javascript
includeNonPublic: true,
confirmDangerous: true
```

## Domain Reload Rules

Unity can become temporarily unavailable while compiling scripts, refreshing assets, or changing play mode.

| Rule | Reason |
|---|---|
| Do not trigger compilation and wait in the same `evalJsCode` call. | Domain Reload can kill the session. |
| Split file edits, refresh requests, and compiler checks into separate calls. | Unity needs editor ticks between steps. |
| Reinitialize the MCP client after session loss. | Domain Reload or server restart invalidates session ids. |
| Retry busy errors after Unity becomes idle. | The server intentionally rejects unsafe eval during busy states. |

Recommended script-edit flow:

1. Edit or create files.
2. Return from `evalJsCode`.
3. Call `scheduleAssetRefresh()` or `requestScriptCompilation()`.
4. Reconnect if needed.
5. Query `getCompilationState()` and `getCompilerMessages()`.

## Safety Flags

| Flag | Used For |
|---|---|
| `confirm: true` | Destructive or project-changing operations. |
| `confirmDangerous: true` | Non-public reflection or dangerous member access. |

Common operations requiring `confirm: true`:

- `objects.destroy(...)`
- `components.remove(...)`
- `assets.move(...)`
- `assets.deleteAsset(...)`
- `prefabs.applyOverrides(...)`
- `prefabs.revertOverrides(...)`
- `prefabs.unpack(...)`
- `pipeline.addPackage(...)`
- `pipeline.removePackage(...)`
- `pipeline.buildPlayer(...)`
- non-`YuzeToolkit/MCP` menu items

## Path Rules

Bridge commands that perform project file IO resolve paths inside the Unity project root.

| Good | Bad |
|---|---|
| `Assets/Settings/GameSettings.asset` | `../outside-project/file.txt` |
| `Packages/com.yuzetoolkit.mcptool/README.md` | `C:/Users/Name/Desktop/file.txt` |
| `Temp/YuzeMcpTool-GameView.png` | Network paths or unrelated absolute paths |

## Helper Path Migration

| Old Path | New Path / Function |
|---|---|
| `Runtime/runtime-state.mjs` | `Runtime/runtime.mjs#getState()` |
| `Runtime/unity-log.mjs` | `Runtime/runtime.mjs#getRecentLogs()` / `clearLogs()` |
| `Runtime/batch.mjs` | `Runtime/runtime.mjs#executeBatch()` |
| `Runtime/object-query.mjs` | `Runtime/objects.mjs#find()` / `get()` |
| `Runtime/object-edit.mjs` | `Runtime/objects.mjs#create()` / `destroy()` / `setTransform()` |
| `Runtime/component.mjs` | `Runtime/components.mjs` |
| `Runtime/runtime-inspect.mjs` | `Runtime/diagnostics.mjs` |
| `Editor/editor-state.mjs` | `Editor/editor.mjs#getState()` |
| `Editor/compilation.mjs` | `Editor/editor.mjs#getCompilationState()` / `scheduleAssetRefresh()` |
| `Editor/selection.mjs` | `Editor/editor.mjs#getSelection()` / `setSelection()` |
| `Editor/menu-command.mjs` | `Editor/editor.mjs#executeMenuItem()` |
| `Editor/screenshot.mjs` | `Editor/editor.mjs#screenshotGameView()` |
| `Editor/asset-db.mjs` | `Editor/assets.mjs` |
| `Editor/scene.mjs` | `Editor/scenes.mjs` |
| `Editor/prefab.mjs` | `Editor/prefabs.mjs` |
| `Editor/serialized-object.mjs` | `Editor/serialized.mjs` |
| `Editor/package-manager.mjs` / `Editor/test-runner.mjs` / `Editor/build.mjs` | `Editor/pipeline.mjs` |
| `Editor/editor-inspect.mjs` | `Editor/project.mjs` |

## Troubleshooting

| Error | Fix |
|---|---|
| `Session not found` | Reinitialize the MCP client. |
| `Unity Editor is compiling scripts` | Wait for compilation, then retry. |
| `Unity Editor is updating assets` | Wait for AssetDatabase refresh. |
| `Unity Editor is changing play mode` | Wait for the play mode transition. |
| `Command '...' is Editor-only` | Use the Unity Editor or switch to Runtime helpers. |
| `Unknown command` | Call `runtime.getState()` and inspect `registeredCommands`. |
| `Unknown action` | Check the relevant helper description or command table. |
| Unity hangs | Avoid synchronous infinite loops in `execute()`; timeout cannot reliably interrupt a blocked main thread. |
