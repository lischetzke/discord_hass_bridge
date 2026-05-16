using System;

namespace DiscordHass.App;

/// <summary>
/// One snapshot of recent publish latencies, in milliseconds. <see cref="Count"/> is the number
/// of samples currently in the ring buffer (0 when nothing has been recorded yet); the percentile
/// fields are 0 in that case.
/// </summary>
internal readonly record struct LatencySnapshot(double P50Ms, double P95Ms, double P99Ms, int Count);

/// <summary>
/// Fixed-capacity ring buffer of recent latency samples for p50/p95/p99 reporting on the
/// Settings → Performance section. Not designed for high-frequency recording — call
/// <see cref="Record"/> at the cadence of HA publishes (a few per second at most) and
/// <see cref="Snapshot"/> only when the UI needs to refresh. Reads + writes are guarded by
/// a per-instance lock; <see cref="Snapshot"/> allocates a sort copy of the filled samples.
/// </summary>
internal sealed class LatencyHistogram
{
    private readonly object _gate = new();
    private readonly double[] _samples;
    private int _next;
    private int _filled;

    public LatencyHistogram(int capacity = 256)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;

    public void Record(TimeSpan latency)
    {
        double ms = latency.TotalMilliseconds;
        if (double.IsNaN(ms) || ms < 0) ms = 0;
        lock (_gate)
        {
            _samples[_next] = ms;
            _next = (_next + 1) % _samples.Length;
            if (_filled < _samples.Length) _filled++;
        }
    }

    public LatencySnapshot Snapshot()
    {
        double[] copy;
        int count;
        lock (_gate)
        {
            count = _filled;
            if (count == 0) return new LatencySnapshot(0, 0, 0, 0);
            copy = new double[count];
            Array.Copy(_samples, copy, count);
        }
        Array.Sort(copy);
        return new LatencySnapshot(
            Percentile(copy, 0.50),
            Percentile(copy, 0.95),
            Percentile(copy, 0.99),
            count);
    }

    /// <summary>
    /// Nearest-rank percentile on the already-sorted array. Matches the convention used by
    /// most monitoring tools (Datadog, Prometheus `quantile`).
    /// </summary>
    private static double Percentile(double[] sorted, double q)
    {
        if (sorted.Length == 0) return 0;
        if (sorted.Length == 1) return sorted[0];
        int rank = (int)Math.Ceiling(q * sorted.Length);
        if (rank < 1) rank = 1;
        if (rank > sorted.Length) rank = sorted.Length;
        return sorted[rank - 1];
    }
}
