# Helper 参考

[README](../README_zh.md) | [English](HELPER_MODULES.md) | [项目设计](PROJECT_DESIGN_zh.md) | [高级说明](ADVANCED_USAGE_zh.md)

[![Runtime](https://img.shields.io/badge/Runtime-5%20modules-2ecc71)](#runtime-helpers)
[![Editor](https://img.shields.io/badge/Editor-9%20modules-3498db)](#editor-helpers)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](../README_zh.md#设计取舍)

YuzeMcpTool 只暴露一个 MCP tool：`evalJsCode`。在这个 tool 里，Agent 从 `YuzeToolkit/mcp/...` import JavaScript helper module。

Discovery 起点：

```javascript
async function execute() {
  const index = await import('YuzeToolkit/mcp/index.mjs');
  return index.description;
}
```

## 模块索引

| 分类 | 模块 |
|---|---|
| Runtime helpers | `runtime`, `objects`, `components`, `diagnostics`, `reflection` |
| Editor helpers | `editor`, `assets`, `importers`, `scenes`, `prefabs`, `serialized`, `project`, `pipeline`, `validation` |

Runtime helper 可在 Editor 或 Runtime/Player 中运行，前提是底层 Unity API 可用。Editor helper 依赖 `UnityEditor`，在 Runtime/Player 中会明确失败。

## Runtime Helpers

### `Runtime/runtime.mjs`

环境状态、Unity 日志和 command batch。

| 函数 | 用途 |
|---|---|
| `getState()` | 环境、Unity 版本、平台、播放状态、路径、active scene、已注册 commands。 |
| `getRecentLogs(count, type)` | MCP 捕获的 Unity 日志。 |
| `clearLogs()` | 清空 MCP log buffer。 |
| `executeBatch({ commands, stopOnError? })` | 按顺序执行多个 bridge command。 |

### `Runtime/objects.mjs`

Scene GameObject、hierarchy 和 Transform 操作。

| 函数 | 用途 | 安全 |
|---|---|---|
| `find({ by, value, includeInactive, limit })` | 按 `name`、`path`、`tag` 或 `component` 查找 GameObject。 | 只读 |
| `get(target)` | 检查单个 GameObject。 | 只读 |
| `create(args)` | 创建空对象或 primitive GameObject。 | 修改场景 |
| `destroy(target, confirm)` | 销毁 GameObject。 | 需要 `confirm: true` |
| `duplicate(target, name)` | 复制 GameObject。 | 修改场景 |
| `setParent(target, parent, worldPositionStays)` | 修改 hierarchy 父对象。 | 修改场景 |
| `setTransform(args)` | 设置位置、旋转或缩放。 | 修改场景 |
| `setActive(target, active)` | 修改 active 状态。 | 修改场景 |
| `setNameLayerTag(args)` | 修改 name、layer 或 tag。 | 修改场景 |

### `Runtime/components.mjs`

Component 读取、编辑和实例方法调用。

| 函数 | 用途 | 安全 |
|---|---|---|
| `list(target)` | 列出 GameObject 上的 Component。 | 只读 |
| `get(args)` | 按 type/index 读取一个 Component。 | 只读 |
| `add(target, type)` | 添加 Component。 | 修改场景 |
| `remove(args)` | 删除 Component。 | 需要 `confirm: true` |
| `setProperty(args)` | 设置一个 public instance field/property。 | 修改 Component |
| `setProperties(args)` | 设置多个 public fields/properties。 | 修改 Component |
| `callMethod(args)` | 调用 public instance method。 | 取决于方法 |
| `listTypes(args)` | 搜索可用 Component 类型。 | 只读 |

非 public method 调用需要 `includeNonPublic: true` 和 `confirmDangerous: true`。

### `Runtime/diagnostics.mjs`

只读运行时诊断。

| 函数 | 用途 |
|---|---|
| `listCameras()` | Scene cameras 和常用设置。 |
| `getPhysicsState()` | Physics2D 设置和 Collider2D 摘要。 |
| `getGraphicsState()` | Render pipeline、quality、color space。 |
| `listCanvases()` | Canvas 对象和 render settings。 |
| `listLoadedTextures(limit)` | 已加载 Texture 对象的尺寸和类型。 |

### `Runtime/reflection.mjs`

项目自定义 API 的 C# 类型发现和 static method 调用。

| 函数 | 用途 | 安全 |
|---|---|---|
| `getNamespaces()` | 列出 public namespaces。 | 只读 |
| `getTypes(namespaceName)` | 列出某 namespace 下的 public types。 | 只读 |
| `getTypeDetails(fullName)` | 列出某 type 的 public members。 | 只读 |
| `findMethods(args)` | 搜索 public methods。 | 非 public 搜索需要 `confirmDangerous: true` |
| `callStaticMethod(args)` | 调用 public static method。 | 非 public 调用需要 `confirmDangerous: true` |

## Editor Helpers

### `Editor/editor.mjs`

Editor 状态、编译、Selection、菜单、播放模式和截图。

| 函数 | 用途 | 安全 |
|---|---|---|
| `getState()` | Editor 状态、active scene、selection 摘要。 | 只读 |
| `getCompilationState()` | 编译和资源刷新状态。 | 只读 |
| `requestScriptCompilation()` | 请求脚本编译。 | 可能触发 reload |
| `scheduleAssetRefresh()` | 请求 AssetDatabase refresh。 | 可能触发 reload |
| `getCompilerMessages(count)` | 最近类似编译器的错误/警告。 | 只读 |
| `getSelection()` / `setSelection(items)` | 读取或设置 Editor selection。 | 修改 selection |
| `executeMenuItem(path, confirm)` | 执行 Editor menu item。 | 非 Yuze 菜单需要 `confirm: true` |
| `setPlayMode(isPlaying)` / `setPause(isPaused)` | 控制播放/暂停状态。 | 改变 Editor 状态 |
| `screenshotGameView(path)` | 捕获 Game View。 | 写入截图文件 |

### `Editor/assets.mjs`

AssetDatabase 搜索、项目文本 IO、依赖、脚本、材质和刷新。

| 函数 | 用途 | 安全 |
|---|---|---|
| `find(args)` | 使用 Unity filter 搜索资产。 | 只读 |
| `getInfo(path)` | 资产元数据。 | 只读 |
| `readText(path)` / `writeText(args)` | 读写文本资源。 | 写入会修改项目 |
| `createFolder(parent, name)` | 创建 AssetDatabase 文件夹。 | 修改项目 |
| `copy(from, to)` | 复制资产。 | 修改项目 |
| `move(from, to, confirm)` | 移动或重命名资产。 | 需要 `confirm: true` |
| `deleteAsset(path, confirm)` | 删除资产。 | 需要 `confirm: true` |
| `refreshNow()` | 立即刷新 AssetDatabase。 | 可能触发 reload |
| `getDependencies(path, recursive)` | 资产依赖。 | 只读 |
| `findReferences(path, folders, limit)` | 查找资产引用。 | 只读 |
| `createScript(args)` | 创建 MonoBehaviour 脚本。 | 可能触发 reload |
| `applyScriptTextEdits(args)` | patch 脚本文件。 | 可能触发 reload |
| `createMaterial(args)` | 创建 Material 资产。 | 修改项目 |

### `Editor/importers.mjs`

AssetImporter 检查和编辑。

| 函数 | 用途 | 安全 |
|---|---|---|
| `get(path, includeProperties)` | Importer 摘要和可选 serialized properties。 | 只读 |
| `setProperty(args)` | 设置一个 importer SerializedProperty。 | 修改 importer |
| `setMany(args)` | 设置多个 importer properties。 | 修改 importer |
| `reimport(path)` | 强制重新导入。 | 修改导入结果 |

### `Editor/scenes.mjs`

Scene 文件和已打开 Scene hierarchy。

| 函数 | 用途 | 安全 |
|---|---|---|
| `listOpenScenes()` | 列出已打开 Scene。 | 只读 |
| `getSceneHierarchy(args)` | 读取已打开 Scene hierarchy。 | 只读 |
| `openScene(path, mode)` | 打开 Scene。 | 改变 Editor 状态 |
| `createScene(args)` | 创建新 Scene。 | 修改项目/session |
| `saveScene()` / `saveSceneAs(path)` | 保存 Scene。 | 写入 Scene asset |
| `setActiveScene(path)` | 设置 active scene。 | 改变 Editor 状态 |

### `Editor/prefabs.mjs`

Prefab instance、asset、stage、override 和 unpack 操作。

| 函数 | 用途 | 安全 |
|---|---|---|
| `instantiate(args)` | 实例化 Prefab asset。 | 修改场景 |
| `createFromObject(args)` | 把 scene object 保存为 Prefab。 | 修改项目 |
| `createVariant(args)` | 创建 Prefab variant。 | 修改项目 |
| `openStage(path)` / `closeStage()` / `saveStage()` | Prefab Stage 工作流。 | 改变 Editor/project 状态 |
| `getOverrides(target)` | 读取 Prefab overrides。 | 只读 |
| `applyOverrides(target, confirm)` | Apply overrides。 | 需要 `confirm: true` |
| `revertOverrides(target, confirm)` | Revert overrides。 | 需要 `confirm: true` |
| `unpack(target, mode, confirm)` | Unpack Prefab instance。 | 需要 `confirm: true` |

### `Editor/serialized.mjs`

SerializedObject 和 Inspector property 读写。

| 函数 | 用途 | 安全 |
|---|---|---|
| `get(args)` | 读取全部可见 property 或一个 `propertyPath`。 | 只读 |
| `set(args)` | 设置一个 SerializedProperty value。 | 修改对象/资产 |
| `setMany(args)` | 设置多个 SerializedProperty value。 | 修改对象/资产 |
| `resizeArray(args)` | 调整 serialized array 大小。 | 修改对象/资产 |
| `insertArrayElement(args)` | 插入数组元素。 | 修改对象/资产 |
| `deleteArrayElement(args)` | 删除数组元素。 | 修改对象/资产 |

target 可以是 `assetPath`、`guid`、`instanceId` 或 GameObject selector。

### `Editor/project.mjs`

项目级诊断。

| 函数 | 用途 |
|---|---|
| `getProjectSettings()` | Product/company/application id、tags、layers。 |
| `getProfilerState()` | Profiler 可用性和 recording flags。 |
| `getToolState()` | 当前 Editor tool、pivot mode、pivot rotation。 |

### `Editor/pipeline.mjs`

Package Manager、Test Runner 和 BuildPipeline 工作流。

| 函数 | 用途 | 安全 |
|---|---|---|
| `listPackages()` | 列出 registered packages。 | 只读 |
| `addPackage(packageId, confirm)` | 添加 package。 | 需要 `confirm: true` |
| `removePackage(packageName, confirm)` | 移除 package。 | 需要 `confirm: true` |
| `searchPackages(packageName)` | 搜索 package registry。 | 只读/request |
| `getPackageRequest(id)` | 查询 package request。 | 只读 |
| `runTests(mode)` / `getTestRun(id)` | 运行或查询测试。 | 可选 Test Framework；未安装时不支持 |
| `getBuildSettings()` | 读取 build scenes 和 target。 | 只读 |
| `buildPlayer(locationPathName, confirm)` | 构建 player。 | 需要 `confirm: true` |
| `getBuild(id)` | 查询 build request。 | 只读 |

### `Editor/validation.mjs`

项目健康检查。

| 函数 | 用途 |
|---|---|
| `run(args)` | 运行全部校验。 |
| `missingScripts(args)` | 查找缺失 MonoBehaviour scripts。 |
| `missingReferences(args)` | 查找 loaded scenes 中损坏的 serialized references。 |
| `serializedFieldTooltips(args)` | 检查 `[SerializeField]` 字段附近是否有 `[Tooltip]`。 |

## 常见工作流

| 目标 | 优先入口 |
|---|---|
| 检查 Unity 环境 | `Runtime/runtime.mjs#getState()` |
| 检查场景对象 | `Runtime/objects.mjs` |
| 读取 live component 数据 | `Runtime/components.mjs` |
| 编辑 Inspector 字段 | `Editor/serialized.mjs` |
| 搜索项目资源 | `Editor/assets.mjs#find()` |
| 修改 importer 设置 | `Editor/importers.mjs` |
| 处理 Prefab | `Editor/prefabs.mjs` |
| 运行测试或构建 | `Editor/pipeline.mjs` |
| 调用自定义 static C# API | `Runtime/reflection.mjs` |
| 使用底层 commands | [高级说明](ADVANCED_USAGE_zh.md) |
