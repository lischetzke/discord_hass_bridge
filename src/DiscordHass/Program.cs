using System;
using System.Threading;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Ui;

namespace DiscordHass;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using Mutex singleton = new(initiallyOwned: true, AppConstants.SingletonMutexName, out bool createdNew);
        if (!createdNew)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();

        AppPaths.EnsureAppDataDirExists();
        ConfigStore configStore = new();
        AppConfig config = configStore.Load();

        using ApplicationContext appContext = new TrayApplicationContext(config, configStore);
        Application.Run(appContext);
        return 0;
    }
}
