# UnityEvalTool

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-222?logo=unity)](https://unity.com/releases/editor/archive)
[![MCP](https://img.shields.io/badge/MCP-HTTP%20JSON--RPC-4b7bec)](https://modelcontextprotocol.io/)
[![PuerTS](https://img.shields.io/badge/PuerTS-3.0.0-00a8ff)](https://github.com/Tencent/puerts)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)
[![AI Built](https://img.shields.io/badge/Built%20by-AI-orange)](#status-and-guarantee)

[中文](README_zh.md) | [Helper reference](docs/HELPER_MODULES.md) | [Runtime services](docs/RUNTIME_SERVICES.md) | [Project design](docs/PROJECT_DESIGN.md) | [Advanced notes](docs/ADVANCED_USAGE.md)

UnityEvalTool is a Unity-side MCP server for AI agents. It exposes one MCP tool — `evalJsCode` — which runs agent-supplied JavaScript inside Unity through PuerTS. Helper modules cover Editor and Runtime/Player workflows: scenes, GameObjects, components, assets, prefabs, importers, serialized fields, tests, builds, validation, object formatting, and project-specific C# APIs.

![UnityEvalTool overview](docs/Images/UnityEvalTool-Overview.png)

## What It Does

Use this package when you want an AI agent to operate inside Unity rather than only edit files from the outside.

- Inspect Editor and Runtime/Player state.
- Query and edit GameObjects, Components, scenes, assets, Prefabs, importers, serialized fields.
- Read logs, run validation, run tests, inspect build settings.
- Run project-specific JavaScript or direct PuerTS `CS.*` calls for ad-hoc debugging.
- Extend the package directly when a helper or C# tool is missing.

The design is script-first: one stable MCP tool plus a helper layer the agent imports as needed. The MCP surface stays small while helpers cover broad Unity automation; direct PuerTS `CS.*` interop remains available when helpers do not cover the task.

## Quick Start

### 1. Install PuerTS First

UnityEvalTool runs JavaScript inside Unity through PuerTS. Install both before adding this package:

- `com.tencent.puerts.core` — PuerTS core
- One JavaScript backend — pick one of `com.tencent.puerts.v8`, `com.tencent.puerts.quickjs`, `com.tencent.puerts.nodejs`, `com.tencent.puerts.webgl`

Follow the official install steps:

- [PuerTS Unity install guide](https://puerts.github.io/en/docs/puerts/unity/install/)
- [PuerTS GitHub repository](https://github.com/Tencent/puerts)

UnityEvalTool does not depend on, replace, or install PuerTS's own `com.tencent.puerts.mcp` package.

### 2. Install UnityEvalTool

Choose one of the following install methods.

#### Direct Download

1. Download the UnityEvalTool source or release archive from [GitHub](https://github.com/Yuze075/YuzeMcpTool).
2. Extract the archive to a local folder.
3. Confirm the extracted package folder contains `package.json`, `README.md`, `Runtime`, and `Editor`.
4. Open your Unity project.
5. Open `Window/Package Manager`.
6. Click `+`.
7. Choose `Add package from disk...`.
8. Select the extracted package folder's `package.json`.
9. Wait for Unity to import and compile the package.

To use it as an embedded package instead, copy the extracted folder to:

```text
Packages/com.yuzetoolkit.unityevaltool
```

Then reopen or refocus Unity and wait for package import.

#### GitHub URL

Unity Package Manager UI:

1. Open your Unity project.
2. Open `Window/Package Manager`.
3. Click `+`.
4. Choose `Add package from git URL...`.
5. Paste:

```text
https://github.com/Yuze075/UnityEvalTool.git
```

6. Click `Add`.
7. Wait for Unity to resolve, import, and compile the package.

Or edit `Packages/manifest.json` manually and add the entry alongside your existing dependencies:

```json
{
  "dependencies": {
    "com.yuzetoolkit.unityevaltool": "https://github.com/Yuze075/UnityEvalTool.git"
  }
}
```

### 3. Start Unity And Check The Server

The MCP server is installed stopped by default. Open `UnityEvalTool/Window` and use the MCP Service tab when you want an MCP client to connect.

| Item | Value |
|---|---|
| MCP endpoint | `http://127.0.0.1:3100/mcp` |
| Health check | `http://127.0.0.1:3100/health` |
| MCP protocol | HTTP POST JSON-RPC MCP session endpoint |
| Default transport | Non-WebGL: `TcpListener`; WebGL: WebSocket placeholder that returns an unsupported result |
| Editor window | `UnityEvalTool/Window` |
| Exposed MCP tool | `evalJsCode` |

Open `UnityEvalTool/Window` to manage Tools, start or stop the MCP server, edit host/port/token-auth settings, copy the endpoint, inspect MCP conversations, manage the CLI bridge, copy the CLI command, launch CLI, and inspect active CLI connections. MCP token auth is optional. When enabled, clients must send either `Authorization: Bearer <token>` or `X-UnityEvalTool-Token: <token>`.

Runtime/Player builds can start the same MCP server from code with `McpServer.Shared.Start(...)` or a scene-owned `McpServerBehaviour`. Runtime does not automatically read the Editor window's `EditorPrefs`; pass the desired host, port, token, timeout, and session settings into `McpServerOptions`. See [Runtime services](docs/RUNTIME_SERVICES.md).

### CLI Bridge

The package also includes a local TCP bridge for the `unity` CLI. It is separate from MCP: MCP still exposes only `evalJsCode`, while the CLI reads richer command metadata for command help and argument parsing.

All CLI bridge operations are in `UnityEvalTool/Window`, under the CLI Service and CLI Consoles tabs.

Common CLI usage:

```bash
unity --host 127.0.0.1 --port 49231 --token <token> -help
unity --host 127.0.0.1 --port 49231 --token <token> runtime --help
unity --host 127.0.0.1 --port 49231 --token <token> runtime getState
unity --host 127.0.0.1 --port 49231 --token <token> eval-js --code "return 1 + 2;"
```

Global help lists Built-ins with short descriptions and Tools only. Use `<tool> --help` to list that tool's commands, and `<tool> <command> --help` for one command's arguments.

CLI token auth is configurable in the CLI Bridge window and is enabled by default. Leave the token empty to generate one on start. Domain Reload, script compilation, and Unity Test Runner can close live CLI connections; if the bridge was started from the Editor it will restore the listener after reload, but CLI clients still need to reconnect.

Runtime/Player builds can start the CLI bridge with `CliBridgeServer.Shared.Start(new CliBridgeOptions { ... })`. Use `Port = 0` to let the OS choose a free port, then read `CliBridgeServer.Shared.State.Port` and `State.Token` for the CLI connect command. See [Runtime services](docs/RUNTIME_SERVICES.md).

### 4. Configure Your MCP Client

All examples below point to the default local endpoint:

```text
http://127.0.0.1:3100/mcp
```

Keep the server bound to loopback unless you intentionally control the network environment. To let other devices on the LAN connect, set `McpServerOptions.Host = "0.0.0.0"` or use the Host field on `McpServerBehaviour`.

Non-WebGL builds use the same `TcpListener`-based transport in the Editor and Player. WebGL cannot listen for inbound TCP/HTTP connections; its WebSocket transport is currently a placeholder that returns an unsupported result until a relay protocol is implemented.

#### Claude Code

CLI:

```bash
claude mcp add --transport http unityevaltool --scope project http://127.0.0.1:3100/mcp
claude mcp list
```

Project-scope config is written to `.mcp.json` at the project root. Local and user scopes go to `~/.claude.json`.

Manual `.mcp.json`:

```json
{
  "mcpServers": {
    "unityevaltool": {
      "type": "http",
      "url": "http://127.0.0.1:3100/mcp"
    }
  }
}
```

Inside Claude Code, run `/mcp` to inspect or authenticate configured servers.

#### Codex

CLI:

```bash
codex mcp add unityevaltool --url http://127.0.0.1:3100/mcp
codex mcp list
```

Codex CLI and the Codex IDE extension share configuration at `~/.codex/config.toml`. Manual TOML:

```toml
[mcp_servers.unityevaltool]
url = "http://127.0.0.1:3100/mcp"
```

Restart Codex or reload its MCP config after editing the file directly.

#### Cursor

Project config: `.cursor/mcp.json`. Global config: `~/.cursor/mcp.json`.

```json
{
  "mcpServers": {
    "unityevaltool": {
      "type": "http",
      "url": "http://127.0.0.1:3100/mcp"
    }
  }
}
```

In the Cursor app, open Settings → MCP to add or enable the server. CLI:

```bash
cursor-agent mcp list
cursor-agent mcp list-tools unityevaltool
```

#### Gemini CLI

CLI:

```bash
gemini mcp add --transport http unityevaltool http://127.0.0.1:3100/mcp
gemini mcp list
```

Project config: `.gemini/settings.json`. User config: `~/.gemini/settings.json`.

```json
{
  "mcpServers": {
    "unityevaltool": {
      "httpUrl": "http://127.0.0.1:3100/mcp",
      "trust": false
    }
  }
}
```

Use `--scope user` with `gemini mcp add` to make the server available outside the current project.

#### VS Code / GitHub Copilot

Workspace config: `.vscode/mcp.json`.

```json
{
  "servers": {
    "unityevaltool": {
      "type": "http",
      "url": "http://127.0.0.1:3100/mcp"
    }
  }
}
```

UI flow:

1. Open the Command Palette.
2. Run `MCP: Add Server` or `MCP: Open Workspace Folder MCP Configuration`.
3. Choose HTTP and enter `http://127.0.0.1:3100/mcp`.
4. Open GitHub Copilot Chat, switch to Agent mode, and enable `unityevaltool` in the tools picker.

### 5. Verify The Connection

1. Open Unity and open `UnityEvalTool/Window`.
2. Confirm the endpoint is `http://127.0.0.1:3100/mcp` and the server is running.
3. Configure your MCP client.
4. Ask the client to list MCP tools — you should see `evalJsCode`.

Recommended first prompt:

```text
Use the Unity MCP tool. First call evalJsCode to import tools/index and read its description. Then inspect the current Unity state before making changes.
```

### Troubleshooting

| Problem | What to check |
|---|---|
| Client cannot connect | Unity is open, the server window says running, port `3100` is free, the URL ends in `/mcp`. |
| LAN device cannot connect | Set Host to `0.0.0.0`, allow the port through the OS firewall, and use the Unity device's LAN IP in the client URL. |
| Server cannot start in WebGL | WebGL cannot listen for inbound TCP/HTTP connections; the WebSocket transport currently returns an explicit unsupported result until a relay is implemented. |
| No tools appear | Client uses HTTP / Streamable HTTP (not stdio) and points to `/mcp`, not `/health`. |
| `Session not found` | Reinitialize or restart the MCP client. Domain Reload or a server restart invalidates sessions. |
| CLI cannot connect after tests or compilation | Reopen the CLI with the copied connect command. The bridge listener auto-restores after Domain Reload when it was started from the Editor, but existing TCP clients cannot survive reload. |
| Tool calls fail during compile | Wait for Unity compilation or asset refresh to finish, then retry. |
| Editor helper fails in Player | Editor helpers require `UnityEditor`; use Runtime helpers in Runtime/Player. |

## Feature Map

| Area | What the agent can do |
|---|---|
| Runtime | Environment state, logs, GameObjects, Components, diagnostics, reflection, object formatting. |
| Editor | Compilation state, selection, menu commands, play mode, screenshots. |
| Assets | Search, read/write text assets, move/copy/delete, dependencies, scripts, materials. |
| Scenes and Prefabs | Open/save scenes, inspect hierarchy, instantiate/create Prefabs, manage overrides. |
| Serialized data | Read/write Inspector serialized fields and arrays. |
| Pipeline | Packages, tests, build settings, build requests. |
| Validation | Missing scripts, missing references, `[SerializeField]` tooltip checks. |
| Custom logic | Generated C# tool modules, JavaScript helpers, or PuerTS C# interop for project-specific APIs. |

## Design Choice

UnityEvalTool exposes one MCP tool:

```text
evalJsCode
```

The agent runs JavaScript inside Unity and imports helper modules from:

```text
tools/index
tools/<name>
```

The MCP tool list stays small and stable while the helper layer covers everyday Unity automation. Built-in helpers are generated from C# classes registered with `[EvalTool(name, description)]`: each generated module exports small JavaScript functions that validate the tool is enabled, call public C# instance methods through PuerTS, and format return values for MCP output. Generated function metadata includes ordered `parameters` and legacy `parameterTypes`. `tools/index.listTools()` intentionally returns concise tool summaries; call `tools/index.getToolDetails('toolName')` or import the concrete module and inspect `functions` before using unfamiliar methods. The module index scans Unity `Resources/tools` helper modules, so projects and packages can add JavaScript helpers without editing this package. For one-off work, agents can also call Unity/C# APIs directly through PuerTS `CS.*`.

### Compared With Multi-Tool Unity MCP Plugins

| Choice | Better for | Tradeoff |
|---|---|---|
| UnityEvalTool | Custom automation, project-specific debugging, Runtime/Player inspection, arbitrary Unity-side JavaScript and PuerTS `CS.*` interop. | Agent must write valid JavaScript; common Editor tasks are less discoverable than a long named-tool list. |
| Multi-tool plugins | Out-of-the-box Editor workflows with a visible tool catalog. | Hard to express custom multi-step workflows when the plugin lacks the exact tool you need. |

### Relationship To PuerTS's Built-In MCP

PuerTS ships its own MCP-related package (`com.tencent.puerts.mcp`). UnityEvalTool is independent: it includes its own MCP server, session tracking, Unity-aware helper modules, safety flags, and Runtime/Player support, and does not require or interfere with the PuerTS MCP package.

## Extending The Package

If a helper does not cover your project, extend the package directly:

1. Add a `.mjs` or `.js` JavaScript helper under `Resources/tools` for runtime-safe helpers, or `Editor/Resources/tools` for Editor-only helpers, when the behavior is just orchestration. Export `description` so the dynamic index can list it, then call `tools/index.refreshTools()` or use the Server Window Tools refresh button.
2. Add or extend a C# class when the operation needs Unity APIs or explicit safety checks. Put built-in runtime tools under `Tools/Runtime` and built-in Editor tools under `Tools/Editor`, keep the namespace `YuzeToolkit`, add `[EvalTool("name", "description")]`, expose operations as public instance methods, and describe each exported method with `[EvalFunction("...")]`; generated `tools/<name>` modules call those public methods directly through PuerTS.
3. Register C# tools with `EvalToolRegistry.Register<TTool>()` or `TryRegister<TTool>()`; `TTool` must be a `class` with a public parameterless constructor. The main runtime and Editor assemblies do not register built-in tools directly: `UnityEvalTool.Tools` registers runtime tools, and `UnityEvalTool.Editor.Tools` registers Editor tools.
4. Implement `IMcpTool` only when generated metadata exists. `IMcpTool` is a code-generation contract with non-null `Name`, `Description`, and `Functions`; registry metadata comes from `IMcpTool` first, and only non-`IMcpTool` classes need `[EvalTool]`.
5. Give public C# tool methods explicit return types instead of hiding the result behind `object`. Prefer primitives, `List<T>`, `Dictionary<string, TValue>`, or data composed from those types; these are the recommended return values because the server can always serialize them as JSON MCP text content.
6. Update [Helper reference](docs/HELPER_MODULES.md) when you add a helper, and [Advanced notes](docs/ADVANCED_USAGE.md) when you add a destructive operation.
7. Read [Project design](docs/PROJECT_DESIGN.md) before changing the server, bridge, session, or helper architecture.

## Documentation

| Document | Purpose |
|---|---|
| [README](README.md) | Install PuerTS, install UnityEvalTool, configure MCP clients, verify the connection. |
| [Helper reference](docs/HELPER_MODULES.md) | Runtime and Editor helper module catalog. |
| [Runtime services](docs/RUNTIME_SERVICES.md) | Start and configure MCP and CLI services from Runtime/Player scripts, including Editor parity limits. |
| [Project design](docs/PROJECT_DESIGN.md) | Architecture, request flow, extension points, lifecycle rules. |
| [Advanced notes](docs/ADVANCED_USAGE.md) | Generated tool calls, PuerTS C# interop, safety flags, Domain Reload. |
| [中文 README](README_zh.md) | Chinese overview and quick start. |

Minimal `evalJsCode` call:

```javascript
async function execute() {
  const index = await import('tools/index');
  return index.description;
}
```

## Status And Guarantee

This project is implemented entirely by AI. It is provided as editable source and a practical reference implementation, not as a guaranteed product.

No guarantee is made for correctness, stability, completeness, security, or production suitability. If a feature is missing or broken, the intended workflow is to let your own AI agent inspect and modify this package for your project.

## License

MIT License. See [LICENSE](LICENSE).
