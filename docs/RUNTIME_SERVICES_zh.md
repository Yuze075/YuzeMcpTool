# Runtime 服务

[README](../README_zh.md) | [English](RUNTIME_SERVICES.md) | [项目设计](PROJECT_DESIGN_zh.md) | [高级说明](ADVANCED_USAGE_zh.md)

本文说明如何在 Runtime/Player 里用脚本启动和配置 UnityEvalTool 服务。

UnityEvalTool 有两个互相独立的 Runtime 服务：

| 服务 | API | 用途 |
|---|---|---|
| MCP server | `McpServer.Shared.Start(...)` 或 `McpServerBehaviour` | HTTP MCP endpoint，暴露 `evalJsCode`。 |
| CLI bridge | `CliBridgeServer.Shared.Start(...)` 或 `tools/cli.startCliBridge(...)` | 给 `unity` CLI 使用的 TCP bridge。 |

两个服务都能在 Editor 和非 WebGL Player 中运行。WebGL 目前不能承载这两个监听服务，因为 WebGL 不能接受入站 TCP/HTTP 连接；WebSocket transport 只保证可编译并返回明确的不支持结果。

## 用组件启动 MCP

如果希望服务生命周期跟随场景对象，可以把 `YuzeToolkit.McpServerBehaviour` 挂到场景中的 GameObject 上。

序列化字段：

| 字段 | 默认值 | 含义 |
|---|---|---|
| `startOnEnable` | `true` | 组件启用时自动启动。 |
| `host` | `127.0.0.1` | 监听地址。只有受控局域网调试时才用 `0.0.0.0`。 |
| `port` | `3100` | HTTP MCP 端口。 |
| `bindLocalhostAliases` | `true` | loopback host 下同时绑定 IPv4/IPv6 loopback。 |
| `requireToken` | `false` | MCP 请求是否必须带 token。 |
| `token` | 空 | 鉴权 token；开启鉴权且为空时启动时自动生成。 |

组件在 `OnEnable` 中用 owner id 启动 shared server，在 `OnDisable` 中释放 owner。若还有其他 owner 要求服务运行，禁用该组件不会停止 shared listener。

## 用代码启动 MCP

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

只有一个手动启动方时可以用 `Start(options)`。如果多个系统都可能要求服务保持运行，用 `StartWithOwner(ownerId, options)` 更清楚。服务已经运行后，Runtime option 变更不会自动应用到 listener；要换 host、port 或鉴权模式，需要先 stop 再 start。

MCP token 客户端必须发送以下任意一种：

```text
Authorization: Bearer <token>
X-UnityEvalTool-Token: <token>
```

## 用代码启动 CLI Bridge

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

`Port = 0` 表示让操作系统自动选择可用端口。启动后从 `CliBridgeServer.Shared.State.Port` 读取实际端口，再传给 CLI：

```bash
unity --host 127.0.0.1 --port <state.Port> --token <state.Token>
```

已有 MCP eval session 时，也可以通过 runtime `cli` helper 按需启动 bridge：

```javascript
async function execute() {
  const cli = await import('tools/cli');
  return cli.startCliBridge('127.0.0.1', 0, '', true);
}
```

这适合先通过 MCP 连上 Unity，再临时打开 CLI bridge。

## Runtime 配置来源

Player 不会读取 Unity Editor 的 `EditorPrefs`。如果要在 Runtime 复现 Editor 设置，需要自己加载配置，并把同样的值传进 options 对象。

推荐宿主项目使用这些来源：

| 来源 | 适合场景 |
|---|---|
| `Resources` 或 Addressables 中的 `ScriptableObject` | 随开发构建发布的项目默认值。 |
| `Application.persistentDataPath` 下的 JSON | 不重新打包也能覆盖本机设置。 |
| 命令行参数 | CI、自动化、每次启动指定端口/token。 |
| 环境变量 | Headless 或服务器部署。 |
| 自定义启动场景对象 | 简单的场景级调试配置。 |

示例配置 DTO：

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

启动时把它直接映射到 `McpServerOptions` 和 `CliBridgeOptions`。如果 token 字段为空且开启了 token 鉴权，服务会自动生成 token，并通过 `State.Token` 暴露出来。

## Runtime 能否和 Editor 配置完全一致

Runtime 能做到服务级网络配置一致，但不会自动继承 Editor-only 配置。

| 内容 | Runtime 一致性 |
|---|---|
| MCP host/port/token/body limit/eval timeout/session limits | 可以。把同样的值传入 `McpServerOptions`。 |
| CLI host/port/token/body limit/eval timeout | 可以。把同样的值传入 `CliBridgeOptions`。 |
| Runtime C# tools 和 `Resources/tools` JavaScript helpers | 可以，前提是相关程序集/资源被打进 Player。 |
| assets、scenes、prefabs、importers、pipeline、validation 等 Editor-only tools | 不可以。它们依赖 `UnityEditor`，Player 中不存在。 |
| Editor window 设置和自动启动 | 不会自动一致。它们存于 `EditorPrefs`，Player 要用自己的配置来源。 |
| Editor 里的 tool 启用/禁用持久化 | 不会自动一致。Runtime tool state 是内存级，除非启动脚本调用 `EvalToolRegistry.SetEnabled(...)`。 |
| CLI 启动/复制命令 UI | 不可以。这些只是 Editor-only 便利功能。 |

如果要让 Runtime 工具启用状态匹配 Editor，需要在启动时显式应用：

```csharp
EvalToolRegistry.SetEnabled("objects", true);
EvalToolRegistry.SetEnabled("reflection", false);
```

## 安全说明

两个服务都能执行 Unity 内 JavaScript。除非你明确控制网络环境，否则保持 `Host = "127.0.0.1"`。如果绑定 `0.0.0.0`，要开启 token 鉴权并限制系统防火墙。除非项目有明确的远程调试策略，不要在生产构建中默认启用这些服务。

