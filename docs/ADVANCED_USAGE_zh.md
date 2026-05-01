# 高级说明

[README](../README_zh.md) | [English](ADVANCED_USAGE.md) | [Helper 参考](HELPER_MODULES_zh.md) | [项目设计](PROJECT_DESIGN_zh.md)

[![Bridge](https://img.shields.io/badge/Bridge-Commands-9b59b6)](#bridge-commands)
[![PuerTS](https://img.shields.io/badge/PuerTS-C%23%20Interop-00a8ff)](#puerts-c-interop)
[![Safety](https://img.shields.io/badge/Safety-confirm%20required-e67e22)](#安全确认参数)

本文面向 Agent 和维护者，说明底层 bridge command、直接 PuerTS C# interop、安全确认参数、Domain Reload 规则和排错。

## Bridge Commands

helper module 是推荐 API。只有 helper 没覆盖需求，或需要调试 helper 层时，才直接调用 bridge command。

响应格式：

```json
{ "success": true, "result": {} }
```

或：

```json
{ "success": false, "error": "message" }
```

直接调用：

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
| `runtime.getState` | 不需要 action 参数。 |
| `log.execute` | `getRecent`, `clear` |
| `object.execute` | `find`, `get`, `create`, `destroy`, `duplicate`, `setParent`, `setTransform`, `setActive`, `setNameLayerTag` |
| `component.execute` | `list`, `get`, `add`, `remove`, `setProperty`, `setProperties`, `callMethod`, `listTypes` |
| `runtime.diagnostics` | `cameraList`, `physicsState`, `graphicsState`, `uiList`, `textureList` |
| `reflection.execute` | `getNamespaces`, `getTypes`, `getTypeDetails`, `findMethods`, `callStaticMethod` |
| `batch.execute` | 参数为 `{ commands, stopOnError? }`。 |

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

Editor command 需要 Unity Editor。Runtime/Player 中调用会返回 Editor-only 错误。

## Batch Commands

短 command 序列可以用 `Runtime/runtime.mjs#executeBatch()` 或直接调用 `batch.execute`。

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

不要把脚本编译、AssetDatabase refresh、package add/remove、build、需要后续轮询的 test 放进同一个 batch。

## PuerTS C# Interop

直接 JavaScript 运行在 PuerTS 中，不是 Node.js。

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
- 返回 JSON-friendly 摘要，不要返回原始 `UnityEngine.Object` 大对象图。

## 项目自定义 C# API

项目 static method 可用 `Runtime/reflection.mjs`：

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

bridge command 做项目文件 IO 时，路径会解析到 Unity 项目根目录内。

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
| `Unknown command` | 调用 `runtime.getState()`，检查 `registeredCommands`。 |
| `Unknown action` | 检查相关 helper description 或 command 表。 |
| Unity 卡死 | 避免 `execute()` 中同步死循环；timeout 不能可靠中断阻塞主线程。 |
