using System.Collections.Generic;
using DiscordHass.Update;
using Xunit;

namespace DiscordHass.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public void SelectExeAsset_PrefersRuntimeMatch()
    {
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "DiscordHass-v1.0.0-win-arm64.exe", BrowserDownloadUrl = "https://e/arm64" },
            new() { Name = "DiscordHass-v1.0.0-win-x64.exe",   BrowserDownloadUrl = "https://e/x64"   },
            new() { Name = "checksums.txt",                    BrowserDownloadUrl = "https://e/sums"  },
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.NotNull(picked);
        Assert.Equal("DiscordHass-v1.0.0-win-x64.exe", picked!.Name);
    }

    [Fact]
    public void SelectExeAsset_FallsBackToAnyExe()
    {
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "DiscordHass-v1.0.0-portable.exe", BrowserDownloadUrl = "https://e/p" },
            new() { Name = "DiscordHass-v1.0.0-portable.exe.sha256", BrowserDownloadUrl = "https://e/p.sha256" },
        };
        GitHubReleaseAsset? picked = UpdateChecker.SelectExeAsset(assets, "win-x64");
        Assert.NotNull(picked);
        Assert.Equal("DiscordHass-v1.0.0-portable.exe", picked!.Name);
    }

    [Fact]
    public void SelectExeAsset_ReturnsNullWhenNoExe()
    {
        var assets = new List<GitHubReleaseAsset>
        {
            new() { Name = "checksums.txt", BrowserDownloadUrl = "https://e/sums" },
        };
        Assert.Null(UpdateChecker.SelectExeAsset(assets, "win-x64"));
    }

    [Fact]
    public void BuildUpdate_RequiresSidecar()
    {
        var release = new GitHubRelease
        {
            TagName = "v1.2.3",
            HtmlUrl = "https://github.com/x/y/releases/tag/v1.2.3",
            Assets = new List<GitHubReleaseAsset>
            {
                new() { Name = "DiscordHass-v1.2.3-win-x64.exe", BrowserDownloadUrl = "https://e/exe", Size = 50_000_000 },
                // no .sha256 sibling
            },
        };
        ReleaseVersion.TryParse(release.TagName, out ReleaseVersion v);
        Assert.Null(UpdateChecker.BuildUpdate(release, v, "win-x64"));
    }

    [Fact]
    public void BuildUpdate_HappyPath()
    {
        var release = new GitHubRelease
        {
            TagName = "v1.2.3",
            HtmlUrl = "https://github.com/x/y/releases/tag/v1.2.3",
            Assets = new List<GitHubReleaseAsset>
            {
                new() { Name = "DiscordHass-v1.2.3-win-x64.exe",        BrowserDownloadUrl = "https://e/exe", Size = 50_000_000 },
                new() { Name = "DiscordHass-v1.2.3-win-x64.exe.sha256", BrowserDownloadUrl = "https://e/sha" },
            },
        };
        ReleaseVersion.TryParse(release.TagName, out ReleaseVersion v);
        UpdateAvailable? upd = UpdateChecker.BuildUpdate(release, v, "win-x64");
        Assert.NotNull(upd);
        Assert.Equal("v1.2.3", upd!.TagName);
        Assert.Equal("https://e/exe", upd.ExeAssetUrl);
        Assert.Equal("https://e/sha", upd.ShaAssetUrl);
        Assert.Equal(50_000_000, upd.ExeAssetSize);
    }
}
