using System;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Update;

namespace DiscordHass.Ui;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayIconHost _trayHost;
    private readonly BridgeService _bridge;
    private readonly UpdateService _updates;

    public TrayApplicationContext(AppConfig config, ConfigStore configStore)
    {
        _bridge  = new BridgeService(config, configStore);
        _updates = new UpdateService(config, configStore);
        _trayHost = new TrayIconHost(config, configStore, _bridge, _updates);
        _trayHost.QuitRequested += OnQuitRequested;
        _trayHost.Show();
        _bridge.Start();
        _updates.Start();
    }

    private async void OnQuitRequested(object? sender, EventArgs e)
    {
        try
        {
            await _updates.StopAsync().ConfigureAwait(true);
            await _bridge.StopAsync().ConfigureAwait(true);
        }
        finally
        {
            ExitThread();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayHost.Dispose();
            _bridge.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _updates.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        base.Dispose(disposing);
    }
}
