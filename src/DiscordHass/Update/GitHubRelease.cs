using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DiscordHass.Update;

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubReleaseAsset>? Assets { get; set; }
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    [JsonPropertyName("content_type")] public string? ContentType { get; set; }
}

/// <summary>
/// Normalized "we have an update" payload — exposed to the UI and downloader.
/// </summary>
internal sealed record UpdateAvailable(
    ReleaseVersion Version,
    string TagName,
    string HtmlUrl,
    string ExeAssetUrl,
    string ExeAssetName,
    long ExeAssetSize,
    string ShaAssetUrl);
