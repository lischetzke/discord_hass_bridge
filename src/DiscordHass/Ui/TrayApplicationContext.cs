using System;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;

namespace DiscordHass.Ui;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayIconHost _trayHost;
    private readonly BridgeService _bridge;

    public TrayApplicationContext(AppConfig config, ConfigStore configStore)
    {
        _bridge = new BridgeService(config, configStore);
        _trayHost = new TrayIconHost(config, configStore, _bridge);
        _trayHost.QuitRequested += OnQuitRequested;
        _trayHost.Show();
        _bridge.Start();
    }

    private async void OnQuitRequested(object? sender, EventArgs e)
    {
        try
        {
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
        }
        base.Dispose(disposing);
    }
}
