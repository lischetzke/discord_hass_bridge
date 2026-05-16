using System;
using System.Globalization;
using System.Text;

namespace DiscordHass.App;

/// <summary>
/// One-stop formatter that turns the live <see cref="AppMetrics"/> + <see cref="LatencyHistogram"/>
/// snapshots into the same multi-line report shown on Settings → Performance, and bundled into
/// the diagnostics zip's <c>performance.txt</c>. Pure function — no I/O, safe to unit-test.
///
/// The output intentionally uses ASCII formatting (no unicode dots) so it survives email + GitHub
/// comment quoting unchanged.
/// </summary>
internal static class PerformanceReport
{
    /// <summary>
    /// Format the current snapshot. Pass <see cref="LatencyHistogram"/> directly so callers can
    /// hand in the bridge's instance without exposing the bridge type to formatters.
    /// </summary>
    public static string Format(LatencyHistogram? publishLatency)
    {
        TimeSpan uptime = AppMetrics.ProcessUptime();
        long cpuMs   = AppMetrics.CpuTimeMs();
        long wsBytes = AppMetrics.WorkingSetBytes();
        long pvBytes = AppMetrics.PrivateBytes();
        long allocB  = AppMetrics.GcAllocatedBytes();
        (int g0, int g1, int g2) = AppMetrics.GcCollections();

        StringBuilder sb = new();
        sb.AppendLine($"Uptime:          {FormatUptime(uptime)}");
        sb.AppendLine($"CPU time:        {cpuMs:N0} ms");
        sb.AppendLine($"Memory:          ws {FormatBytes(wsBytes)} / priv {FormatBytes(pvBytes)}");
        sb.AppendLine($"GC:              {FormatBytes(allocB)} alloc - Gen 0/1/2 {g0}/{g1}/{g2}");
        sb.AppendLine($"Threads/handles: {AppMetrics.ThreadCount()} / {AppMetrics.HandleCount()}");
        sb.AppendLine($"Discord events:  {AppMetrics.DiscordEventsReceived:N0}");
        sb.AppendLine($"HA frames:       sent {AppMetrics.HaFramesSent:N0} / recv {AppMetrics.HaFramesReceived:N0}");
        sb.AppendLine($"Camera polls:    {AppMetrics.CameraRegistryPolls:N0}");
        sb.AppendLine($"Reconnects:      Discord {AppMetrics.DiscordReconnects} / HA {AppMetrics.HaReconnects}");
        sb.AppendLine($"Helper publish:  {AppMetrics.HelperPublishes:N0}  (test {AppMetrics.PublishTestInvocations})");

        if (publishLatency is not null)
        {
            LatencySnapshot snap = publishLatency.Snapshot();
            if (snap.Count == 0)
            {
                sb.AppendLine("Publish latency: (no samples yet)");
            }
            else
            {
                CultureInfo c = CultureInfo.InvariantCulture;
                sb.Append("Publish latency: p50 ").Append(snap.P50Ms.ToString("0.#", c)).Append(" ms / ");
                sb.Append("p95 ").Append(snap.P95Ms.ToString("0.#", c)).Append(" ms / ");
                sb.Append("p99 ").Append(snap.P99Ms.ToString("0.#", c)).Append(" ms / n=").Append(snap.Count);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public static string FormatBytes(long bytes)
    {
        // InvariantCulture so the output is grep-friendly regardless of where the bundle was
        // generated (German locales use a comma as decimal separator otherwise).
        CultureInfo c = CultureInfo.InvariantCulture;
        if (bytes < 1024) return bytes.ToString(c) + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", c) + " KB";
        if (bytes < 1024L * 1024L * 1024L) return (bytes / 1024.0 / 1024.0).ToString("0.#", c) + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.##", c) + " GB";
    }

    public static string FormatUptime(TimeSpan ts)
        => ts.Days > 0 ? $"{ts.Days}d {ts:hh\\:mm\\:ss}" : ts.ToString(@"hh\:mm\:ss");
}
