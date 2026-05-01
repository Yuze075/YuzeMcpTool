# Client Setup

[README](../README.md) | [中文](CLIENT_SETUP_zh.md) | [Helper reference](HELPER_MODULES.md) | [Project design](PROJECT_DESIGN.md) | [Advanced notes](ADVANCED_USAGE.md)

[![MCP](https://img.shields.io/badge/MCP-Streamable%20HTTP-4b7bec)](https://modelcontextprotocol.io/)
[![Endpoint](https://img.shields.io/badge/Default%20endpoint-127.0.0.1%3A3100-blue)](#endpoint)

This page is for human setup. It explains how to install the Unity package, start the MCP server, configure common clients, and verify that the connection works.

## Endpoint

YuzeMcpTool runs a local Streamable HTTP MCP server inside Unity.

| Item | Value |
|---|---|
| MCP URL | `http://127.0.0.1:3100/mcp` or `http://localhost:3100/mcp` |
| Health URL | `http://127.0.0.1:3100/health` or `http://localhost:3100/health` |
| Transport | Streamable HTTP / HTTP |
| Tool exposed | `evalJsCode` |
| Unity menu | `YuzeToolkit/MCP/Server Window` |

Keep the server bound to loopback unless you have a controlled network setup. By default, the server listens on both `127.0.0.1` and `localhost` for local client compatibility.

## Install

Embedded package:

```text
Packages/com.yuzetoolkit.mcptool
```

Git URL package:

```text
https://github.com/Yuze075/YuzeMcpTool.git
```

Unity should resolve these dependencies from `package.json`:

| Dependency | Version / note |
|---|---|
| Unity | `2022.3` or newer |
| [`com.tencent.puerts.core`](https://github.com/Tencent/puerts) | `3.0.0` |
| PuerTS backend | Install one backend package: `com.tencent.puerts.v8`, `com.tencent.puerts.quickjs`, `com.tencent.puerts.nodejs`, or `com.tencent.puerts.webgl`. |
| `com.unity.test-framework` | `1.4.0` |

This repository already has PuerTS installed as embedded packages under `Packages/com.tencent.puerts.*`. Other projects still need to install PuerTS or make it resolvable through their package source before YuzeMcpTool can run. See the official [PuerTS Unity install guide](https://puerts.github.io/docs/puerts/unity/install/).

YuzeMcpTool uses the PuerTS runtime and backend packages. It does not require or install PuerTS's own `com.tencent.puerts.mcp` package.

## Start The Server

In the Unity Editor, the server starts automatically by default.

Menu actions:

| Menu | Purpose |
|---|---|
| `YuzeToolkit/MCP/Server Window` | View status, endpoint, sessions, eval results, and errors. |
| `YuzeToolkit/MCP/Start Server` | Start the MCP server. |
| `YuzeToolkit/MCP/Stop Server` | Stop the MCP server and clear active sessions. |
| `YuzeToolkit/MCP/Log Status` | Print current server status to the Unity Console. |

Runtime/Player usage is optional. Add `McpServerBehaviour` to a GameObject, or start manually:

```csharp
McpServer.Shared.Start(new McpServerOptions { Port = 3100 });
```

Editor helper modules only work in the Unity Editor. Runtime helper modules can work in Runtime/Player when the underlying Unity APIs are available.

## Configure MCP Clients

### Claude Code

```bash
claude mcp add --transport http yuzemcptool http://127.0.0.1:3100/mcp
```

Useful checks:

```bash
claude mcp list
```

Inside Claude Code, run:

```text
/mcp
```

### Cursor

Project config:

```text
.cursor/mcp.json
```

Global config:

```text
~/.cursor/mcp.json
```

Example:

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

Cursor CLI checks:

```bash
cursor-agent mcp list
cursor-agent mcp list-tools yuzemcptool
```

### VS Code / GitHub Copilot

Workspace config:

```text
.vscode/mcp.json
```

Example:

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

VS Code can also add and manage MCP servers from the MCP server UI and command palette. When `.vscode/mcp.json` is open, VS Code may show inline actions to start, stop, or restart servers.

### Windsurf

Prefer the Cascade MCP UI:

1. Open Windsurf MCP/Cascade settings.
2. Add a custom server.
3. Choose HTTP / Streamable HTTP if available.
4. Use `http://127.0.0.1:3100/mcp`.

Raw JSON configuration can vary by Windsurf version, so prefer the UI unless you are matching a known local schema.

### Claude Desktop

Claude Desktop support for direct local Streamable HTTP can vary by version and distribution path. If a direct HTTP URL is not accepted, use one of these approaches:

- Package a Claude Desktop Extension (`.mcpb`).
- Use a wrapper such as a local stdio-to-HTTP bridge.
- Use Claude Code for direct local HTTP while developing.

## Verify The Connection

1. Open Unity.
2. Open `YuzeToolkit/MCP/Server Window`.
3. Confirm the endpoint is `http://127.0.0.1:3100/mcp`.
4. Configure your MCP client.
5. Ask the client to list MCP tools.

Expected tool list:

```text
evalJsCode
```

Recommended first agent instruction:

```text
Use evalJsCode to import YuzeToolkit/mcp/index.mjs and return index.description.
```

## Manual JSON-RPC Check

Use this when debugging a client integration.

Initialize:

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

The response includes an `Mcp-Session-Id` header. Send that header on later `/mcp` requests.

Call the tool:

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

End a session:

```text
DELETE /mcp
Mcp-Session-Id: <session id>
```

## Troubleshooting

| Problem | What to check |
|---|---|
| Client cannot connect | Unity is open, server window says running, port `3100` is free, endpoint URL is correct. |
| `Session not found` | Reinitialize the MCP client. Domain Reload or server restart can invalidate sessions. |
| `Parse error` or `Invalid character '\'` | The HTTP body is not valid JSON. This often happens when the whole JSON request was escaped one extra time, for example `{\\\"jsonrpc\\\"...}` instead of `{"jsonrpc":...}`. |
| Tool calls fail during compile | Wait for Unity compilation or asset refresh to finish. |
| Editor helper fails in Player | Editor helpers require `UnityEditor`; use Runtime helpers in Runtime/Player. |
| No tools appear | Confirm the client uses HTTP/Streamable HTTP and points to `/mcp`, not `/health`. |
