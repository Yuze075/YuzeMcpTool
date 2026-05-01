# 客户端设置

[README](../README_zh.md) | [English](CLIENT_SETUP.md) | [Helper 参考](HELPER_MODULES_zh.md) | [项目设计](PROJECT_DESIGN_zh.md) | [高级说明](ADVANCED_USAGE_zh.md)

[![MCP](https://img.shields.io/badge/MCP-Streamable%20HTTP-4b7bec)](https://modelcontextprotocol.io/)
[![Endpoint](https://img.shields.io/badge/Default%20endpoint-127.0.0.1%3A3100-blue)](#endpoint)

本文面向人类配置使用，说明如何安装 Unity package、启动 MCP server、配置常见客户端，并验证连接是否正常。

## Endpoint

YuzeMcpTool 在 Unity 内运行一个本地 Streamable HTTP MCP server。

| 项目 | 值 |
|---|---|
| MCP URL | `http://127.0.0.1:3100/mcp` 或 `http://localhost:3100/mcp` |
| Health URL | `http://127.0.0.1:3100/health` 或 `http://localhost:3100/health` |
| Transport | Streamable HTTP / HTTP |
| 暴露的 tool | `evalJsCode` |
| Unity 菜单 | `YuzeToolkit/MCP/Server Window` |

除非你有受控网络环境，否则保持 server 绑定在 loopback。本地开发默认同时监听 `127.0.0.1` 和 `localhost`，兼容不同客户端的本地地址写法。

## 安装

Embedded package：

```text
Packages/com.yuzetoolkit.mcptool
```

Git URL package：

```text
https://github.com/Yuze075/YuzeMcpTool.git
```

Unity 会从 `package.json` 解析这些依赖：

| 依赖 | 版本 / 说明 |
|---|---|
| Unity | `2022.3` 或更新 |
| [`com.tencent.puerts.core`](https://github.com/Tencent/puerts) | `3.0.0` |
| PuerTS backend | 需要安装一个 backend package：`com.tencent.puerts.v8`、`com.tencent.puerts.quickjs`、`com.tencent.puerts.nodejs` 或 `com.tencent.puerts.webgl`。 |
| `com.unity.test-framework` | `1.4.0` |

当前仓库已经在 `Packages/com.tencent.puerts.*` 下安装了 embedded PuerTS package。其他项目仍然需要先安装 PuerTS，或保证 Unity 能从自己的 package source 解析它，YuzeMcpTool 才能运行。见官方 [PuerTS Unity 安装文档](https://puerts.github.io/docs/puerts/unity/install/)。

YuzeMcpTool 使用的是 PuerTS runtime 和 backend package，不要求也不会安装 PuerTS 自带的 `com.tencent.puerts.mcp` 包。

## 启动服务

Unity Editor 中，server 默认会自动启动。

菜单：

| 菜单 | 用途 |
|---|---|
| `YuzeToolkit/MCP/Server Window` | 查看状态、endpoint、session、eval 结果和错误。 |
| `YuzeToolkit/MCP/Start Server` | 启动 MCP server。 |
| `YuzeToolkit/MCP/Stop Server` | 停止 MCP server 并清空活跃 session。 |
| `YuzeToolkit/MCP/Log Status` | 把当前 server 状态打印到 Unity Console。 |

Runtime/Player 使用是可选的。可以把 `McpServerBehaviour` 加到 GameObject 上，或手动启动：

```csharp
McpServer.Shared.Start(new McpServerOptions { Port = 3100 });
```

Editor helper module 只能在 Unity Editor 中工作。Runtime helper module 在底层 Unity API 可用时可在 Runtime/Player 中工作。

## 配置 MCP 客户端

### Claude Code

```bash
claude mcp add --transport http yuzemcptool http://127.0.0.1:3100/mcp
```

检查：

```bash
claude mcp list
```

Claude Code 内执行：

```text
/mcp
```

### Cursor

项目配置：

```text
.cursor/mcp.json
```

全局配置：

```text
~/.cursor/mcp.json
```

示例：

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

Cursor CLI 检查：

```bash
cursor-agent mcp list
cursor-agent mcp list-tools yuzemcptool
```

### VS Code / GitHub Copilot

Workspace 配置：

```text
.vscode/mcp.json
```

示例：

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

VS Code 也可以通过 MCP server UI 和 command palette 添加、管理 MCP server。打开 `.vscode/mcp.json` 时，VS Code 可能显示内联 start、stop、restart 操作。

### Windsurf

优先使用 Cascade MCP UI：

1. 打开 Windsurf MCP/Cascade 设置。
2. 添加 custom server。
3. 如果可选，选择 HTTP / Streamable HTTP。
4. 填入 `http://127.0.0.1:3100/mcp`。

Windsurf 不同版本的原始 JSON 配置可能不同；除非你明确知道当前 schema，否则优先使用 UI。

### Claude Desktop

Claude Desktop 对本地 Streamable HTTP 直连的支持会因版本和分发方式不同而变化。如果不接受直接 HTTP URL，使用以下方案之一：

- 打包 Claude Desktop Extension (`.mcpb`)。
- 使用本地 stdio-to-HTTP wrapper。
- 开发阶段使用 Claude Code 直连本地 HTTP。

## 验证连接

1. 打开 Unity。
2. 打开 `YuzeToolkit/MCP/Server Window`。
3. 确认 endpoint 是 `http://127.0.0.1:3100/mcp`。
4. 配置 MCP 客户端。
5. 让客户端列出 MCP tools。

预期 tool：

```text
evalJsCode
```

推荐第一条 Agent 指令：

```text
Use evalJsCode to import YuzeToolkit/mcp/index.mjs and return index.description.
```

## 手动 JSON-RPC 检查

调试客户端集成时使用。

Initialize：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "initialize",
  "params": {
    "protocolVersion": "2025-06-18",
    "clientInfo": {
      "name": "debug-client",
      "version": "1.0.0"
    }
  }
}
```

响应会包含 `Mcp-Session-Id` header。后续 `/mcp` 请求要带上这个 header。

调用 tool：

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "evalJsCode",
    "arguments": {
      "code": "async function execute() { const index = await import('YuzeToolkit/mcp/index.mjs'); return index.description; }"
    }
  }
}
```

结束 session：

```text
DELETE /mcp
Mcp-Session-Id: <session id>
```

## 故障排查

| 问题 | 检查 |
|---|---|
| 客户端无法连接 | Unity 已打开、Server Window 显示 running、`3100` 端口空闲、endpoint URL 正确。 |
| `Session not found` | 重新 initialize MCP 客户端。Domain Reload 或 server 重启会让 session 失效。 |
| `Parse error` 或 `Invalid character '\'` | HTTP body 不是合法 JSON。常见原因是整段 JSON 被多转义了一层，例如发成 `{\\\"jsonrpc\\\"...}`，而不是 `{"jsonrpc":...}`。 |
| 编译期间 tool 调用失败 | 等 Unity 编译或资源刷新结束。 |
| Player 中 Editor helper 失败 | Editor helper 依赖 `UnityEditor`；Runtime/Player 中用 Runtime helper。 |
| 看不到 tools | 确认客户端使用 HTTP/Streamable HTTP，并连接 `/mcp`，不是 `/health`。 |
