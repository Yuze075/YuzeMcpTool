# Runtime Services

[README](../README.md) | [中文](RUNTIME_SERVICES_zh.md) | [Project design](PROJECT_DESIGN.md) | [Advanced notes](ADVANCED_USAGE.md)

This page explains how to start and configure UnityEvalTool services from Runtime/Player code.

UnityEvalTool has two independent Runtime services:

| Service | API | Purpose |
|---|---|---|
| MCP server | `McpServer.Shared.Start(...)` or `McpServerBehaviour` | HTTP MCP endpoint that exposes `evalJsCode`. |
| CLI bridge | `CliBridgeServer.Shared.Start(...)` or `tools/cli.startCliBridge(...)` | TCP bridge used by the `unity` CLI. |

Both services run in the Editor and in non-WebGL Player builds. WebGL currently cannot host either listener because it cannot accept inbound TCP/HTTP connections; the WebSocket transports compile but return an unsupported result.

## Start MCP With A Component

Add `YuzeToolkit.McpServerBehaviour` to a scene object when you want scene-owned service lifetime.

Serialized fields:

| Field | Default | Meaning |
|---|---|---|
| `startOnEnable` | `true` | Start when the component is enabled. |
| `host` | `127.0.0.1` | Bind address. Use `0.0.0.0` only for controlled LAN access. |
| `port` | `3100` | HTTP MCP port. |
| `bindLocalhostAliases` | `true` | Bind IPv4 and IPv6 loopback aliases when using loopback. |
| `requireToken` | `false` | Require token auth for MCP requests. |
| `token` | empty | Token to require. Empty generates one when auth is enabled. |

The component starts the shared server with an owner id in `OnEnable` and releases that owner in `OnDisable`. If another owner is still running the server, disabling this component does not stop the shared listener.

## Start MCP From Code

```csharp
using UnityEngine;
using YuzeToolkit;

public sealed class RuntimeMcpStartup : MonoBehaviour
{
    private const string OwnerId = "game-runtime";

    private void Start()
    {
        McpServer.Shared.StartWithOwner(OwnerId, new McpServerOptions
        {
            Host = "127.0.0.1",
            Port = 3100,
            BindLocalhostAliases = true,
            RequireToken = true,
            Token = "replace-with-runtime-token",
            MaxRequestBodyBytes = 1024 * 1024,
            DefaultEvalTimeoutSeconds = 30,
            MaxSessions = 16,
            SessionIdleTimeoutSeconds = 30 * 60
        });

        Debug.Log(McpServer.Shared.State.Endpoint);
    }

    private void OnDestroy()
    {
        McpServer.Shared.StopOwner(OwnerId);
    }
}
```

Use `Start(options)` when you only need one manual owner. Use `StartWithOwner(ownerId, options)` when multiple systems may request the shared server. Runtime option changes do not apply to a running listener; stop and start again to bind a different host, port, or auth mode.

MCP token clients must send one of:

```text
Authorization: Bearer <token>
X-UnityEvalTool-Token: <token>
```

## Start The CLI Bridge From Code

```csharp
using UnityEngine;
using YuzeToolkit;

public sealed class RuntimeCliStartup : MonoBehaviour
{
    private void Start()
    {
        CliBridgeServer.Shared.Start(new CliBridgeOptions
        {
            Host = "127.0.0.1",
            Port = 0,
            RequireToken = true,
            Token = "replace-with-runtime-token",
            MaxRequestBytes = 1024 * 1024,
            DefaultEvalTimeoutSeconds = 30
        });

        var state = CliBridgeServer.Shared.State;
        Debug.Log($"CLI bridge: {state.Endpoint}");
    }

    private void OnDestroy()
    {
        CliBridgeServer.Shared.Stop();
    }
}
```

`Port = 0` asks the OS for a free port. Read the selected port from `CliBridgeServer.Shared.State.Port` and pass it to the CLI:

```bash
unity --host 127.0.0.1 --port <state.Port> --token <state.Token>
```

The bridge can also be started from an existing eval session through the runtime `cli` helper:

```javascript
async function execute() {
  const cli = await import('tools/cli');
  return cli.startCliBridge('127.0.0.1', 0, '', true);
}
```

This is useful when MCP is already connected and you want to open a CLI bridge on demand.

## Runtime Configuration Sources

Player builds do not read Unity Editor `EditorPrefs`. To reproduce Editor settings in Runtime, load your own config and pass the same values into options objects.

Recommended host-project patterns:

| Source | Good for |
|---|---|
| `ScriptableObject` in `Resources` or Addressables | Project defaults that ship with a development build. |
| JSON file under `Application.persistentDataPath` | Local overrides without rebuilding. |
| Command-line arguments | CI, automation, and per-launch port/token choices. |
| Environment variables | Headless or server deployments. |
| Custom bootstrap scene object | Simple scene-specific setup. |

Example config DTO:

```csharp
[System.Serializable]
public sealed class UnityEvalRuntimeServiceConfig
{
    public bool startMcp = true;
    public string mcpHost = "127.0.0.1";
    public int mcpPort = 3100;
    public bool mcpRequireToken = false;
    public string mcpToken = "";

    public bool startCli = false;
    public string cliHost = "127.0.0.1";
    public int cliPort = 0;
    public bool cliRequireToken = true;
    public string cliToken = "";
}
```

Map it directly to `McpServerOptions` and `CliBridgeOptions` during startup. If a token field is empty and token auth is enabled, the service generates a token and exposes it through `State.Token`.

## Can Runtime Match Editor Configuration Exactly?

Runtime can match the service-level network configuration, but it does not automatically inherit Editor-only configuration.

| Area | Runtime parity |
|---|---|
| MCP host/port/token/body limit/eval timeout/session limits | Yes, pass the same values into `McpServerOptions`. |
| CLI host/port/token/body limit/eval timeout | Yes, pass the same values into `CliBridgeOptions`. |
| Runtime C# tools and `Resources/tools` JavaScript helpers | Yes, if the assemblies/assets are included in the Player. |
| Editor-only tools such as assets, scenes, prefabs, importers, pipeline, validation | No. They depend on `UnityEditor` and are not available in Player. |
| Editor window settings and auto-start | No. They are stored in `EditorPrefs`; Player must use its own config source. |
| Editor tool enable/disable persistence | Not automatically. Runtime tool state is in-memory unless your startup code calls `EvalToolRegistry.SetEnabled(...)`. |
| CLI launch/copy command UI | No. Those helpers are Editor-only convenience code. |

To make Runtime tool enable states match Editor, apply them explicitly at startup:

```csharp
EvalToolRegistry.SetEnabled("objects", true);
EvalToolRegistry.SetEnabled("reflection", false);
```

## Security Notes

Both services can execute Unity-side JavaScript. Keep `Host = "127.0.0.1"` unless you intentionally control the network. If you bind `0.0.0.0`, use token auth and restrict the OS firewall. Do not ship these services enabled in production builds unless your project has a deliberate remote-debug policy.

