using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;

namespace DiscordHass.Ui;

internal sealed class TrayIconHost : IDisposable
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly BridgeService _bridge;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _autostartItem;
    private SettingsForm? _settingsForm;
    private StatusForm? _statusForm;

    public event EventHandler? QuitRequested;

    public TrayIconHost(AppConfig config, ConfigStore configStore, BridgeService bridge)
    {
        _config = config;
        _configStore = configStore;
        _bridge = bridge;

        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = AutostartManager.IsEnabled() };
        _autostartItem.Click += (_, _) => ToggleAutostart();

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
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_config, _configStore, _bridge);
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
        if (string.IsNullOrEmpty(_config.HaBaseUrl))
        {
            return;
        }
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

    private void OnBridgeStatusChanged(object? sender, EventArgs e)
    {
        if (_notifyIcon is null) return;
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
        catch (InvalidOperationException)
        {
            // window not yet created
        }
    }

    private void UpdateTrayText()
    {
        string discord = ShortPhase(_bridge.DiscordStatus.Phase);
        string ha = ShortPhase(_bridge.HaStatus.Phase);
        string text = $"{AppConstants.DisplayName}\nDiscord: {discord}\nHA: {ha}";
        if (text.Length > 127)
        {
            text = text[..127];
        }
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _settingsForm?.Dispose();
        _statusForm?.Dispose();
    }
}
