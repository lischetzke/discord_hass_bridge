using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;
using DiscordHass.HomeAssistant;
using DiscordHass.Update;

namespace DiscordHass.Ui;

/// <summary>
/// v0.2.0 Settings form. Replaces the old four-tab layout with a single scrollable surface
/// of four <see cref="CollapsiblePanel"/> sections: General, Home Assistant, Discord, States.
/// HA and Discord show a status chip on their header when configuration is missing or
/// re-authorization is required, so the user can see what needs attention without opening
/// each section.
///
/// The form is fully themed via <see cref="ThemeColors"/> — no more system-default light
/// chrome leaking through in dark mode. Save / Cancel docked at the bottom; sections stack
/// from the top.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly BridgeService _bridge;
    private readonly UpdateService _updates;

    // Section panels.
    private CollapsiblePanel _generalSection = null!;
    private CollapsiblePanel _haSection = null!;
    private CollapsiblePanel _discordSection = null!;
    private CollapsiblePanel _statesSection = null!;

    // General controls.
    private CheckBox _autostartCheckbox = null!;
    private CheckBox _minimizeToTrayCheckbox = null!;
    private CheckBox _autoUpdateCheckbox = null!;
    private Label _currentVersionLabel = null!;
    private Label _lastCheckedLabel = null!;
    private Button _checkNowButton = null!;
    private LinkLabel _releasesLink = null!;
    private Button _runSetupButton = null!;
    private Button _copyDiagnosticsButton = null!;
    private Button _openConfigFolderButton = null!;
    private System.Windows.Forms.Timer? _perfTimer;
    private Label _perfUptime = null!;
    private Label _perfCpu = null!;
    private Label _perfMemory = null!;
    private Label _perfGc = null!;
    private Label _perfThreadsHandles = null!;
    private Label _perfDiscordEvents = null!;
    private Label _perfHaFrames = null!;
    private Label _perfCamera = null!;
    private Label _perfReconnects = null!;
    private Label _perfPublishes = null!;
    private Label _perfLatency = null!;
    private long _lastCpuSampleMs = -1;
    private DateTime _lastCpuSampleWall = DateTime.MinValue;

    // HA controls.
    private TextBox _haUrlBox = null!;
    private TextBox _haTokenBox = null!;
    private Button _haTestButton = null!;
    private Label _haStatusLabel = null!;

    // Discord controls.
    private TextBox _discordClientIdBox = null!;
    private TextBox _discordClientSecretBox = null!;
    private Button _discordAuthorizeButton = null!;
    private Button _discordRevokeButton = null!;
    private Button _copyRedirectButton = null!;
    private Label _discordStatusLabel = null!;

    // States controls.
    private TextBox _helperPrefixBox = null!;
    private readonly Dictionary<string, FlagRow> _flagRows = new();

    private sealed class FlagRow
    {
        public CheckBox Enabled { get; init; } = null!;
        public TextBox NameSuffix { get; init; } = null!;
        public TextBox Icon { get; init; } = null!;
        public Label Preview { get; init; } = null!;
        public Button TestButton { get; init; } = null!;
    }

    public SettingsForm(AppConfig config, ConfigStore configStore, BridgeService bridge, UpdateService updates)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;
        _updates = updates;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = $"{AppConstants.DisplayName} — Settings";
        ClientSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = true;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.OnSurface;

        // Add the scrollable section host BEFORE the bottom save bar. WinForms applies
        // Controls[Count-1] first when computing dock layout, so the save bar (added last,
        // Dock=Bottom) carves out the bottom 56px first, and the section host (Dock=Fill,
        // added first) then claims the area above it — meaning scrolled content can reach
        // the bottom of the visible area without being hidden behind the save bar. Same
        // class of bug that bit the Overview form earlier.
        BuildSections();
        BuildSaveBar();
        LoadValuesFromConfig();
        RefreshAllPreviews();
        RefreshTestButtonsEnabled();
        RefreshSectionStatusChips();

        _bridge.StatusChanged += OnBridgeStatusChanged;
        FormClosed += (_, _) =>
        {
            _bridge.StatusChanged -= OnBridgeStatusChanged;
            _perfTimer?.Stop();
            _perfTimer?.Dispose();
        };

        ResumeLayout(performLayout: true);
    }

    private void BuildSaveBar()
    {
        Panel bar = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = ThemeColors.Surface,
        };
        Button save = MakeAction("Save && Close");
        save.Click += (_, _) =>
        {
            SaveAll();
            _ = _bridge.RestartAsync();
            Close();
        };
        Button cancel = MakeAction("Cancel");
        cancel.Click += (_, _) => Close();

        FlowLayoutPanel right = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(0, 14, 16, 0),
            WrapContents = false,
        };
        save.Margin = new Padding(8, 0, 0, 0);
        cancel.Margin = new Padding(8, 0, 0, 0);
        save.Width = 140;
        right.Controls.Add(save);
        right.Controls.Add(cancel);

        bar.Controls.Add(right);
        Controls.Add(bar);
    }

    private void BuildSections()
    {
        // Scrollable host that holds the four sections stacked top-down.
        Panel scroll = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = ThemeColors.Background,
            Padding = new Padding(20, 20, 20, 20),
        };

        _statesSection  = new CollapsiblePanel("States")          { Dock = DockStyle.Top };
        _discordSection = new CollapsiblePanel("Discord")         { Dock = DockStyle.Top };
        _haSection      = new CollapsiblePanel("Home Assistant")  { Dock = DockStyle.Top };
        _generalSection = new CollapsiblePanel("General")         { Dock = DockStyle.Top };

        BuildGeneralContent(_generalSection.ContentArea);
        BuildHaContent(_haSection.ContentArea);
        BuildDiscordContent(_discordSection.ContentArea);
        BuildStatesContent(_statesSection.ContentArea);

        // ContentHeight values must clear the bottom-most control in each section's
        // BuildXxxContent method PLUS the CollapsiblePanel's vertical padding (12 top +
        // 18 bottom = 30 px eaten). General runs to y≈510, Discord status label to
        // y≈260, States' test-hint help icon to y≈388.
        _generalSection.ContentHeight = 560;
        _haSection.ContentHeight = 220;
        _discordSection.ContentHeight = 320;
        _statesSection.ContentHeight = 440;

        // Default expanded section. Open General because that's the safe, no-action-required area.
        _generalSection.Expanded = true;

        // Re-show chips on header resize.
        foreach (CollapsiblePanel s in new[] { _generalSection, _haSection, _discordSection, _statesSection })
        {
            s.ExpansionChanged += (_, _) => RefreshSectionStatusChips();
        }

        // Bottom of the scrollable area lives at the top z-order so we add sections in
        // reverse stack order: bottommost first.
        scroll.Controls.Add(_statesSection);
        scroll.Controls.Add(_discordSection);
        scroll.Controls.Add(_haSection);
        scroll.Controls.Add(_generalSection);

        Controls.Add(scroll);
    }

    // ===== General section =====

    private void BuildGeneralContent(Panel host)
    {
        host.SuspendLayout();
        int y = 4;

        _autostartCheckbox = new CheckBox
        {
            Text = "Start with Windows (sign-in)",
            Location = new Point(0, y), AutoSize = true, ForeColor = ThemeColors.OnSurface,
        };
        host.Controls.Add(_autostartCheckbox);
        y += 28;

        _minimizeToTrayCheckbox = new CheckBox
        {
            Text = "Minimize to tray when closing settings window",
            Location = new Point(0, y), AutoSize = true, ForeColor = ThemeColors.OnSurface,
        };
        host.Controls.Add(_minimizeToTrayCheckbox);
        y += 32;

        _runSetupButton = MakeAction("Run setup wizard again…");
        _runSetupButton.Width = 220;
        _runSetupButton.Location = new Point(0, y);
        _runSetupButton.Click += (_, _) =>
        {
            using OnboardingWizardForm wiz = new(_config, _configStore, _bridge);
            wiz.ShowDialog(this);
            LoadValuesFromConfig();
            RefreshSectionStatusChips();
        };
        host.Controls.Add(_runSetupButton);
        y += 44;

        // ---- Updates subsection ----
        Label updatesHeader = MakeSubsectionHeader("Updates");
        updatesHeader.Location = new Point(0, y);
        host.Controls.Add(updatesHeader);
        y += 26;

        _autoUpdateCheckbox = new CheckBox
        {
            Text = "Check for updates automatically (once per day)",
            Location = new Point(0, y), AutoSize = true, ForeColor = ThemeColors.OnSurface,
        };
        host.Controls.Add(_autoUpdateCheckbox);
        y += 28;

        _currentVersionLabel = new Label
        {
            Location = new Point(0, y), AutoSize = true, ForeColor = ThemeColors.OnSurfaceDim,
            Text = $"Installed: v{AppConstants.GetVersionString()}",
        };
        host.Controls.Add(_currentVersionLabel);
        y += 22;

        _lastCheckedLabel = new Label
        {
            Location = new Point(0, y), AutoSize = true, ForeColor = ThemeColors.OnSurfaceDim,
            Text = FormatLastChecked(),
        };
        host.Controls.Add(_lastCheckedLabel);
        y += 28;

        _checkNowButton = MakeAction("Check now");
        _checkNowButton.Location = new Point(0, y);
        _checkNowButton.Click += async (_, _) => await OnCheckNowClickedAsync().ConfigureAwait(true);
        host.Controls.Add(_checkNowButton);

        _releasesLink = new LinkLabel
        {
            Location = new Point(180, y + 6), AutoSize = true,
            LinkColor = ThemeColors.Accent, ActiveLinkColor = ThemeColors.Accent,
            BackColor = ThemeColors.Background,
            Text = "View all releases on GitHub",
        };
        _releasesLink.LinkClicked += (_, _) => OpenUrlInBrowser(AppConstants.GitHubReleasesUrl);
        host.Controls.Add(_releasesLink);
        y += 44;

        // ---- Diagnostics subsection ----
        Label diagHeader = MakeSubsectionHeader("Diagnostics");
        diagHeader.Location = new Point(0, y);
        host.Controls.Add(diagHeader);
        y += 26;

        Label diagHint = new()
        {
            Location = new Point(0, y), AutoSize = false, Size = new Size(740, 36),
            ForeColor = ThemeColors.OnSurfaceDim,
            Text = "Every Discord IPC frame is appended to rpc-events.log. Copy diagnostics packs " +
                   "that log + a redacted config + a performance snapshot into a sharable zip.",
        };
        host.Controls.Add(diagHint);
        y += 40;

        _openConfigFolderButton = MakeAction("Open config folder");
        _openConfigFolderButton.Width = 180;
        _openConfigFolderButton.Location = new Point(0, y);
        _openConfigFolderButton.Click += (_, _) =>
        {
            try
            {
                AppPaths.EnsureAppDataDirExists();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppPaths.AppDataDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open folder:\r\n{ex.Message}", AppConstants.DisplayName);
            }
        };
        host.Controls.Add(_openConfigFolderButton);

        _copyDiagnosticsButton = MakeAction("Copy diagnostics…");
        _copyDiagnosticsButton.Width = 180;
        _copyDiagnosticsButton.Location = new Point(196, y);
        _copyDiagnosticsButton.Click += (_, _) => CreateAndShowDiagnostics();
        host.Controls.Add(_copyDiagnosticsButton);
        y += 40;

        // ---- Performance subsection ----
        Label perfHeader = MakeSubsectionHeader("Performance");
        perfHeader.Location = new Point(0, y);
        host.Controls.Add(perfHeader);

        HelpHintIcon perfHelp = new(HelpContent.TopicIds.GeneralPerformance);
        host.Controls.Add(perfHeader);
        host.Controls.Add(perfHelp);
        perfHelp.AlignWithLabel(perfHeader);
        y += 26;

        _perfUptime         = MakePerfLabel(0,   y);
        _perfCpu            = MakePerfLabel(0,   y + 20);
        _perfMemory         = MakePerfLabel(0,   y + 40);
        _perfGc             = MakePerfLabel(0,   y + 60);
        _perfThreadsHandles = MakePerfLabel(0,   y + 80);
        _perfDiscordEvents  = MakePerfLabel(360, y);
        _perfHaFrames       = MakePerfLabel(360, y + 20);
        _perfCamera         = MakePerfLabel(360, y + 40);
        _perfReconnects     = MakePerfLabel(360, y + 60);
        _perfPublishes      = MakePerfLabel(360, y + 80);
        _perfLatency        = new Label
        {
            Location = new Point(0, y + 104), AutoSize = false, Size = new Size(740, 18),
            ForeColor = ThemeColors.OnSurfaceDim,
        };
        host.Controls.AddRange(new Control[] {
            _perfUptime, _perfCpu, _perfMemory, _perfGc, _perfThreadsHandles,
            _perfDiscordEvents, _perfHaFrames, _perfCamera, _perfReconnects, _perfPublishes,
            _perfLatency,
        });

        _perfTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _perfTimer.Tick += (_, _) => RefreshPerformance();
        RefreshPerformance();
        _perfTimer.Start();

        host.ResumeLayout(performLayout: true);
    }

    // ===== HA section =====

    private void BuildHaContent(Panel host)
    {
        host.SuspendLayout();

        Label urlLabel = new()
        {
            Text = "Base URL (e.g. http://homeassistant.local:8123)",
            Location = new Point(0, 4), AutoSize = true,
            ForeColor = ThemeColors.OnSurface,
        };
        HelpHintIcon urlHelp = new(HelpContent.TopicIds.HaUrl);
        host.Controls.Add(urlLabel);
        host.Controls.Add(urlHelp);
        urlHelp.AlignWithLabel(urlLabel);

        _haUrlBox = new TextBox
        {
            Location = new Point(0, 30),
            Width = 740,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _haUrlBox.TextChanged += (_, _) => RefreshSectionStatusChips();
        host.Controls.Add(_haUrlBox);

        Label tokenLabel = new()
        {
            Text = "Long-lived access token",
            Location = new Point(0, 66), AutoSize = true,
            ForeColor = ThemeColors.OnSurface,
        };
        HelpHintIcon tokenHelp = new(HelpContent.TopicIds.HaToken);
        host.Controls.Add(tokenLabel);
        host.Controls.Add(tokenHelp);
        tokenHelp.AlignWithLabel(tokenLabel);

        _haTokenBox = new TextBox
        {
            Location = new Point(0, 92),
            Width = 740,
            UseSystemPasswordChar = true,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _haTokenBox.TextChanged += (_, _) => RefreshSectionStatusChips();
        host.Controls.Add(_haTokenBox);

        _haTestButton = MakeAction("Test connection");
        _haTestButton.Width = 160;
        _haTestButton.Location = new Point(0, 128);
        _haTestButton.Click += async (_, _) => await TestHaConnectionAsync().ConfigureAwait(true);
        host.Controls.Add(_haTestButton);

        _haStatusLabel = new Label
        {
            Location = new Point(176, 132), AutoSize = false, Size = new Size(560, 24),
            Text = "", ForeColor = ThemeColors.OnSurfaceDim,
        };
        host.Controls.Add(_haStatusLabel);

        host.ResumeLayout(performLayout: true);
    }

    // ===== Discord section =====

    private void BuildDiscordContent(Panel host)
    {
        host.SuspendLayout();

        Label idLabel = new()
        {
            Text = "Client ID",
            Location = new Point(0, 4), AutoSize = true,
            ForeColor = ThemeColors.OnSurface,
        };
        HelpHintIcon idHelp = new(HelpContent.TopicIds.DiscordClientId);
        Button registrationGuideButton = MakeAction("How do I get this?");
        registrationGuideButton.Width = 170;
        registrationGuideButton.Click += (_, _) => HelpDialog.ShowTopic(this, HelpContent.TopicIds.DiscordRegistrationGuide);

        host.Controls.Add(idLabel);
        host.Controls.Add(idHelp);
        idHelp.AlignWithLabel(idLabel);
        registrationGuideButton.Location = new Point(idHelp.Right + 14, idLabel.Top - 4);
        host.Controls.Add(registrationGuideButton);

        _discordClientIdBox = new TextBox
        {
            Location = new Point(0, 30),
            Width = 740,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _discordClientIdBox.TextChanged += (_, _) => RefreshSectionStatusChips();
        host.Controls.Add(_discordClientIdBox);

        Label secretLabel = new()
        {
            Text = "Client Secret",
            Location = new Point(0, 66), AutoSize = true,
            ForeColor = ThemeColors.OnSurface,
        };
        HelpHintIcon secretHelp = new(HelpContent.TopicIds.DiscordClientSecret);
        host.Controls.Add(secretLabel);
        host.Controls.Add(secretHelp);
        secretHelp.AlignWithLabel(secretLabel);

        _discordClientSecretBox = new TextBox
        {
            Location = new Point(0, 92),
            Width = 740,
            UseSystemPasswordChar = true,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        host.Controls.Add(_discordClientSecretBox);

        _discordAuthorizeButton = MakeAction("Authorize…");
        _discordAuthorizeButton.Width = 160;
        _discordAuthorizeButton.Location = new Point(0, 128);
        _discordAuthorizeButton.Click += async (_, _) => await AuthorizeDiscordAsync().ConfigureAwait(true);
        host.Controls.Add(_discordAuthorizeButton);

        _discordRevokeButton = MakeAction("Clear cached tokens");
        _discordRevokeButton.Width = 180;
        _discordRevokeButton.Location = new Point(176, 128);
        _discordRevokeButton.Click += (_, _) =>
        {
            _config.DiscordAccessTokenProtected = null;
            _config.DiscordAccessTokenExpiresAtUnix = 0;
            _config.DiscordRefreshTokenProtected = null;
            _configStore.Save(_config);
            _discordStatusLabel.Text = "Cached tokens cleared. Re-authorize to reconnect.";
            _discordStatusLabel.ForeColor = ThemeColors.OnSurfaceDim;
            RefreshSectionStatusChips();
        };
        host.Controls.Add(_discordRevokeButton);

        _copyRedirectButton = MakeAction("Copy redirect URI");
        _copyRedirectButton.Width = 170;
        _copyRedirectButton.Location = new Point(370, 128);
        _copyRedirectButton.Click += (_, _) =>
        {
            try { Clipboard.SetText(AppConstants.DiscordOAuthRedirectUri); } catch { /* best effort */ }
        };
        host.Controls.Add(_copyRedirectButton);

        Label redirectHint = new()
        {
            Location = new Point(0, 170), AutoSize = false, Size = new Size(740, 32),
            ForeColor = ThemeColors.OnSurfaceDim,
            Text = $"Redirect URI to paste into Discord → OAuth2 → Redirects:\r\n{AppConstants.DiscordOAuthRedirectUri}",
        };
        host.Controls.Add(redirectHint);

        _discordStatusLabel = new Label
        {
            Location = new Point(0, 212), AutoSize = false, Size = new Size(740, 48),
            Text = "", ForeColor = ThemeColors.OnSurfaceDim,
        };
        host.Controls.Add(_discordStatusLabel);

        host.ResumeLayout(performLayout: true);
    }

    // ===== States section =====

    private void BuildStatesContent(Panel host)
    {
        host.SuspendLayout();

        Label intro = new()
        {
            Text = "Helpers DiscordHass mirrors into Home Assistant. Toggle the checkbox to " +
                   "enable/disable publishing. Renaming a helper here will rename the matching " +
                   "input_boolean in HA on next reconnect.",
            Location = new Point(0, 0), AutoSize = false, Size = new Size(760, 32),
            ForeColor = ThemeColors.OnSurfaceDim,
        };
        host.Controls.Add(intro);

        Label prefixLabel = new()
        {
            Text = "Helper name prefix",
            Location = new Point(0, 42), AutoSize = true,
            ForeColor = ThemeColors.OnSurface,
        };
        HelpHintIcon prefixHelp = new(HelpContent.TopicIds.StatesPrefix);
        host.Controls.Add(prefixLabel);
        host.Controls.Add(prefixHelp);
        prefixHelp.AlignWithLabel(prefixLabel);

        _helperPrefixBox = new TextBox
        {
            Location = new Point(prefixHelp.Right + 12, 38),
            Width = 220,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _helperPrefixBox.TextChanged += (_, _) => RefreshAllPreviews();
        host.Controls.Add(_helperPrefixBox);

        int headerY = 84;
        Label hdrEnabled = MakeColumnHeader("Enabled", 0, headerY);
        Label hdrName    = MakeColumnHeader("Name",    72, headerY);
        Label hdrIcon    = MakeColumnHeader("Icon",   232, headerY);
        Label hdrSlug    = MakeColumnHeader("Entity ID slug", 392, headerY);
        Label hdrTest    = MakeColumnHeader("",       680, headerY);
        HelpHintIcon iconColHelp = new(HelpContent.TopicIds.StatesIcon);
        host.Controls.Add(hdrEnabled);
        host.Controls.Add(hdrName);
        host.Controls.Add(hdrIcon);
        host.Controls.Add(iconColHelp);
        iconColHelp.AlignWithLabel(hdrIcon);
        host.Controls.Add(hdrSlug);
        host.Controls.Add(hdrTest);

        int y = headerY + 24;
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            string capturedFlagId = def.FlagId;
            CheckBox enabled = new()
            {
                Location = new Point(16, y + 4),
                AutoSize = true,
                Text = "",
                ForeColor = ThemeColors.OnSurface,
            };
            enabled.CheckedChanged += (_, _) => RefreshTestButtonsEnabled();
            TextBox nameBox = new()
            {
                Location = new Point(72, y),
                Width = 150,
                BackColor = ThemeColors.Surface,
                ForeColor = ThemeColors.OnSurface,
                BorderStyle = BorderStyle.FixedSingle,
            };
            nameBox.TextChanged += (_, _) => RefreshRowPreview(capturedFlagId);

            TextBox iconBox = new()
            {
                Location = new Point(232, y),
                Width = 150,
                BackColor = ThemeColors.Surface,
                ForeColor = ThemeColors.OnSurface,
                BorderStyle = BorderStyle.FixedSingle,
            };

            Label preview = new()
            {
                Location = new Point(392, y + 3), AutoSize = false, Size = new Size(280, 20),
                ForeColor = ThemeColors.OnSurfaceDim, Text = "",
            };

            Button testButton = MakeAction("Test");
            testButton.Width = 64;
            testButton.Location = new Point(680, y - 1);
            testButton.Enabled = false;
            testButton.Click += async (_, _) => await OnTestPublishClickedAsync(capturedFlagId, testButton).ConfigureAwait(true);

            host.Controls.Add(enabled);
            host.Controls.Add(nameBox);
            host.Controls.Add(iconBox);
            host.Controls.Add(preview);
            host.Controls.Add(testButton);

            _flagRows[capturedFlagId] = new FlagRow
            {
                Enabled = enabled,
                NameSuffix = nameBox,
                Icon = iconBox,
                Preview = preview,
                TestButton = testButton,
            };
            y += 30;
        }

        host.ResumeLayout(performLayout: true);
    }

    // ===== Theming helpers =====

    private static Button MakeAction(string text)
    {
        Button b = new()
        {
            Text = text,
            Width = 120,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = ThemeColors.SurfaceMuted,
            ForeColor = ThemeColors.OnSurface,
            UseCompatibleTextRendering = false,
        };
        b.FlatAppearance.BorderColor = ThemeColors.Divider;
        b.FlatAppearance.MouseOverBackColor = ThemeColors.Surface;
        b.FlatAppearance.MouseDownBackColor = ThemeColors.SurfaceMuted;
        return b;
    }

    private static Label MakeSubsectionHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        ForeColor = ThemeColors.OnSurface,
    };

    private static Label MakeColumnHeader(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        Location = new Point(x, y),
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        ForeColor = ThemeColors.OnSurface,
    };

    private static Label MakePerfLabel(int x, int y) => new()
    {
        Location = new Point(x, y), AutoSize = false, Size = new Size(340, 20),
        ForeColor = ThemeColors.OnSurfaceDim,
    };

    // ===== Save / load =====

    private void LoadValuesFromConfig()
    {
        _haUrlBox.Text = _config.HaBaseUrl;
        _haTokenBox.Text = SecretProtector.Unprotect(_config.HaTokenProtected) ?? "";
        _discordClientIdBox.Text = _config.DiscordClientId;
        _discordClientSecretBox.Text = SecretProtector.Unprotect(_config.DiscordClientSecretProtected) ?? "";

        UpdateDiscordStatusLabel();

        _helperPrefixBox.Text = _config.HelperPrefix ?? "";

        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            if (!_flagRows.TryGetValue(def.FlagId, out FlagRow? row)) continue;
            FlagOverride? ov = _config.FlagOverrides.GetValueOrDefault(def.FlagId);
            row.Enabled.Checked = _config.EnabledFlags.Contains(def.FlagId);
            row.NameSuffix.Text = ov?.NameSuffix ?? def.DefaultNameSuffix;
            row.Icon.Text = ov?.Icon ?? def.DefaultIcon;
        }

        _autostartCheckbox.Checked = AutostartManager.IsEnabled();
        _minimizeToTrayCheckbox.Checked = _config.MinimizeToTrayOnClose;
        _autoUpdateCheckbox.Checked = _config.CheckUpdatesAutomatically;
    }

    private void UpdateDiscordStatusLabel()
    {
        if (string.IsNullOrEmpty(_config.DiscordRefreshTokenProtected))
        {
            _discordStatusLabel.Text = "Not authorized. Click Authorize once Client ID and Client Secret are filled in.";
            _discordStatusLabel.ForeColor = ThemeColors.OnSurfaceDim;
        }
        else if (!DiscordScopes.Matches(_config.DiscordAuthorizedScopeKey))
        {
            _discordStatusLabel.Text = "Re-authorize required: cached tokens are for an older permission set.";
            _discordStatusLabel.ForeColor = ThemeColors.StatusWarn;
        }
        else if (!string.IsNullOrEmpty(_config.DiscordGrantedScopes))
        {
            _discordStatusLabel.Text = $"Authorized. Granted scopes: {_config.DiscordGrantedScopes}";
            _discordStatusLabel.ForeColor = ThemeColors.StatusOk;
        }
        else
        {
            _discordStatusLabel.Text = "Authorized.";
            _discordStatusLabel.ForeColor = ThemeColors.StatusOk;
        }
    }

    private void SaveAll()
    {
        _config.HaBaseUrl = _haUrlBox.Text.Trim();
        _config.HaTokenProtected = string.IsNullOrEmpty(_haTokenBox.Text) ? null : SecretProtector.Protect(_haTokenBox.Text);

        _config.DiscordClientId = _discordClientIdBox.Text.Trim();
        _config.DiscordClientSecretProtected = string.IsNullOrEmpty(_discordClientSecretBox.Text) ? null : SecretProtector.Protect(_discordClientSecretBox.Text);

        _config.HelperPrefix = (_helperPrefixBox.Text ?? "").Trim();

        _config.EnabledFlags.Clear();
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            if (!_flagRows.TryGetValue(def.FlagId, out FlagRow? row)) continue;
            FlagOverride ov = _config.GetOrCreateOverride(def.FlagId);
            string suffix = (row.NameSuffix.Text ?? "").Trim();
            string icon = (row.Icon.Text ?? "").Trim();
            ov.NameSuffix = string.IsNullOrEmpty(suffix) || suffix == def.DefaultNameSuffix ? null : suffix;
            ov.Icon = string.IsNullOrEmpty(icon) || icon == def.DefaultIcon ? null : icon;
            if (row.Enabled.Checked) _config.EnabledFlags.Add(def.FlagId);
        }

        _config.MinimizeToTrayOnClose = _minimizeToTrayCheckbox.Checked;
        _config.CheckUpdatesAutomatically = _autoUpdateCheckbox.Checked;

        try
        {
            AutostartManager.SetEnabled(_autostartCheckbox.Checked);
            _config.AutostartEnabled = _autostartCheckbox.Checked;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update autostart setting:\r\n{ex.Message}",
                AppConstants.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _configStore.Save(_config);
    }

    // ===== Section status chips =====

    /// <summary>
    /// Update the HA / Discord section headers with a status chip when something needs
    /// attention. Driven by the values currently in the controls (so the chip clears the
    /// moment the user types a token), not the saved config.
    /// </summary>
    private void RefreshSectionStatusChips()
    {
        if (string.IsNullOrWhiteSpace(_haUrlBox.Text) || string.IsNullOrWhiteSpace(_haTokenBox.Text))
        {
            _haSection.SetStatusChip("Setup required", ThemeColors.StatusError);
        }
        else
        {
            _haSection.SetStatusChip(null, ThemeColors.SurfaceMuted);
        }

        if (string.IsNullOrWhiteSpace(_discordClientIdBox.Text) || string.IsNullOrWhiteSpace(_discordClientSecretBox.Text))
        {
            _discordSection.SetStatusChip("Setup required", ThemeColors.StatusError);
        }
        else if (string.IsNullOrEmpty(_config.DiscordRefreshTokenProtected))
        {
            _discordSection.SetStatusChip("Authorize required", ThemeColors.StatusWarn);
        }
        else if (!DiscordScopes.Matches(_config.DiscordAuthorizedScopeKey))
        {
            _discordSection.SetStatusChip("Re-authorize required", ThemeColors.StatusWarn);
        }
        else
        {
            _discordSection.SetStatusChip(null, ThemeColors.SurfaceMuted);
        }
    }

    private void OnBridgeStatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(new Action(() => { RefreshTestButtonsEnabled(); RefreshSectionStatusChips(); }));
        else { RefreshTestButtonsEnabled(); RefreshSectionStatusChips(); }
    }

    private void RefreshTestButtonsEnabled()
    {
        bool haOk = _bridge.HaStatus.Phase == ConnectionPhase.Connected;
        foreach (FlagRow row in _flagRows.Values)
        {
            row.TestButton.Enabled = haOk && row.Enabled.Checked;
        }
    }

    // ===== Slug preview =====

    private void RefreshAllPreviews()
    {
        foreach (string flagId in _flagRows.Keys) RefreshRowPreview(flagId);
    }

    private void RefreshRowPreview(string flagId)
    {
        if (!_flagRows.TryGetValue(flagId, out FlagRow? row)) return;
        StateFlagDefinition? def = StateFlagDefinitions.FindByFlagId(flagId);
        if (def is null) return;

        string prefix = (_helperPrefixBox.Text ?? "").Trim();
        string suffix = string.IsNullOrWhiteSpace(row.NameSuffix.Text) ? def.DefaultNameSuffix : row.NameSuffix.Text.Trim();
        string friendly = prefix.Length == 0 ? suffix : $"{prefix} {suffix}";
        string slug = FlagResolver.Slugify(friendly);
        row.Preview.Text = string.IsNullOrEmpty(slug) ? "(invalid)" : $"input_boolean.{slug}";
    }

    // ===== Test publish per flag =====

    private async Task OnTestPublishClickedAsync(string flagId, Button button)
    {
        string priorText = button.Text;
        button.Enabled = false;
        button.Text = "…";
        try
        {
            StateFlagDefinition? def = StateFlagDefinitions.FindByFlagId(flagId);
            if (def is null)
            {
                MessageBox.Show(this, $"Unknown flag: {flagId}", AppConstants.DisplayName);
                return;
            }
            bool currentDiscordValue = def.ValueSelector(_bridge.CurrentVoiceState);
            await _bridge.PublishTestAsync(flagId, desiredOn: !currentDiscordValue, default).ConfigureAwait(true);
            button.Text = "✓";
            await Task.Delay(1500).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Test publish failed:\r\n{ex.Message}", AppConstants.DisplayName);
        }
        finally
        {
            if (!IsDisposed && !button.IsDisposed)
            {
                button.Text = priorText;
                button.Enabled = _bridge.HaStatus.Phase == ConnectionPhase.Connected;
            }
        }
    }

    // ===== HA test connection =====

    private async Task TestHaConnectionAsync()
    {
        _haTestButton.Enabled = false;
        _haStatusLabel.ForeColor = ThemeColors.OnSurfaceDim;
        _haStatusLabel.Text = "Connecting…";

        try
        {
            HaTestResult result = await SetupActions.TestHaConnectionAsync(
                _haUrlBox.Text.Trim(), _haTokenBox.Text, CancellationToken.None).ConfigureAwait(true);
            switch (result)
            {
                case HaTestResult.MissingInput m:
                    _haStatusLabel.ForeColor = ThemeColors.StatusWarn;
                    _haStatusLabel.Text = m.Message;
                    break;
                case HaTestResult.Success s:
                    _haStatusLabel.ForeColor = ThemeColors.StatusOk;
                    _haStatusLabel.Text = $"Connected. Found {s.InputBooleanCount} existing input_boolean helper(s).";
                    break;
                case HaTestResult.Failure f:
                    _haStatusLabel.ForeColor = ThemeColors.StatusError;
                    _haStatusLabel.Text = $"Failed: {f.Message}";
                    break;
            }
        }
        finally
        {
            _haTestButton.Enabled = true;
        }
    }

    // ===== Discord authorize =====

    private async Task AuthorizeDiscordAsync()
    {
        string clientId = _discordClientIdBox.Text.Trim();
        string clientSecret = _discordClientSecretBox.Text;

        _discordAuthorizeButton.Enabled = false;
        _discordStatusLabel.ForeColor = ThemeColors.OnSurfaceDim;
        _discordStatusLabel.Text = "Stopping bridge…";

        await _bridge.StopAsync().ConfigureAwait(true);

        try
        {
            _discordStatusLabel.Text = "Connecting to Discord… approve the prompt in Discord when it appears.";
            DiscordAuthResult result = await SetupActions.AuthorizeDiscordAsync(
                clientId, clientSecret, CancellationToken.None).ConfigureAwait(true);
            switch (result)
            {
                case DiscordAuthResult.MissingInput m:
                    _discordStatusLabel.ForeColor = ThemeColors.StatusWarn;
                    _discordStatusLabel.Text = m.Message;
                    break;
                case DiscordAuthResult.Success s:
                    SetupActions.PersistDiscordTokens(_config, _configStore, clientId, clientSecret, s.Tokens);
                    _discordStatusLabel.ForeColor = ThemeColors.StatusOk;
                    _discordStatusLabel.Text = "Authorized. Tokens cached — bridge will reconnect when you save.";
                    RefreshSectionStatusChips();
                    break;
                case DiscordAuthResult.Failure f:
                    _discordStatusLabel.ForeColor = ThemeColors.StatusError;
                    _discordStatusLabel.Text = $"Failed: {f.Message}";
                    break;
            }
        }
        finally
        {
            _discordAuthorizeButton.Enabled = true;
            _bridge.Start();
        }
    }

    // ===== Update-related =====

    private async Task OnCheckNowClickedAsync()
    {
        _checkNowButton.Enabled = false;
        string priorText = _checkNowButton.Text;
        _checkNowButton.Text = "Checking…";
        try
        {
            bool found = await _updates.CheckNowAsync().ConfigureAwait(true);
            _lastCheckedLabel.Text = FormatLastChecked();
            if (found && _updates.Available is not null)
            {
                MessageBox.Show(this,
                    $"Update available: {_updates.Available.TagName}\r\n\r\nRight-click the tray icon to install.",
                    $"{AppConstants.DisplayName} — Update",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (_updates.State == UpdateState.Faulted)
            {
                MessageBox.Show(this,
                    $"Update check failed: {_updates.LastError}",
                    $"{AppConstants.DisplayName} — Update",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                MessageBox.Show(this,
                    $"You're up to date (v{AppConstants.GetVersionString()}).",
                    $"{AppConstants.DisplayName} — Update",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        finally
        {
            _checkNowButton.Text = priorText;
            _checkNowButton.Enabled = true;
        }
    }

    private string FormatLastChecked()
    {
        if (_config.LastUpdateCheckUnix <= 0) return "Last checked: never";
        DateTimeOffset when = DateTimeOffset.FromUnixTimeSeconds(_config.LastUpdateCheckUnix).ToLocalTime();
        return $"Last checked: {when:yyyy-MM-dd HH:mm}";
    }

    // ===== Performance refresh =====

    private void RefreshPerformance()
    {
        if (IsDisposed) return;

        TimeSpan uptime = AppMetrics.ProcessUptime();
        long cpuMs = AppMetrics.CpuTimeMs();
        DateTime now = DateTime.UtcNow;
        string cpuPct = "—";
        if (_lastCpuSampleMs >= 0)
        {
            double dWall = (now - _lastCpuSampleWall).TotalMilliseconds;
            if (dWall > 0)
            {
                double dCpu = cpuMs - _lastCpuSampleMs;
                double pct = dCpu / (dWall * Math.Max(1, Environment.ProcessorCount)) * 100.0;
                cpuPct = pct.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }
        }
        _lastCpuSampleMs = cpuMs;
        _lastCpuSampleWall = now;

        long wsBytes = AppMetrics.WorkingSetBytes();
        long privBytes = AppMetrics.PrivateBytes();
        long allocBytes = AppMetrics.GcAllocatedBytes();
        (int g0, int g1, int g2) = AppMetrics.GcCollections();

        _perfUptime.Text = $"Uptime:          {PerformanceReport.FormatUptime(uptime)}";
        _perfCpu.Text = $"CPU time:        {cpuMs:N0} ms  ({cpuPct})";
        _perfMemory.Text = $"Memory:          ws {PerformanceReport.FormatBytes(wsBytes)} / priv {PerformanceReport.FormatBytes(privBytes)}";
        _perfGc.Text = $"GC:              {PerformanceReport.FormatBytes(allocBytes)} alloc · Gen 0/1/2 {g0}/{g1}/{g2}";
        _perfThreadsHandles.Text = $"Threads/handles: {AppMetrics.ThreadCount()} / {AppMetrics.HandleCount()}";

        _perfDiscordEvents.Text = $"Discord events:  {AppMetrics.DiscordEventsReceived:N0}";
        _perfHaFrames.Text = $"HA frames:       sent {AppMetrics.HaFramesSent:N0} / recv {AppMetrics.HaFramesReceived:N0}";
        _perfCamera.Text = $"Camera polls:    {AppMetrics.CameraRegistryPolls:N0}";
        _perfReconnects.Text = $"Reconnects:      Discord {AppMetrics.DiscordReconnects} · HA {AppMetrics.HaReconnects}";
        _perfPublishes.Text = $"Helper publish:  {AppMetrics.HelperPublishes:N0}  (test {AppMetrics.PublishTestInvocations})";

        LatencySnapshot snap = _bridge.PublishLatency.Snapshot();
        _perfLatency.Text = snap.Count == 0
            ? "Publish latency: (no samples yet)"
            : $"Publish latency: p50 {snap.P50Ms.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} ms · p95 {snap.P95Ms.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} ms · p99 {snap.P99Ms.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} ms · n={snap.Count}";
    }

    // ===== Diagnostics =====

    private void CreateAndShowDiagnostics()
    {
        try
        {
            System.IO.FileInfo file = DiagnosticsBundle.Create(_config, _bridge);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{file.FullName}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                MessageBox.Show(this, $"Diagnostics bundle written to:\r\n{file.FullName}", AppConstants.DisplayName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not create diagnostics bundle:\r\n{ex.Message}",
                AppConstants.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void OpenUrlInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // user can copy from the link text if launch fails
        }
    }
}
