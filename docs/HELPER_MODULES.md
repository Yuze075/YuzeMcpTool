# Helper Reference

[README](../README.md) | [中文](HELPER_MODULES_zh.md) | [Project design](PROJECT_DESIGN.md) | [Advanced notes](ADVANCED_USAGE.md)

[![Runtime](https://img.shields.io/badge/Runtime-6%20modules-2ecc71)](#runtime-helpers)
[![Editor](https://img.shields.io/badge/Editor-9%20modules-3498db)](#editor-helpers)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](../README.md#design-choice)

YuzeMcpTool exposes one MCP tool, `evalJsCode`. Inside that tool, agents import helper modules from `tools/...`. Built-in modules are generated from registered C# classes marked with `[McpTool(name, description)]`; each C# module exports semantic functions that validate the tool is enabled, call public C# instance methods through PuerTS, and leave final result formatting to the MCP server. Project and package extensions can also provide JavaScript modules under `Resources/tools`.

Generated C# helpers should prefer primitives, `List<T>`, `Dictionary<string, TValue>`, or data composed from those types. The server returns those values as JSON text content, which is the most stable and recommended tool result shape.

Use helper modules first for common workflows because they expose compact descriptions and stable return data. When a helper does not cover the task, run Unity/C# APIs directly with PuerTS `CS.*` interop inside `evalJsCode`; promote repeated project-specific code into a C# tool or JavaScript helper.

Start discovery with:

```javascript
async function execute() {
  const index = await import('tools/index');
  return index.description;
}
```

## Module Index

| Category | Modules |
|---|---|
| Runtime helpers | `runtime`, `objects`, `components`, `diagnostics`, `reflection`, `inspect` |
| Editor helpers | `editor`, `assets`, `importers`, `scenes`, `prefabs`, `serialized`, `project`, `pipeline`, `validation` |

Runtime helpers can run in Editor or Runtime/Player when the underlying Unity API is available. Editor helpers require `UnityEditor` and fail clearly in Runtime/Player.

Generated helper functions use positional arguments, such as `assets.find('t:Prefab', 20, ['Assets'])`. Each generated C# module exposes `functions[].description`, ordered `functions[].parameters`, legacy `functions[].parameterTypes`, and `isEnabled()` for the current enabled state.

## Runtime Helpers

### `tools/runtime`

Environment state and Unity logs.

| Function | Purpose |
|---|---|
| `getState()` | Environment, Unity version, platform, play state, paths, active scene, registered tools. |
| `getRecentLogs(count?, type?)` | MCP-captured Unity logs. |
| `clearLogs()` | Clear the MCP log buffer. |

### `tools/objects`

Scene GameObject, hierarchy, and Transform operations.

| Function | Purpose | Safety |
|---|---|---|
| `find(...)` | Find GameObjects by `name`, `path`, `tag`, or `component`. | Read-only |
| `get(target)` | Inspect one GameObject. | Read-only |
| `create(name?, primitive?, parent?, localPosition?, position?, localScale?)` | Create an empty or primitive GameObject. | Mutates scene |
| `destroy(target, confirm?)` | Destroy a GameObject. | Requires `confirm: true` |
| `duplicate(target, name?)` | Duplicate a GameObject. | Mutates scene |
| `setParent(target, parent?, worldPositionStays?)` | Change hierarchy parent. | Mutates scene |
| `setTransform(target, position?, localPosition?, rotationEuler?, localRotationEuler?, localScale?)` | Set position, rotation, or scale. | Mutates scene |
| `setActive(target, active)` | Change active state. | Mutates scene |
| `setNameLayerTag(target, name?, layer?, tag?)` | Change name, layer, or tag. | Mutates scene |

### `tools/components`

Component reads, edits, and instance method calls.

| Function | Purpose | Safety |
|---|---|---|
| `list(target)` | List components on a GameObject. | Read-only |
| `get(target, type?, index?)` | Read one component by type/index. | Read-only |
| `add(target, type)` | Add a Component. | Mutates scene |
| `remove(target, type?, index?, confirm?)` | Remove a Component. | Requires `confirm: true` |
| `setProperty(target, type, member, value, index?, includeNonPublic?, includeStatic?, confirmDangerous?)` | Set one field/property. | Mutates component |
| `setProperties(target, type, values, index?, includeNonPublic?, includeStatic?, confirmDangerous?)` | Set multiple fields/properties. | Mutates component |
| `callMethod(target, type, method, args?, index?, includeNonPublic?, confirmDangerous?)` | Call an instance method. | Method-dependent |
| `listTypes(query?, limit?)` | Search available Component types. | Read-only |

Non-public method calls require `includeNonPublic: true` and `confirmDangerous: true`.

### `tools/diagnostics`

Read-only runtime diagnostics.

| Function | Purpose |
|---|---|
| `listCameras()` | Scene cameras and common settings. |
| `getPhysicsState()` | Physics2D settings and Collider2D summaries. |
| `getGraphicsState()` | Render pipeline, quality, color space. |
| `listCanvases()` | Canvas objects and render settings. |
| `listLoadedTextures(limit?)` | Loaded texture objects with size and type. |

### `tools/inspect`

Formatting helpers for C#/Unity object references.

| Function | Purpose |
|---|---|
| `describe(value?, depth?)` | Return a default summary DTO. |
| `format(value?, mode?, depth?)` | Format a value with mode `default`, `summary`, `name`, `path`, `text`, `json`, or `yaml`. |
| `toName(value?)` | Return a Unity/C# object's name. |
| `toPath(value?)` | Return a scene hierarchy path or asset path. |
| `toJson(value?, mode?, depth?)` | Return a JSON string for a formatted value. |
| `toYaml(value?, depth?)` | Return a YAML string for a formatted value. |

### `tools/reflection`

C# type discovery and static method calls for project-specific APIs.

| Function | Purpose | Safety |
|---|---|---|
| `getNamespaces()` | List public namespaces. | Read-only |
| `getTypes(namespaceName)` | List public types in a namespace. | Read-only |
| `getTypeDetails(fullName)` | List public members for a type. | Read-only |
| `findMethods(query?, type?, includeNonPublic?, confirmDangerous?, limit?)` | Search public methods. | Non-public search requires `confirmDangerous: true` |
| `callStaticMethod(type, method, args?, includeNonPublic?, confirmDangerous?)` | Call a static method. | Non-public call requires `confirmDangerous: true` |

## Editor Helpers

### `tools/editor`

Editor state, compilation, selection, menu commands, play mode, and screenshots.

| Function | Purpose | Safety |
|---|---|---|
| `getState()` | Editor state, active scene, selection summary. | Read-only |
| `getCompilationState()` | Compilation and asset refresh state. | Read-only |
| `requestScriptCompilation()` | Request script compilation. | May trigger reload |
| `scheduleAssetRefresh()` | Request AssetDatabase refresh. | May trigger reload |
| `getCompilerMessages(count?)` | Recent compiler-like errors/warnings. | Read-only |
| `getSelection()` / `setSelection(items)` | Read or set Editor selection. | Selection mutation |
| `executeMenuItem(path, confirm?)` | Execute an Editor menu item. | Non-Yuze menu requires `confirm: true` |
| `setPlayMode(isPlaying)` / `setPause(isPaused)` | Control play/pause state. | Changes Editor state |
| `screenshotGameView(path?)` | Capture Game View. | Writes screenshot file |

### `tools/assets`

AssetDatabase search, project text IO, dependencies, scripts, materials, and refresh.

| Function | Purpose | Safety |
|---|---|---|
| `find(filter, limit?, folders?)` | Search assets with Unity filters. | Read-only |
| `getInfo(path)` | Asset metadata. | Read-only |
| `readText(path)` / `writeText(path, text, refresh?, confirmOverwrite?)` | Read or write text assets. | Write mutates project |
| `createFolder(parent?, name)` | Create an AssetDatabase folder. | Mutates project |
| `copy(from, to, confirmOverwrite?)` | Copy an asset. | Mutates project |
| `move(from, to, confirm?)` | Move or rename an asset. | Requires `confirm: true` |
| `deleteAsset(path, confirm?)` | Delete an asset. | Requires `confirm: true` |
| `refreshNow()` | Refresh AssetDatabase now. | May trigger reload |
| `getDependencies(path, recursive?)` | Asset dependencies. | Read-only |
| `findReferences(path, folders?, limit?)` | Search asset references. | Read-only |
| `createScript(path, className?, namespaceName?, confirmOverwrite?)` | Create a MonoBehaviour script. | May trigger reload |
| `applyScriptTextEdits(path, edits, refresh?, confirm?)` | Patch a script file. | May trigger reload |
| `createMaterial(path, shaderName?, properties?, confirmOverwrite?)` | Create a Material asset. | Mutates project |

### `tools/importers`

AssetImporter inspection and edits.

| Function | Purpose | Safety |
|---|---|---|
| `get(path, includeProperties?)` | Importer summary and optional serialized properties. | Read-only |
| `setProperty(path, propertyPath, value, saveAndReimport?, confirm?)` | Set one importer SerializedProperty. | Mutates importer |
| `setMany(path, changes, saveAndReimport?, confirm?)` | Set many importer properties. | Mutates importer |
| `reimport(path)` | Force reimport. | Mutates imported asset |

### `tools/scenes`

Scene files and open scene hierarchy.

| Function | Purpose | Safety |
|---|---|---|
| `listOpenScenes()` | List open scenes. | Read-only |
| `getSceneHierarchy(depth?, includeComponents?, limit?)` | Read open scene hierarchy. | Read-only |
| `openScene(path, mode?, confirm?, saveDirtyScenes?)` | Open a scene. | Changes Editor state |
| `createScene(path?, setup?, mode?, confirm?, saveDirtyScenes?)` | Create a new scene. | Mutates project/session |
| `saveScene()` / `saveSceneAs(path)` | Save scene. | Writes scene asset |
| `setActiveScene(path)` | Set active scene. | Changes Editor state |

### `tools/prefabs`

Prefab instance, asset, stage, override, and unpack operations.

| Function | Purpose | Safety |
|---|---|---|
| `instantiate(path, parent?, position?)` | Instantiate a Prefab asset. | Mutates scene |
| `createFromObject(target, path, confirmOverwrite?)` | Save a scene object as Prefab. | Mutates project |
| `createVariant(basePath, path, confirmOverwrite?)` | Create a Prefab variant. | Mutates project |
| `openStage(path)` / `closeStage()` / `saveStage()` | Prefab Stage workflow. | Changes Editor/project state |
| `getOverrides(target)` | Read Prefab overrides. | Read-only |
| `applyOverrides(target, confirm?)` | Apply overrides. | Requires `confirm: true` |
| `revertOverrides(target, confirm?)` | Revert overrides. | Requires `confirm: true` |
| `unpack(target, mode?, confirm?)` | Unpack a Prefab instance. | Requires `confirm: true` |

### `tools/serialized`

SerializedObject and Inspector property reads/writes.

| Function | Purpose | Safety |
|---|---|---|
| `get(target, propertyPath?)` | Read all visible properties or one `propertyPath`. | Read-only |
| `set(target, propertyPath, value, confirm?)` | Set one SerializedProperty value. | Mutates object/asset |
| `setMany(target, changes, confirm?)` | Set multiple SerializedProperty values. | Mutates object/asset |
| `resizeArray(target, propertyPath, size, confirm?)` | Resize serialized array. | Mutates object/asset |
| `insertArrayElement(target, propertyPath, index?, confirm?)` | Insert array element. | Mutates object/asset |
| `deleteArrayElement(target, propertyPath, index, confirm?)` | Delete array element. | Mutates object/asset |

Targets can be `assetPath`, `guid`, `instanceId`, or a GameObject selector.

### `tools/project`

Project-level diagnostics.

| Function | Purpose |
|---|---|
| `getProjectSettings()` | Product/company/application id, tags, layers. |
| `getProfilerState()` | Profiler availability and recording flags. |
| `getToolState()` | Active Editor tool, pivot mode, pivot rotation. |

### `tools/pipeline`

Package Manager, Test Runner, and BuildPipeline workflows.

| Function | Purpose | Safety |
|---|---|---|
| `listPackages()` | List registered packages. | Read-only |
| `addPackage(packageId, confirm?)` | Add a package. | Requires `confirm: true` |
| `removePackage(packageName, confirm?)` | Remove a package. | Requires `confirm: true` |
| `searchPackages(packageName?)` | Search package registry. | Read-only/request |
| `getPackageRequest(id)` | Poll package request. | Read-only |
| `runTests(mode?, testName?)` / `getTestRun(id)` | Run or poll tests. | Optional Test Framework; unsupported without it |
| `getBuildSettings()` | Read build scenes and target. | Read-only |
| `buildPlayer(locationPathName, confirm?)` | Build player. | Requires `confirm: true` |
| `getBuild(id)` | Poll build request. | Read-only |

### `tools/validation`

Project health checks.

| Function | Purpose |
|---|---|
| `run(folders?, limit?)` | Run all validation checks. |
| `missingScripts()` | Find missing MonoBehaviour scripts. |
| `missingReferences(limit?)` | Find broken serialized references in loaded scenes. |
| `serializedFieldTooltips(folders?, limit?)` | Check serialized fields for `[Tooltip]`, including public fields, `[SerializeField]`, and `[SerializeReference]`. |

## Common Workflows

| Goal | Start With |
|---|---|
| Check Unity environment | `tools/runtime#getState()` |
| Inspect scene objects | `tools/objects` |
| Read live component data | `tools/components` |
| Edit Inspector fields | `tools/serialized` |
| Search project assets | `tools/assets#find()` |
| Modify importer settings | `tools/importers` |
| Work with Prefabs | `tools/prefabs` |
| Run tests or builds | `tools/pipeline` |
| Call custom static C# API | `tools/reflection` |
| Run one-off Unity/C# code | PuerTS `CS.*` interop in `evalJsCode` |
| Use low-level commands | [Advanced notes](ADVANCED_USAGE.md) |
