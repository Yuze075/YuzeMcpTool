# YuzeMcpTool

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-222?logo=unity)](https://unity.com/releases/editor/archive)
[![MCP](https://img.shields.io/badge/MCP-Streamable%20HTTP-4b7bec)](https://modelcontextprotocol.io/)
[![PuerTS](https://img.shields.io/badge/PuerTS-3.0.0-00a8ff)](https://github.com/Tencent/puerts)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![AI Built](https://img.shields.io/badge/Built%20by-AI-orange)](#状态与保证)

[English](README.md) | [Helper 参考](docs/HELPER_MODULES_zh.md) | [项目设计](docs/PROJECT_DESIGN_zh.md) | [高级说明](docs/ADVANCED_USAGE_zh.md)

YuzeMcpTool 是一个跑在 Unity 内部、给 AI Agent 使用的 MCP server。它只暴露一个 MCP tool —— `evalJsCode` —— Agent 通过它把 JavaScript 提交到 PuerTS 中执行。helper module 覆盖 Editor 和 Runtime/Player 工作流：Scene、GameObject、Component、Asset、Prefab、Importer、序列化字段、测试、构建、校验，以及项目自定义 C# API。

![YuzeMcpTool overview](docs/Images/YuzeMcpTool-Overview.png)

## 这个工具做什么

当你希望 AI 不只是从外部改文件，而是能进入 Unity 内部检查和操作时，使用这个包。

- 检查 Editor 和 Runtime/Player 状态。
- 查询和编辑 GameObject、Component、Scene、Asset、Prefab、Importer、序列化字段。
- 读取日志、运行校验、跑测试、检查构建设置。
- 跑项目专用 JavaScript 做临时调试。
- helper 或 bridge command 不够用时，直接扩展这个包。

设计上是脚本优先：一个稳定的 MCP tool，加一层按需 import 的 JavaScript helper。MCP 表面保持很小，helper 层覆盖日常 Unity 自动化。

## 快速开始

### 1. 先安装 PuerTS

YuzeMcpTool 通过 PuerTS 在 Unity 内运行 JavaScript。安装本包前先装好：

- `com.tencent.puerts.core` —— PuerTS 核心
- 任选一个 JavaScript backend：`com.tencent.puerts.v8`、`com.tencent.puerts.quickjs`、`com.tencent.puerts.nodejs`、`com.tencent.puerts.webgl`

具体安装步骤参考官方文档：

- [PuerTS Unity 安装文档](https://puerts.github.io/docs/puerts/unity/install/)
- [PuerTS GitHub 仓库](https://github.com/Tencent/puerts)

YuzeMcpTool 不依赖、不替代、也不会安装 PuerTS 自带的 `com.tencent.puerts.mcp` 包。

### 2. 安装 YuzeMcpTool

选择下面任意一种方式安装。

#### 直接下载安装

1. 从 [GitHub](https://github.com/Yuze075/YuzeMcpTool) 下载 YuzeMcpTool 源码或 release 压缩包。
2. 把压缩包解压到本地目录。
3. 确认解压出来的 package 文件夹里有 `package.json`、`README.md`、`Runtime`、`Editor` 和 `Resources`。
4. 打开你的 Unity 项目。
5. 打开 `Window/Package Manager`。
6. 点击 `+`。
7. 选择 `Add package from disk...`。
8. 选中解压后 package 文件夹里的 `package.json`。
9. 等 Unity 导入并编译完成。

如果想作为 embedded package 使用，把解压后的 package 文件夹复制到：

```text
Packages/com.yuzetoolkit.mcptool
```

然后重新打开或切回 Unity，等待 package 导入。

#### 通过 GitHub 链接安装

Unity Package Manager 界面安装：

1. 打开你的 Unity 项目。
2. 打开 `Window/Package Manager`。
3. 点击 `+`。
4. 选择 `Add package from git URL...`。
5. 粘贴：

```text
https://github.com/Yuze075/YuzeMcpTool.git
```

6. 点击 `Add`。
7. 等 Unity 解析、导入并编译完成。

也可以手动改 `Packages/manifest.json`，把这一项加到已有 dependencies 旁边：

```json
{
  "dependencies": {
    "com.yuzetoolkit.mcptool": "https://github.com/Yuze075/YuzeMcpTool.git"
  }
}
```

### 3. 启动 Unity 并检查服务

Unity Editor 中 MCP server 默认自动启动。

| 项目 | 值 |
|---|---|
| MCP endpoint | `http://127.0.0.1:3100/mcp` |
| Health check | `http://127.0.0.1:3100/health` |
| Transport | Streamable HTTP / HTTP |
| Server window | `YuzeToolkit/MCP/Server Window` |
| 暴露的 MCP tool | `evalJsCode` |

在 `YuzeToolkit/MCP/Server Window` 中可以启动/停止服务、复制 endpoint、查看活跃 session 和最近错误。

### 4. 配置 MCP 客户端

下面所有示例都连接默认本地 endpoint：

```text
http://127.0.0.1:3100/mcp
```

除非你有受控网络环境，否则保持 server 只绑定本机 loopback。

#### Claude Code

CLI：

```bash
claude mcp add --transport http yuzemcptool --scope project http://127.0.0.1:3100/mcp
claude mcp list
```

项目级配置写入项目根目录的 `.mcp.json`，local/user scope 写入 `~/.claude.json`。

手动 `.mcp.json`：

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

在 Claude Code 内执行 `/mcp` 可以查看和处理已配置的 server。

#### Codex

CLI：

```bash
codex mcp add yuzemcptool --url http://127.0.0.1:3100/mcp
codex mcp list
```

Codex CLI 和 Codex IDE extension 共享 `~/.codex/config.toml`。手动 TOML：

```toml
[mcp_servers.yuzemcptool]
url = "http://127.0.0.1:3100/mcp"
```

直接改文件后，重启 Codex 或重新加载 MCP 配置。

#### Cursor

项目配置：`.cursor/mcp.json`，全局配置：`~/.cursor/mcp.json`。

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

在 Cursor 中打开 Settings → MCP 添加或启用 server。CLI：

```bash
cursor-agent mcp list
cursor-agent mcp list-tools yuzemcptool
```

#### Gemini CLI

CLI：

```bash
gemini mcp add --transport http yuzemcptool http://127.0.0.1:3100/mcp
gemini mcp list
```

项目配置：`.gemini/settings.json`，用户配置：`~/.gemini/settings.json`。

```json
{
  "mcpServers": {
    "yuzemcptool": {
      "httpUrl": "http://127.0.0.1:3100/mcp",
      "trust": false
    }
  }
}
```

如果要让 server 在当前项目外也可用，执行 `gemini mcp add` 时加 `--scope user`。

#### VS Code / GitHub Copilot

Workspace 配置：`.vscode/mcp.json`。

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

UI 配置：

1. 打开 Command Palette。
2. 执行 `MCP: Add Server` 或 `MCP: Open Workspace Folder MCP Configuration`。
3. 选择 HTTP / Streamable HTTP，填入 `http://127.0.0.1:3100/mcp`。
4. 打开 GitHub Copilot Chat，切换到 Agent mode，在 tools picker 中启用 `yuzemcptool`。

### 5. 验证连接

1. 打开 Unity 并打开 `YuzeToolkit/MCP/Server Window`。
2. 确认 endpoint 是 `http://127.0.0.1:3100/mcp`，并且 server 在运行。
3. 配置 MCP 客户端。
4. 让客户端列出 MCP tools，应看到 `evalJsCode`。

推荐第一条提示词：

```text
Use the Unity MCP tool. First call evalJsCode to import YuzeToolkit/mcp/index.mjs and read its description. Then inspect the current Unity state before making changes.
```

### 故障排查

| 问题 | 检查 |
|---|---|
| 客户端无法连接 | Unity 已打开、Server Window 显示 running、`3100` 端口空闲、URL 以 `/mcp` 结尾。 |
| 看不到 tools | 客户端使用 HTTP / Streamable HTTP（不是 stdio），并连接 `/mcp`，不是 `/health`。 |
| `Session not found` | 重新 initialize 或重启 MCP 客户端。Domain Reload 或 server 重启会让 session 失效。 |
| 编译期间 tool 调用失败 | 等 Unity 编译或资源刷新结束后重试。 |
| Player 中 Editor helper 失败 | Editor helper 依赖 `UnityEditor`；Runtime/Player 中改用 Runtime helper。 |

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

YuzeMcpTool 只暴露一个 MCP tool：

```text
evalJsCode
```

Agent 在 Unity 内运行 JavaScript，并从下面这些路径 import helper module：

```text
YuzeToolkit/mcp/index.mjs
YuzeToolkit/mcp/Runtime/*.mjs
YuzeToolkit/mcp/Editor/*.mjs
```

MCP 工具列表保持很小且稳定，helper 层覆盖日常 Unity 自动化。

### 和多工具型 Unity MCP 插件对比

| 方案 | 更适合 | 代价 |
|---|---|---|
| YuzeMcpTool | 自定义自动化、项目专用调试、Runtime/Player 检查、任意 Unity 侧 JavaScript。 | Agent 必须能写有效的 JavaScript；常见 Editor 操作不如长工具列表直观。 |
| 多工具插件 | 开箱即用的 Editor 工作流、可见的工具目录。 | 当插件没有正好对应的工具时，多步骤自定义流程难以表达。 |

### 与 PuerTS 自带 MCP 的关系

PuerTS 自带一个 MCP 相关 package（`com.tencent.puerts.mcp`）。YuzeMcpTool 是独立的：自带 MCP server、session 跟踪、Unity 专用 helper module、安全标志和 Runtime/Player 支持，不依赖也不会干扰 PuerTS 的 MCP 包。

## 自己扩展工具

如果 helper 覆盖不了你的项目需求，直接扩展这个包：

1. 只是编排现有能力时，在 `Resources/YuzeToolkit/mcp/Runtime` 或 `Resources/YuzeToolkit/mcp/Editor` 增加 JavaScript helper。
2. 需要 Unity API、异步 Unity 流程或显式安全检查时，新增或扩展 C# bridge command。
3. 新 helper 更新到 [Helper 参考](docs/HELPER_MODULES_zh.md)；破坏性操作更新到 [高级说明](docs/ADVANCED_USAGE_zh.md)。
4. 改 server、bridge、session 或 helper 架构前，先读 [项目设计](docs/PROJECT_DESIGN_zh.md)。

## 文档

| 文档 | 用途 |
|---|---|
| [README](README_zh.md) | 安装 PuerTS、安装 YuzeMcpTool、配置 MCP 客户端、验证连接。 |
| [Helper 参考](docs/HELPER_MODULES_zh.md) | Runtime 和 Editor helper module 目录。 |
| [项目设计](docs/PROJECT_DESIGN_zh.md) | 架构、请求流程、扩展点、生命周期规则。 |
| [高级说明](docs/ADVANCED_USAGE_zh.md) | 直接 bridge 调用、PuerTS C# interop、安全标志、Domain Reload。 |
| [English README](README.md) | 英文概览和快速开始。 |

最小 `evalJsCode` 调用：

```javascript
async function execute() {
  const index = await import('YuzeToolkit/mcp/index.mjs');
  return index.description;
}
```

## 状态与保证

这个项目整体由 AI 实现。它是可修改源码和实用参考实现，不是有保证的产品。

不对正确性、稳定性、完整性、安全性或生产适用性做保证。如果缺功能或有 bug，推荐让你自己的 AI Agent 检查并修改这个包以适配你的项目。

## License

MIT License. See [LICENSE](LICENSE).
