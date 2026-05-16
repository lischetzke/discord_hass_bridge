using System;
using DiscordHass.App;
using Xunit;

namespace DiscordHass.Tests;

public class PerformanceReportTests
{
    [Theory]
    [InlineData(0,            "0 B")]
    [InlineData(512,          "512 B")]
    [InlineData(1024,         "1 KB")]
    [InlineData(1500,         "1.5 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(30L * 1024 * 1024, "30 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void FormatBytes_HandlesRanges(long input, string expected)
    {
        Assert.Equal(expected, PerformanceReport.FormatBytes(input));
    }

    [Fact]
    public void FormatUptime_BelowOneDay_ShowsHhMmSs()
    {
        Assert.Equal("01:02:03", PerformanceReport.FormatUptime(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void FormatUptime_AboveOneDay_PrefixesDayCount()
    {
        Assert.Equal("2d 03:04:05", PerformanceReport.FormatUptime(new TimeSpan(2, 3, 4, 5)));
    }

    [Fact]
    public void Format_IncludesEveryHeadlineLine()
    {
        // No latency histogram → no Publish latency line. Asserts the other rows are present.
        string report = PerformanceReport.Format(publishLatency: null);
        Assert.Contains("Uptime:",          report);
        Assert.Contains("CPU time:",        report);
        Assert.Contains("Memory:",          report);
        Assert.Contains("GC:",              report);
        Assert.Contains("Threads/handles:", report);
        Assert.Contains("Discord events:",  report);
        Assert.Contains("HA frames:",       report);
        Assert.Contains("Camera polls:",    report);
        Assert.Contains("Reconnects:",      report);
        Assert.Contains("Helper publish:",  report);
        Assert.DoesNotContain("Publish latency:", report);
    }

    [Fact]
    public void Format_WithEmptyHistogram_ShowsNoSamplesYet()
    {
        LatencyHistogram hist = new();
        string report = PerformanceReport.Format(hist);
        Assert.Contains("Publish latency: (no samples yet)", report);
    }

    [Fact]
    public void Format_WithRecordedSamples_ShowsPercentiles()
    {
        LatencyHistogram hist = new(capacity: 10);
        for (int i = 1; i <= 10; i++) hist.Record(TimeSpan.FromMilliseconds(i * 5));
        string report = PerformanceReport.Format(hist);
        // Just assert the line is there and reports n=10; the actual percentile math is
        // covered by LatencyHistogramTests.
        Assert.Contains("Publish latency:", report);
        Assert.Contains("n=10", report);
    }
}
