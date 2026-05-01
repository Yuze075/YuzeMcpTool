# YuzeMcpTool

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-222?logo=unity)](https://unity.com/releases/editor/archive)
[![MCP](https://img.shields.io/badge/MCP-Streamable%20HTTP-4b7bec)](https://modelcontextprotocol.io/)
[![PuerTS](https://img.shields.io/badge/PuerTS-3.0.0-00a8ff)](https://github.com/Tencent/puerts)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![AI Built](https://img.shields.io/badge/Built%20by-AI-orange)](#状态与保证)

[English](README.md) | [客户端设置](docs/CLIENT_SETUP_zh.md) | [Helper 参考](docs/HELPER_MODULES_zh.md) | [项目设计](docs/PROJECT_DESIGN_zh.md) | [高级说明](docs/ADVANCED_USAGE_zh.md)

YuzeMcpTool 是一个给 AI Agent 使用的 Unity MCP 服务。它只暴露一个 MCP tool：`evalJsCode`。Agent 可以通过 PuerTS 在 Unity 内运行 JavaScript，并使用 helper module 检查或操作 Editor、Scene、Asset、运行时对象、测试、构建和项目自定义 C# API。

![YuzeMcpTool overview](docs/Images/YuzeMcpTool-Overview.png)

## 这个工具做什么

当你希望 AI 不只是从外部改文件，而是能进入 Unity 里检查和操作项目时，可以使用这个包。

- 检查 Unity Editor 或 Runtime/Player 状态。
- 查询和编辑 GameObject、Component、Scene、Asset、Prefab、Importer 和序列化字段。
- 读取日志、运行校验、运行测试、检查构建设置。
- 在 Unity 内运行自定义 JavaScript 做项目专用调试。
- 当 helper 或 bridge command 不够用时，直接扩展这个包。

YuzeMcpTool 是脚本优先的设计。它不暴露几十个单独 MCP tool，而是给 Agent 一个稳定入口，让 Agent 在项目内部运行能访问 Unity 的 JavaScript。

## 快速开始

### 1. 安装包

Embedded package：

```text
Packages/com.yuzetoolkit.mcptool
```

Unity Package Manager Git URL：

```text
https://github.com/Yuze075/YuzeMcpTool.git
```

依赖：

| 依赖 | 版本 / 说明 |
|---|---|
| Unity | `2022.3` 或更新 |
| PuerTS | [`com.tencent.puerts.core`](https://github.com/Tencent/puerts) `3.0.0`，并且需要一个 backend package，例如 `com.tencent.puerts.v8`、`quickjs`、`nodejs` 或 `webgl`。 |
| Unity Test Framework | `com.unity.test-framework` `1.4.0` |

当前仓库已经在 `Packages/com.tencent.puerts.*` 下包含 embedded PuerTS package。把 YuzeMcpTool 安装到其他 Unity 项目时，需要先安装 PuerTS，或让 Unity 从你的 package source 解析它。见官方 [PuerTS Unity 安装文档](https://puerts.github.io/docs/puerts/unity/install/)。

YuzeMcpTool 使用的是 PuerTS runtime 和 backend package，不要求也不会安装 PuerTS 自带的 `com.tencent.puerts.mcp` 包。

### 2. 启动 Unity

Unity Editor 中 MCP server 默认自动启动。

| 项目 | 值 |
|---|---|
| MCP endpoint | `http://127.0.0.1:3100/mcp` |
| Health check | `http://127.0.0.1:3100/health` |
| Server window | `YuzeToolkit/MCP/Server Window` |

打开 Server Window 可以启动/停止服务、复制 endpoint、查看活跃 session 和最近错误。

### 3. 连接 AI 客户端

把 MCP 客户端配置为 Streamable HTTP：

```text
http://127.0.0.1:3100/mcp
```

连接后，让 Agent 列出 MCP tools。它应该只看到 `evalJsCode`。

推荐第一条提示词：

```text
Use the Unity MCP tool. First call evalJsCode to import YuzeToolkit/mcp/index.mjs and read its description. Then inspect the current Unity state before making changes.
```

## MCP 客户端配置

不同客户端的配置格式略有差异。

| 客户端 | 推荐配置 |
|---|---|
| Claude Code | `claude mcp add --transport http yuzemcptool http://127.0.0.1:3100/mcp` |
| Cursor | 当前项目用 `.cursor/mcp.json`，全局用 `~/.cursor/mcp.json`。 |
| VS Code / GitHub Copilot | 使用 `.vscode/mcp.json` 或 MCP server UI。VS Code 使用 `servers`。 |
| Windsurf | 优先使用 Cascade MCP UI。直接写 JSON 时，不同版本 schema 可能不同。 |
| Claude Desktop | 如果不能直接使用本地 HTTP MCP URL，优先使用 Desktop Extension (`.mcpb`) 或 wrapper。直接 HTTP 用 Claude Code 更简单。 |

Cursor 示例：

```json
{
  "mcpServers": {
    "yuzemcptool": {
      "type": "http",
      "url": "http://127.0.0.1:3100/mcp"
    }
  }
}
```

VS Code 示例：

```json
{
  "servers": {
    "yuzemcptool": {
      "type": "http",
      "url": "http://127.0.0.1:3100/mcp"
    }
  }
}
```

更多连接与排错见 [客户端设置](docs/CLIENT_SETUP_zh.md)。

## 功能地图

| 领域 | Agent 可以做什么 |
|---|---|
| Runtime | 环境状态、日志、batch、GameObject、Component、诊断、reflection。 |
| Editor | 编译状态、Selection、菜单、播放模式、截图。 |
| Assets | 搜索、读写文本资源、移动/复制/删除、依赖、脚本、材质。 |
| Scenes and Prefabs | 打开/保存 Scene、检查 hierarchy、实例化/创建 Prefab、管理 override。 |
| Serialized data | 读写 Inspector 序列化字段和数组。 |
| Pipeline | Package、测试、构建设置、构建请求。 |
| Validation | 缺失脚本、缺失引用、`[SerializeField]` Tooltip 检查。 |
| Custom logic | 直接 bridge 调用或 PuerTS C# interop，处理项目自定义 API。 |

## 设计取舍

YuzeMcpTool 刻意只暴露一个 MCP tool：

```text
evalJsCode
```

AI 在 Unity 内运行 JavaScript，并从这些路径 import helper module：

```text
YuzeToolkit/mcp/index.mjs
YuzeToolkit/mcp/Runtime/*.mjs
YuzeToolkit/mcp/Editor/*.mjs
```

这样 MCP 工具列表很小且稳定，同时仍能覆盖大量 Unity 自动化需求。

### 和其他 Unity MCP 插件相比

| 方案 | 更适合 | 代价 |
|---|---|---|
| YuzeMcpTool | 自定义自动化、项目专用调试、Runtime/Player 检查、任意 Unity 侧 JavaScript。 | Agent 必须能写和修 JavaScript；常见 Editor 操作不如“大工具列表”直观。 |
| 大工具集 Unity MCP 插件 | 开箱即用的 Editor-only 工作流、可见工具目录、Agent 少写脚本。 | 如果插件没有正好需要的工具，自定义多步骤流程会更受限制。 |

### 为什么不用 PuerTS 自带 MCP

PuerTS 也有 MCP 相关支持，但本包没有直接使用它。原因是这里需要更多 Unity 专用 helper、显式安全确认、session 监控、Runtime/Player 支持，以及可预测的单 tool 行为。本地使用时，PuerTS 自带 MCP 的工具面也偏少，不足以支撑这个包的工作流，稳定性也不适合作为主入口。

## 自己扩展工具

如果 helper 覆盖不了你的项目需求，推荐直接扩展这个包：

1. 只是编排现有能力时，优先在 `Resources/YuzeToolkit/mcp/Runtime` 或 `Resources/YuzeToolkit/mcp/Editor` 增加 JavaScript helper。
2. 需要 Unity API、Editor API、异步 Unity 流程或安全确认时，再新增或扩展 C# bridge command。
3. 新 helper 记到 `docs/HELPER_MODULES_zh.md`；危险操作记到 `docs/ADVANCED_USAGE_zh.md`。
4. 深改 server、bridge、session 或 helper 结构前，让你的 AI 先读 [项目设计](docs/PROJECT_DESIGN_zh.md)。

## 文档

| 文档 | 用途 |
|---|---|
| [客户端设置](docs/CLIENT_SETUP_zh.md) | 安装、启动、配置 MCP 客户端、验证连接。 |
| [Helper 参考](docs/HELPER_MODULES_zh.md) | Runtime 和 Editor helper module 目录。 |
| [项目设计](docs/PROJECT_DESIGN_zh.md) | 架构、请求流程、扩展点和维护注意事项。 |
| [高级说明](docs/ADVANCED_USAGE_zh.md) | 直接 bridge 调用、PuerTS interop、安全、Domain Reload、迁移说明。 |
| [English README](README.md) | 英文概览和快速开始。 |

## 状态与保证

这个项目整体由 AI 实现。它是可修改源码和实用参考实现，不是有保证的产品。

不对正确性、稳定性、完整性、安全性或生产适用性做保证。如果缺功能或有 bug，推荐让你自己的 AI Agent 检查并修改这个包，让它适配你的项目。

## AI Agent 参考

大多数人类读者可以读到这里就停下。详细 API 参考在 docs：

- 从 [Helper 参考](docs/HELPER_MODULES_zh.md) 开始。
- 深改 server、bridge、session 或 helper 架构前，先读 [项目设计](docs/PROJECT_DESIGN_zh.md)。
- 需要直接 bridge 调用或 PuerTS C# interop 时看 [高级说明](docs/ADVANCED_USAGE_zh.md)。
- 连接或 session 出问题时看 [客户端设置](docs/CLIENT_SETUP_zh.md)。

最小 `evalJsCode` 调用：

```javascript
async function execute() {
  const index = await import('YuzeToolkit/mcp/index.mjs');
  return index.description;
}
```

## License

MIT License. See [LICENSE](LICENSE).
