using DiscordHass.Update;
using Xunit;

namespace DiscordHass.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3",       1, 2, 3, null)]
    [InlineData("v1.2.3",      1, 2, 3, null)]
    [InlineData("V1.2.3",      1, 2, 3, null)]
    [InlineData("  1.2.3  ",   1, 2, 3, null)]
    [InlineData("1.2",         1, 2, 0, null)]
    [InlineData("0.1.0",       0, 1, 0, null)]
    [InlineData("1.0.0-beta",  1, 0, 0, "beta")]
    [InlineData("1.0.0-beta+commit.sha", 1, 0, 0, "beta")]
    [InlineData("1.0.0+meta",  1, 0, 0, null)]
    public void TryParse_AcceptsValid(string input, int major, int minor, int patch, string? pre)
    {
        Assert.True(ReleaseVersion.TryParse(input, out ReleaseVersion v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(pre, v.PreRelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("foo")]
    [InlineData("1")]
    [InlineData("1.")]
    [InlineData("a.b.c")]
    [InlineData("-1.2.3")]
    public void TryParse_RejectsInvalid(string? input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out _));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.0", "1.1.0", -1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.2.3", "1.2.3",  0)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void Compare_Major_Minor_Patch(string a, string b, int expected)
    {
        Assert.True(ReleaseVersion.TryParse(a, out ReleaseVersion va));
        Assert.True(ReleaseVersion.TryParse(b, out ReleaseVersion vb));
        Assert.Equal(expected, System.Math.Sign(va.CompareTo(vb)));
    }

    [Fact]
    public void PreRelease_SortsBelowFinal()
    {
        Assert.True(ReleaseVersion.TryParse("1.0.0", out ReleaseVersion final));
        Assert.True(ReleaseVersion.TryParse("1.0.0-rc1", out ReleaseVersion rc));
        Assert.True(final.CompareTo(rc) > 0);
        Assert.True(rc.CompareTo(final) < 0);
    }

    [Fact]
    public void PreRelease_LexicographicOrder()
    {
        Assert.True(ReleaseVersion.TryParse("1.0.0-alpha", out ReleaseVersion alpha));
        Assert.True(ReleaseVersion.TryParse("1.0.0-beta",  out ReleaseVersion beta));
        Assert.True(alpha.CompareTo(beta) < 0);
    }

    [Theory]
    [InlineData(1, 2, 3, null,   "1.2.3")]
    [InlineData(1, 2, 3, "rc1",  "1.2.3-rc1")]
    public void ToString_Roundtrip(int major, int minor, int patch, string? pre, string expected)
    {
        ReleaseVersion v = new(major, minor, patch, pre);
        Assert.Equal(expected, v.ToString());
    }
}
