# 高级说明

[README](../README_zh.md) | [English](ADVANCED_USAGE.md) | [Helper 参考](HELPER_MODULES_zh.md) | [项目设计](PROJECT_DESIGN_zh.md)

[![Tools](https://img.shields.io/badge/Tools-Generated-9b59b6)](#tool-invokes)
[![PuerTS](https://img.shields.io/badge/PuerTS-C%23%20Interop-00a8ff)](#puerts-c-interop)
[![Safety](https://img.shields.io/badge/Safety-confirm%20required-e67e22)](#安全确认参数)

本文面向 Agent 和维护者，说明生成的 `tools/<name>` 模块、PuerTS C# interop、安全确认参数、Domain Reload 规则和排错。

## Tool Invokes

生成的 `tools/<name>` 模块是常见流程的推荐 API。C# tool module 导出语义 JavaScript 函数，每次调用都会确认 tool 仍处于启用状态，再通过 PuerTS 调用 C# public 实例方法，并把返回值格式化成 MCP 输出。调用使用位置参数；`module.functions[]` 暴露 `description`、有序的 `parameters` 和兼容旧字段的 `parameterTypes`。需要读取实时启用状态时使用 `module.isEnabled()`。

直接 module 调用：

```javascript
async function execute() {
  const assets = await import('tools/assets');
  return assets.find('t:Prefab', 20, ['Assets']);
}
```

## Runtime Tools

| Tool | Actions |
|---|---|
| `runtime` | `getState`, `getRecentLogs`, `clearLogs` |
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

Editor tool 需要 Unity Editor。Runtime/Player 中调用会返回 Editor-only 错误。

## Sequential Commands

短命令序列直接写在 JavaScript 里顺序调用，不再通过 bridge-level batch 包一层。

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

不要把脚本编译、AssetDatabase refresh、package add/remove、build、需要后续轮询的 test 放进同一个 batch。

## PuerTS C# Interop

直接 JavaScript 运行在 PuerTS 中，不是 Node.js。

直接 PuerTS interop 也是一等能力。一次性调试或不值得沉淀成 helper 的项目 API，可以直接通过 `CS.*` 调用 Unity/C#。

Runtime API 示例：

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

规则：

- Runtime Unity API 使用 `CS.UnityEngine.*`。
- `CS.UnityEditor.*` 只能在 Unity Editor 中使用。
- Unity 需要 `System.Type` 时，用 `puer.$typeof(...)` 或 `puerts.$typeof(...)`。
- 不要假设 Node.js 模块可用，例如 `fs`。
- 返回基础类型、List、Dictionary 组成的数据，这是服务端最稳定的 JSON 输出路径。
- 可以返回 `UnityEngine.Object` 或 C# 自定义对象，但服务端会把它们转成摘要，不能依赖完整对象图。

## 返回值规则

`evalJsCode` 的 JavaScript 返回值不会在 JS runner 里提前转成字符串。结果回到 C# 后，MCP 服务端统一处理：

| 返回类型 | 服务端处理 |
|---|---|
| `string`、number、bool、null | 作为 JSON 值写入 MCP text content。 |
| `List<T>`、array、`IEnumerable<T>` | 递归转换元素，然后作为 JSON array 返回。 |
| `Dictionary<string, TValue>` 或字典形数据 | 递归转换 value，然后作为 JSON object 返回。 |
| `UnityEngine.Object` | 返回 `name`、`type`、`instanceId` 等摘要；资源补 asset path/guid；GameObject/Component 补层级和组件关系。 |
| C# 自定义类型 | 返回 `type`、`string`、public instance members；member 会递归转换到深度限制内。 |

最推荐的工具返回值是基础类型、List 和 Dictionary 组成的显式 DTO。它们一定能被服务端正确 JSON 化，也最容易让 MCP 客户端和 AI 稳定读取。

## 项目自定义 C# API

项目 static method 可用 `tools/reflection`：

```javascript
async function execute() {
  const reflection = await import('tools/reflection');
  return reflection.callStaticMethod("MyGame.EditorTools.AssetReport", "CreateReport", ["Assets"]);
}
```

非 public 调用需要：

```javascript
includeNonPublic: true,
confirmDangerous: true
```

## Domain Reload 规则

Unity 在编译脚本、刷新资源或切换 play mode 时会短暂不可用。

| 规则 | 原因 |
|---|---|
| 不要在同一次 `evalJsCode` 中触发编译后等待完成。 | Domain Reload 可能杀掉 session。 |
| 文件编辑、刷新请求、编译检查拆成多次调用。 | Unity 需要 editor tick 推进状态。 |
| session 丢失后重新 initialize MCP 客户端。 | Domain Reload 或 server 重启会让 session id 失效。 |
| busy error 等 Unity idle 后重试。 | server 会避免在不安全状态执行 eval。 |

推荐脚本编辑流程：

1. 编辑或创建文件。
2. 从 `evalJsCode` 返回。
3. 调用 `scheduleAssetRefresh()` 或 `requestScriptCompilation()`。
4. 必要时重连。
5. 查询 `getCompilationState()` 和 `getCompilerMessages()`。

## 安全确认参数

| 参数 | 用途 |
|---|---|
| `confirm: true` | 破坏性或项目级变更。 |
| `confirmDangerous: true` | 非 public reflection 或危险 member access。 |

常见需要 `confirm: true` 的操作：

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
- 非 `YuzeToolkit/MCP` 菜单项

## 路径规则

执行项目文件 IO 的 tool call 会把路径解析到 Unity 项目根目录内。

| 正确 | 错误 |
|---|---|
| `Assets/Settings/GameSettings.asset` | `../outside-project/file.txt` |
| `Packages/com.yuzetoolkit.mcptool/README.md` | `C:/Users/Name/Desktop/file.txt` |
| `Temp/YuzeMcpTool-GameView.png` | 网络路径或无关绝对路径 |

## 故障排查

| 错误 | 处理 |
|---|---|
| `Session not found` | 重新 initialize MCP 客户端。 |
| `Unity Editor is compiling scripts` | 等待编译结束后重试。 |
| `Unity Editor is updating assets` | 等待 AssetDatabase refresh。 |
| `Unity Editor is changing play mode` | 等待 play mode 切换结束。 |
| `Command '...' is Editor-only` | 使用 Unity Editor，或改用 Runtime helper。 |
| `Unknown or disabled MCP tool` | 调用 `runtime.getState()`，检查 `registeredTools`；如果工具存在，再检查 Server Window 里的工具开关。 |
| `Unknown action` | 检查相关 helper description 或 command 表。 |
| Unity 卡死 | 避免 `execute()` 中同步死循环；timeout 不能可靠中断阻塞主线程。 |
