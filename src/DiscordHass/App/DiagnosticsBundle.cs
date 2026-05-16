using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using DiscordHass.Config;

namespace DiscordHass.App;

/// <summary>
/// Builds a self-contained zip of redacted config + RPC log + environment summary that the user
/// can attach to an issue report. All paths are resolved against <see cref="AppPaths"/>; the
/// archive is written to <c>%TEMP%\DiscordHass-diag-&lt;timestamp&gt;.zip</c> and the path returned
/// so the caller can reveal it in Explorer.
/// </summary>
internal static class DiagnosticsBundle
{
    /// <summary>
    /// Builds and returns a <see cref="FileInfo"/> for the newly written zip. The caller is
    /// responsible for opening it / showing it to the user. Throws on I/O failures so the UI
    /// can surface a meaningful error.
    /// </summary>
    public static FileInfo Create(AppConfig config, BridgeService bridge)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (bridge is null) throw new ArgumentNullException(nameof(bridge));

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string zipPath = Path.Combine(Path.GetTempPath(), $"DiscordHass-diag-{timestamp}.zip");

        if (File.Exists(zipPath)) File.Delete(zipPath);
        using FileStream fs = new(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive zip = new(fs, ZipArchiveMode.Create);

        TryAddRpcLog(zip);
        TryAddRedactedConfig(zip);
        AddPerformanceTxt(zip, bridge);
        AddDiagnosticsTxt(zip, config, bridge);

        return new FileInfo(zipPath);
    }

    private static void AddPerformanceTxt(ZipArchive zip, BridgeService bridge)
    {
        try
        {
            string body =
                "DiscordHass performance snapshot" + Environment.NewLine +
                "Generated (UTC): " + DateTime.UtcNow.ToString("O") + Environment.NewLine +
                "App version:     " + AppConstants.GetVersionString() + Environment.NewLine +
                Environment.NewLine +
                PerformanceReport.Format(bridge.PublishLatency);
            ZipArchiveEntry entry = zip.CreateEntry("performance.txt", CompressionLevel.Optimal);
            using Stream s = entry.Open();
            using StreamWriter w = new(s, Encoding.UTF8);
            w.Write(body);
        }
        catch
        {
            // Best effort — never fail the whole bundle over the perf file.
        }
    }

    private static void TryAddRpcLog(ZipArchive zip)
    {
        string logPath = Path.Combine(AppPaths.AppDataDir, "rpc-events.log");
        if (!File.Exists(logPath)) return;
        try
        {
            zip.CreateEntryFromFile(logPath, "rpc-events.log", CompressionLevel.Optimal);
        }
        catch
        {
            // Best effort — the log file may be open by the running app or unreadable.
        }
    }

    private static void TryAddRedactedConfig(ZipArchive zip)
    {
        if (!File.Exists(AppPaths.ConfigFile)) return;
        try
        {
            string raw = File.ReadAllText(AppPaths.ConfigFile);
            string redacted = DiagnosticsRedactor.Redact(raw);
            ZipArchiveEntry entry = zip.CreateEntry("config-redacted.json", CompressionLevel.Optimal);
            using Stream s = entry.Open();
            using StreamWriter w = new(s, Encoding.UTF8);
            w.Write(redacted);
        }
        catch
        {
            // Best effort.
        }
    }

    private static void AddDiagnosticsTxt(ZipArchive zip, AppConfig config, BridgeService bridge)
    {
        StringBuilder sb = new();
        sb.AppendLine($"DiscordHass diagnostics");
        sb.AppendLine($"Generated (UTC):     {DateTime.UtcNow:O}");
        sb.AppendLine($"App version:         {AppConstants.GetVersionString()}");
        sb.AppendLine($"OS:                  {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
        sb.AppendLine($".NET version:        {Environment.Version}");
        sb.AppendLine($"Process uptime:      {AppMetrics.ProcessUptime()}");
        sb.AppendLine();
        sb.AppendLine("=== Connection state ===");
        sb.AppendLine($"Discord phase:       {bridge.DiscordStatus.Phase}");
        sb.AppendLine($"Discord last error:  {bridge.DiscordStatus.LastError ?? "(none)"}");
        sb.AppendLine($"Discord changed at:  {bridge.DiscordStatus.ChangedAt?.ToString("O") ?? "(never)"}");
        sb.AppendLine($"HA phase:            {bridge.HaStatus.Phase}");
        sb.AppendLine($"HA last error:       {bridge.HaStatus.LastError ?? "(none)"}");
        sb.AppendLine($"HA changed at:       {bridge.HaStatus.ChangedAt?.ToString("O") ?? "(never)"}");
        sb.AppendLine($"Discord username:    {bridge.DiscordUserName ?? "(unknown)"}");
        sb.AppendLine();
        sb.AppendLine("=== Config (non-secret) ===");
        sb.AppendLine($"Onboarding done:     {config.HasCompletedOnboarding}");
        sb.AppendLine($"Granted scopes:      {config.DiscordGrantedScopes ?? "(unknown)"}");
        sb.AppendLine($"Authorized scope key:{config.DiscordAuthorizedScopeKey ?? "(none)"}");
        sb.AppendLine($"Helper prefix:       {config.HelperPrefix}");
        sb.AppendLine($"Enabled flags:       {string.Join(", ", config.EnabledFlags)}");
        sb.AppendLine($"Autostart:           {config.AutostartEnabled}");
        sb.AppendLine($"Auto-update:         {config.CheckUpdatesAutomatically}");
        sb.AppendLine($"Minimize to tray:    {config.MinimizeToTrayOnClose}");
        sb.AppendLine();
        sb.AppendLine("=== Performance (see also performance.txt) ===");
        sb.Append(PerformanceReport.Format(bridge.PublishLatency));

        ZipArchiveEntry entry = zip.CreateEntry("diagnostics.txt", CompressionLevel.Optimal);
        using Stream s = entry.Open();
        using StreamWriter w = new(s, Encoding.UTF8);
        w.Write(sb.ToString());
    }
}
