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
        DiscordHass.Discord.RpcDebugLog.ResetForSession();
        ConfigStore configStore = new();
        AppConfig config = configStore.Load();

        // Upgrade-safe wizard gating: if the existing config already has every credential the
        // wizard would set, mark onboarding complete so v0.1.x users never see the wizard.
        // Idempotent — safe to run every launch.
        if (!config.HasCompletedOnboarding && LooksConfigured(config))
        {
            config.HasCompletedOnboarding = true;
            try { configStore.Save(config); } catch { /* tolerate */ }
        }

        using ApplicationContext appContext = new TrayApplicationContext(config, configStore);
        Application.Run(appContext);
        return 0;
    }

    /// <summary>
    /// True when every credential the onboarding wizard would set is already present.
    /// Strictly stronger than "have they used the app before?" — partial setups intentionally
    /// still trigger the wizard so missing pieces get guided.
    /// </summary>
    internal static bool LooksConfigured(AppConfig c) =>
           !string.IsNullOrWhiteSpace(c.HaBaseUrl)
        && !string.IsNullOrWhiteSpace(c.HaTokenProtected)
        && !string.IsNullOrWhiteSpace(c.DiscordClientId)
        && !string.IsNullOrWhiteSpace(c.DiscordRefreshTokenProtected);

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
