# Helper Reference

[README](../README.md) | [中文](HELPER_MODULES_zh.md) | [Client setup](CLIENT_SETUP.md) | [Project design](PROJECT_DESIGN.md) | [Advanced notes](ADVANCED_USAGE.md)

[![Runtime](https://img.shields.io/badge/Runtime-5%20modules-2ecc71)](#runtime-helpers)
[![Editor](https://img.shields.io/badge/Editor-9%20modules-3498db)](#editor-helpers)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](../README.md#design-choice)

YuzeMcpTool exposes one MCP tool, `evalJsCode`. Inside that tool, agents import JavaScript helper modules from `YuzeToolkit/mcp/...`.

Start discovery with:

```javascript
async function execute() {
  const index = await import('YuzeToolkit/mcp/index.mjs');
  return index.description;
}
```

## Module Index

| Category | Modules |
|---|---|
| Runtime helpers | `runtime`, `objects`, `components`, `diagnostics`, `reflection` |
| Editor helpers | `editor`, `assets`, `importers`, `scenes`, `prefabs`, `serialized`, `project`, `pipeline`, `validation` |

Runtime helpers can run in Editor or Runtime/Player when the underlying Unity API is available. Editor helpers require `UnityEditor` and fail clearly in Runtime/Player.

## Runtime Helpers

### `Runtime/runtime.mjs`

Environment state, Unity logs, and command batching.

| Function | Purpose |
|---|---|
| `getState()` | Environment, Unity version, platform, play state, paths, active scene, registered commands. |
| `getRecentLogs(count, type)` | MCP-captured Unity logs. |
| `clearLogs()` | Clear the MCP log buffer. |
| `executeBatch({ commands, stopOnError? })` | Run multiple bridge commands in order. |

### `Runtime/objects.mjs`

Scene GameObject, hierarchy, and Transform operations.

| Function | Purpose | Safety |
|---|---|---|
| `find({ by, value, includeInactive, limit })` | Find GameObjects by `name`, `path`, `tag`, or `component`. | Read-only |
| `get(target)` | Inspect one GameObject. | Read-only |
| `create(args)` | Create an empty or primitive GameObject. | Mutates scene |
| `destroy(target, confirm)` | Destroy a GameObject. | Requires `confirm: true` |
| `duplicate(target, name)` | Duplicate a GameObject. | Mutates scene |
| `setParent(target, parent, worldPositionStays)` | Change hierarchy parent. | Mutates scene |
| `setTransform(args)` | Set position, rotation, or scale. | Mutates scene |
| `setActive(target, active)` | Change active state. | Mutates scene |
| `setNameLayerTag(args)` | Change name, layer, or tag. | Mutates scene |

### `Runtime/components.mjs`

Component reads, edits, and instance method calls.

| Function | Purpose | Safety |
|---|---|---|
| `list(target)` | List components on a GameObject. | Read-only |
| `get(args)` | Read one component by type/index. | Read-only |
| `add(target, type)` | Add a Component. | Mutates scene |
| `remove(args)` | Remove a Component. | Requires `confirm: true` |
| `setProperty(args)` | Set one public instance field/property. | Mutates component |
| `setProperties(args)` | Set multiple public fields/properties. | Mutates component |
| `callMethod(args)` | Call a public instance method. | Method-dependent |
| `listTypes(args)` | Search available Component types. | Read-only |

Non-public method calls require `includeNonPublic: true` and `confirmDangerous: true`.

### `Runtime/diagnostics.mjs`

Read-only runtime diagnostics.

| Function | Purpose |
|---|---|
| `listCameras()` | Scene cameras and common settings. |
| `getPhysicsState()` | Physics2D settings and Collider2D summaries. |
| `getGraphicsState()` | Render pipeline, quality, color space. |
| `listCanvases()` | Canvas objects and render settings. |
| `listLoadedTextures(limit)` | Loaded texture objects with size and type. |

### `Runtime/reflection.mjs`

C# type discovery and static method calls for project-specific APIs.

| Function | Purpose | Safety |
|---|---|---|
| `getNamespaces()` | List public namespaces. | Read-only |
| `getTypes(namespaceName)` | List public types in a namespace. | Read-only |
| `getTypeDetails(fullName)` | List public members for a type. | Read-only |
| `findMethods(args)` | Search public methods. | Non-public search requires `confirmDangerous: true` |
| `callStaticMethod(args)` | Call a public static method. | Non-public call requires `confirmDangerous: true` |

## Editor Helpers

### `Editor/editor.mjs`

Editor state, compilation, selection, menu commands, play mode, and screenshots.

| Function | Purpose | Safety |
|---|---|---|
| `getState()` | Editor state, active scene, selection summary. | Read-only |
| `getCompilationState()` | Compilation and asset refresh state. | Read-only |
| `requestScriptCompilation()` | Request script compilation. | May trigger reload |
| `scheduleAssetRefresh()` | Request AssetDatabase refresh. | May trigger reload |
| `getCompilerMessages(count)` | Recent compiler-like errors/warnings. | Read-only |
| `getSelection()` / `setSelection(items)` | Read or set Editor selection. | Selection mutation |
| `executeMenuItem(path, confirm)` | Execute an Editor menu item. | Non-Yuze menu requires `confirm: true` |
| `setPlayMode(isPlaying)` / `setPause(isPaused)` | Control play/pause state. | Changes Editor state |
| `screenshotGameView(path)` | Capture Game View. | Writes screenshot file |

### `Editor/assets.mjs`

AssetDatabase search, project text IO, dependencies, scripts, materials, and refresh.

| Function | Purpose | Safety |
|---|---|---|
| `find(args)` | Search assets with Unity filters. | Read-only |
| `getInfo(path)` | Asset metadata. | Read-only |
| `readText(path)` / `writeText(args)` | Read or write text assets. | Write mutates project |
| `createFolder(parent, name)` | Create an AssetDatabase folder. | Mutates project |
| `copy(from, to)` | Copy an asset. | Mutates project |
| `move(from, to, confirm)` | Move or rename an asset. | Requires `confirm: true` |
| `deleteAsset(path, confirm)` | Delete an asset. | Requires `confirm: true` |
| `refreshNow()` | Refresh AssetDatabase now. | May trigger reload |
| `getDependencies(path, recursive)` | Asset dependencies. | Read-only |
| `findReferences(path, folders, limit)` | Search asset references. | Read-only |
| `createScript(args)` | Create a MonoBehaviour script. | May trigger reload |
| `applyScriptTextEdits(args)` | Patch a script file. | May trigger reload |
| `createMaterial(args)` | Create a Material asset. | Mutates project |

### `Editor/importers.mjs`

AssetImporter inspection and edits.

| Function | Purpose | Safety |
|---|---|---|
| `get(path, includeProperties)` | Importer summary and optional serialized properties. | Read-only |
| `setProperty(args)` | Set one importer SerializedProperty. | Mutates importer |
| `setMany(args)` | Set many importer properties. | Mutates importer |
| `reimport(path)` | Force reimport. | Mutates imported asset |

### `Editor/scenes.mjs`

Scene files and open scene hierarchy.

| Function | Purpose | Safety |
|---|---|---|
| `listOpenScenes()` | List open scenes. | Read-only |
| `getSceneHierarchy(args)` | Read open scene hierarchy. | Read-only |
| `openScene(path, mode)` | Open a scene. | Changes Editor state |
| `createScene(args)` | Create a new scene. | Mutates project/session |
| `saveScene()` / `saveSceneAs(path)` | Save scene. | Writes scene asset |
| `setActiveScene(path)` | Set active scene. | Changes Editor state |

### `Editor/prefabs.mjs`

Prefab instance, asset, stage, override, and unpack operations.

| Function | Purpose | Safety |
|---|---|---|
| `instantiate(args)` | Instantiate a Prefab asset. | Mutates scene |
| `createFromObject(args)` | Save a scene object as Prefab. | Mutates project |
| `createVariant(args)` | Create a Prefab variant. | Mutates project |
| `openStage(path)` / `closeStage()` / `saveStage()` | Prefab Stage workflow. | Changes Editor/project state |
| `getOverrides(target)` | Read Prefab overrides. | Read-only |
| `applyOverrides(target, confirm)` | Apply overrides. | Requires `confirm: true` |
| `revertOverrides(target, confirm)` | Revert overrides. | Requires `confirm: true` |
| `unpack(target, mode, confirm)` | Unpack a Prefab instance. | Requires `confirm: true` |

### `Editor/serialized.mjs`

SerializedObject and Inspector property reads/writes.

| Function | Purpose | Safety |
|---|---|---|
| `get(args)` | Read all visible properties or one `propertyPath`. | Read-only |
| `set(args)` | Set one SerializedProperty value. | Mutates object/asset |
| `setMany(args)` | Set multiple SerializedProperty values. | Mutates object/asset |
| `resizeArray(args)` | Resize serialized array. | Mutates object/asset |
| `insertArrayElement(args)` | Insert array element. | Mutates object/asset |
| `deleteArrayElement(args)` | Delete array element. | Mutates object/asset |

Targets can be `assetPath`, `guid`, `instanceId`, or a GameObject selector.

### `Editor/project.mjs`

Project-level diagnostics.

| Function | Purpose |
|---|---|
| `getProjectSettings()` | Product/company/application id, tags, layers. |
| `getProfilerState()` | Profiler availability and recording flags. |
| `getToolState()` | Active Editor tool, pivot mode, pivot rotation. |

### `Editor/pipeline.mjs`

Package Manager, Test Runner, and BuildPipeline workflows.

| Function | Purpose | Safety |
|---|---|---|
| `listPackages()` | List registered packages. | Read-only |
| `addPackage(packageId, confirm)` | Add a package. | Requires `confirm: true` |
| `removePackage(packageName, confirm)` | Remove a package. | Requires `confirm: true` |
| `searchPackages(packageName)` | Search package registry. | Read-only/request |
| `getPackageRequest(id)` | Poll package request. | Read-only |
| `runTests(mode)` / `getTestRun(id)` | Run or poll tests. | Test-dependent |
| `getBuildSettings()` | Read build scenes and target. | Read-only |
| `buildPlayer(locationPathName, confirm)` | Build player. | Requires `confirm: true` |
| `getBuild(id)` | Poll build request. | Read-only |

### `Editor/validation.mjs`

Project health checks.

| Function | Purpose |
|---|---|
| `run(args)` | Run all validation checks. |
| `missingScripts(args)` | Find missing MonoBehaviour scripts. |
| `missingReferences(args)` | Find broken serialized references in loaded scenes. |
| `serializedFieldTooltips(args)` | Check `[SerializeField]` fields for nearby `[Tooltip]`. |

## Common Workflows

| Goal | Start With |
|---|---|
| Check Unity environment | `Runtime/runtime.mjs#getState()` |
| Inspect scene objects | `Runtime/objects.mjs` |
| Read live component data | `Runtime/components.mjs` |
| Edit Inspector fields | `Editor/serialized.mjs` |
| Search project assets | `Editor/assets.mjs#find()` |
| Modify importer settings | `Editor/importers.mjs` |
| Work with Prefabs | `Editor/prefabs.mjs` |
| Run tests or builds | `Editor/pipeline.mjs` |
| Call custom static C# API | `Runtime/reflection.mjs` |
| Use low-level commands | [Advanced notes](ADVANCED_USAGE.md) |
