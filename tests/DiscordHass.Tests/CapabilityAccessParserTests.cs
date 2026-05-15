using System.Collections.Generic;
using DiscordHass.App;
using Xunit;

namespace DiscordHass.Tests;

public class CapabilityAccessParserTests
{
    private static readonly HashSet<string> DiscordOnly =
        new(System.StringComparer.OrdinalIgnoreCase) { "Discord.exe" };

    [Theory]
    [InlineData("C:#Users#me#AppData#Local#Discord#app-1.0.9200#Discord.exe", "C:\\Users\\me\\AppData\\Local\\Discord\\app-1.0.9200\\Discord.exe")]
    [InlineData("D:#Program Files#Discord#Discord.exe", "D:\\Program Files\\Discord\\Discord.exe")]
    [InlineData("", "")]
    public void DecodeKeyName_ReplacesHashWithBackslash(string encoded, string expected)
    {
        Assert.Equal(expected, CapabilityAccessParser.DecodeKeyName(encoded));
    }

    [Fact]
    public void EncodeKeyName_IsInverseOfDecode()
    {
        const string path = @"C:\Users\me\AppData\Local\Discord\app-1.0.9200\Discord.exe";
        Assert.Equal(path, CapabilityAccessParser.DecodeKeyName(CapabilityAccessParser.EncodeKeyName(path)));
    }

    [Theory]
    [InlineData(100, 0,    true)]   // in use: started, never stopped
    [InlineData(100, 200,  false)]  // ended cleanly
    [InlineData(0,   0,    false)]  // never used at all
    [InlineData(0,   200,  false)]  // stopped without start (corrupt/edge)
    [InlineData(-1,  0,    false)]  // negative start ignored
    public void IsInUse_SemanticsMatchWindows(long start, long stop, bool expected)
    {
        var entry = new CapabilityAccessEntry("name", start, stop);
        Assert.Equal(expected, CapabilityAccessParser.IsInUse(entry));
    }

    [Fact]
    public void FindInUseExe_ReturnsDecodedPath_WhenDiscordHasStopZero()
    {
        var entries = new[]
        {
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9200#Discord.exe", 100, 0),
        };
        string? exe = CapabilityAccessParser.FindInUseExe(entries, DiscordOnly);
        Assert.Equal(@"C:\Users\me\AppData\Local\Discord\app-1.0.9200\Discord.exe", exe);
    }

    [Fact]
    public void FindInUseExe_ReturnsNull_WhenDiscordIsNotInUse()
    {
        var entries = new[]
        {
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9200#Discord.exe", 100, 200),
        };
        Assert.Null(CapabilityAccessParser.FindInUseExe(entries, DiscordOnly));
    }

    [Fact]
    public void FindInUseExe_IgnoresUnrelatedAppsInUse()
    {
        var entries = new[]
        {
            new CapabilityAccessEntry("C:#Program Files#Zoom#bin#Zoom.exe",                                     50,  0),
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9200#Discord.exe",            100,  200),
            new CapabilityAccessEntry("C:#Program Files#Microsoft#Teams#current#Teams.exe",                    150,  0),
        };
        Assert.Null(CapabilityAccessParser.FindInUseExe(entries, DiscordOnly));
    }

    [Fact]
    public void FindInUseExe_HandlesMultipleDiscordVersions_ReturnsTheActiveOne()
    {
        var entries = new[]
        {
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9000#Discord.exe", 100, 500),
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9200#Discord.exe", 600, 0),
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#Discord#app-1.0.9300#Discord.exe", 200, 300),
        };
        string? exe = CapabilityAccessParser.FindInUseExe(entries, DiscordOnly);
        Assert.Equal(@"C:\Users\me\AppData\Local\Discord\app-1.0.9200\Discord.exe", exe);
    }

    [Fact]
    public void FindInUseExe_HandlesPtbAndCanaryChannels()
    {
        HashSet<string> all = CapabilityAccessParser.DiscordExeBasenames as HashSet<string>
                              ?? new HashSet<string>(CapabilityAccessParser.DiscordExeBasenames);

        var entries = new[]
        {
            new CapabilityAccessEntry("C:#Users#me#AppData#Local#DiscordPTB#app-1.0.9200#DiscordPTB.exe", 700, 0),
        };
        string? exe = CapabilityAccessParser.FindInUseExe(entries, all);
        Assert.EndsWith("DiscordPTB.exe", exe);
    }

    [Theory]
    [InlineData(123L)]
    [InlineData(0L)]
    public void AsFileTimeLong_AcceptsLongDirectly(long value)
    {
        Assert.Equal(value, CapabilityAccessParser.AsFileTimeLong(value));
    }

    [Fact]
    public void AsFileTimeLong_AcceptsByteArray()
    {
        long value = 0x0123456789ABCDEFL;
        byte[] bytes = System.BitConverter.GetBytes(value);
        Assert.Equal(value, CapabilityAccessParser.AsFileTimeLong(bytes));
    }

    [Fact]
    public void AsFileTimeLong_ReturnsZeroForNullOrUnknown()
    {
        Assert.Equal(0, CapabilityAccessParser.AsFileTimeLong(null));
        Assert.Equal(0, CapabilityAccessParser.AsFileTimeLong("not a number"));
        Assert.Equal(0, CapabilityAccessParser.AsFileTimeLong(new byte[] { 1, 2, 3 })); // too short
    }
}
