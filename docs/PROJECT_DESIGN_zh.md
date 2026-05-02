# 项目设计

[README](../README_zh.md) | [English](PROJECT_DESIGN.md) | [Helper 参考](HELPER_MODULES_zh.md) | [高级说明](ADVANCED_USAGE_zh.md)

[![Audience](https://img.shields.io/badge/Audience-AI%20agents%20%2F%20maintainers-8e44ad)](#谁需要读)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](#工具表面)
[![Runtime](https://img.shields.io/badge/Runtime-Editor%20%2B%20Player-2ecc71)](#模块地图)

本文面向 AI Agent 和维护者。大多数人类用户只需要读 README。

## 谁需要读

修改 server 启动、MCP 协议处理、session、C# tool、PuerTS eval、helper module 或安全规则前，先读这页。

如果只是安装和使用，读 [README](../README_zh.md) 即可。

## 设计摘要

YuzeMcpTool 在 Unity 内运行一个本地 MCP server。server 只暴露一个 MCP tool：`evalJsCode`。每个 MCP client session 拥有一个持久化 PuerTS JavaScript VM。Agent 提交 `async function execute() { ... }`，常见流程从 `tools/...` 导入 helper module，必要时也可以通过 PuerTS `CS.*` 直接调用 Unity/C# API，然后拿到 JSON-friendly 的返回结果。

这个包的核心设计是“小而稳定的 MCP 表面 + 可脚本化的 Unity 内部运行环境”：

| 目标 | 设计选择 |
|---|---|
| 简化 MCP 客户端集成 | 只暴露 `evalJsCode`。 |
| 让 AI 处理项目专用流程 | 通过 PuerTS 在 Unity 内运行自定义 JavaScript 和直接 Unity/C# interop。 |
| 让常见 Unity 任务可发现 | 通过实现 `IMcpTool` 或带 `[McpTool]` 的已注册 C# class，以及可选 JavaScript Resources 工具生成 `tools/<name>` module。 |
| 明确危险操作 | 文档中指定 `confirm: true` 或 `confirmDangerous: true`。 |
| 支持 Editor 和 Runtime/Player | Runtime command 避免 `UnityEditor`；Editor command 只在 Editor 注册。 |

## 模块地图

| 区域 | 主要文件 | 职责 |
|---|---|---|
| Server | `Runtime/Server/McpServer.cs`、`McpServerOptions.cs`、`McpSession*.cs` | `/mcp`、`/health`、JSON-RPC、session 生命周期、活跃 session 快照。 |
| Transport | `Runtime/Server/IMcpTransport.cs`、`TcpMcpTransport.cs`、`WebGlWebSocketMcpTransport.cs` | 平台相关监听实现，统一成 MCP server 可处理的 request/response。 |
| Eval | `Runtime/Eval/EvalJsCodeTool.cs`、`PuerTsEvalSession.cs`、`McpScriptLoader.cs`、`McpToolCatalog.cs` | MCP tool 定义、Unity busy 状态保护、每 session 的 PuerTS VM、虚拟 module 加载和动态工具目录。 |
| Bridge | `Runtime/Bridge/*.cs` | `McpToolAttribute`、代码生成用 `IMcpTool` 契约、tool registry、descriptor 和值格式化。 |
| Runtime tools | `Tools/Runtime/*.cs` | Runtime-safe 的 Unity 状态、日志、GameObject、Component、诊断、reflection；这个程序集自行注册自己的工具。 |
| PuerTS bindings | `Editor/PuertsCfg.cs`、`Tools/Editor/PuertsToolsCfg.cs` | core 和 tool 类型的 Editor-only PuerTS binding / typing 配置。 |
| Editor tools | `Tools/Editor/*.cs` | Editor-only 的资源、场景、Prefab、Importer、SerializedObject、测试、构建、校验；这个程序集自行注册自己的工具。 |
| Editor startup | `Editor/McpEditorBootstrap.cs`、`Editor/McpServerWindow.cs` | 启动偏好、菜单项、server 监控窗口和工具开关 UI。 |
| Tool modules | `tools/index`、`tools/<name>`、`Resources/tools/<name>` | 面向 Agent 的 helper API；内置能力从 C# tool 生成，扩展 JavaScript module 从 Resources 加载。 |
| Docs | `README*.md`、`docs/*.md` | 人类快速开始、MCP 客户端配置、helper 目录、设计说明、高级规则。 |

## 请求流程

1. Unity 加载 package。
2. Runtime 和 Editor tool 程序集通过 `McpToolRegistry.Register<TTool>()` / `TryRegister<TTool>()` 自行注册 C# tool。主 Runtime 程序集不引用任何内置 tool 类型；主 Editor 程序集只应用持久化工具开关，并在用户启用启动偏好时启动 server。
3. `McpServer` 根据平台选择 transport：非 WebGL 构建统一使用 `TcpListener` transport，WebGL 使用 WebSocket 占位 transport 并返回明确的不支持错误。
4. MCP client 向 `http://127.0.0.1:3100/mcp` 发送 `initialize`。如果 Host 配置为 `0.0.0.0`，客户端需要使用运行 Unity 设备的实际局域网 IP。
5. `McpServer` 创建 session，并通过 `Mcp-Session-Id` 响应头返回 session id。
6. Client 调用 `tools/list`；server 只返回 `evalJsCode`。
7. Client 调用 `tools/call`，工具名为 `evalJsCode`。
8. `EvalJsCodeTool` 检查 Unity busy 状态，创建或复用当前 session 的 PuerTS VM，并执行提交的 `execute()`。
9. JavaScript 导入生成的或资源加载的 tool module。
10. 生成的 C# module 导出语义函数和 `isEnabled()`。语义函数每次调用都会确认 tool 仍处于启用状态，再通过 PuerTS 调用 C# public 实例方法，并把返回值格式化成 JSON-friendly 数据。helper 未覆盖时，JavaScript 可以在同一个 VM 中直接使用 PuerTS `CS.*` interop。
11. Eval 结果被序列化为 MCP text/image content，再返回给 client。

## 工具表面

公开 MCP 表面刻意很小：

```text
evalJsCode
```

实际工作表面是 helper module 层：

```text
tools/index
tools/<name>
```

Agent 应该先读 `tools/index`，再读目标 module 的 `description`，常见流程优先调用 helper 函数。索引会整合已注册 C# tool 和 Unity `Resources/tools` JavaScript helper，所以本包、项目和其他包的工具会一起被发现。生成的 C# module 使用位置参数；通过 `functions[].description`、有序的 `functions[].parameters` 和兼容旧字段的 `functions[].parameterTypes` 暴露函数说明与调用顺序上的参数信息；通过 `isEnabled()` 暴露实时启用状态；每次调用都会校验 tool 启用状态，通过 PuerTS 调用 C# public 实例方法。一次性或项目专用逻辑也可以直接使用 `CS.*` interop，反复使用的逻辑应该沉淀成 helper。JS 返回后，`EvalJsCodeTool` 在服务端出口统一把结果格式化成 MCP content。

## 返回值处理

C# tool 的 public 方法应该使用明确返回类型，不要用 `object` 隐藏实际语义。最推荐返回基础类型、`List<T>`、`IEnumerable<T>`、`Dictionary<string, TValue>` 或由这些类型组成的数据；这类结果会被 `McpValueFormatter` 转成 JSON，并作为 MCP text content 返回，最容易被客户端和 AI 稳定解析。

`UnityEngine.Object` 允许作为返回值，但不推荐直接作为主要 DTO。服务端会把它摘要成小对象：普通 Unity object 包含 `name`、`type`、`instanceId`；资源对象会补充 asset path 和 guid；`GameObject` 会补充层级路径、scene、transform 和 components；`Component` 会补充 component 摘要以及所属 GameObject 摘要。

C# 自定义类型会被当作降级路径处理：服务端返回 `type`、`string` 和 public instance members。字段和属性会递归转换到深度限制内；循环引用会被标记，读取失败的 member 会标记为不可读。需要稳定协议时，应主动返回显式 DTO 字典或列表，而不是依赖自定义对象反射。

`LitJson` 只负责 JSON parse/stringify。字典/列表构造和参数读取集中在 `McpData`，最终 MCP 输出格式集中在 `McpValueFormatter`。

## 扩展点

| 需求 | 推荐扩展方式 |
|---|---|
| 用 JavaScript 组合现有工具 | `.mjs` 或 `.js` runtime-safe helper 放在 `Resources/tools`，Editor-only helper 放在 `Editor/Resources/tools`；导出 `description` 供索引发现。 |
| 从 C# 暴露新的 Unity 操作 | 在 `Tools/Runtime` 或 `Tools/Editor` 增加 class，公开实例方法，并用 `[McpFunction("...")]` 标注导出方法说明。普通 class 必须标 `[McpTool("name", "description")]`；生成类可以改为实现 `IMcpTool`，此时 `Name`、`Description`、`Functions` 提供全部注册元数据，`Functions` 必须非空。 |
| 注册 Runtime-safe C# tool | 从 Runtime tool 程序集 bootstrap 调用 `McpToolRegistry.TryRegister<TTool>()`；`TTool` 必须满足 `class, new()`。 |
| 注册 Editor-only C# tool | 从 Editor tool 程序集 bootstrap 调用 `McpToolRegistry.TryRegister<TTool>()`；Editor 可用性由 Editor-only 程序集决定，不再由 tool 属性决定。 |
| 访问项目自定义 static API | 优先用 `tools/reflection`；reflection 不够时再加 command。 |
| 给新 helper 或 tool 补文档 | 更新 `HELPER_MODULES_zh.md`；涉及安全或生命周期规则时更新 `ADVANCED_USAGE_zh.md`。 |

扩展名要清楚且稳定。如果操作会删除、移动、覆盖、构建、安装 package、调用非 public 代码，或大范围改变项目状态，必须要求显式确认参数。

## 安全与生命周期规则

| 规则 | 原因 |
|---|---|
| 不要在同一次 eval 里触发编译或 AssetDatabase refresh 后等待完成。 | Domain Reload 可能销毁 VM/session。 |
| eval 执行是全局串行的。 | Unity API 和 PuerTS tick 对主线程敏感；多个 session 拥有独立 VM，但 eval 调用会逐个处理。 |
| busy-state error 后等待 Unity idle 再重试。 | Unity 编译、更新资源、切换 play mode 时，server 会拒绝 eval。 |
| 优先返回基础类型、List、Dictionary 组成的数据。 | 这是服务端可以确定 JSON 化的稳定协议。 |
| 文件 IO 保持在 Unity 项目根目录内。 | Bridge command 只用于项目内自动化。 |
| 破坏性操作必须使用 `confirm: true`。 | 降低误删、误移动、误构建、误改 package 的风险。 |
| 非 public reflection 或危险访问必须使用 `confirmDangerous: true`。 | 区分普通检查和高风险操作。 |

## 读者地图

| 读者 | 从哪里开始 | 覆盖内容 |
|---|---|---|
| 人类用户 | `README.md` / `README_zh.md` | 工具做什么、怎么安装、endpoint、客户端配置。 |
| 使用工具的 AI Agent | `HELPER_MODULES.md` / `HELPER_MODULES_zh.md` | helper module、常见工作流、安全选择。 |
| 修改内部实现的 AI Agent | 本文和 `ADVANCED_USAGE.md` | 请求流程、模块边界、bridge 调用、PuerTS interop、生命周期风险。 |

## 已知取舍

- 单 tool 模型灵活，但不如暴露大量命名工具的插件直观。
- Agent 必须能写出有效 JavaScript；JS 能力弱时即使 Unity 侧 API 正确也会失败。
- JavaScript 运行在 PuerTS 中，不是 Node.js —— `fs`、`path` 等 Node 模块不可用。
- Runtime/Player 的工具启用状态当前是内存级，除非宿主项目在启动时应用自己的配置；Editor 工具开关通过 EditorPrefs 持久化。
- runtime-safe command 在 Runtime/Player 可用，Editor helper module 必须在 Unity Editor 中使用。
- WebGL 不能监听入站 TCP/HTTP 连接；WebSocket transport 当前只负责可编译和明确失败，不提供真实 relay 连接。
- `0.0.0.0` 会暴露到局域网，当前 server 没有鉴权；只应在受控网络中使用。
- 如果 Unity 主线程被阻塞，eval timeout 不一定能安全中断。
