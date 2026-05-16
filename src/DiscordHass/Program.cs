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
    /// True when the user has used the app before, so the first-run wizard should be
    /// suppressed. Originally this checked all four credentials (HA URL + HA token +
    /// Discord client id + Discord refresh token), but that was too strict: BridgeService
    /// clears <c>DiscordRefreshTokenProtected</c> whenever the required scope set changes
    /// between versions (the v0.1.x → v0.2.0 drop of <c>rpc.video.read</c> hit exactly this
    /// path), and the user's "Clear cached tokens" button does too. Both cases left
    /// long-time users staring at a wizard despite having a perfectly working setup.
    ///
    /// The real signal we need is "has the user ever entered an HA URL". A non-empty
    /// HaBaseUrl means they've gone through Settings at least once — they don't need the
    /// wizard, and anything missing is clearly visible on the Settings section status
    /// chips. Fresh installs (config.json freshly created) still have an empty HaBaseUrl
    /// and correctly get the wizard.
    /// </summary>
    internal static bool LooksConfigured(AppConfig c) =>
        !string.IsNullOrWhiteSpace(c.HaBaseUrl);

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
