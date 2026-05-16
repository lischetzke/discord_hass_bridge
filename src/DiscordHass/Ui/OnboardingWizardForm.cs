using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;

namespace DiscordHass.Ui;

/// <summary>
/// First-run setup wizard. Five steps: Welcome, Home Assistant, Discord, Preferences, Done.
/// State machine driven by a private <see cref="_step"/> int; each step replaces the
/// contents of <see cref="_stepHost"/>. Status: closed normally → caller checks
/// <see cref="Completed"/> to know whether to persist <see cref="AppConfig.HasCompletedOnboarding"/>.
///
/// Reuses <see cref="SetupActions"/> for the HA-connection test and Discord OAuth flow so the
/// logic stays identical to <see cref="SettingsForm"/>.
/// </summary>
internal sealed class OnboardingWizardForm : Form
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly BridgeService _bridge;

    private const int StepCount = 5;
    private int _step = 1;

    private Label _titleLabel = null!;
    private Label _stepIndicatorLabel = null!;
    private Panel _stepHost = null!;
    private Button _backButton = null!;
    private Button _skipButton = null!;
    private Button _nextButton = null!;

    // Persisted across steps so values entered earlier survive a Back navigation.
    private string _haUrl = "";
    private string _haToken = "";
    private string _discordClientId = "";
    private string _discordClientSecret = "";
    private DiscordTokens? _discordTokensPending;
    private bool _autostart;
    private bool _autoUpdate = true;
    private string _haTestStatus = "";
    private Color _haTestStatusColor = Color.Empty;
    private string _discordAuthStatus = "";
    private Color _discordAuthStatusColor = Color.Empty;

    /// <summary>True if the user reached and clicked Finish on step 5.</summary>
    public bool Completed { get; private set; }

    public OnboardingWizardForm(AppConfig config, ConfigStore configStore, BridgeService bridge)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{AppConstants.DisplayName} — Setup";
        ClientSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = true;
        BackColor = ThemeColors.Background;
        ForeColor = ThemeColors.OnSurface;

        _haUrl = _config.HaBaseUrl ?? "";
        _haToken = SecretProtector.Unprotect(_config.HaTokenProtected) ?? "";
        _discordClientId = _config.DiscordClientId ?? "";
        _discordClientSecret = SecretProtector.Unprotect(_config.DiscordClientSecretProtected) ?? "";
        _autostart = AutostartManager.IsEnabled();
        _autoUpdate = _config.CheckUpdatesAutomatically;

        BuildLayout();
        // Render the first step once the form has had a chance to lay out its child
        // controls — otherwise _stepHost.ClientSize is still the default (200×100) and
        // step content like the Welcome label ends up sized for an area smaller than the
        // form. Subscribing to Shown rather than calling Render() inline guarantees the
        // step host has its real docked size by the time we touch it.
        Shown += (_, _) => Render();
        ResumeLayout(performLayout: true);
    }

    private void BuildLayout()
    {
        // Root TableLayoutPanel: three rows (header / step host / button bar) so the dock
        // order can't conspire to hide the button bar behind the Fill step host. This is
        // the same lesson learned the hard way in OverviewForm and SettingsForm.
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = ThemeColors.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // step content
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));   // buttons

        // === Header row ===
        Panel header = new()
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Surface,
            Margin = Padding.Empty,
        };
        _titleLabel = new Label
        {
            Location = new Point(20, 12),
            Font = new Font(Font.FontFamily, Font.Size + 4F, FontStyle.Bold),
            ForeColor = ThemeColors.OnSurface,
            AutoSize = true,
        };
        _stepIndicatorLabel = new Label
        {
            Location = new Point(20, 38),
            ForeColor = ThemeColors.OnSurfaceDim,
            AutoSize = true,
        };
        header.Controls.Add(_titleLabel);
        header.Controls.Add(_stepIndicatorLabel);

        // === Step host row ===
        // Outer adds the padding, inner (the field) is what RenderXxx methods populate.
        // Same trick as CollapsiblePanel: child controls in WinForms only respect their
        // parent's Padding when they're docked or anchored, not when they use explicit
        // Location values — so we wrap a padded outer Panel around an unpadded Fill
        // inner Panel. Controls added to the inner at (0, 0) appear visually inset by
        // the outer's padding, which gives the wizard step content its left/top margin.
        Panel stepHostOuter = new()
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Background,
            Padding = new Padding(24, 18, 24, 12),
            Margin = Padding.Empty,
        };
        _stepHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Background,
        };
        stepHostOuter.Controls.Add(_stepHost);

        // === Button bar row ===
        Panel buttonBar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = ThemeColors.Surface,
            Margin = Padding.Empty,
        };

        _backButton = MakeFlatButton("Back");
        _backButton.Width = 100;
        _backButton.Margin = Padding.Empty;
        _backButton.Click += (_, _) => GoBack();

        _skipButton = MakeFlatButton("Skip");
        _skipButton.Width = 100;
        _skipButton.Margin = new Padding(8, 0, 0, 0);
        _skipButton.Click += (_, _) => GoNext();

        _nextButton = MakeFlatButton("Next");
        _nextButton.Width = 100;
        _nextButton.Margin = new Padding(8, 0, 0, 0);
        _nextButton.Click += async (_, _) => await OnNextClickedAsync().ConfigureAwait(true);

        // Back left, Next/Skip right. Using FlowLayoutPanel for *both* slots so the
        // Padding actually positions the children (a plain Panel ignores Padding for
        // absolutely-positioned children — that's the bug that previously left the Back
        // button stuck at (0, 0) of its slot).
        FlowLayoutPanel leftSlot = new()
        {
            Dock = DockStyle.Left,
            Width = 140,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(20, 16, 0, 0),
            WrapContents = false,
        };
        leftSlot.Controls.Add(_backButton);

        FlowLayoutPanel rightSlot = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = ThemeColors.Surface,
            Padding = new Padding(0, 16, 20, 0),
            WrapContents = false,
        };
        rightSlot.Controls.Add(_nextButton);
        rightSlot.Controls.Add(_skipButton);

        buttonBar.Controls.Add(rightSlot);
        buttonBar.Controls.Add(leftSlot);

        // Place rows in the table and add the table to the form. TableLayoutPanel
        // doesn't care about the order of Controls.Add for siblings — it places each
        // child at its specified (column, row) coordinate.
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(stepHostOuter, 0, 1);
        root.Controls.Add(buttonBar, 0, 2);
        Controls.Add(root);

        // The Next button is the default action — pressing Enter should advance the wizard.
        AcceptButton = _nextButton;
    }

    private void Render()
    {
        _stepHost.SuspendLayout();
        _stepHost.Controls.Clear();
        _stepIndicatorLabel.Text = $"Step {_step} of {StepCount}";

        switch (_step)
        {
            case 1: RenderWelcome();   _titleLabel.Text = "Welcome to DiscordHass"; break;
            case 2: RenderHa();        _titleLabel.Text = "Connect to Home Assistant"; break;
            case 3: RenderDiscord();   _titleLabel.Text = "Register a Discord application"; break;
            case 4: RenderPrefs();     _titleLabel.Text = "Preferences"; break;
            case 5: RenderDone();      _titleLabel.Text = "All set"; break;
        }

        _backButton.Enabled = _step > 1;
        _skipButton.Visible = _step is 2 or 3;
        _nextButton.Text = _step switch
        {
            5 => "Finish",
            _ => "Next",
        };

        _stepHost.ResumeLayout(performLayout: true);
    }

    private void RenderWelcome()
    {
        Label intro = new()
        {
            Text =
                "DiscordHass mirrors your Discord voice-session state into Home Assistant " +
                "input_boolean helpers in real time.\r\n\r\n" +
                "You'll set up two connections in this wizard:\r\n" +
                "  • Home Assistant — base URL and a long-lived access token\r\n" +
                "  • Discord — a tiny Discord application you create yourself (one-time)\r\n\r\n" +
                "DiscordHass creates a handful of input_boolean helpers in HA (in_call, " +
                "mic_muted, speaker_muted, camera_on, etc.) and keeps them in sync with what " +
                "Discord is doing. Use them in any automation that can read an input_boolean.\r\n\r\n" +
                "Click Next when you're ready.",
            Dock = DockStyle.Fill,
            ForeColor = ThemeColors.OnSurface,
            AutoSize = false,
        };
        _stepHost.Controls.Add(intro);
    }

    private TextBox _haUrlBox = null!;
    private TextBox _haTokenBox = null!;
    private Label _haTestStatusLabel = null!;

    private void RenderHa()
    {
        Label urlLabel = new() { Text = "Base URL", Location = new Point(0, 4), AutoSize = true, ForeColor = ThemeColors.OnSurface };
        HelpHintIcon urlHelp = new(HelpContent.TopicIds.HaUrl);
        _haUrlBox = new TextBox
        {
            Location = new Point(0, 26),
            Width = _stepHost.ClientSize.Width,
            Text = _haUrl,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };

        Label tokenLabel = new() { Text = "Long-lived access token", Location = new Point(0, 62), AutoSize = true, ForeColor = ThemeColors.OnSurface };
        HelpHintIcon tokenHelp = new(HelpContent.TopicIds.HaToken);
        _haTokenBox = new TextBox
        {
            Location = new Point(0, 84),
            Width = _stepHost.ClientSize.Width,
            Text = _haToken,
            UseSystemPasswordChar = true,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };

        Button testButton = MakeFlatButton("Test connection");
        testButton.Location = new Point(0, 120);
        testButton.Width = 160;
        testButton.Click += async (_, _) =>
        {
            testButton.Enabled = false;
            _haTestStatusLabel.ForeColor = ThemeColors.OnSurfaceDim;
            _haTestStatusLabel.Text = "Connecting…";
            HaTestResult result = await SetupActions.TestHaConnectionAsync(
                _haUrlBox.Text.Trim(), _haTokenBox.Text, CancellationToken.None).ConfigureAwait(true);
            switch (result)
            {
                case HaTestResult.MissingInput m:
                    _haTestStatusLabel.ForeColor = ThemeColors.StatusWarn;
                    _haTestStatusLabel.Text = m.Message;
                    break;
                case HaTestResult.Success s:
                    _haTestStatusLabel.ForeColor = ThemeColors.StatusOk;
                    _haTestStatusLabel.Text = $"Connected. Found {s.InputBooleanCount} existing input_boolean helper(s).";
                    break;
                case HaTestResult.Failure f:
                    _haTestStatusLabel.ForeColor = ThemeColors.StatusError;
                    _haTestStatusLabel.Text = $"Failed: {f.Message}";
                    break;
            }
            _haTestStatusColor = _haTestStatusLabel.ForeColor;
            _haTestStatus = _haTestStatusLabel.Text;
            testButton.Enabled = true;
        };

        _haTestStatusLabel = new Label
        {
            Location = new Point(170, 124),
            AutoSize = false,
            Size = new Size(_stepHost.ClientSize.Width - 170, 20),
            Text = _haTestStatus,
            ForeColor = _haTestStatusColor == Color.Empty ? ThemeColors.OnSurfaceDim : _haTestStatusColor,
        };

        _stepHost.Controls.AddRange(new Control[]
        {
            urlLabel, urlHelp, _haUrlBox,
            tokenLabel, tokenHelp, _haTokenBox,
            testButton, _haTestStatusLabel,
        });
        urlHelp.AlignWithLabel(urlLabel);
        tokenHelp.AlignWithLabel(tokenLabel);
    }

    private TextBox _discordIdBox = null!;
    private TextBox _discordSecretBox = null!;
    private Label _discordAuthLabel = null!;

    private void RenderDiscord()
    {
        Label intro = new()
        {
            Text = "Follow these steps in your browser, then paste the values below:",
            Location = new Point(0, 0),
            AutoSize = true,
            ForeColor = ThemeColors.OnSurfaceDim,
        };
        LinkLabel openPortal = new()
        {
            Text = "Open Discord Developer Portal →",
            Location = new Point(0, 22),
            AutoSize = true,
            LinkColor = ThemeColors.Accent,
            ActiveLinkColor = ThemeColors.Accent,
            BackColor = ThemeColors.Background,
        };
        openPortal.LinkClicked += (_, _) => OpenUrl("https://discord.com/developers/applications");
        HelpHintIcon guideHelp = new(HelpContent.TopicIds.DiscordRegistrationGuide);

        Button copyRedirect = MakeFlatButton("Copy redirect URI");
        copyRedirect.Location = new Point(290, 18);
        copyRedirect.Width = 160;
        copyRedirect.Click += (_, _) =>
        {
            try { Clipboard.SetText(AppConstants.DiscordOAuthRedirectUri); } catch { /* best effort */ }
        };
        Label redirectHint = new()
        {
            Text = $"Add this exact redirect URI in OAuth2 → Redirects: {AppConstants.DiscordOAuthRedirectUri}",
            Location = new Point(0, 52),
            AutoSize = false,
            Size = new Size(_stepHost.ClientSize.Width, 18),
            ForeColor = ThemeColors.OnSurfaceDim,
        };

        Label idLabel = new() { Text = "Client ID", Location = new Point(0, 88), AutoSize = true, ForeColor = ThemeColors.OnSurface };
        HelpHintIcon idHelp = new(HelpContent.TopicIds.DiscordClientId);
        _discordIdBox = new TextBox
        {
            Location = new Point(0, 110),
            Width = _stepHost.ClientSize.Width,
            Text = _discordClientId,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };

        Label secretLabel = new() { Text = "Client Secret", Location = new Point(0, 146), AutoSize = true, ForeColor = ThemeColors.OnSurface };
        HelpHintIcon secretHelp = new(HelpContent.TopicIds.DiscordClientSecret);
        _discordSecretBox = new TextBox
        {
            Location = new Point(0, 168),
            Width = _stepHost.ClientSize.Width,
            Text = _discordClientSecret,
            UseSystemPasswordChar = true,
            BackColor = ThemeColors.Surface,
            ForeColor = ThemeColors.OnSurface,
            BorderStyle = BorderStyle.FixedSingle,
        };

        Button authorizeButton = MakeFlatButton("Authorize…");
        authorizeButton.Location = new Point(0, 204);
        authorizeButton.Width = 160;
        _discordAuthLabel = new Label
        {
            Location = new Point(180, 208),
            AutoSize = false,
            Size = new Size(_stepHost.ClientSize.Width - 180, 20),
            Text = _discordAuthStatus,
            ForeColor = _discordAuthStatusColor == Color.Empty ? ThemeColors.OnSurfaceDim : _discordAuthStatusColor,
        };

        authorizeButton.Click += async (_, _) =>
        {
            authorizeButton.Enabled = false;
            _discordAuthLabel.ForeColor = ThemeColors.OnSurfaceDim;
            _discordAuthLabel.Text = "Stopping bridge…";
            await _bridge.StopAsync().ConfigureAwait(true);
            try
            {
                _discordAuthLabel.Text = "Approve the prompt in Discord when it appears…";
                DiscordAuthResult result = await SetupActions.AuthorizeDiscordAsync(
                    _discordIdBox.Text.Trim(), _discordSecretBox.Text, CancellationToken.None).ConfigureAwait(true);
                switch (result)
                {
                    case DiscordAuthResult.MissingInput m:
                        _discordAuthLabel.ForeColor = ThemeColors.StatusWarn;
                        _discordAuthLabel.Text = m.Message;
                        break;
                    case DiscordAuthResult.Success s:
                        _discordTokensPending = s.Tokens;
                        _discordAuthLabel.ForeColor = ThemeColors.StatusOk;
                        _discordAuthLabel.Text = "Authorized. Click Next to continue.";
                        break;
                    case DiscordAuthResult.Failure f:
                        _discordAuthLabel.ForeColor = ThemeColors.StatusError;
                        _discordAuthLabel.Text = $"Failed: {f.Message}";
                        break;
                }
                _discordAuthStatus = _discordAuthLabel.Text;
                _discordAuthStatusColor = _discordAuthLabel.ForeColor;
            }
            finally
            {
                authorizeButton.Enabled = true;
                _bridge.Start();
            }
        };

        _stepHost.Controls.AddRange(new Control[]
        {
            intro, openPortal, guideHelp, copyRedirect, redirectHint,
            idLabel, idHelp, _discordIdBox,
            secretLabel, secretHelp, _discordSecretBox,
            authorizeButton, _discordAuthLabel,
        });
        guideHelp.AlignWithLabel(openPortal);
        idHelp.AlignWithLabel(idLabel);
        secretHelp.AlignWithLabel(secretLabel);
    }

    private CheckBox _autostartCheckbox = null!;
    private CheckBox _autoUpdateCheckbox = null!;

    private void RenderPrefs()
    {
        Label intro = new()
        {
            Text = "A few one-time preferences. You can change all of these later from Settings.",
            Location = new Point(0, 0),
            AutoSize = true,
            ForeColor = ThemeColors.OnSurfaceDim,
        };

        _autostartCheckbox = new CheckBox
        {
            Text = "Start with Windows (sign-in)",
            Location = new Point(0, 40),
            AutoSize = true,
            Checked = _autostart,
            ForeColor = ThemeColors.OnSurface,
        };

        _autoUpdateCheckbox = new CheckBox
        {
            Text = "Check for updates automatically (once per day)",
            Location = new Point(0, 72),
            AutoSize = true,
            Checked = _autoUpdate,
            ForeColor = ThemeColors.OnSurface,
        };

        _stepHost.Controls.AddRange(new Control[]
        {
            intro,
            _autostartCheckbox,
            _autoUpdateCheckbox,
        });
    }

    private void RenderDone()
    {
        string ha = string.IsNullOrWhiteSpace(_haUrl) ? "(skipped)" : _haUrl;
        string discord = string.IsNullOrWhiteSpace(_discordClientId) ? "(skipped)" : "configured";
        string autostart = _autostart ? "enabled" : "disabled";
        string autoupdate = _autoUpdate ? "enabled" : "disabled";

        Label intro = new()
        {
            Text =
                "Setup complete. Click Finish to save and open the Overview window.\r\n\r\n" +
                $"  Home Assistant:     {ha}\r\n" +
                $"  Discord:            {discord}\r\n" +
                $"  Start with Windows: {autostart}\r\n" +
                $"  Auto-update:        {autoupdate}\r\n\r\n" +
                "You can change any of these later from Settings.",
            Dock = DockStyle.Fill,
            ForeColor = ThemeColors.OnSurface,
            AutoSize = false,
        };
        _stepHost.Controls.Add(intro);
    }

    private void CaptureStepValues()
    {
        switch (_step)
        {
            case 2:
                _haUrl = _haUrlBox.Text.Trim();
                _haToken = _haTokenBox.Text;
                break;
            case 3:
                _discordClientId = _discordIdBox.Text.Trim();
                _discordClientSecret = _discordSecretBox.Text;
                break;
            case 4:
                _autostart = _autostartCheckbox.Checked;
                _autoUpdate = _autoUpdateCheckbox.Checked;
                break;
        }
    }

    private async Task OnNextClickedAsync()
    {
        CaptureStepValues();
        if (_step == 5)
        {
            await FinishAsync().ConfigureAwait(true);
            return;
        }
        _step++;
        Render();
    }

    private void GoBack()
    {
        CaptureStepValues();
        if (_step > 1)
        {
            _step--;
            Render();
        }
    }

    private void GoNext()
    {
        CaptureStepValues();
        if (_step < 5)
        {
            _step++;
            Render();
        }
    }

    private async Task FinishAsync()
    {
        // Persist HA fields (only if URL is set — token alone makes no sense).
        if (!string.IsNullOrWhiteSpace(_haUrl))
        {
            _config.HaBaseUrl = _haUrl;
            _config.HaTokenProtected = string.IsNullOrEmpty(_haToken) ? null : SecretProtector.Protect(_haToken);
        }

        if (_discordTokensPending is not null && !string.IsNullOrWhiteSpace(_discordClientId))
        {
            SetupActions.PersistDiscordTokens(_config, _configStore, _discordClientId, _discordClientSecret, _discordTokensPending);
        }
        else if (!string.IsNullOrWhiteSpace(_discordClientId))
        {
            // User filled in client id but didn't authorize — save the id so they don't have
            // to re-type it. Bridge will fault until they authorize, which is fine.
            _config.DiscordClientId = _discordClientId;
            if (!string.IsNullOrWhiteSpace(_discordClientSecret))
            {
                _config.DiscordClientSecretProtected = SecretProtector.Protect(_discordClientSecret);
            }
        }

        try
        {
            AutostartManager.SetEnabled(_autostart);
            _config.AutostartEnabled = _autostart;
        }
        catch { /* tolerate; not critical */ }
        _config.CheckUpdatesAutomatically = _autoUpdate;

        _config.HasCompletedOnboarding = true;
        _configStore.Save(_config);

        Completed = true;

        // Kick the bridge so the new credentials are picked up.
        await _bridge.RestartAsync().ConfigureAwait(true);

        Close();
    }

    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    private static Button MakeFlatButton(string text)
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
}
