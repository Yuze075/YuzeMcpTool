#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace YuzeToolkit
{
    internal sealed class UnityEvalToolWindow : EditorWindow
    {
        private const int RefreshIntervalMilliseconds = 500;
        private const int LabelWidth = 150;
        private const string ToolExpandedPrefPrefix = nameof(YuzeToolkit) + ".UnityEvalToolWindow.ToolExpanded.";
        private const string McpPrefKeyAutoStart = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".AutoStart";
        private const string McpPrefKeyHost = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Host";
        private const string McpPrefKeyPort = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Port";
        private const string McpPrefKeyBindLocalhostAliases = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".BindLocalhostAliases";
        private const string McpPrefKeyRequireToken = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".RequireToken";
        private const string McpPrefKeyToken = nameof(YuzeToolkit) + "." + nameof(McpServer) + ".Token";
        private const string ToolEnabledPrefPrefix = nameof(YuzeToolkit) + ".McpTool.Enabled.";
        private static readonly Color RunningColor = new(0.2f, 0.65f, 0.32f);
        private static readonly Color StoppedColor = new(0.78f, 0.25f, 0.2f);
        private static readonly Color WarningColor = new(0.85f, 0.58f, 0.18f);
        private static readonly Color ErrorColor = new(0.78f, 0.22f, 0.18f);
        private static readonly Color CardBorderColor = new(0.24f, 0.24f, 0.24f);
        private static readonly Color CardBackgroundColor = new(0.16f, 0.16f, 0.16f, 0.28f);
        private static readonly Color ActiveTabColor = new(0.12f, 0.36f, 0.82f, 1f);
        private static readonly Color InactiveTabColor = new(0.12f, 0.12f, 0.12f, 0.3f);
        private static readonly Color EnabledButtonColor = new(0.12f, 0.36f, 0.82f, 1f);
        private static readonly Color DisabledButtonColor = new(0.36f, 0.36f, 0.36f, 0.95f);

        private Button _startButton = null!;
        private Button _stopButton = null!;
        private Button _copyEndpointButton = null!;
        private Button _saveSettingsButton = null!;
        private Button _serverTabButton = null!;
        private Button _toolsTabButton = null!;
        private Button _conversationsTabButton = null!;
        private Button _cliServiceTabButton = null!;
        private Button _cliConsolesTabButton = null!;
        private Label _statusValue = null!;
        private Label _environmentValue = null!;
        private Label _endpointValue = null!;
        private Label _authValue = null!;
        private Label _activeConversationsValue = null!;
        private Label _startedValue = null!;
        private Label _uptimeValue = null!;
        private TextField _hostField = null!;
        private IntegerField _portField = null!;
        private Toggle _bindLocalhostAliasesToggle = null!;
        private Toggle _requireTokenToggle = null!;
        private TextField _tokenField = null!;
        private VisualElement _serverRoot = null!;
        private VisualElement _serverMessages = null!;
        private VisualElement _sessionsRoot = null!;
        private VisualElement _toolsRoot = null!;
        private VisualElement _cliRoot = null!;
        private VisualElement _cliConsolesRoot = null!;
        private Label _cliStatusValue = null!;
        private Label _cliEndpointValue = null!;
        private Label _cliTransportValue = null!;
        private Label _cliAuthValue = null!;
        private Label _cliActiveSessionsValue = null!;
        private TextField _cliHostField = null!;
        private IntegerField _cliPortField = null!;
        private Toggle _cliRequireTokenToggle = null!;
        private TextField _cliTokenField = null!;
        private Button _cliSaveSettingsButton = null!;
        private Button _cliStartButton = null!;
        private Button _cliStopButton = null!;
        private Button _cliCopyCommandButton = null!;
        private Button _cliLaunchButton = null!;
        private VisualElement _cliMessages = null!;
        private string _currentEndpoint = string.Empty;
        private WindowTab _activeTab = WindowTab.Tools;
        private readonly HashSet<string> _expandedTools = new(StringComparer.Ordinal);

        private enum WindowTab
        {
            Tools,
            McpService,
            McpConversations,
            CliService,
            CliConsoles
        }

        [MenuItem("UnityEvalTool/Window")]
        public static void Open()
        {
            var window = GetWindow<UnityEvalToolWindow>("UnityEvalTool");
            window.minSize = new Vector2(720, 480);
            window.Show();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexDirection = FlexDirection.Column;

            root.Add(BuildToolbar());
            root.Add(BuildTabBar());

            var scrollView = new ScrollView
            {
                mode = ScrollViewMode.Vertical,
            };
            scrollView.style.flexGrow = 1;
            scrollView.style.paddingLeft = 8;
            scrollView.style.paddingRight = 8;
            scrollView.style.paddingTop = 8;
            scrollView.style.paddingBottom = 8;
            root.Add(scrollView);

            _serverRoot = new VisualElement();
            _toolsRoot = new VisualElement();
            _sessionsRoot = new VisualElement();
            _cliRoot = new VisualElement();
            _cliConsolesRoot = new VisualElement();
            scrollView.Add(_serverRoot);
            scrollView.Add(_toolsRoot);
            scrollView.Add(_sessionsRoot);
            scrollView.Add(_cliRoot);
            scrollView.Add(_cliConsolesRoot);

            BuildServerTab(_serverRoot);
            BuildCliTab(_cliRoot);
            SetActiveTab(_activeTab);

            RefreshView();
            root.schedule.Execute(RefreshView).Every(RefreshIntervalMilliseconds);
        }

        private void BuildServerTab(VisualElement parent)
        {
            var settingsCard = CreateCard();
            parent.Add(settingsCard);

            var settingsTitle = new Label("Settings");
            settingsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            settingsTitle.style.marginBottom = 6;
            settingsCard.Add(settingsTitle);

            var options = LoadMcpOptions();
            _hostField = CreateTextField("Host", "Host address to bind.", options.Host);
            _portField = CreateIntegerField("Port", "Local TCP port for the MCP endpoint.", options.Port);
            _bindLocalhostAliasesToggle = CreateToggle("Bind Localhost Aliases", "Bind both IPv4 and IPv6 loopback aliases when using localhost.", options.BindLocalhostAliases);
            _requireTokenToggle = CreateToggle("Require Token", "Require Authorization: Bearer or X-UnityEvalTool-Token on MCP requests.", options.RequireToken);
            _tokenField = CreateTextField("Token", "Token required when MCP token auth is enabled. Leave empty to generate one on start.", options.Token);
            settingsCard.Add(_hostField);
            settingsCard.Add(_portField);
            settingsCard.Add(_bindLocalhostAliasesToggle);
            settingsCard.Add(_requireTokenToggle);
            settingsCard.Add(_tokenField);

            _saveSettingsButton = new Button(SaveSettings)
            {
                text = "Save Settings",
                tooltip = "Save MCP server settings. Restart the server to apply changes when it is already running.",
            };
            _saveSettingsButton.style.width = 120;
            _saveSettingsButton.style.marginTop = 4;
            settingsCard.Add(_saveSettingsButton);

            var serverCard = CreateCard();
            parent.Add(serverCard);

            _statusValue = AddField(serverCard, "Status");
            _environmentValue = AddField(serverCard, "Environment");
            _endpointValue = AddField(serverCard, "Endpoint");
            _authValue = AddField(serverCard, "Auth");
            _activeConversationsValue = AddField(serverCard, "Active Conversations");
            _startedValue = AddField(serverCard, "Started");
            _uptimeValue = AddField(serverCard, "Uptime");

            _copyEndpointButton = new Button(CopyEndpoint)
            {
                text = "Copy Endpoint",
                tooltip = "Copy the current MCP endpoint to the clipboard.",
            };
            _copyEndpointButton.style.width = 130;
            _copyEndpointButton.style.marginTop = 4;
            serverCard.Add(_copyEndpointButton);

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.marginTop = 6;
            serverCard.Add(controls);

            _startButton = CreateToolbarButton("Start", "Start the MCP server.", () =>
            {
                SaveSettings();
                StartMcpServer();
                LoadMcpOptionsIntoFields();
                RefreshView();
            });
            controls.Add(_startButton);

            _stopButton = CreateToolbarButton("Stop", "Stop the MCP server and clear active sessions.", () =>
            {
                StopMcpServer();
                RefreshView();
            });
            controls.Add(_stopButton);

            controls.Add(CreateToolbarButton("Refresh", "Refresh the displayed MCP state.", RefreshView));

            _serverMessages = new VisualElement();
            _serverMessages.style.marginTop = 6;
            serverCard.Add(_serverMessages);
        }

        private void BuildCliTab(VisualElement parent)
        {
            var settingsCard = CreateCard();
            parent.Add(settingsCard);

            var settingsTitle = new Label("CLI Settings");
            settingsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            settingsTitle.style.marginBottom = 6;
            settingsCard.Add(settingsTitle);

            var options = CliBridgeEditorBootstrap.LoadOptions();
            _cliHostField = CreateTextField("Host", "Host address to bind.", options.Host);
            _cliPortField = CreateIntegerField("Port", "Local TCP port. Use 0 to select an available port.", options.Port);
            _cliRequireTokenToggle = CreateToggle("Require Token", "Require a matching token during CLI hello.", options.RequireToken);
            _cliTokenField = CreateTextField("Token", "Token required by CLI clients. Leave empty to generate one on start.", options.Token);
            settingsCard.Add(_cliHostField);
            settingsCard.Add(_cliPortField);
            settingsCard.Add(_cliRequireTokenToggle);
            settingsCard.Add(_cliTokenField);

            _cliSaveSettingsButton = new Button(SaveCliSettings)
            {
                text = "Save Settings",
                tooltip = "Save CLI bridge settings. Restart the bridge to apply changes when it is already running.",
            };
            _cliSaveSettingsButton.style.width = 120;
            _cliSaveSettingsButton.style.marginTop = 4;
            settingsCard.Add(_cliSaveSettingsButton);

            var serviceCard = CreateCard();
            parent.Add(serviceCard);

            _cliStatusValue = AddField(serviceCard, "Status");
            _cliEndpointValue = AddField(serviceCard, "Endpoint");
            _cliTransportValue = AddField(serviceCard, "Transport");
            _cliAuthValue = AddField(serviceCard, "Auth");
            _cliActiveSessionsValue = AddField(serviceCard, "Active Sessions");

            var controls = new VisualElement();
            controls.style.flexDirection = FlexDirection.Row;
            controls.style.marginTop = 6;
            serviceCard.Add(controls);

            _cliStartButton = CreateToolbarButton("Start", "Start the CLI bridge.", () =>
            {
                SaveCliSettings();
                CliBridgeEditorBootstrap.StartBridge();
                LoadCliOptions();
                RefreshView();
            });
            controls.Add(_cliStartButton);

            _cliStopButton = CreateToolbarButton("Stop", "Stop the CLI bridge and close active CLI connections.", () =>
            {
                CliBridgeEditorBootstrap.StopBridge();
                RefreshView();
            });
            controls.Add(_cliStopButton);

            _cliCopyCommandButton = CreateToolbarButton("Copy Command", "Copy the CLI connect command.", () =>
            {
                SaveCliSettings();
                CliBridgeEditorBootstrap.CopyConnectCommand();
                LoadCliOptions();
                RefreshView();
            }, 120);
            controls.Add(_cliCopyCommandButton);

            _cliLaunchButton = CreateToolbarButton("Launch CLI", "Start the bridge if needed, then launch the local unity CLI executable.", () =>
            {
                SaveCliSettings();
                CliBridgeEditorBootstrap.LaunchCli();
                LoadCliOptions();
                RefreshView();
            }, 100);
            controls.Add(_cliLaunchButton);

            controls.Add(CreateToolbarButton("Refresh", "Refresh the displayed CLI bridge state.", RefreshView));

            _cliMessages = new VisualElement();
            _cliMessages.style.marginTop = 6;
            serviceCard.Add(_cliMessages);
        }

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.minHeight = 30;
            toolbar.style.paddingLeft = 8;
            toolbar.style.paddingRight = 8;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderBottomColor = CardBorderColor;

            var title = new Label("UnityEvalTool");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            toolbar.Add(title);

            toolbar.Add(CreateToolbarButton("Refresh", "Refresh the displayed MCP state.", RefreshView));
            toolbar.Add(CreateToolbarButton("Reload Tools", "Refresh JavaScript MCP tools from Resources/tools.", RefreshTools, 100));
            return toolbar;
        }

        private VisualElement BuildTabBar()
        {
            var tabBar = new VisualElement();
            tabBar.style.flexDirection = FlexDirection.Row;
            tabBar.style.paddingLeft = 8;
            tabBar.style.paddingRight = 8;
            tabBar.style.paddingTop = 6;
            tabBar.style.paddingBottom = 6;
            tabBar.style.borderBottomWidth = 1;
            tabBar.style.borderBottomColor = CardBorderColor;

            _toolsTabButton = CreateTabButton("Tools", WindowTab.Tools);
            _serverTabButton = CreateTabButton("MCP Service", WindowTab.McpService);
            _conversationsTabButton = CreateTabButton("MCP Conversations", WindowTab.McpConversations);
            _cliServiceTabButton = CreateTabButton("CLI Service", WindowTab.CliService);
            _cliConsolesTabButton = CreateTabButton("CLI Consoles", WindowTab.CliConsoles);
            tabBar.Add(_toolsTabButton);
            tabBar.Add(_serverTabButton);
            tabBar.Add(_conversationsTabButton);
            tabBar.Add(_cliServiceTabButton);
            tabBar.Add(_cliConsolesTabButton);
            return tabBar;
        }

        private Button CreateTabButton(string text, WindowTab tab)
        {
            var button = new Button(() => SetActiveTab(tab))
            {
                text = text,
                tooltip = $"Show {text} information.",
            };
            button.style.width = tab is WindowTab.McpConversations or WindowTab.CliConsoles ? 150 : 110;
            button.style.height = 26;
            button.style.marginRight = 4;
            return button;
        }

        private void SetActiveTab(WindowTab tab)
        {
            _activeTab = tab;

            if (_serverRoot != null)
                _serverRoot.style.display = tab == WindowTab.McpService ? DisplayStyle.Flex : DisplayStyle.None;
            if (_toolsRoot != null)
                _toolsRoot.style.display = tab == WindowTab.Tools ? DisplayStyle.Flex : DisplayStyle.None;
            if (_sessionsRoot != null)
                _sessionsRoot.style.display = tab == WindowTab.McpConversations ? DisplayStyle.Flex : DisplayStyle.None;
            if (_cliRoot != null)
                _cliRoot.style.display = tab == WindowTab.CliService ? DisplayStyle.Flex : DisplayStyle.None;
            if (_cliConsolesRoot != null)
                _cliConsolesRoot.style.display = tab == WindowTab.CliConsoles ? DisplayStyle.Flex : DisplayStyle.None;

            SetTabButtonState(_serverTabButton, tab == WindowTab.McpService);
            SetTabButtonState(_toolsTabButton, tab == WindowTab.Tools);
            SetTabButtonState(_conversationsTabButton, tab == WindowTab.McpConversations);
            SetTabButtonState(_cliServiceTabButton, tab == WindowTab.CliService);
            SetTabButtonState(_cliConsolesTabButton, tab == WindowTab.CliConsoles);
        }

        private static void SetTabButtonState(Button button, bool active)
        {
            if (button == null) return;
            button.style.backgroundColor = active ? ActiveTabColor : InactiveTabColor;
            button.style.color = active ? Color.white : new Color(0.82f, 0.82f, 0.82f, 1f);
            button.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
        }

        private static Button CreateToolbarButton(string text, string tooltip, Action clicked, int width = 76)
        {
            var button = new Button(clicked)
            {
                text = text,
                tooltip = tooltip,
            };
            button.style.width = width;
            button.style.marginLeft = 4;
            return button;
        }

        private static void StartMcpServer()
        {
            var options = LoadMcpOptions();
            McpServer.Shared.Start(options);
            SaveGeneratedMcpToken(options);
            EditorPrefs.SetBool(McpPrefKeyAutoStart, McpServer.Shared.State.IsRunning);
        }

        private static void SaveGeneratedMcpToken(McpServerOptions options)
        {
            var state = McpServer.Shared.State;
            if (!state.IsRunning || !options.RequireToken || !string.IsNullOrWhiteSpace(options.Token)) return;
            options.Token = state.Token;
            SaveMcpOptions(options);
        }

        private static void StopMcpServer()
        {
            McpServer.Shared.Stop();
            EditorPrefs.SetBool(McpPrefKeyAutoStart, false);
        }

        private static McpServerOptions LoadMcpOptions()
        {
            return new McpServerOptions
            {
                Host = EditorPrefs.GetString(McpPrefKeyHost, "127.0.0.1"),
                Port = EditorPrefs.GetInt(McpPrefKeyPort, 3100),
                BindLocalhostAliases = EditorPrefs.GetBool(McpPrefKeyBindLocalhostAliases, true),
                RequireToken = EditorPrefs.GetBool(McpPrefKeyRequireToken, false),
                Token = EditorPrefs.GetString(McpPrefKeyToken, string.Empty)
            };
        }

        private static void SaveMcpOptions(McpServerOptions options)
        {
            EditorPrefs.SetString(McpPrefKeyHost, options.Host);
            EditorPrefs.SetInt(McpPrefKeyPort, options.Port);
            EditorPrefs.SetBool(McpPrefKeyBindLocalhostAliases, options.BindLocalhostAliases);
            EditorPrefs.SetBool(McpPrefKeyRequireToken, options.RequireToken);
            EditorPrefs.SetString(McpPrefKeyToken, options.Token);
        }

        private static void ApplyPersistedToolStates()
        {
            foreach (var tool in EvalToolCatalog.ListTools(true))
            {
                if (!EditorPrefs.HasKey(ToolEnabledPrefPrefix + tool.Name)) continue;
                EvalToolSettings.SetEnabled(tool.Name, EditorPrefs.GetBool(ToolEnabledPrefPrefix + tool.Name, true));
            }
        }

        private static void SetToolEnabled(string name, bool enabled)
        {
            EditorPrefs.SetBool(ToolEnabledPrefPrefix + name, enabled);
            EvalToolSettings.SetEnabled(name, enabled);
        }

        private static TextField CreateTextField(string label, string tooltip, string value)
        {
            var field = new TextField(label)
            {
                value = value,
                tooltip = tooltip,
            };
            field.style.marginBottom = 3;
            return field;
        }

        private static IntegerField CreateIntegerField(string label, string tooltip, int value)
        {
            var field = new IntegerField(label)
            {
                value = value,
                tooltip = tooltip,
            };
            field.style.marginBottom = 3;
            return field;
        }

        private static Toggle CreateToggle(string label, string tooltip, bool value)
        {
            var field = new Toggle(label)
            {
                value = value,
                tooltip = tooltip,
            };
            field.style.marginBottom = 3;
            return field;
        }

        private void SaveSettings()
        {
            SaveMcpOptions(new McpServerOptions
            {
                Host = _hostField.value,
                Port = _portField.value,
                BindLocalhostAliases = _bindLocalhostAliasesToggle.value,
                RequireToken = _requireTokenToggle.value,
                Token = _tokenField.value
            });
        }

        private void SaveCliSettings()
        {
            CliBridgeEditorBootstrap.SaveOptions(new CliBridgeOptions
            {
                Host = _cliHostField.value,
                Port = _cliPortField.value,
                RequireToken = _cliRequireTokenToggle.value,
                Token = _cliTokenField.value
            });
        }

        private void LoadCliOptions()
        {
            var options = CliBridgeEditorBootstrap.LoadOptions();
            _cliHostField.value = options.Host;
            _cliPortField.value = options.Port;
            _cliRequireTokenToggle.value = options.RequireToken;
            _cliTokenField.value = options.Token;
        }

        private void LoadMcpOptionsIntoFields()
        {
            var options = LoadMcpOptions();
            _hostField.value = options.Host;
            _portField.value = options.Port;
            _bindLocalhostAliasesToggle.value = options.BindLocalhostAliases;
            _requireTokenToggle.value = options.RequireToken;
            _tokenField.value = options.Token;
        }

        private void SetSettingsEnabled(bool enabled)
        {
            _hostField.SetEnabled(enabled);
            _portField.SetEnabled(enabled);
            _bindLocalhostAliasesToggle.SetEnabled(enabled);
            _requireTokenToggle.SetEnabled(enabled);
            _tokenField.SetEnabled(enabled);
            _saveSettingsButton.SetEnabled(enabled);
        }

        private void SetCliSettingsEnabled(bool enabled)
        {
            _cliHostField.SetEnabled(enabled);
            _cliPortField.SetEnabled(enabled);
            _cliRequireTokenToggle.SetEnabled(enabled);
            _cliTokenField.SetEnabled(enabled);
            _cliSaveSettingsButton.SetEnabled(enabled);
        }

        private void RefreshView()
        {
            if (_sessionsRoot == null) return;

            var state = McpServer.Shared.State;
            _currentEndpoint = state.Endpoint;

            _statusValue.text = state.Status;
            _statusValue.style.color = state.IsRunning ? RunningColor : StoppedColor;
            _environmentValue.text = state.Environment;
            _endpointValue.text = string.IsNullOrEmpty(state.Endpoint) ? "-" : state.Endpoint;
            _authValue.text = state.RequireToken ? "token required" : "disabled";
            _activeConversationsValue.text = state.ActiveSessionCount.ToString();
            _startedValue.text = FormatDateTime(state.StartedAtUtc);
            _uptimeValue.text = FormatDuration(TimeSpan.FromSeconds(state.UptimeSeconds));

            _startButton.SetEnabled(!state.IsRunning);
            _stopButton.SetEnabled(state.IsRunning);
            _copyEndpointButton.SetEnabled(!string.IsNullOrEmpty(state.Endpoint));
            SetSettingsEnabled(!state.IsRunning);

            _serverMessages.Clear();
            if (!string.IsNullOrWhiteSpace(state.LastError))
                _serverMessages.Add(CreateMessage(state.LastError, ErrorColor));

            RefreshCliView();
            RefreshToolsView();
            RefreshSessions(state);
        }

        private void RefreshCliView()
        {
            var state = CliBridgeServer.Shared.State;
            _cliStatusValue.text = state.Status;
            _cliStatusValue.style.color = state.IsRunning ? RunningColor : StoppedColor;
            _cliEndpointValue.text = string.IsNullOrEmpty(state.Endpoint) ? "-" : state.Endpoint;
            _cliTransportValue.text = string.IsNullOrEmpty(state.Transport) ? "-" : state.Transport;
            _cliAuthValue.text = state.RequireToken ? "token required" : "disabled";
            _cliActiveSessionsValue.text = state.ActiveSessionCount.ToString();

            _cliStartButton.SetEnabled(!state.IsRunning);
            _cliStopButton.SetEnabled(state.IsRunning);
            _cliCopyCommandButton.SetEnabled(true);
            _cliLaunchButton.SetEnabled(true);
            SetCliSettingsEnabled(!state.IsRunning);

            _cliMessages.Clear();
            if (!string.IsNullOrWhiteSpace(state.LastError))
                _cliMessages.Add(CreateMessage(state.LastError, ErrorColor));

            RefreshCliConsoles(state);
        }

        private void RefreshTools()
        {
            _ = EvalToolCatalog.GetIndex(true);
            ApplyPersistedToolStates();
            RefreshView();
        }

        private void RefreshToolsView()
        {
            _toolsRoot.Clear();
            var tools = EvalToolCatalog.ListTools(false).OrderBy(tool => tool.Name, StringComparer.Ordinal).ToList();
            if (tools.Count == 0)
            {
                _toolsRoot.Add(CreateMessage("No MCP tools are registered.", WarningColor));
                return;
            }

            var csharpTools = tools
                .Where(tool => string.Equals(tool.Source, "csharp", StringComparison.OrdinalIgnoreCase))
                .OrderBy(tool => tool.EditorOnly ? 0 : 1)
                .ThenBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();
            var jsTools = tools
                .Where(tool => string.Equals(tool.Source, "js", StringComparison.OrdinalIgnoreCase))
                .OrderBy(tool => tool.EditorOnly ? 0 : 1)
                .ThenBy(tool => tool.Name, StringComparer.Ordinal)
                .ToList();

            _toolsRoot.Add(CreateToolSection("C# Tools", csharpTools, "No C# MCP tools are registered."));
            _toolsRoot.Add(CreateToolSection("JS Tools", jsTools, "No JavaScript MCP tools found in Resources/tools."));
        }

        private VisualElement CreateToolSection(
            string title,
            IReadOnlyList<EvalToolDescriptor> tools,
            string emptyMessage)
        {
            var section = CreateCard();

            var header = new Label($"{title} ({tools.Count})");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginBottom = 6;
            section.Add(header);

            if (tools.Count == 0)
            {
                section.Add(CreateMessage(emptyMessage, WarningColor));
                return section;
            }

            foreach (var tool in tools)
                section.Add(CreateToolItem(tool));

            return section;
        }

        private VisualElement CreateToolItem(EvalToolDescriptor tool)
        {
            var expanded = IsToolExpanded(tool.Name);

            var item = new VisualElement();
            item.style.marginBottom = 6;
            item.style.paddingLeft = 4;
            item.style.paddingRight = 4;
            item.style.paddingTop = 4;
            item.style.paddingBottom = 4;
            item.style.borderBottomWidth = 1;
            item.style.borderBottomColor = CardBorderColor;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.minHeight = 28;
            item.Add(header);

            var nameButton = new Button(() =>
            {
                SetToolExpanded(tool.Name, !IsToolExpanded(tool.Name));
                RefreshToolsView();
            })
            {
                text = expanded ? "▼ " + tool.Name : "▶ " + tool.Name,
                tooltip = "Open or close tool details.",
            };
            nameButton.style.width = 150;
            nameButton.style.marginLeft = 4;
            nameButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(nameButton);

            var source = new Label(tool.Source + (tool.EditorOnly ? " / Editor" : string.Empty));
            source.style.width = 120;
            source.style.opacity = 0.72f;
            source.style.marginLeft = 4;
            header.Add(source);

            var description = new Label(tool.Description);
            description.style.flexGrow = 1;
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginLeft = 6;
            header.Add(description);

            var stateButton = CreateToolStateButton(tool);
            stateButton.style.marginLeft = 8;
            header.Add(stateButton);

            var detail = new VisualElement();
            detail.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            detail.style.marginTop = 6;
            detail.style.paddingLeft = 12;
            item.Add(detail);

            AddField(detail, "Name").text = tool.Name;
            AddField(detail, "Import Path").text = $"tools/{tool.Name}";
            AddField(detail, "Source").text = tool.Source;
            AddField(detail, "Editor Only").text = tool.EditorOnly ? "yes" : "no";
            AddTextBlock(detail, "Description", tool.Description);
            AddToolFunctions(detail, tool);

            return item;
        }

        private Button CreateToolStateButton(EvalToolDescriptor tool)
        {
            var button = new Button(() =>
            {
                SetToolEnabled(tool.Name, !tool.Enabled);
                RefreshToolsView();
            })
            {
                tooltip = "Enable or disable importing and invoking this MCP sub-tool.",
            };
            button.style.width = 96;
            button.style.height = 24;
            SetToolStateButtonStyle(button, tool.Enabled);
            return button;
        }

        private bool IsToolExpanded(string toolName)
        {
            if (_expandedTools.Contains(toolName)) return true;
            if (!EditorPrefs.GetBool(ToolExpandedPrefPrefix + toolName, false)) return false;
            _expandedTools.Add(toolName);
            return true;
        }

        private void SetToolExpanded(string toolName, bool expanded)
        {
            if (expanded)
                _expandedTools.Add(toolName);
            else
                _expandedTools.Remove(toolName);
            EditorPrefs.SetBool(ToolExpandedPrefPrefix + toolName, expanded);
        }

        private static void SetToolStateButtonStyle(Button button, bool enabled)
        {
            button.text = enabled ? "Enabled" : "Disabled";
            button.style.backgroundColor = enabled ? EnabledButtonColor : DisabledButtonColor;
            button.style.color = Color.white;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
        }

        private static void AddToolFunctions(VisualElement parent, EvalToolDescriptor tool)
        {
            var title = new Label("Functions");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 6;
            title.style.marginBottom = 3;
            parent.Add(title);

            if (tool.Functions.Count == 0)
            {
                var message = tool.Source.Equals("js", StringComparison.OrdinalIgnoreCase)
                    ? "JavaScript tools are loaded as source files; generated function metadata is not available."
                    : "No function metadata is defined.";
                parent.Add(CreateMessage(message, WarningColor));
                return;
            }

            foreach (var function in tool.Functions)
                parent.Add(CreateFunctionRow(function));
        }

        private static VisualElement CreateFunctionRow(EvalToolFunctionDescriptor function)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.FlexStart;
            row.style.paddingLeft = 6;
            row.style.paddingRight = 6;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = CardBorderColor;

            var name = new Label(function.MethodName);
            name.style.width = 150;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(name);

            var parameters = new Label(function.ParameterTypes.Count == 0 ? "()" : $"({string.Join(", ", function.ParameterTypes)})");
            parameters.style.width = 220;
            parameters.style.opacity = 0.72f;
            row.Add(parameters);

            var description = new Label(function.Description);
            description.style.flexGrow = 1;
            description.style.whiteSpace = WhiteSpace.Normal;
            row.Add(description);
            return row;
        }

        private void RefreshSessions(McpServerState state)
        {
            _sessionsRoot.Clear();
            if (state.Sessions.Count == 0)
            {
                _sessionsRoot.Add(CreateMessage("No active MCP conversations.", WarningColor));
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var session in state.Sessions)
                _sessionsRoot.Add(CreateSessionCard(session, now));
        }

        private void RefreshCliConsoles(CliBridgeState state)
        {
            _cliConsolesRoot.Clear();
            if (!state.IsRunning)
            {
                _cliConsolesRoot.Add(CreateMessage("CLI bridge is stopped.", WarningColor));
                return;
            }

            if (state.Sessions.Count == 0)
            {
                _cliConsolesRoot.Add(CreateMessage("No active CLI connections.", WarningColor));
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var session in state.Sessions)
                _cliConsolesRoot.Add(CreateSessionCard(session, now));
        }

        private static VisualElement CreateSessionCard(EvalSessionSnapshot session, DateTime nowUtc)
        {
            var card = CreateCard();

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            card.Add(header);

            var title = new Label(GetClientName(session));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1;
            header.Add(title);

            var id = new Label(ShortId(session.Id));
            id.style.opacity = 0.65f;
            id.tooltip = session.Id;
            header.Add(id);

            AddField(card, "Protocol").text = session.ProtocolVersion;
            AddField(card, "Created").text = FormatDateTime(session.CreatedAtUtc);
            AddField(card, "Idle").text = FormatDuration(nowUtc - session.LastSeenUtc);
            AddField(card, "VM").text = session.HasEvalSession ? "created" : "not created";
            AddField(card, "Logic Result").text = GetLogicResult(session);
            AddField(card, "Eval Count").text = $"{session.TotalEvalCount} total / {session.FailedEvalCount} failed";

            if (session.EvalStatus == EvalExecutionStatus.Running)
            {
                AddField(card, "Current Eval").text = $"{ShortId(session.CurrentRequestId)} running {FormatMilliseconds(session.CurrentEvalElapsedMs)} / timeout {session.CurrentTimeoutSeconds}s";
                AddField(card, "Reset Session").text = session.CurrentResetSession ? "yes" : "no";
                AddTextBlock(card, "Code", session.CurrentCodeSummary);
            }
            else if (session.HasLastEval)
            {
                AddField(card, "Last Eval").text = $"{session.LastEvalStatus} in {FormatMilliseconds(session.LastEvalDurationMs)} at {FormatDateTime(session.LastEvalFinishedAtUtc)}";
                if (!string.IsNullOrWhiteSpace(session.LastEvalError))
                    card.Add(CreateMessage(session.LastEvalError, ErrorColor));
            }

            AddField(card, "Tool Function Count").text = $"{session.TotalToolFunctionCount} total / {session.FailedToolFunctionCount} failed";
            if (session.ActiveToolFunctionCount > 0)
                AddField(card, "Current Tool Function").text = $"{session.CurrentToolFunctionName} ({session.ActiveToolFunctionCount} active, {FormatMilliseconds(session.CurrentToolFunctionElapsedMs)})";
            else if (session.HasLastToolFunction)
                AddField(card, "Last Tool Function").text = $"{session.LastToolFunctionName} {(session.LastToolFunctionSucceeded ? "succeeded" : "failed")} in {FormatMilliseconds(session.LastToolFunctionDurationMs)}";

            if (!string.IsNullOrWhiteSpace(session.LastToolFunctionError))
                card.Add(CreateMessage(session.LastToolFunctionError, WarningColor));

            return card;
        }

        private static VisualElement CreateCard()
        {
            var card = new VisualElement();
            card.style.paddingLeft = 8;
            card.style.paddingRight = 8;
            card.style.paddingTop = 8;
            card.style.paddingBottom = 8;
            card.style.marginBottom = 8;
            card.style.borderTopWidth = 1;
            card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopColor = CardBorderColor;
            card.style.borderBottomColor = CardBorderColor;
            card.style.borderLeftColor = CardBorderColor;
            card.style.borderRightColor = CardBorderColor;
            card.style.backgroundColor = CardBackgroundColor;
            return card;
        }

        private static Label AddField(VisualElement parent, string labelText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom = 3;
            parent.Add(row);

            var label = new Label(labelText);
            label.style.width = LabelWidth;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(label);

            var value = new Label("-");
            value.style.flexGrow = 1;
            value.style.whiteSpace = WhiteSpace.Normal;
            row.Add(value);

            return value;
        }

        private static void AddTextBlock(VisualElement parent, string labelText, string value)
        {
            var label = new Label(labelText);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 4;
            parent.Add(label);

            var block = new Label(string.IsNullOrEmpty(value) ? "-" : value);
            block.style.whiteSpace = WhiteSpace.Normal;
            block.style.paddingLeft = 6;
            block.style.paddingRight = 6;
            block.style.paddingTop = 4;
            block.style.paddingBottom = 4;
            block.style.marginTop = 2;
            block.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
            parent.Add(block);
        }

        private static VisualElement CreateMessage(string text, Color accentColor)
        {
            var box = new VisualElement();
            box.style.borderLeftWidth = 3;
            box.style.borderLeftColor = accentColor;
            box.style.backgroundColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.12f);
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            box.style.paddingTop = 5;
            box.style.paddingBottom = 5;
            box.style.marginTop = 4;
            box.style.marginBottom = 4;

            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            box.Add(label);
            return box;
        }

        private void CopyEndpoint()
        {
            if (string.IsNullOrEmpty(_currentEndpoint)) return;
            EditorGUIUtility.systemCopyBuffer = _currentEndpoint;
        }

        private static string GetClientName(EvalSessionSnapshot session)
        {
            return string.IsNullOrWhiteSpace(session.ClientName) ? "(unknown client)" : session.ClientName;
        }

        private static string GetLogicResult(EvalSessionSnapshot session)
        {
            if (session.EvalStatus == EvalExecutionStatus.Running)
                return "running";
            if (!session.HasLastEval)
                return "no eval yet";
            if (session.LastEvalStatus == EvalExecutionStatus.Failed)
                return "failed";
            if (session.HasLastToolFunction && !session.LastToolFunctionSucceeded)
                return "eval succeeded, last tool function failed";
            return "succeeded";
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "-";
            return id.Length <= 8 ? id : id[..8];
        }

        private static string FormatDateTime(DateTime utc)
        {
            return utc == default ? "-" : utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string FormatMilliseconds(double milliseconds)
        {
            return milliseconds < 1000
                ? $"{Math.Max(0, milliseconds):0}ms"
                : $"{Math.Max(0, milliseconds / 1000):0.00}s";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
            if (duration.TotalMinutes >= 1) return $"{duration.Minutes}m {duration.Seconds}s";
            return $"{Math.Max(0, duration.TotalSeconds):0}s";
        }
    }
}
