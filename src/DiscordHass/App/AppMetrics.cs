using System;
using System.Diagnostics;
using System.Threading;

namespace DiscordHass.App;

/// <summary>
/// Process-wide counters + lightweight process samplers for the Settings → Performance section
/// and the diagnostics bundle. Counter fields are incremented via <see cref="Interlocked"/>,
/// so they're safe to call from any thread on any hot path. Sampler methods touch
/// <see cref="Process.GetCurrentProcess"/> and <see cref="GC"/> directly — never call them in a
/// tight loop. The intended cadence is 1Hz from a WinForms timer when the metrics view is open.
/// </summary>
internal static class AppMetrics
{
    // ===== Cumulative counters (incremented from event hot paths) =====
    public static long DiscordEventsReceived;
    public static long HaFramesSent;
    public static long HaFramesReceived;
    public static long CameraRegistryPolls;
    public static long DiscordReconnects;
    public static long HaReconnects;
    public static long HelperPublishes;
    public static long PublishTestInvocations;

    public static void IncrementDiscordEvent()     => Interlocked.Increment(ref DiscordEventsReceived);
    public static void IncrementHaFrameSent()      => Interlocked.Increment(ref HaFramesSent);
    public static void IncrementHaFrameReceived()  => Interlocked.Increment(ref HaFramesReceived);
    public static void IncrementCameraPoll()       => Interlocked.Increment(ref CameraRegistryPolls);
    public static void IncrementDiscordReconnect() => Interlocked.Increment(ref DiscordReconnects);
    public static void IncrementHaReconnect()      => Interlocked.Increment(ref HaReconnects);
    public static void IncrementHelperPublish()    => Interlocked.Increment(ref HelperPublishes);
    public static void IncrementTestPublish()      => Interlocked.Increment(ref PublishTestInvocations);

    // ===== Snapshots (read-on-demand) =====

    /// <summary>Total CPU time consumed by this process since start, in milliseconds.</summary>
    public static long CpuTimeMs()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return (long)p.TotalProcessorTime.TotalMilliseconds;
        }
        catch { return 0; }
    }

    public static long WorkingSetBytes()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return p.WorkingSet64;
        }
        catch { return 0; }
    }

    public static long PrivateBytes()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return p.PrivateMemorySize64;
        }
        catch { return 0; }
    }

    public static int ThreadCount()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return p.Threads.Count;
        }
        catch { return 0; }
    }

    public static int HandleCount()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return p.HandleCount;
        }
        catch { return 0; }
    }

    public static long GcAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);

    public static (int Gen0, int Gen1, int Gen2) GcCollections()
        => (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));

    public static TimeSpan ProcessUptime()
    {
        try
        {
            using Process p = Process.GetCurrentProcess();
            return DateTime.UtcNow - p.StartTime.ToUniversalTime();
        }
        catch { return TimeSpan.Zero; }
    }
}
