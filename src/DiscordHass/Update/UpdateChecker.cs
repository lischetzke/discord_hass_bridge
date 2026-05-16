using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.App;

namespace DiscordHass.Update;

internal sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message, Exception? inner = null) : base(message, inner) { }
}

internal sealed class UpdateChecker
{
    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _runtime;

    public UpdateChecker(HttpClient? http = null, string? owner = null, string? repo = null, string? runtime = null)
    {
        _http = http ?? new HttpClient();
        _owner = owner ?? AppConstants.GitHubOwner;
        _repo = repo ?? AppConstants.GitHubRepo;
        _runtime = runtime ?? "win-x64";

        // GitHub requires a User-Agent header.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd($"{AppConstants.ProductName}/{AppConstants.GetVersionString()}"))
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordHass/0.0.0");
        }
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Returns an UpdateAvailable if GitHub's latest release is newer than <paramref name="currentVersion"/>
    /// AND has a matching exe asset + sha256 sidecar. Returns null otherwise.
    /// </summary>
    public async Task<UpdateAvailable?> CheckAsync(ReleaseVersion currentVersion, CancellationToken ct)
    {
        string url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
        GitHubRelease? release;
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                // No releases published yet — not an error.
                return null;
            }
            resp.EnsureSuccessStatusCode();
            release = await resp.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (UpdateCheckException) { throw; }
        catch (Exception ex)
        {
            throw new UpdateCheckException($"Could not query latest release: {ex.Message}", ex);
        }

        if (release is null || release.Draft) return null;
        if (!ReleaseVersion.TryParse(release.TagName, out ReleaseVersion releaseVersion)) return null;
        if (releaseVersion.CompareTo(currentVersion) <= 0) return null;

        return BuildUpdate(release, releaseVersion, _runtime);
    }

    internal static UpdateAvailable? BuildUpdate(GitHubRelease release, ReleaseVersion version, string runtime)
    {
        if (release.Assets is null) return null;

        GitHubReleaseAsset? exeAsset = SelectExeAsset(release.Assets, runtime);
        if (exeAsset?.BrowserDownloadUrl is null || string.IsNullOrEmpty(exeAsset.Name)) return null;

        GitHubReleaseAsset? shaAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, exeAsset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (shaAsset?.BrowserDownloadUrl is null || string.IsNullOrEmpty(shaAsset.Name)) return null;

        // Defense-in-depth: the sha sidecar name is derived from the exe name, but if the source
        // ever changes, refuse anything that would be unsafe to use as a file path.
        if (!IsSafeAssetName(shaAsset.Name)) return null;

        return new UpdateAvailable(
            version,
            release.TagName ?? version.ToString(),
            release.HtmlUrl ?? $"https://github.com/{release.Name}",
            exeAsset.BrowserDownloadUrl!,
            exeAsset.Name!,
            exeAsset.Size,
            shaAsset.BrowserDownloadUrl!);
    }

    internal static GitHubReleaseAsset? SelectExeAsset(IReadOnlyList<GitHubReleaseAsset> assets, string runtime)
    {
        // Prefer the exact runtime suffix, fall back to any *.exe.
        GitHubReleaseAsset? match = null;
        foreach (GitHubReleaseAsset a in assets)
        {
            if (string.IsNullOrEmpty(a.Name)) continue;
            if (!a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            // Reject any asset name that would be unsafe to use as a file path. A compromised
            // release could otherwise name an asset "..\..\Windows\System32\evil.exe" and the
            // downstream Path.Combine in UpdateService would resolve outside the staging dir.
            if (!IsSafeAssetName(a.Name)) continue;
            if (a.Name.IndexOf(runtime, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return a;
            }
            match ??= a;
        }
        return match;
    }

    /// <summary>
    /// True when <paramref name="name"/> is a plain filename with no directory components or
    /// other characters that would be unsafe to use in a file path. Required because release
    /// asset names come from a remote source (GitHub) and could be attacker-influenced if the
    /// release is ever compromised.
    /// </summary>
    internal static bool IsSafeAssetName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name != Path.GetFileName(name)) return false;
        if (name.Contains('/') || name.Contains('\\')) return false;
        if (name.Contains("..", StringComparison.Ordinal)) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return true;
    }
}
