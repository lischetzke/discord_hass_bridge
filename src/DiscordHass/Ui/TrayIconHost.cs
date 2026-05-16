using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Update;

namespace DiscordHass.Ui;

internal sealed class TrayIconHost : IDisposable
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly BridgeService _bridge;
    private readonly UpdateService _updates;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _updateItem;
    private readonly Dictionary<TrayIconVariant, Icon> _iconCache = new();
    private SettingsForm? _settingsForm;
    private OverviewForm? _overviewForm;
    private OnboardingWizardForm? _wizardForm;
    private UpdateProgressForm? _updateForm;

    public event EventHandler? QuitRequested;

    private enum TrayIconVariant { Idle, Ok, Warn, Fault }

    public TrayIconHost(AppConfig config, ConfigStore configStore, BridgeService bridge, UpdateService updates)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;
        _updates = updates;

        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutostartManager.IsEnabled() };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _updateItem = new ToolStripMenuItem("Check for updates…");
        _updateItem.Click += async (_, _) => await OnUpdateMenuClickedAsync().ConfigureAwait(true);

        _menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(TrayIconVariant.Idle),
            Visible = false,
            Text = AppConstants.DisplayName,
            ContextMenuStrip = _menu,
        };
        // Single left-click → Overview. Right-click is intercepted by ContextMenuStrip.
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenOverview();
        };
        // Keep double-click as a redundant fallback for users used to the old behaviour.
        _notifyIcon.DoubleClick += (_, _) => OpenOverview();

        _bridge.StatusChanged += OnBridgeStatusChanged;
        _updates.StateChanged += OnUpdateStateChanged;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
        UpdateTrayVisual();

        // First-run wizard takes priority over showing Settings. The Program.LooksConfigured
        // backfill ensures upgrading users from v0.1.x don't get the wizard.
        if (!_config.HasCompletedOnboarding)
        {
            OpenWizard();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("Overview…", null, (_, _) => OpenOverview());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Home Assistant", null, (_, _) => OpenHaInBrowser());
        menu.Items.Add("Reconnect", null, async (_, _) => await _bridge.RestartAsync().ConfigureAwait(false));
        menu.Items.Add("Help…", null, (_, _) => HelpDialog.ShowTopic(null, HelpContent.TopicIds.OverviewIntro));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void OpenOverview()
    {
        if (_wizardForm is not null && !_wizardForm.IsDisposed)
        {
            _wizardForm.Activate();
            return;
        }

        if (_overviewForm is null || _overviewForm.IsDisposed)
        {
            _overviewForm = new OverviewForm(_config, _bridge);
            _overviewForm.SettingsRequested        += (_, _) => OpenSettings();
            _overviewForm.ReconnectRequested       += async (_, _) => await _bridge.RestartAsync().ConfigureAwait(true);
            _overviewForm.OpenHaRequested          += (_, _) => OpenHaInBrowser();
            _overviewForm.OpenHelpRequested        += (_, _) => HelpDialog.ShowTopic(_overviewForm, HelpContent.TopicIds.OverviewIntro);
            _overviewForm.CopyDiagnosticsRequested += (_, _) => CreateAndShowDiagnostics(_overviewForm);
            _overviewForm.FormClosed += (_, _) => _overviewForm = null;
        }
        _overviewForm.Show();
        _overviewForm.Activate();
    }

    private void OpenSettings()
    {
        if (_wizardForm is not null && !_wizardForm.IsDisposed)
        {
            _wizardForm.Activate();
            return;
        }

        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_config, _configStore, _bridge, _updates);
            _settingsForm.FormClosed += (_, _) =>
            {
                _settingsForm = null;
                _autostartItem.Checked = AutostartManager.IsEnabled();
            };
        }
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OpenWizard()
    {
        if (_wizardForm is not null && !_wizardForm.IsDisposed)
        {
            _wizardForm.Activate();
            return;
        }
        _wizardForm = new OnboardingWizardForm(_config, _configStore, _bridge);
        _wizardForm.FormClosed += (_, _) =>
        {
            _wizardForm = null;
            _autostartItem.Checked = AutostartManager.IsEnabled();
            // If the user finished the wizard, open the Overview so they can see the live state.
            if (_config.HasCompletedOnboarding)
            {
                OpenOverview();
            }
        };
        _wizardForm.Show();
        _wizardForm.Activate();
    }

    private void OpenHaInBrowser()
    {
        if (string.IsNullOrEmpty(_config.HaBaseUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = _config.HaBaseUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}", AppConstants.DisplayName);
        }
    }

    private void CreateAndShowDiagnostics(IWin32Window? owner)
    {
        try
        {
            System.IO.FileInfo file = DiagnosticsBundle.Create(_config, _bridge);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{file.FullName}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                MessageBox.Show(owner, $"Diagnostics bundle written to:\r\n{file.FullName}", AppConstants.DisplayName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not create diagnostics bundle:\r\n{ex.Message}", AppConstants.DisplayName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            AutostartManager.SetEnabled(_autostartItem.Checked);
            _config.AutostartEnabled = _autostartItem.Checked;
            _configStore.Save(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update autostart: {ex.Message}", AppConstants.DisplayName);
            _autostartItem.Checked = AutostartManager.IsEnabled();
        }
    }

    private async System.Threading.Tasks.Task OnUpdateMenuClickedAsync()
    {
        UpdateAvailable? available = _updates.Available;
        if (available is null)
        {
            _updateItem.Enabled = false;
            _updateItem.Text = "Checking for updates…";
            await _updates.CheckNowAsync().ConfigureAwait(true);
            available = _updates.Available;
            _updateItem.Enabled = true;
            RefreshUpdateMenuItem();

            if (available is null)
            {
                _notifyIcon.BalloonTipTitle = AppConstants.DisplayName;
                _notifyIcon.BalloonTipText = $"You're up to date (v{AppConstants.GetVersionString()}).";
                _notifyIcon.ShowBalloonTip(3000);
                return;
            }
        }

        ShowUpdateForm(available);
    }

    private void ShowUpdateForm(UpdateAvailable available)
    {
        DialogResult confirm = MessageBox.Show(
            $"Install update {available.TagName}? The app will restart automatically.",
            $"{AppConstants.DisplayName} — Update available",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (confirm != DialogResult.Yes) return;

        if (_updateForm is null || _updateForm.IsDisposed)
        {
            _updateForm = new UpdateProgressForm(_updates, available);
            _updateForm.FormClosed += (_, _) => _updateForm = null;
        }
        _updateForm.Show();
        _updateForm.Activate();
    }

    private void OnBridgeStatusChanged(object? sender, EventArgs e)
    {
        try
        {
            if (_menu.InvokeRequired)
            {
                _menu.BeginInvoke(new Action(UpdateTrayVisual));
            }
            else
            {
                UpdateTrayVisual();
            }
        }
        catch (InvalidOperationException) { /* window not yet created */ }
    }

    private void OnUpdateStateChanged(object? sender, EventArgs e)
    {
        try
        {
            if (_menu.InvokeRequired)
            {
                _menu.BeginInvoke(new Action(() => { RefreshUpdateMenuItem(); MaybeShowAvailableBalloon(); }));
            }
            else
            {
                RefreshUpdateMenuItem();
                MaybeShowAvailableBalloon();
            }
        }
        catch (InvalidOperationException) { }
    }

    private void RefreshUpdateMenuItem()
    {
        switch (_updates.State)
        {
            case UpdateState.UpdateAvailable when _updates.Available is not null:
                _updateItem.Text = $"Install update: {_updates.Available.TagName}…";
                _updateItem.Image = null;
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Bold);
                break;
            case UpdateState.Checking:
                _updateItem.Text = "Checking for updates…";
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Regular);
                break;
            case UpdateState.Downloading:
                _updateItem.Text = "Downloading update…";
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Regular);
                break;
            case UpdateState.Installing:
                _updateItem.Text = "Installing update…";
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Regular);
                break;
            case UpdateState.Faulted:
                _updateItem.Text = "Check for updates… (last check failed)";
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Regular);
                break;
            default:
                _updateItem.Text = "Check for updates…";
                _updateItem.Font = new Font(_updateItem.Font, FontStyle.Regular);
                break;
        }
    }

    private DateTime _lastBalloonAt = DateTime.MinValue;

    private void MaybeShowAvailableBalloon()
    {
        if (_updates.State != UpdateState.UpdateAvailable || _updates.Available is null) return;
        if ((DateTime.UtcNow - _lastBalloonAt) < TimeSpan.FromHours(6)) return;
        _lastBalloonAt = DateTime.UtcNow;

        _notifyIcon.BalloonTipTitle = $"{AppConstants.DisplayName} — update available";
        _notifyIcon.BalloonTipText  = $"{_updates.Available.TagName} is ready to install. Right-click the tray icon to install.";
        _notifyIcon.ShowBalloonTip(5000);
    }

    private void UpdateTrayVisual()
    {
        TrayIconVariant variant = PickVariant(_bridge.DiscordStatus.Phase, _bridge.HaStatus.Phase);
        _notifyIcon.Icon = LoadIcon(variant);
        _notifyIcon.Text = BuildTooltip();
    }

    private string BuildTooltip()
    {
        string discord = ShortPhase(_bridge.DiscordStatus.Phase);
        string ha = ShortPhase(_bridge.HaStatus.Phase);
        string text = $"{AppConstants.DisplayName}\nDiscord: {discord}\nHA: {ha}";
        // NotifyIcon tooltip is limited to 127 chars.
        return text.Length > 127 ? text[..127] : text;
    }

    private static TrayIconVariant PickVariant(ConnectionPhase discord, ConnectionPhase ha)
    {
        // Worst-case selection: any fault wins, then any in-progress, then OK if both connected,
        // else idle.
        if (discord == ConnectionPhase.Faulted || ha == ConnectionPhase.Faulted)
            return TrayIconVariant.Fault;
        if (discord is ConnectionPhase.Connecting or ConnectionPhase.Reconnecting
         || ha      is ConnectionPhase.Connecting or ConnectionPhase.Reconnecting)
            return TrayIconVariant.Warn;
        if (discord == ConnectionPhase.Connected && ha == ConnectionPhase.Connected)
            return TrayIconVariant.Ok;
        return TrayIconVariant.Idle;
    }

    private Icon LoadIcon(TrayIconVariant variant)
    {
        if (_iconCache.TryGetValue(variant, out Icon? cached)) return cached;

        string resourceName = variant switch
        {
            TrayIconVariant.Ok    => "DiscordHass.tray-ok.ico",
            TrayIconVariant.Warn  => "DiscordHass.tray-warn.ico",
            TrayIconVariant.Fault => "DiscordHass.tray-fault.ico",
            _                     => "DiscordHass.tray-idle.ico",
        };
        System.Reflection.Assembly assembly = typeof(TrayIconHost).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(resourceName)
                                       ?? assembly.GetManifestResourceStream("DiscordHass.tray.ico");
        Icon icon = stream is null ? SystemIcons.Application : new Icon(stream);
        _iconCache[variant] = icon;
        return icon;
    }

    private static string ShortPhase(ConnectionPhase phase) => phase switch
    {
        ConnectionPhase.Idle => "idle",
        ConnectionPhase.Connecting => "connecting",
        ConnectionPhase.Connected => "connected",
        ConnectionPhase.Reconnecting => "reconnecting",
        ConnectionPhase.Faulted => "fault",
        _ => phase.ToString().ToLowerInvariant(),
    };

    public void Dispose()
    {
        _bridge.StatusChanged -= OnBridgeStatusChanged;
        _updates.StateChanged -= OnUpdateStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _settingsForm?.Dispose();
        _overviewForm?.Dispose();
        _wizardForm?.Dispose();
        _updateForm?.Dispose();
        foreach (Icon icon in _iconCache.Values)
        {
            icon.Dispose();
        }
    }
}
