# Project Design

[README](../README.md) | [中文](PROJECT_DESIGN_zh.md) | [Helper reference](HELPER_MODULES.md) | [Advanced notes](ADVANCED_USAGE.md)

[![Audience](https://img.shields.io/badge/Audience-AI%20agents%20%2F%20maintainers-8e44ad)](#who-should-read-this)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](#tool-surface)
[![Runtime](https://img.shields.io/badge/Runtime-Editor%20%2B%20Player-2ecc71)](#module-map)

This page is for AI agents and maintainers. Most human users only need the README.

## Who Should Read This

Read this page before changing server startup, MCP protocol handling, sessions, bridge commands, PuerTS eval, helper modules, or safety rules.

If you only want to install and use the package, read [README](../README.md).

## Design Summary

YuzeMcpTool runs a local MCP server inside Unity. The server exposes one MCP tool, `evalJsCode`. Each MCP client session owns a persistent PuerTS JavaScript VM. The agent submits an `async function execute() { ... }`, imports helper modules from `YuzeToolkit/mcp/...`, and receives a JSON-friendly result.

The package is designed around a small stable MCP surface and a scriptable Unity-side runtime:

| Goal | Design choice |
|---|---|
| Keep MCP client integration simple | Expose only `evalJsCode`. |
| Let AI handle project-specific workflows | Run custom JavaScript inside Unity through PuerTS. |
| Keep common Unity tasks discoverable | Provide helper modules under `YuzeToolkit/mcp/Runtime` and `YuzeToolkit/mcp/Editor`. |
| Keep dangerous operations explicit | Require `confirm: true` or `confirmDangerous: true` where documented. |
| Support Editor and Runtime/Player use | Runtime commands avoid `UnityEditor`; Editor commands are registered only in the Editor. |

## Module Map

| Area | Main files | Responsibility |
|---|---|---|
| Server | `Runtime/Server/McpServer.cs`, `McpServerOptions.cs`, `McpSession*.cs` | HTTP listener, `/mcp`, `/health`, JSON-RPC, session lifecycle, active session snapshots. |
| Eval | `Runtime/Eval/EvalJsCodeTool.cs`, `PuerTsEvalSession.cs`, `McpScriptLoader.cs` | MCP tool definition, busy-state guard, per-session PuerTS VM, module loading from package Resources. |
| Bridge | `Runtime/Bridge/*.cs` | C# command interface, command registry, command context, sync/async invocation from JavaScript. |
| Runtime commands | `Runtime/Commands/*.cs` | Runtime-safe Unity state, logs, GameObjects, Components, diagnostics, reflection, batching. |
| Editor commands | `Editor/Commands/*.cs` | Editor-only operations for assets, scenes, prefabs, importers, serialized data, tests, builds, validation. |
| Editor startup | `Editor/McpEditorBootstrap.cs`, `Editor/McpServerWindow.cs` | Auto-start, menu items, server monitor window, Editor command registration. |
| Helper modules | `Resources/YuzeToolkit/mcp/**/*.mjs` | Agent-facing JavaScript helper API layered over bridge commands. |
| Docs | `README*.md`, `docs/*.md` | Human quick start, MCP client configuration, helper catalog, design, advanced rules. |

## Request Flow

1. Unity loads the package.
2. In the Editor, `McpEditorBootstrap` registers runtime and Editor commands, then starts the server if auto-start is enabled.
3. The MCP client sends `initialize` to `http://127.0.0.1:3100/mcp`.
4. `McpServer` creates a session and returns the `Mcp-Session-Id` response header.
5. The client calls `tools/list`; the server returns only `evalJsCode`.
6. The client calls `tools/call` with `evalJsCode`.
7. `EvalJsCodeTool` checks Unity busy state, creates or reuses the session PuerTS VM, and executes the submitted `execute()` function.
8. JavaScript imports helper modules or calls `Mcp.invoke(...)` / `Mcp.invokeAsync(...)`.
9. Bridge commands run on the Unity side and return JSON text.
10. The eval result is serialized into MCP text/image content and returned to the client.

## Tool Surface

The public MCP surface is intentionally small:

```text
evalJsCode
```

The real working surface is the helper module layer:

```text
YuzeToolkit/mcp/index.mjs
YuzeToolkit/mcp/Runtime/*.mjs
YuzeToolkit/mcp/Editor/*.mjs
```

Agents should start with `index.mjs`, read the target module's `description`, then call helper functions. Direct bridge calls are allowed, but helper modules are preferred because they hide command argument details and document safety requirements.

## Extension Points

| Need | Recommended extension |
|---|---|
| Compose existing commands in a cleaner way | Add a JavaScript helper in `Resources/YuzeToolkit/mcp/Runtime` or `Resources/YuzeToolkit/mcp/Editor`. |
| Expose a new Unity operation | Add an `IMcpCommand` implementation in `Runtime/Commands` or `Editor/Commands`. |
| Register runtime-safe command | Register it in `McpCommandRegistry.EnsureDefaultCommands()`. |
| Register Editor-only command | Register it from `McpEditorBootstrap.RegisterEditorCommands()`. |
| Add project-specific static API access | Prefer `Runtime/reflection.mjs` first; add a command only when reflection is not enough. |
| Add docs for a new helper or command | Update `HELPER_MODULES.md` and `ADVANCED_USAGE.md` when safety or lifecycle rules matter. |

Keep extension names explicit and stable. If an operation can delete, move, overwrite, build, install packages, invoke non-public code, or change project state broadly, require an explicit confirmation argument.

## Safety And Lifecycle Rules

| Rule | Why it matters |
|---|---|
| Do not trigger compilation or AssetDatabase refresh and then wait in the same eval call. | Domain Reload can destroy the VM/session. |
| Retry after busy-state errors. | The server rejects eval while Unity is compiling, updating assets, or switching play mode. |
| Return plain serializable values. | Raw `UnityEngine.Object` graphs do not serialize usefully. |
| Keep file IO inside the Unity project root. | Bridge commands are intended for project-local automation. |
| Use `confirm: true` for destructive operations. | It makes accidental delete/move/build/package operations harder. |
| Use `confirmDangerous: true` for non-public reflection or dangerous access. | It separates ordinary inspection from high-risk operations. |

## Reader Map

| Reader | Start here | Covers |
|---|---|---|
| Human user | `README.md` / `README_zh.md` | What the tool does, install flow, endpoint, client config. |
| AI agent using the tool | `HELPER_MODULES.md` / `HELPER_MODULES_zh.md` | Helper modules, common workflows, safety choices. |
| AI agent changing internals | This page and `ADVANCED_USAGE.md` | Request flow, module boundaries, bridge calls, PuerTS interop, lifecycle risks. |

## Known Tradeoffs

- The single-tool model is flexible but less immediately discoverable than plugins that expose many named tools.
- Agents need to write valid JavaScript. Weak JavaScript generation can fail even when the Unity-side API is correct.
- Direct JavaScript runs in PuerTS, not Node.js — Node-style modules (`fs`, `path`, etc.) are not available.
- Runtime-safe commands work in Runtime/Player; Editor helper modules require the Unity Editor.
- A blocked Unity main thread cannot always be interrupted safely by the eval timeout.
