# Project Design

[README](../README.md) | [中文](PROJECT_DESIGN_zh.md) | [Helper reference](HELPER_MODULES.md) | [Runtime services](RUNTIME_SERVICES.md) | [Advanced notes](ADVANCED_USAGE.md)

[![Audience](https://img.shields.io/badge/Audience-AI%20agents%20%2F%20maintainers-8e44ad)](#who-should-read-this)
[![Tool](https://img.shields.io/badge/MCP%20Tool-evalJsCode-orange)](#tool-surface)
[![Runtime](https://img.shields.io/badge/Runtime-Editor%20%2B%20Player-2ecc71)](#module-map)

This page is for AI agents and maintainers. Most human users only need the README.

## Who Should Read This

Read this page before changing server startup, MCP protocol handling, sessions, C# tools, PuerTS eval, helper modules, or safety rules.

If you only want to install and use the package, read [README](../README.md).

## Design Summary

UnityEvalTool runs a local MCP server and CLI bridge inside Unity. The MCP server exposes one MCP tool, `evalJsCode`. Each MCP client session owns a persistent PuerTS JavaScript VM. The agent submits an `async function execute() { ... }`, imports helper modules from `tools/...` for common workflows, can directly call Unity/C# APIs through PuerTS `CS.*` when needed, and receives a JSON-friendly result.

The same service classes are used by the Editor and by Runtime/Player builds. Editor windows and auto-start use `EditorPrefs`; Runtime/Player hosts must pass their own values into `McpServerOptions`, `McpServerBehaviour`, and `CliBridgeOptions`. See [Runtime services](RUNTIME_SERVICES.md) for startup code and parity limits.

The package is designed around a small stable MCP surface and a scriptable Unity-side runtime:

| Goal | Design choice |
|---|---|
| Keep MCP client integration simple | Expose only `evalJsCode`. |
| Let AI handle project-specific workflows | Run custom JavaScript and direct Unity/C# interop inside Unity through PuerTS. |
| Keep common Unity tasks discoverable | Provide generated `tools/<name>` modules from registered C# classes that either implement `IMcpTool` or use `[EvalTool]`, plus optional JavaScript Resources tools. |
| Keep dangerous operations explicit | Require `confirm: true` or `confirmDangerous: true` where documented. |
| Support Editor and Runtime/Player use | Runtime commands avoid `UnityEditor`; Editor commands are registered only in the Editor. |

## Module Map

| Area | Main files | Responsibility |
|---|---|---|
| Server | `Runtime/MCP/McpServer.cs`, `McpServerOptions.cs`, `McpServerBehaviour.cs`, `McpServerState.cs` | `/mcp`, `/health`, JSON-RPC, session lifecycle, active session snapshots, component-owned startup. |
| MCP transport | `Runtime/MCP/IMcpTransport.cs`, `TcpMcpTransport.cs`, `WebSocketMcpTransport.cs` | Platform-specific listener implementations normalized into server request/response objects. |
| CLI bridge | `Runtime/CLI/CliBridgeServer.cs`, `CliBridgeTool.cs`, `ICliBridgeTransport.cs`, `TcpCliBridgeTransport.cs` | `unity` long connection or one-shot command entry, CLI-specific catalog, and eval forwarding. |
| Eval | `Runtime/Core/Eval/EvalExecutor.cs`, `EvalVmSession.cs`, `EvalScriptLoader.cs`, `EvalToolCatalog.cs` | MCP tool definition, busy-state guard, per-session PuerTS VM, virtual module loading, and dynamic tool catalog. |
| Bridge | `Runtime/Core/Bridge/*.cs` | `EvalToolAttribute`, code-generation `IMcpTool` contract, tool registry, descriptors, and value formatting. |
| Runtime tools | `Tools/Runtime/*.cs` | Runtime-safe Unity state, logs, GameObjects, Components, diagnostics, and reflection. This assembly registers its own tools. |
| PuerTS bindings | `Editor/PuertsCfg.cs`, `Tools/Editor/PuertsToolsCfg.cs` | Editor-only PuerTS binding and typing configuration for core and tool types. |
| Editor tools | `Tools/Editor/*.cs` | Editor-only operations for assets, scenes, prefabs, importers, serialized data, tests, builds, validation. This assembly registers its own tools. |
| Editor startup | `Editor/MCP/McpEditorBootstrap.cs`, `Editor/CLI/CliBridgeEditorBootstrap.cs`, `Editor/Window/UnityEvalToolWindow.cs` | Startup preferences, one menu item, unified service window, connection views, and tool toggle UI. |
| Tool modules | `tools/index`, `tools/<name>`, `Resources/tools/<name>` | Agent-facing helper API. Built-ins are generated from C# tools; extension JavaScript modules are loaded from Resources. |
| Docs | `README*.md`, `docs/*.md` | Human quick start, MCP client configuration, helper catalog, design, advanced rules. |

## Request Flow

1. Unity loads the package.
2. Runtime and Editor tool assemblies register their own C# tools with `EvalToolRegistry.Register<TTool>()` / `TryRegister<TTool>()`. The main runtime assembly does not reference built-in tool types, and the main Editor assembly only applies persisted tool states and starts the server when requested.
3. `McpServer` selects transport by platform: non-WebGL builds use the `TcpListener` transport, and WebGL uses the WebSocket placeholder transport that returns a clear unsupported result.
4. The MCP client sends `initialize` to `http://127.0.0.1:3100/mcp`. If Host is `0.0.0.0`, the client must use the Unity device's actual LAN IP.
5. `McpServer` creates a session and returns the `Mcp-Session-Id` response header.
6. The client calls `tools/list`; the server returns only `evalJsCode`.
7. The client calls `tools/call` with `evalJsCode`.
8. `EvalExecutor` checks Unity busy state, creates or reuses the session PuerTS VM, and executes the submitted `execute()` function.
9. JavaScript imports generated or resource-backed tool modules.
10. Generated C# modules expose semantic functions plus `isEnabled()`. Semantic calls validate the tool is enabled, call public C# instance methods through PuerTS, and format return values into JSON-friendly data. If no helper covers the job, JavaScript may directly use PuerTS `CS.*` interop in the same VM.
11. The eval result is serialized into MCP text/image content and returned to the client.

## Runtime Service Configuration

Runtime service configuration is explicit. `McpServerOptions` controls MCP host, port, localhost alias binding, token auth, max request body size, eval timeout, max sessions, and idle session timeout. `CliBridgeOptions` controls CLI host, port, token auth, max request size, and eval timeout. If token auth is enabled and the token is empty, the service generates a token on start and exposes it through the service state.

Editor configuration persistence is intentionally Editor-only:

| Editor feature | Runtime equivalent |
|---|---|
| MCP window fields stored in `EditorPrefs` | Load project-owned config and pass it into `McpServerOptions` or `McpServerBehaviour`. |
| CLI window fields stored in `EditorPrefs` | Load project-owned config and pass it into `CliBridgeOptions`. |
| Editor auto-start after domain reload | Add a Runtime bootstrap component or startup system. |
| Tool enable states persisted by `McpToolEditorSettings` | Call `EvalToolRegistry.SetEnabled(...)` from Runtime startup if you need non-default states. |

Runtime can match Editor network settings exactly when the same values are supplied, but it cannot expose Editor-only tools or Editor window workflows in Player because those depend on `UnityEditor`.

## Tool Surface

The public MCP surface is intentionally small:

```text
evalJsCode
```

The real working surface is the helper module layer:

```text
tools/index
tools/<name>
```

Agents should start with `tools/index`, read the target module's `description`, then call helper functions for common workflows. The index combines registered C# tools and Unity `Resources/tools` JavaScript helpers, so tools from this package, the project, and other packages are discovered together. Generated C# modules use positional arguments, expose `functions[].description`, ordered `functions[].parameters`, legacy `functions[].parameterTypes`, expose `isEnabled()` for live state, validate enabled state on each call, and call public C# instance methods through PuerTS. Direct `CS.*` interop is also supported for one-off or project-specific logic; repeated logic should be promoted into a helper. After JavaScript returns, `EvalExecutor` formats the final result into MCP content at the server boundary.

## Result Handling

Public C# tool methods should use explicit return types instead of hiding the shape behind `object`. Prefer primitives, `List<T>`, `IEnumerable<T>`, `Dictionary<string, TValue>`, or data composed from those types; `EvalValueFormatter` serializes these values to JSON text content, making them the most stable and recommended result shape for clients and agents.

Returning `UnityEngine.Object` is supported, but should not be the main DTO shape. The server summarizes it into a compact object: common Unity objects include `name`, `type`, and `instanceId`; asset objects also include asset path and guid; `GameObject` includes hierarchy path, scene, transform, and components; `Component` includes the component summary and its owning GameObject summary.

Custom C# objects are a fallback path. The server returns `type`, `string`, and public instance members. Fields and properties are recursively converted within the depth limit; cycles are marked, and unreadable members are marked as unreadable. For a stable protocol, return explicit DTO dictionaries or lists instead of relying on reflection over custom objects.

`LitJson` only owns JSON parse/stringify. Dictionary/list construction and parameter reads live in `EvalData`, while final MCP output formatting lives in `EvalValueFormatter`.

## Extension Points

| Need | Recommended extension |
|---|---|
| Compose existing tools in JavaScript | Add a `.mjs` or `.js` runtime-safe helper in `Resources/tools`, or an Editor-only helper in `Editor/Resources/tools`; export `description` for index discovery. |
| Expose a new Unity operation from C# | Add a class in `Tools/Runtime` or `Tools/Editor`, expose public instance methods, and annotate exported methods with `[EvalFunction("...")]`. Ordinary classes must use `[EvalTool("name", "description")]`; generated classes can implement `IMcpTool` instead, in which case `Name`, `Description`, and `Functions` provide all registry metadata and `Functions` must be non-null. |
| Register runtime-safe C# tool | Register it from the runtime tool assembly bootstrap with `EvalToolRegistry.TryRegister<TTool>()`; `TTool` must be `class, new()`. |
| Register Editor-only C# tool | Register it from the Editor tool assembly bootstrap with `EvalToolRegistry.TryRegister<TTool>()`; Editor availability comes from the Editor-only assembly, not a tool property. |
| Add project-specific static API access | Prefer `tools/reflection` first; add a command only when reflection is not enough. |
| Add docs for a new helper or tool | Update `HELPER_MODULES.md` and `ADVANCED_USAGE.md` when safety or lifecycle rules matter. |

Keep extension names explicit and stable. If an operation can delete, move, overwrite, build, install packages, invoke non-public code, or change project state broadly, require an explicit confirmation argument.

## Safety And Lifecycle Rules

| Rule | Why it matters |
|---|---|
| Do not trigger compilation or AssetDatabase refresh and then wait in the same eval call. | Domain Reload can destroy the VM/session. |
| Eval execution is globally serialized. | Unity API and PuerTS ticks are main-thread-sensitive; multiple sessions keep separate VMs but eval calls are processed one at a time. |
| Retry after busy-state errors. | The server rejects eval while Unity is compiling, updating assets, or switching play mode. |
| Prefer data composed from primitives, lists, and dictionaries. | This is the stable protocol shape that the server can deterministically serialize as JSON. |
| Keep file IO inside the Unity project root. | MCP tool calls are intended for project-local automation. |
| Use `confirm: true` for destructive operations. | It makes accidental delete/move/build/package operations harder. |
| Use `confirmDangerous: true` for non-public reflection or dangerous access. | It separates ordinary inspection from high-risk operations. |

## Reader Map

| Reader | Start here | Covers |
|---|---|---|
| Human user | `README.md` / `README_zh.md` | What the tool does, install flow, endpoint, client config. |
| AI agent using the tool | `HELPER_MODULES.md` / `HELPER_MODULES_zh.md` | Helper modules, common workflows, safety choices. |
| Runtime host integrator | `RUNTIME_SERVICES.md` / `RUNTIME_SERVICES_zh.md` | Starting MCP/CLI from scripts and matching Editor service settings. |
| AI agent changing internals | This page and `ADVANCED_USAGE.md` | Request flow, module boundaries, bridge calls, PuerTS interop, lifecycle risks. |

## Known Tradeoffs

- The single-tool model is flexible but less immediately discoverable than plugins that expose many named tools.
- Agents need to write valid JavaScript. Weak JavaScript generation can fail even when the Unity-side API is correct.
- Direct JavaScript runs in PuerTS, not Node.js — Node-style modules (`fs`, `path`, etc.) are not available.
- Runtime/Player tool enabled states are currently in-memory unless a host project applies its own startup configuration; Editor toggles are persisted through `EditorPrefs`.
- Runtime-safe commands work in Runtime/Player; Editor helper modules require the Unity Editor.
- WebGL cannot listen for inbound TCP/HTTP connections; the WebSocket transport currently compiles and fails clearly instead of running a real relay-backed connection.
- `0.0.0.0` exposes the service to the LAN. Token auth exists for MCP and CLI, but it is still a powerful debug surface that can execute Unity-side JavaScript; use it only on controlled networks.
- A blocked Unity main thread cannot always be interrupted safely by the eval timeout.
