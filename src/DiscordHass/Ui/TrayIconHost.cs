using System;
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
    private SettingsForm? _settingsForm;
    private StatusForm? _statusForm;
    private UpdateProgressForm? _updateForm;

    public event EventHandler? QuitRequested;

    public TrayIconHost(AppConfig config, ConfigStore configStore, BridgeService bridge, UpdateService updates)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;
        _updates = updates;

        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutostartManager.IsEnabled() };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        _updateItem = new ToolStripMenuItem("Check for updates…")
        {
            Visible = true,
        };
        _updateItem.Click += async (_, _) => await OnUpdateMenuClickedAsync().ConfigureAwait(true);

        _menu = BuildMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = false,
            Text = AppConstants.DisplayName,
            ContextMenuStrip = _menu,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenStatus();

        _bridge.StatusChanged += OnBridgeStatusChanged;
        _updates.StateChanged += OnUpdateStateChanged;
    }

    public void Show()
    {
        _notifyIcon.Visible = true;
        UpdateTrayText();
        if (string.IsNullOrEmpty(_config.HaBaseUrl) || string.IsNullOrEmpty(_config.DiscordClientId))
        {
            OpenSettings();
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        ContextMenuStrip menu = new();
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add("Status…", null, (_, _) => OpenStatus());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open Home Assistant", null, (_, _) => OpenHaInBrowser());
        menu.Items.Add("Reconnect", null, async (_, _) => await _bridge.RestartAsync().ConfigureAwait(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_updateItem);
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void OpenSettings()
    {
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

    private void OpenStatus()
    {
        if (_statusForm is null || _statusForm.IsDisposed)
        {
            _statusForm = new StatusForm(_config, _bridge);
            _statusForm.FormClosed += (_, _) => _statusForm = null;
        }
        _statusForm.Show();
        _statusForm.Activate();
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
                _menu.BeginInvoke(new Action(UpdateTrayText));
            }
            else
            {
                UpdateTrayText();
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

    private void UpdateTrayText()
    {
        string discord = ShortPhase(_bridge.DiscordStatus.Phase);
        string ha = ShortPhase(_bridge.HaStatus.Phase);
        string text = $"{AppConstants.DisplayName}\nDiscord: {discord}\nHA: {ha}";
        if (text.Length > 127) text = text[..127];
        _notifyIcon.Text = text;
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

    private static Icon LoadAppIcon()
    {
        System.Reflection.Assembly assembly = typeof(TrayIconHost).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream("DiscordHass.tray.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream);
    }

    public void Dispose()
    {
        _bridge.StatusChanged -= OnBridgeStatusChanged;
        _updates.StateChanged -= OnUpdateStateChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _settingsForm?.Dispose();
        _statusForm?.Dispose();
        _updateForm?.Dispose();
    }
}
