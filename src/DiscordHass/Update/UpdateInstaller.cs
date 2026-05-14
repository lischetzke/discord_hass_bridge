using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DiscordHass.Update;

internal sealed class UpdateInstallException : Exception
{
    public UpdateInstallException(string message, Exception? inner = null) : base(message, inner) { }
}

internal static class UpdateInstaller
{
    public const string PostUpdateArg = "--post-update";
    public const string WaitForPidArg = "--wait-for-pid";
    public const string OldExeSuffix = ".old";

    /// <summary>
    /// Performs the rename-swap dance, launches the new exe with `--post-update --wait-for-pid &lt;ourPid&gt;`,
    /// and returns once the new process has been spawned. Caller is expected to immediately exit the
    /// process (e.g. Application.Exit()) so the new instance can claim the singleton mutex.
    /// </summary>
    public static Task SwapAndRelaunchAsync(string newExeStagingPath)
    {
        string currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new UpdateInstallException("Cannot determine current executable path");

        if (!File.Exists(newExeStagingPath))
        {
            throw new UpdateInstallException($"Staging file not found: {newExeStagingPath}");
        }

        string oldSiblingPath = currentExe + OldExeSuffix;

        // Clean any stale .old sibling so the move below can succeed.
        TryDelete(oldSiblingPath);

        // Step 1: rename the running exe out of the way. Windows allows renaming a running PE.
        try
        {
            File.Move(currentExe, oldSiblingPath);
        }
        catch (Exception ex)
        {
            throw new UpdateInstallException(
                $"Could not move running exe to {oldSiblingPath}: {ex.Message}", ex);
        }

        // Step 2: move the staged new exe into the canonical location.
        try
        {
            File.Move(newExeStagingPath, currentExe);
        }
        catch (Exception ex)
        {
            // Rollback: put the old exe back where it was so the user isn't stranded.
            TryMove(oldSiblingPath, currentExe);
            throw new UpdateInstallException(
                $"Could not move new exe to {currentExe}: {ex.Message}", ex);
        }

        // Step 3: launch the new exe with args telling it to wait for our exit + clean up.
        int ourPid = Environment.ProcessId;
        ProcessStartInfo psi = new()
        {
            FileName        = currentExe,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(currentExe)!,
        };
        psi.ArgumentList.Add(PostUpdateArg);
        psi.ArgumentList.Add(WaitForPidArg);
        psi.ArgumentList.Add(ourPid.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new UpdateInstallException($"Could not launch new exe: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Best-effort delete of the .old sibling left by a previous update. Safe to call on every launch.
    /// </summary>
    public static void CleanupOldSibling()
    {
        try
        {
            string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe)) return;
            string oldPath = currentExe + OldExeSuffix;
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }
        catch
        {
            // Best effort — Windows may still be holding the file briefly after the previous exit.
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryMove(string src, string dst)
    {
        try { if (File.Exists(src)) File.Move(src, dst); } catch { /* best effort */ }
    }
}
