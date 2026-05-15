using System;
using System.Globalization;
using System.IO;
using System.Text;
using DiscordHass.App;

namespace DiscordHass.Discord;

/// <summary>
/// Lightweight file logger for Discord RPC traffic, used as a diagnostic tool when
/// state propagation looks wrong (e.g. camera state not updating).
///
/// Cleanup policy:
///   - Truncated on every app launch (<see cref="ResetForSession"/>), so each session
///     starts with a clean file.
///   - Within a session, capped at <see cref="MaxBytes"/>. When exceeded, the current
///     file is rotated to "<name>.1" (overwriting any previous .1) and a fresh log
///     starts. Worst-case on-disk footprint is therefore ~2x MaxBytes.
/// </summary>
internal static class RpcDebugLog
{
    /// <summary>Soft cap per file; rotation kicks in shortly after this is exceeded.</summary>
    public const long MaxBytes = 5L * 1024 * 1024;

    private static readonly object _lock = new();
    private static string? _path;
    private static long _currentBytes;

    public static string Path
    {
        get
        {
            if (_path is null)
            {
                AppPaths.EnsureAppDataDirExists();
                _path = System.IO.Path.Combine(AppPaths.AppDataDir, "rpc-events.log");
            }
            return _path;
        }
    }

    public static string RotatedPath => Path + ".1";

    /// <summary>Truncate the log at app startup so each session is self-contained.</summary>
    public static void ResetForSession()
    {
        try
        {
            lock (_lock)
            {
                string header =
                    $"# DiscordHass RPC debug log\n" +
                    $"# Session started {DateTimeOffset.Now:O}\n" +
                    $"# Cap {MaxBytes / 1024 / 1024} MB per file, rotates to .1 on overflow.\n" +
                    $"# Format: [HH:mm:ss.fff] <kind> <label>: <json>\n\n";
                File.WriteAllText(Path, header);
                _currentBytes = Encoding.UTF8.GetByteCount(header);
            }
        }
        catch { /* best effort */ }
    }

    public static void LogSend(string commandOrEvent, string json) => Append("SEND", commandOrEvent, json);
    public static void LogRecv(string opCode, string json)         => Append("RECV", opCode, json);
    public static void LogEvent(string eventName, string json)     => Append("EVT ", eventName, json);
    public static void LogNote(string note)                        => Append("NOTE", "-",      note);

    private static void Append(string kind, string label, string content)
    {
        try
        {
            string line = string.Create(CultureInfo.InvariantCulture, $"[{DateTime.Now:HH:mm:ss.fff}] {kind} {label}: {content}\n");
            lock (_lock)
            {
                File.AppendAllText(Path, line);
                _currentBytes += Encoding.UTF8.GetByteCount(line);
                if (_currentBytes > MaxBytes)
                {
                    Rotate();
                }
            }
        }
        catch { /* best effort — never crash a receive loop because logging failed */ }
    }

    private static void Rotate()
    {
        try
        {
            string rotated = RotatedPath;
            if (File.Exists(rotated))
            {
                File.Delete(rotated);
            }
            File.Move(Path, rotated);

            string header =
                $"# DiscordHass RPC debug log (continued)\n" +
                $"# Rotated {DateTimeOffset.Now:O} — previous {MaxBytes / 1024 / 1024} MB moved to {System.IO.Path.GetFileName(rotated)}\n\n";
            File.WriteAllText(Path, header);
            _currentBytes = Encoding.UTF8.GetByteCount(header);
        }
        catch
        {
            // If rotation fails (e.g. file locked), drop the in-memory counter so we don't
            // hammer the rename path on every subsequent append.
            _currentBytes = 0;
        }
    }
}
