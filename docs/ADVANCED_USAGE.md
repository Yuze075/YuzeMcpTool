# Advanced Notes

[README](../README.md) | [中文](ADVANCED_USAGE_zh.md) | [Helper reference](HELPER_MODULES.md) | [Runtime services](RUNTIME_SERVICES.md) | [Project design](PROJECT_DESIGN.md)

[![Tools](https://img.shields.io/badge/Tools-Generated-9b59b6)](#tool-invokes)
[![PuerTS](https://img.shields.io/badge/PuerTS-C%23%20Interop-00a8ff)](#puerts-c-interop)
[![Safety](https://img.shields.io/badge/Safety-confirm%20required-e67e22)](#safety-flags)

This page is for agents and maintainers. It covers generated `tools/<name>` modules, PuerTS C# interop, safety flags, Domain Reload rules, and troubleshooting.

## Tool Invokes

Generated `tools/<name>` modules are the preferred API for common workflows. C# tool modules export semantic JavaScript functions that validate the tool is enabled, call public C# instance methods through PuerTS, and format return values for MCP output. Use positional arguments; `module.functions[]` exposes `description`, ordered `parameters`, and legacy `parameterTypes`. Use `module.isEnabled()` when an agent needs the live enabled state.

Direct module call:

```javascript
async function execute() {
  const assets = await import('tools/assets');
  return assets.find('t:Prefab', 20, ['Assets']);
}
```

## Runtime Service Startup

Runtime/Player code can start services directly:

```csharp
McpServer.Shared.Start(new McpServerOptions
{
    Host = "127.0.0.1",
    Port = 3100,
    RequireToken = true,
    Token = "runtime-token"
});

CliBridgeServer.Shared.Start(new CliBridgeOptions
{
    Host = "127.0.0.1",
    Port = 0,
    RequireToken = true,
    Token = "runtime-token"
});
```

Use `McpServerBehaviour` when scene lifetime is enough. Use `McpServer.Shared.StartWithOwner(ownerId, options)` when multiple systems may keep the shared MCP server alive. Runtime settings are not loaded from the Editor window; load your own config and pass it into the options. Details and parity notes are in [Runtime services](RUNTIME_SERVICES.md).

## Runtime Tools

| Tool | Actions |
|---|---|
| `runtime` | `getState`, `getRecentLogs`, `clearLogs` |
| `cli` | `startCliBridge`, `stopCliBridge`, `getCliBridgeState` |
| `objects` | `find`, `get`, `create`, `destroy`, `duplicate`, `setParent`, `setTransform`, `setActive`, `setNameLayerTag` |
| `components` | `list`, `get`, `add`, `remove`, `setProperty`, `setProperties`, `callMethod`, `listTypes` |
| `diagnostics` | `listCameras`, `getPhysicsState`, `getGraphicsState`, `listCanvases`, `listLoadedTextures` |
| `reflection` | `getNamespaces`, `getTypes`, `getTypeDetails`, `findMethods`, `callStaticMethod` |
| `inspect` | `describe`, `format`, `toName`, `toPath`, `toJson`, `toYaml` |

## Editor Tools

| Tool | Actions |
|---|---|
| `editor` | `getState`, `getCompilationState`, `requestScriptCompilation`, `scheduleAssetRefresh`, `getCompilerMessages`, `setPlayMode`, `setPause`, `executeMenuItem`, `getSelection`, `setSelection`, `screenshotGameView` |
| `assets` | `find`, `findPaths`, `findNames`, `getInfo`, `readText`, `writeText`, `createFolder`, `move`, `copy`, `deleteAsset`, `refreshNow`, `getDependencies`, `findReferences`, `createScript`, `applyScriptTextEdits`, `createMaterial` |
| `importers` | `get`, `setProperty`, `setMany`, `reimport` |
| `scenes` | `listOpenScenes`, `getSceneHierarchy`, `openScene`, `createScene`, `saveScene`, `saveSceneAs`, `setActiveScene` |
| `prefabs` | `instantiate`, `createFromObject`, `createVariant`, `openStage`, `closeStage`, `saveStage`, `getOverrides`, `applyOverrides`, `revertOverrides`, `unpack` |
| `serialized` | `get`, `set`, `setMany`, `resizeArray`, `insertArrayElement`, `deleteArrayElement` |
| `project` | `getProjectSettings`, `getProfilerState`, `getToolState` |
| `pipeline` | `listPackages`, `addPackage`, `removePackage`, `searchPackages`, `getPackageRequest`, `runTests`, `getTestRun`, `getBuildSettings`, `buildPlayer`, `getBuild` |
| `validation` | `run`, `missingScripts`, `missingReferences`, `serializedFieldTooltips` |

Editor commands require the Unity Editor. Runtime/Player calls return an Editor-only error.

## Sequential Commands

Write short command sequences directly in JavaScript. Do not wrap them in a bridge-level batch call.

```javascript
async function execute() {
  const objects = await import('tools/objects');
  const diagnostics = await import('tools/diagnostics');
  return {
    players: objects.find('Player', 'name', true, 5),
    graphics: diagnostics.getGraphicsState()
  };
}
```

Do not batch script compilation, AssetDatabase refresh, package add/remove, builds, or test polling that must happen after Unity updates its state.

## PuerTS C# Interop

Direct JavaScript runs in PuerTS, not Node.js.

Direct PuerTS interop is also a first-class capability. Use it for one-off debugging or project-specific APIs that are not worth promoting into a helper yet.

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
- Return data composed from primitives, lists, and dictionaries for the most stable server-side JSON output path.
- Returning `UnityEngine.Object` or custom C# objects is supported, but the server summarizes them instead of preserving a full object graph.

## Return Value Rules

The `evalJsCode` JavaScript runner does not convert the result to text before returning to C#. The MCP server formats the final result at the server boundary:

| Return type | Server handling |
|---|---|
| `string`, number, bool, null | Written as a JSON value into MCP text content. |
| `List<T>`, array, `IEnumerable<T>` | Elements are recursively converted and returned as a JSON array. |
| `Dictionary<string, TValue>` or dictionary-shaped data | Values are recursively converted and returned as a JSON object. |
| `UnityEngine.Object` | Summarized with `name`, `type`, `instanceId`; assets add asset path/guid; GameObject/Component add hierarchy and component relationships. |
| Custom C# object | Returned as `type`, `string`, and public instance members; members are recursively converted within the depth limit. |

The recommended tool result shape is an explicit DTO composed from primitives, lists, and dictionaries. These values always have a deterministic JSON representation and are easiest for MCP clients and agents to parse.

## Project-Specific C# APIs

Use `tools/reflection` for project static methods:

```javascript
async function execute() {
  const reflection = await import('tools/reflection');
  return reflection.callStaticMethod("MyGame.EditorTools.AssetReport", "CreateReport", ["Assets"]);
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
| Reconnect CLI clients after Unity Test Runner, compilation, or Domain Reload. | The Editor can restore the CLI bridge listener after reload, but existing TCP clients are gone. |
| Stop and restart a service to apply host, port, or auth changes. | MCP and CLI listeners bind options only when they start. |
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
- non-`UnityEvalTool` menu items

## Path Rules

Tool calls that perform project file IO resolve paths inside the Unity project root.

| Good | Bad |
|---|---|
| `Assets/Settings/GameSettings.asset` | `../outside-project/file.txt` |
| `Packages/com.yuzetoolkit.unityevaltool/README.md` | `C:/Users/Name/Desktop/file.txt` |
| `Temp/UnityEvalTool-GameView.png` | Network paths or unrelated absolute paths |

## Troubleshooting

| Error | Fix |
|---|---|
| `Session not found` | Reinitialize the MCP client. |
| Runtime service uses old host or port | Stop the service and start it again with new options. |
| Runtime does not match Editor window settings | Player does not read `EditorPrefs`; pass the same values into Runtime options. |
| `Unity Editor is compiling scripts` | Wait for compilation, then retry. |
| `Unity Editor is updating assets` | Wait for AssetDatabase refresh. |
| `Unity Editor is changing play mode` | Wait for the play mode transition. |
| `Command '...' is Editor-only` | Use the Unity Editor or switch to Runtime helpers. |
| `Unknown or disabled MCP tool` | Call `runtime.getState()` and inspect `registeredTools`; check the Server Window tool toggles if the tool exists. |
| `Unknown action` | Check the relevant helper description or command table. |
| Unity hangs | Avoid synchronous infinite loops in `execute()`; timeout cannot reliably interrupt a blocked main thread. |
