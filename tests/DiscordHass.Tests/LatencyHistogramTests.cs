using System;
using DiscordHass.App;
using Xunit;

namespace DiscordHass.Tests;

public class LatencyHistogramTests
{
    [Fact]
    public void EmptyHistogram_ReturnsZeros()
    {
        LatencyHistogram h = new();
        LatencySnapshot snap = h.Snapshot();
        Assert.Equal(0, snap.Count);
        Assert.Equal(0, snap.P50Ms);
        Assert.Equal(0, snap.P95Ms);
        Assert.Equal(0, snap.P99Ms);
    }

    [Fact]
    public void Snapshot_ComputesPercentilesOnSmallSet()
    {
        LatencyHistogram h = new(capacity: 100);
        // Samples 1..100 ms.
        for (int i = 1; i <= 100; i++) h.Record(TimeSpan.FromMilliseconds(i));

        LatencySnapshot snap = h.Snapshot();
        Assert.Equal(100, snap.Count);
        // Nearest-rank: p50 of 100 samples is the 50th value when sorted.
        Assert.Equal(50, snap.P50Ms);
        Assert.Equal(95, snap.P95Ms);
        Assert.Equal(99, snap.P99Ms);
    }

    [Fact]
    public void RingBuffer_OverwritesOldestSamples()
    {
        LatencyHistogram h = new(capacity: 4);
        // First fill with 1..4
        h.Record(TimeSpan.FromMilliseconds(1));
        h.Record(TimeSpan.FromMilliseconds(2));
        h.Record(TimeSpan.FromMilliseconds(3));
        h.Record(TimeSpan.FromMilliseconds(4));
        // Then overwrite slots 0 and 1 with 5 and 6.
        h.Record(TimeSpan.FromMilliseconds(5));
        h.Record(TimeSpan.FromMilliseconds(6));

        LatencySnapshot snap = h.Snapshot();
        Assert.Equal(4, snap.Count);
        // The four most recent samples are 3, 4, 5, 6. p50 (rank ceil(0.5*4)=2) = 4.
        Assert.Equal(4, snap.P50Ms);
    }

    [Fact]
    public void NegativeOrNaN_StoredAsZero()
    {
        LatencyHistogram h = new();
        h.Record(TimeSpan.FromMilliseconds(-50));
        h.Record(TimeSpan.FromMilliseconds(0));
        h.Record(TimeSpan.FromMilliseconds(10));

        LatencySnapshot snap = h.Snapshot();
        Assert.Equal(3, snap.Count);
        // All zeros and 10 — p50 is 0 (rank 2 in sorted [0,0,10]).
        Assert.Equal(0, snap.P50Ms);
    }

    [Fact]
    public void Constructor_RejectsZeroOrNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatencyHistogram(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatencyHistogram(-1));
    }

    [Fact]
    public void SingleSample_ReturnsItselfForAllPercentiles()
    {
        LatencyHistogram h = new();
        h.Record(TimeSpan.FromMilliseconds(42));
        LatencySnapshot snap = h.Snapshot();
        Assert.Equal(1, snap.Count);
        Assert.Equal(42, snap.P50Ms);
        Assert.Equal(42, snap.P95Ms);
        Assert.Equal(42, snap.P99Ms);
    }
}
