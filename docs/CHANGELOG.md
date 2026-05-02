# Changelog

此项目的所有显著更改都将记录在此文件中。

该格式基于[Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/),
并且参考[语义版本控制规范](https://semver.org/lang/zh-CN/)

本文件是属于Yuze的更新日志, 如有问题请联系邮箱[925581968@qq.com](mailto:925581968@qq.com)

## [1.0.0] - 2026-5-1

### Added

* 初始版本更新和提交
* 新增基于 `[McpTool]` C# class 和 `Resources/tools` 的动态工具目录，项目或其他包可通过 C# 注册或新增 `.mjs` helper 扩展工具。
* 新增 Server Window 工具管理区，可刷新 JavaScript 工具并持久化启用/禁用状态。

### Changed

* helper import 路径从旧的分层入口调整为 `tools/<name>`，不再需要 `.mjs` 后缀。
* 包内内置 helper 改为 C# 虚拟模块；`Resources/tools` 主要保留给外部 JavaScript 扩展和 Editor 测试 helper。
* C# tool 不再要求继承 `IMcpTool`；运行时注册改为 `[McpTool(name, description)]` + `McpToolRegistry.Register<TTool>() where TTool : class, new()`。
* `IMcpTool` 移除 Editor-only 属性和默认 `Functions` 实现，保留为后续代码生成契约。
* 内置 Runtime tools 移动到 `Tools/Runtime`，内置 Editor tools 移动到 `Tools/Editor`；两个 tool 程序集自行注册自己的工具和 PuerTS binding 配置。
* 主 Runtime 程序集只保留网络、会话、JS loader/catalog/formatter 等核心逻辑；主 Editor 程序集只保留菜单、Server Window 和启动配置。
* 新增 Editor-only JavaScript 测试 helper `tools/editorJsTest`，用于验证 JS helper 发现与执行流程。
* 包内 C# tool 的 public 返回类型改为显式类型，不再用 `object` 隐藏工具协议。
* `evalJsCode` 结果转换移动到 MCP 服务端出口：基础类型、List、Dictionary 统一作为 JSON text content 返回，UnityObject 和 C# 自定义对象由 `McpValueFormatter` 摘要。
* `LitJson` 收敛为 JSON parse/stringify；字典/列表构造和参数读取移动到 `McpData`。
