using System.Collections.Generic;
using DiscordHass.Update;
using Xunit;

namespace DiscordHass.Tests;

public class UpdaterSecurityTests
{
    private static GitHubReleaseAsset Asset(string name) => new()
    {
        Name = name,
        BrowserDownloadUrl = $"https://example.test/{name}",
        Size = 1234,
    };

    [Theory]
    [InlineData("..\\evil.exe")]
    [InlineData("../evil.exe")]
    [InlineData("..\\..\\Windows\\System32\\evil.exe")]
    [InlineData("subdir/inner.exe")]
    [InlineData("subdir\\inner.exe")]
    [InlineData("evil:stream.exe")]
    [InlineData("evil\nname.exe")]
    [InlineData("evil\"name.exe")]
    [InlineData("evil<name>.exe")]
    [InlineData("evil|pipe.exe")]
    public void IsSafeAssetName_RejectsUnsafe(string name)
    {
        Assert.False(UpdateChecker.IsSafeAssetName(name));
    }

    [Theory]
    [InlineData("DiscordHass-v0.2.0-win-x64.exe")]
    [InlineData("DiscordHass.exe")]
    [InlineData("DiscordHass-v0.1.4-win-x64.exe.sha256")]
    public void IsSafeAssetName_AcceptsPlainNames(string name)
    {
        Assert.True(UpdateChecker.IsSafeAssetName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsSafeAssetName_RejectsNullOrEmpty(string? name)
    {
        Assert.False(UpdateChecker.IsSafeAssetName(name));
    }

    [Fact]
    public void SelectExeAsset_RejectsPathTraversalName()
    {
        List<GitHubReleaseAsset> assets = new()
        {
            Asset("..\\evil-win-x64.exe"),
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.Null(picked);
    }

    [Fact]
    public void SelectExeAsset_PrefersSafeRuntimeMatchOverUnsafe()
    {
        // Even if a malicious asset is listed first, an unsafe name is skipped and we land on
        // the legit runtime-matching asset later in the list.
        List<GitHubReleaseAsset> assets = new()
        {
            Asset("..\\evil-win-x64.exe"),
            Asset("DiscordHass-v0.2.0-win-x64.exe"),
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.NotNull(picked);
        Assert.Equal("DiscordHass-v0.2.0-win-x64.exe", picked!.Name);
    }

    [Fact]
    public void SelectExeAsset_FallsBackToAnyExeWhenNoRuntimeMatch()
    {
        List<GitHubReleaseAsset> assets = new()
        {
            Asset("DiscordHass.exe"),
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.NotNull(picked);
        Assert.Equal("DiscordHass.exe", picked!.Name);
    }

    [Fact]
    public void SelectExeAsset_ReturnsNullForNoExeAssets()
    {
        List<GitHubReleaseAsset> assets = new()
        {
            Asset("DiscordHass-v0.2.0-win-x64.exe.sha256"),
            Asset("source.zip"),
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.Null(picked);
    }

    [Fact]
    public void BuildUpdate_ReturnsResult_WhenAssetAndSidecarSafe()
    {
        GitHubRelease release = ReleaseWith("v0.2.0", "DiscordHass-v0.2.0-win-x64.exe");
        UpdateAvailable? result = UpdateChecker.BuildUpdate(release, new ReleaseVersion(0, 2, 0, null), "win-x64");
        Assert.NotNull(result);
        Assert.Equal("DiscordHass-v0.2.0-win-x64.exe", result!.ExeAssetName);
    }

    [Fact]
    public void BuildUpdate_ReturnsNull_WhenShaSidecarMissing()
    {
        GitHubRelease release = new()
        {
            TagName = "v0.2.0",
            HtmlUrl = "https://example.test/release",
            Assets = new List<GitHubReleaseAsset>
            {
                Asset("DiscordHass-v0.2.0-win-x64.exe"),
                // No .sha256 sibling.
            },
        };
        UpdateAvailable? result = UpdateChecker.BuildUpdate(release, new ReleaseVersion(0, 2, 0, null), "win-x64");
        Assert.Null(result);
    }

    private static GitHubRelease ReleaseWith(string tag, string exeName) => new()
    {
        TagName = tag,
        HtmlUrl = "https://example.test/release",
        Assets = new List<GitHubReleaseAsset>
        {
            Asset(exeName),
            Asset(exeName + ".sha256"),
        },
    };
}
