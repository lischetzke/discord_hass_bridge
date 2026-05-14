using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Ui;
using DiscordHass.Update;

namespace DiscordHass;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // If we were just relaunched after an update, wait for the previous instance to die
        // before claiming the singleton mutex; otherwise we'll see a still-running mutex owner
        // and exit immediately, leaving the user with no app.
        HandlePostUpdateArgs(args);

        // Clean up the .old sibling left by a previous update (best effort).
        UpdateInstaller.CleanupOldSibling();

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

    private static void HandlePostUpdateArgs(string[] args)
    {
        if (args.Length == 0) return;

        bool postUpdate = false;
        int waitForPid = 0;

        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], UpdateInstaller.PostUpdateArg, StringComparison.Ordinal))
            {
                postUpdate = true;
            }
            else if (string.Equals(args[i], UpdateInstaller.WaitForPidArg, StringComparison.Ordinal)
                     && i + 1 < args.Length
                     && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid))
            {
                waitForPid = pid;
                i++;
            }
        }

        if (!postUpdate || waitForPid <= 0) return;

        try
        {
            using Process prev = Process.GetProcessById(waitForPid);
            prev.WaitForExit(milliseconds: 15000);
        }
        catch (ArgumentException)
        {
            // Process is already gone — nothing to wait for.
        }
        catch (InvalidOperationException)
        {
            // Already exited between query and wait.
        }
        catch
        {
            // Any other error — give it a brief grace period and proceed.
            Thread.Sleep(500);
        }
    }
}
