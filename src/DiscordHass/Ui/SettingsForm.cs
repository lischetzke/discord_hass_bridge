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

internal sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly BridgeService _bridge;
    private readonly UpdateService _updates;

    // Home Assistant tab
    private TextBox _haUrlBox = null!;
    private TextBox _haTokenBox = null!;
    private Button _haTestButton = null!;
    private Label _haStatusLabel = null!;

    // Discord tab
    private TextBox _discordClientIdBox = null!;
    private TextBox _discordClientSecretBox = null!;
    private Button _discordAuthorizeButton = null!;
    private Button _discordRevokeButton = null!;
    private Label _discordStatusLabel = null!;

    // States tab
    private TextBox _helperPrefixBox = null!;
    private readonly Dictionary<string, FlagRow> _flagRows = new();

    // General tab
    private CheckBox _autostartCheckbox = null!;
    private CheckBox _minimizeToTrayCheckbox = null!;
    private CheckBox _autoUpdateCheckbox = null!;
    private Label _currentVersionLabel = null!;
    private Label _lastCheckedLabel = null!;
    private Button _checkNowButton = null!;
    private LinkLabel _releasesLink = null!;

    private sealed class FlagRow
    {
        public CheckBox Enabled { get; init; } = null!;
        public TextBox NameSuffix { get; init; } = null!;
        public TextBox Icon { get; init; } = null!;
        public Label Preview { get; init; } = null!;
    }

    public SettingsForm(AppConfig config, ConfigStore configStore, BridgeService bridge, UpdateService updates)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;
        _updates = updates;

        SuspendLayout();
        // Set autoscale BEFORE child controls go in — WinForms uses these as the design-time
        // baseline and scales every Location/Size on child controls by (currentDpi / 96).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = $"{AppConstants.DisplayName} — Settings";
        ClientSize = new Size(740, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = true;

        InitializeUi();
        LoadValuesFromConfig();
        RefreshAllPreviews();
        ResumeLayout(performLayout: true);
    }

    private void InitializeUi()
    {
        TabControl tabs = new()
        {
            Dock = DockStyle.Top,
            Height = ClientSize.Height - 56,
            Padding = new Point(10, 4),
        };
        tabs.TabPages.Add(BuildHomeAssistantTab());
        tabs.TabPages.Add(BuildDiscordTab());
        tabs.TabPages.Add(BuildStatesTab());
        tabs.TabPages.Add(BuildGeneralTab());

        Panel bottom = new() { Dock = DockStyle.Bottom, Height = 56 };
        Button saveButton = new()
        {
            Text = "Save && Close",
            Width = 120, Height = 28, Top = 14,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        saveButton.Left = bottom.ClientSize.Width - saveButton.Width - 16;
        saveButton.Click += (_, _) =>
        {
            SaveAll();
            _ = _bridge.RestartAsync();
            Close();
        };
        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 100, Height = 28, Top = 14,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
        };
        cancelButton.Left = saveButton.Left - cancelButton.Width - 8;
        cancelButton.Click += (_, _) => Close();

        bottom.Controls.Add(saveButton);
        bottom.Controls.Add(cancelButton);

        Controls.Add(tabs);
        Controls.Add(bottom);
    }

    private TabPage BuildHomeAssistantTab()
    {
        TabPage page = new("Home Assistant");

        Label urlLabel = new() { Text = "Base URL (e.g. http://homeassistant.local:8123)", Location = new Point(16, 18), AutoSize = true };
        _haUrlBox = new TextBox { Location = new Point(16, 40), Width = 690 };

        Label tokenLabel = new() { Text = "Long-lived access token", Location = new Point(16, 76), AutoSize = true };
        _haTokenBox = new TextBox { Location = new Point(16, 98), Width = 690, UseSystemPasswordChar = true };

        _haTestButton = new Button { Text = "Test connection", Location = new Point(16, 134), Width = 160 };
        _haTestButton.Click += async (_, _) => await TestHaConnectionAsync().ConfigureAwait(true);

        _haStatusLabel = new Label
        {
            Location = new Point(184, 138), AutoSize = false, Width = 522, Height = 24,
            Text = "", ForeColor = Color.DimGray,
        };

        Label hint = new()
        {
            Location = new Point(16, 184), AutoSize = false, Width = 690, Height = 32,
            ForeColor = Color.DimGray,
            Text = "Create a long-lived access token from your Home Assistant profile page → Security → Long-lived access tokens. " +
                   "The token user must have permission to create input_boolean helpers (admin role).",
        };

        page.Controls.AddRange(new Control[] { urlLabel, _haUrlBox, tokenLabel, _haTokenBox, _haTestButton, _haStatusLabel, hint });
        return page;
    }

    private TabPage BuildDiscordTab()
    {
        TabPage page = new("Discord");

        Label idLabel = new() { Text = "Client ID", Location = new Point(16, 18), AutoSize = true };
        _discordClientIdBox = new TextBox { Location = new Point(16, 40), Width = 690 };

        Label secretLabel = new() { Text = "Client Secret", Location = new Point(16, 76), AutoSize = true };
        _discordClientSecretBox = new TextBox { Location = new Point(16, 98), Width = 690, UseSystemPasswordChar = true };

        _discordAuthorizeButton = new Button { Text = "Authorize…", Location = new Point(16, 134), Width = 160 };
        _discordAuthorizeButton.Click += async (_, _) => await AuthorizeDiscordAsync().ConfigureAwait(true);

        _discordRevokeButton = new Button { Text = "Clear cached tokens", Location = new Point(184, 134), Width = 180 };
        _discordRevokeButton.Click += (_, _) =>
        {
            _config.DiscordAccessTokenProtected = null;
            _config.DiscordAccessTokenExpiresAtUnix = 0;
            _config.DiscordRefreshTokenProtected = null;
            _configStore.Save(_config);
            _discordStatusLabel.Text = "Cached tokens cleared. Re-authorize to reconnect.";
            _discordStatusLabel.ForeColor = Color.DimGray;
        };

        _discordStatusLabel = new Label
        {
            Location = new Point(16, 180), AutoSize = false, Width = 690, Height = 24,
            Text = "", ForeColor = Color.DimGray,
        };

        const string discordDevUrl = "https://discord.com/developers/applications";
        string hintText = $"Register a Discord application at {discordDevUrl} → " +
                          "copy the Application ID (Client ID) and reset/copy the Client Secret. " +
                          "Click Authorize to grant the app access via Discord's approval modal.";
        LinkLabel hint = new()
        {
            Location = new Point(16, 220), AutoSize = false, Width = 690, Height = 60,
            ForeColor = Color.DimGray,
            LinkColor = Color.SteelBlue,
            ActiveLinkColor = Color.RoyalBlue,
            Text = hintText,
            LinkArea = new LinkArea(hintText.IndexOf(discordDevUrl, StringComparison.Ordinal), discordDevUrl.Length),
        };
        hint.LinkClicked += (_, _) => OpenUrlInBrowser(discordDevUrl);

        page.Controls.AddRange(new Control[]
        {
            idLabel, _discordClientIdBox, secretLabel, _discordClientSecretBox,
            _discordAuthorizeButton, _discordRevokeButton, _discordStatusLabel, hint,
        });
        return page;
    }

    private static void OpenUrlInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // user can copy the URL from the label text if launch fails
        }
    }

    private TabPage BuildStatesTab()
    {
        TabPage page = new("States");

        Label intro = new()
        {
            Text = "Customize the friendly name prefix and per-flag suffix and icon. " +
                   "If a helper already exists in Home Assistant, the bridge will rename it to match — " +
                   "automations referencing the old entity_id will need to be updated.",
            Location = new Point(16, 14), Width = 700, Height = 36,
            AutoSize = false, ForeColor = Color.DimGray,
        };
        page.Controls.Add(intro);

        Label prefixLabel = new() { Text = "Helper name prefix", Location = new Point(16, 60), AutoSize = true };
        _helperPrefixBox = new TextBox { Location = new Point(180, 56), Width = 200 };
        _helperPrefixBox.TextChanged += (_, _) => RefreshAllPreviews();
        page.Controls.Add(prefixLabel);
        page.Controls.Add(_helperPrefixBox);

        // Column headers
        int headerY = 96;
        page.Controls.Add(new Label { Text = "Enabled",   Location = new Point(16, headerY),  AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        page.Controls.Add(new Label { Text = "Name",      Location = new Point(86, headerY),  AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        page.Controls.Add(new Label { Text = "Icon",      Location = new Point(236, headerY), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        page.Controls.Add(new Label { Text = "Entity ID", Location = new Point(412, headerY), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });

        int y = headerY + 22;
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            CheckBox enabled = new()
            {
                Location = new Point(28, y + 4),
                AutoSize = true,
                Text = "",
            };
            TextBox nameBox = new() { Location = new Point(86, y), Width = 140 };
            nameBox.TextChanged += (_, _) => RefreshRowPreview(def.FlagId);

            TextBox iconBox = new() { Location = new Point(236, y), Width = 160 };

            Label preview = new()
            {
                Location = new Point(412, y + 3), AutoSize = false, Width = 300, Height = 20,
                ForeColor = Color.DimGray, Text = "",
            };

            page.Controls.Add(enabled);
            page.Controls.Add(nameBox);
            page.Controls.Add(iconBox);
            page.Controls.Add(preview);

            _flagRows[def.FlagId] = new FlagRow
            {
                Enabled = enabled,
                NameSuffix = nameBox,
                Icon = iconBox,
                Preview = preview,
            };
            y += 30;
        }

        const string mdiUrl = "https://pictogrammers.com/library/mdi/";
        string iconHintText = "Tip: icons use Material Design Icons names like mdi:phone-in-talk. " +
                              $"Browse the full set at {mdiUrl}.";
        LinkLabel iconHint = new()
        {
            Location = new Point(16, y + 8), AutoSize = false, Width = 700, Height = 36,
            ForeColor = Color.DimGray,
            LinkColor = Color.SteelBlue,
            ActiveLinkColor = Color.RoyalBlue,
            Text = iconHintText,
            LinkArea = new LinkArea(iconHintText.IndexOf(mdiUrl, StringComparison.Ordinal), mdiUrl.Length),
        };
        iconHint.LinkClicked += (_, _) => OpenUrlInBrowser(mdiUrl);
        page.Controls.Add(iconHint);

        return page;
    }

    private TabPage BuildGeneralTab()
    {
        TabPage page = new("General");

        _autostartCheckbox = new CheckBox
        {
            Text = "Start with Windows (sign-in)",
            Location = new Point(20, 24), AutoSize = true,
        };
        _minimizeToTrayCheckbox = new CheckBox
        {
            Text = "Minimize to tray when closing settings window",
            Location = new Point(20, 56), AutoSize = true,
        };

        // --- Updates section ---
        Label updatesHeader = new()
        {
            Text = "Updates",
            Location = new Point(20, 110), AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
        };
        _autoUpdateCheckbox = new CheckBox
        {
            Text = "Check for updates automatically (once per day)",
            Location = new Point(20, 138), AutoSize = true,
        };
        _currentVersionLabel = new Label
        {
            Location = new Point(20, 170), AutoSize = true, ForeColor = Color.DimGray,
            Text = $"Installed: v{AppConstants.GetVersionString()}",
        };
        _lastCheckedLabel = new Label
        {
            Location = new Point(20, 192), AutoSize = true, ForeColor = Color.DimGray,
            Text = FormatLastChecked(),
        };
        _checkNowButton = new Button
        {
            Text = "Check now", Location = new Point(20, 220), Width = 160, Height = 28,
        };
        _checkNowButton.Click += async (_, _) => await OnCheckNowClickedAsync().ConfigureAwait(true);

        const string releasesUrl = AppConstants.GitHubReleasesUrl;
        _releasesLink = new LinkLabel
        {
            Location = new Point(200, 226), AutoSize = true,
            LinkColor = Color.SteelBlue, ActiveLinkColor = Color.RoyalBlue,
            Text = "View all releases on GitHub",
        };
        _releasesLink.LinkClicked += (_, _) => OpenUrlInBrowser(releasesUrl);

        // --- Diagnostics section ---
        Label diagHeader = new()
        {
            Text = "Diagnostics",
            Location = new Point(20, 280), AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
        };
        Label diagHint = new()
        {
            Location = new Point(20, 308), AutoSize = false, Width = 700, Height = 36,
            ForeColor = Color.DimGray,
            Text = "Every Discord IPC frame from this session is appended to rpc-events.log " +
                   "in the config folder. Reproduce a problem (e.g. toggle your camera), then " +
                   "open the folder to grab the log.",
        };
        Button openConfigFolderButton = new()
        {
            Text = "Open config folder", Location = new Point(20, 352), Width = 200, Height = 28,
        };
        openConfigFolderButton.Click += (_, _) =>
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

        page.Controls.AddRange(new Control[]
        {
            _autostartCheckbox, _minimizeToTrayCheckbox,
            updatesHeader, _autoUpdateCheckbox, _currentVersionLabel, _lastCheckedLabel,
            _checkNowButton, _releasesLink,
            diagHeader, diagHint, openConfigFolderButton,
        });
        return page;
    }

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

    private void LoadValuesFromConfig()
    {
        _haUrlBox.Text = _config.HaBaseUrl;
        _haTokenBox.Text = SecretProtector.Unprotect(_config.HaTokenProtected) ?? "";
        _discordClientIdBox.Text = _config.DiscordClientId;
        _discordClientSecretBox.Text = SecretProtector.Unprotect(_config.DiscordClientSecretProtected) ?? "";

        // Surface the current Discord authorization state so the user can see at a glance
        // what scopes Discord actually granted vs. what the app requested.
        if (string.IsNullOrEmpty(_config.DiscordRefreshTokenProtected))
        {
            _discordStatusLabel.Text = "Not authorized. Click Authorize once Client ID and Client Secret are filled in.";
            _discordStatusLabel.ForeColor = Color.DimGray;
        }
        else if (!DiscordScopes.Matches(_config.DiscordAuthorizedScopeKey))
        {
            _discordStatusLabel.Text = "Re-authorize required: cached tokens are for older permissions.";
            _discordStatusLabel.ForeColor = Color.OrangeRed;
        }
        else if (!string.IsNullOrEmpty(_config.DiscordGrantedScopes))
        {
            string granted = _config.DiscordGrantedScopes!;
            bool videoOk = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(s => string.Equals(s, DiscordScopes.RpcVideoRead, StringComparison.OrdinalIgnoreCase));
            if (videoOk)
            {
                _discordStatusLabel.Text = $"Authorized. Granted scopes: {granted}";
                _discordStatusLabel.ForeColor = Color.SeaGreen;
            }
            else
            {
                _discordStatusLabel.Text = $"Authorized but Discord did NOT grant rpc.video.read — camera state will not work. Granted: {granted}";
                _discordStatusLabel.ForeColor = Color.Firebrick;
            }
        }
        else
        {
            _discordStatusLabel.Text = "Authorized.";
            _discordStatusLabel.ForeColor = Color.SeaGreen;
        }

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

    private void RefreshAllPreviews()
    {
        foreach (string flagId in _flagRows.Keys)
        {
            RefreshRowPreview(flagId);
        }
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
            MessageBox.Show(this, $"Could not update autostart setting:\r\n{ex.Message}", AppConstants.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _configStore.Save(_config);
    }

    private async Task TestHaConnectionAsync()
    {
        string url = _haUrlBox.Text.Trim();
        string token = _haTokenBox.Text;
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
        {
            _haStatusLabel.ForeColor = Color.OrangeRed;
            _haStatusLabel.Text = "Enter a URL and token first.";
            return;
        }

        _haTestButton.Enabled = false;
        _haStatusLabel.ForeColor = Color.DimGray;
        _haStatusLabel.Text = "Connecting…";

        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
            await using HaWebSocketClient client = new(url, token);
            await client.ConnectAndAuthenticateAsync(cts.Token).ConfigureAwait(true);

            System.Text.Json.JsonElement listResult = await client.SendCommandAsync(
                new { type = "input_boolean/list" }, cts.Token).ConfigureAwait(true);
            int count = listResult.ValueKind == System.Text.Json.JsonValueKind.Array
                ? listResult.GetArrayLength()
                : 0;

            _haStatusLabel.ForeColor = Color.SeaGreen;
            _haStatusLabel.Text = $"Connected. Found {count} existing input_boolean helper(s).";
        }
        catch (Exception ex)
        {
            _haStatusLabel.ForeColor = Color.Firebrick;
            _haStatusLabel.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _haTestButton.Enabled = true;
        }
    }

    private async Task AuthorizeDiscordAsync()
    {
        string clientId = _discordClientIdBox.Text.Trim();
        string clientSecret = _discordClientSecretBox.Text;
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            _discordStatusLabel.ForeColor = Color.OrangeRed;
            _discordStatusLabel.Text = "Enter both Client ID and Client Secret first.";
            return;
        }

        _discordAuthorizeButton.Enabled = false;
        _discordStatusLabel.ForeColor = Color.DimGray;
        _discordStatusLabel.Text = "Stopping bridge…";

        await _bridge.StopAsync().ConfigureAwait(true);

        try
        {
            _discordStatusLabel.Text = "Connecting to Discord… approve the prompt in Discord when it appears.";
            using CancellationTokenSource cts = new(TimeSpan.FromMinutes(2));

            await using DiscordRpcSession session = new();
            string code = await session.AuthorizeAsync(clientId, cts.Token).ConfigureAwait(true);

            _discordStatusLabel.Text = "Exchanging code for token…";
            DiscordOAuth oauth = new();
            DiscordTokens tokens = await oauth.ExchangeCodeAsync(
                clientId, clientSecret, code, AppConstants.DiscordOAuthRedirectUri, cts.Token).ConfigureAwait(true);

            _config.DiscordClientId = clientId;
            _config.DiscordClientSecretProtected = SecretProtector.Protect(clientSecret);
            _config.DiscordAccessTokenProtected = SecretProtector.Protect(tokens.AccessToken);
            _config.DiscordAccessTokenExpiresAtUnix = tokens.ExpiresAt.ToUnixTimeSeconds();
            _config.DiscordRefreshTokenProtected = SecretProtector.Protect(tokens.RefreshToken);
            _config.DiscordAuthorizedScopeKey = DiscordScopes.CurrentKey();
            _config.DiscordGrantedScopes = tokens.GrantedScopes;
            _configStore.Save(_config);

            _discordStatusLabel.ForeColor = Color.SeaGreen;
            _discordStatusLabel.Text = "Authorized. Tokens cached — bridge will reconnect when you save.";
        }
        catch (Exception ex)
        {
            _discordStatusLabel.ForeColor = Color.Firebrick;
            _discordStatusLabel.Text = $"Failed: {ex.Message}";
        }
        finally
        {
            _discordAuthorizeButton.Enabled = true;
            _bridge.Start();
        }
    }
}
