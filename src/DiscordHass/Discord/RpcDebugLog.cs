using System;
using System.Globalization;
using System.IO;
using DiscordHass.App;

namespace DiscordHass.Discord;

/// <summary>
/// Lightweight file logger for Discord RPC traffic, used as a diagnostic tool when
/// state propagation looks wrong (e.g. camera state not updating). One file per
/// session — truncated on each launch so the user can reproduce, then send the log.
/// </summary>
internal static class RpcDebugLog
{
    private static readonly object _lock = new();
    private static string? _path;

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

    /// <summary>Truncate the log at app startup so each session is self-contained.</summary>
    public static void ResetForSession()
    {
        try
        {
            lock (_lock)
            {
                File.WriteAllText(Path,
                    $"# DiscordHass RPC debug log\n" +
                    $"# Session started {DateTimeOffset.Now:O}\n" +
                    $"# Format: [HH:mm:ss.fff] <kind> <label>: <json>\n\n");
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
            }
        }
        catch { /* best effort — never crash a receive loop because logging failed */ }
    }
}
