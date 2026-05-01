# 项目设计

[README](../README_zh.md) | [English](PROJECT_DESIGN.md) | [客户端设置](CLIENT_SETUP_zh.md) | [Helper 参考](HELPER_MODULES_zh.md) | [高级说明](ADVANCED_USAGE_zh.md)

[![Audience](https://img.shields.io/badge/Audience-AI%20agents%20%2F%20maintainers-8e44ad)](#谁需要读)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](#工具表面)
[![Runtime](https://img.shields.io/badge/Runtime-Editor%20%2B%20Player-2ecc71)](#模块地图)

本文面向 AI Agent 和维护者。大多数人类用户只需要读 README 和客户端设置。

## 谁需要读

修改 server 启动、MCP 协议处理、session、bridge command、PuerTS eval、helper module 或安全规则前，先读这页。

如果只是安装和使用，读 [README](../README_zh.md) 和 [客户端设置](CLIENT_SETUP_zh.md) 即可。

## 设计摘要

YuzeMcpTool 在 Unity 内运行一个本地 MCP server。server 只暴露一个 MCP tool：`evalJsCode`。每个 MCP client session 拥有一个持久化 PuerTS JavaScript VM。Agent 提交 `async function execute() { ... }`，从 `YuzeToolkit/mcp/...` 导入 helper module，然后拿到 JSON-friendly 的返回结果。

这个包的核心设计是“小而稳定的 MCP 表面 + 可脚本化的 Unity 内部运行环境”：

| 目标 | 设计选择 |
|---|---|
| 简化 MCP 客户端集成 | 只暴露 `evalJsCode`。 |
| 让 AI 处理项目专用流程 | 通过 PuerTS 在 Unity 内运行自定义 JavaScript。 |
| 让常见 Unity 任务可发现 | 在 `YuzeToolkit/mcp/Runtime` 和 `YuzeToolkit/mcp/Editor` 下提供 helper module。 |
| 明确危险操作 | 文档中指定 `confirm: true` 或 `confirmDangerous: true`。 |
| 支持 Editor 和 Runtime/Player | Runtime command 避免 `UnityEditor`；Editor command 只在 Editor 注册。 |

## 模块地图

| 区域 | 主要文件 | 职责 |
|---|---|---|
| Server | `Runtime/Server/McpServer.cs`、`McpServerOptions.cs`、`McpSession*.cs` | HTTP listener、`/mcp`、`/health`、JSON-RPC、session 生命周期、活跃 session 快照。 |
| Eval | `Runtime/Eval/EvalJsCodeTool.cs`、`PuerTsEvalSession.cs`、`McpScriptLoader.cs` | MCP tool 定义、Unity busy 状态保护、每 session 的 PuerTS VM、从 package Resources 加载 module。 |
| Bridge | `Runtime/Bridge/*.cs` | C# command 接口、command registry、command context、JavaScript 同步/异步调用入口。 |
| Runtime commands | `Runtime/Commands/*.cs` | Runtime-safe 的 Unity 状态、日志、GameObject、Component、诊断、reflection、batch。 |
| Editor commands | `Editor/Commands/*.cs` | Editor-only 的资源、场景、Prefab、Importer、SerializedObject、测试、构建、校验。 |
| Editor startup | `Editor/McpEditorBootstrap.cs`、`Editor/McpServerWindow.cs` | 自动启动、菜单项、server 监控窗口、Editor command 注册。 |
| Helper modules | `Resources/YuzeToolkit/mcp/**/*.mjs` | 面向 Agent 的 JavaScript helper API，底层封装 bridge command。 |
| Docs | `README*.md`、`docs/*.md` | 人类快速开始、客户端配置、helper 目录、设计说明、高级规则。 |

## 请求流程

1. Unity 加载 package。
2. 在 Editor 中，`McpEditorBootstrap` 注册 Runtime 和 Editor command，并在启用自动启动时启动 server。
3. MCP client 向 `http://127.0.0.1:3100/mcp` 发送 `initialize`。
4. `McpServer` 创建 session，并通过 `Mcp-Session-Id` 响应头返回 session id。
5. Client 调用 `tools/list`；server 只返回 `evalJsCode`。
6. Client 调用 `tools/call`，工具名为 `evalJsCode`。
7. `EvalJsCodeTool` 检查 Unity busy 状态，创建或复用当前 session 的 PuerTS VM，并执行提交的 `execute()`。
8. JavaScript 导入 helper module，或调用 `Mcp.invoke(...)` / `Mcp.invokeAsync(...)`。
9. Bridge command 在 Unity 侧执行并返回 JSON 文本。
10. Eval 结果被序列化为 MCP text/image content，再返回给 client。

## 工具表面

公开 MCP 表面刻意很小：

```text
evalJsCode
```

实际工作表面是 helper module 层：

```text
YuzeToolkit/mcp/index.mjs
YuzeToolkit/mcp/Runtime/*.mjs
YuzeToolkit/mcp/Editor/*.mjs
```

Agent 应该先读 `index.mjs`，再读目标 module 的 `description`，然后调用 helper 函数。允许直接 bridge 调用，但优先使用 helper module，因为 helper 会隐藏底层参数细节，并说明安全要求。

## 扩展点

| 需求 | 推荐扩展方式 |
|---|---|
| 更方便地组合现有命令 | 在 `Resources/YuzeToolkit/mcp/Runtime` 或 `Resources/YuzeToolkit/mcp/Editor` 增加 JavaScript helper。 |
| 暴露新的 Unity 操作 | 在 `Runtime/Commands` 或 `Editor/Commands` 增加 `IMcpCommand` 实现。 |
| 注册 Runtime-safe command | 在 `McpCommandRegistry.EnsureDefaultCommands()` 注册。 |
| 注册 Editor-only command | 从 `McpEditorBootstrap.RegisterEditorCommands()` 注册。 |
| 访问项目自定义 static API | 优先用 `Runtime/reflection.mjs`；reflection 不够时再加 command。 |
| 给新 helper 或 command 补文档 | 更新 `HELPER_MODULES_zh.md`；涉及安全或生命周期规则时更新 `ADVANCED_USAGE_zh.md`。 |

扩展名要清楚且稳定。如果操作会删除、移动、覆盖、构建、安装 package、调用非 public 代码，或大范围改变项目状态，必须要求显式确认参数。

## 安全与生命周期规则

| 规则 | 原因 |
|---|---|
| 不要在同一次 eval 里触发编译或 AssetDatabase refresh 后等待完成。 | Domain Reload 可能销毁 VM/session。 |
| busy-state error 后等待 Unity idle 再重试。 | Unity 编译、更新资源、切换 play mode 时，server 会拒绝 eval。 |
| 只返回可序列化的简单值。 | 原始 `UnityEngine.Object` 大对象图不能稳定序列化，也不利于 AI 阅读。 |
| 文件 IO 保持在 Unity 项目根目录内。 | Bridge command 只用于项目内自动化。 |
| 破坏性操作必须使用 `confirm: true`。 | 降低误删、误移动、误构建、误改 package 的风险。 |
| 非 public reflection 或危险访问必须使用 `confirmDangerous: true`。 | 区分普通检查和高风险操作。 |

## 文档覆盖情况

完成这轮文档整理后：

| 读者 | 从哪里开始 | 能理解什么 |
|---|---|---|
| 人类用户 | `README.md` / `README_zh.md` | 工具做什么、怎么安装、endpoint、客户端配置、风险说明。 |
| 配置客户端的人 | `CLIENT_SETUP.md` / `CLIENT_SETUP_zh.md` | server 启动、常见 MCP 客户端配置差异、验证和排错。 |
| 使用工具的 AI Agent | `HELPER_MODULES.md` / `HELPER_MODULES_zh.md` | helper module、常见工作流、安全操作选择。 |
| 修改内部实现的 AI Agent | 本文和 `ADVANCED_USAGE.md` | 请求流程、模块边界、bridge 调用、PuerTS interop、生命周期风险。 |

## 已知取舍

- 整个项目由 AI 实现，不保证正确性、稳定性、完整性、安全性或生产适用性。
- 单 tool 模型灵活，但不如暴露大量命名工具的 Unity MCP 插件直观。
- Agent 需要能写出有效 JavaScript；如果 Agent 的 JS 能力弱，即使 Unity 侧 API 正确也会失败。
- 直接 JavaScript 运行在 PuerTS 中，不是 Node.js。
- Runtime/Player 可使用 runtime-safe command，但 Editor helper module 必须在 Unity Editor 中使用。
- 如果 Unity 主线程被阻塞，eval timeout 不一定能安全中断。
