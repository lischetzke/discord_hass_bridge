using DiscordHass.Update;
using Xunit;

namespace DiscordHass.Tests;

public class ShaSidecarTests
{
    [Fact]
    public void Parses_StandardTwoSpaceFormat()
    {
        const string content = "612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6  DiscordHass-v0.1.0-win-x64.exe\n";
        Assert.True(ShaSidecar.TryParse(content, out string hash));
        Assert.Equal("612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6", hash);
    }

    [Fact]
    public void Parses_TabSeparator()
    {
        const string content = "612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6\tfile.exe";
        Assert.True(ShaSidecar.TryParse(content, out string hash));
        Assert.Equal("612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6", hash);
    }

    [Fact]
    public void Parses_BinaryStarPrefixedFilename()
    {
        // GNU sha256sum -b uses an asterisk to mark binary mode: "<hash> *<file>"
        const string content = "612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6 *file.exe";
        Assert.True(ShaSidecar.TryParse(content, out string hash));
        Assert.Equal("612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6", hash);
    }

    [Fact]
    public void TolaratesLeadingBlankLines()
    {
        const string content = "\n\n  \n612EB6C9A867BE26D2A25D0A470BA8C1399BC45B00FBFD52AD71037C629841E6  file\n";
        Assert.True(ShaSidecar.TryParse(content, out string hash));
        Assert.Equal("612eb6c9a867be26d2a25d0a470ba8c1399bc45b00fbfd52ad71037c629841e6", hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash  file.exe")]
    [InlineData("abcd  file.exe")] // too short to be sha256/sha1/md5
    public void RejectsInvalid(string content)
    {
        Assert.False(ShaSidecar.TryParse(content, out _));
    }

    [Theory]
    [InlineData("ABCDEF", "abcdef", true)]
    [InlineData("abcdef", "abcdef", true)]
    [InlineData(" abcdef ", "abcdef", true)]
    [InlineData("abcdef", "abcde0", false)]
    public void Equals_IsCaseAndTrimInsensitive(string a, string b, bool expected)
    {
        Assert.Equal(expected, ShaSidecar.Equals(a, b));
    }
}
