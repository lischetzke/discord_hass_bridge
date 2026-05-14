using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.Update;

internal sealed class UpdateDownloadException : Exception
{
    public UpdateDownloadException(string message, Exception? inner = null) : base(message, inner) { }
}

internal readonly record struct DownloadProgress(long BytesRead, long? TotalBytes)
{
    public double Fraction => TotalBytes is > 0 ? (double)BytesRead / TotalBytes.Value : 0;
}

internal sealed class UpdateDownloader
{
    private readonly HttpClient _http;

    public UpdateDownloader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    /// <summary>
    /// Downloads the exe asset to <paramref name="destinationPath"/>, fetches the SHA-256 sidecar,
    /// and verifies the downloaded file against it. Throws <see cref="UpdateDownloadException"/>
    /// on mismatch or any other failure; cleans up partial files.
    /// </summary>
    public async Task DownloadAndVerifyAsync(
        UpdateAvailable update,
        string destinationPath,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        // Fetch sha256 sidecar first — small, fails fast if missing/malformed.
        string sidecarContent;
        try
        {
            sidecarContent = await _http.GetStringAsync(update.ShaAssetUrl, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new UpdateDownloadException($"Could not fetch SHA-256 sidecar: {ex.Message}", ex);
        }

        if (!ShaSidecar.TryParse(sidecarContent, out string expectedHash))
        {
            throw new UpdateDownloadException("SHA-256 sidecar was malformed");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);

        try
        {
            await DownloadStreamingAsync(update.ExeAssetUrl, destinationPath, update.ExeAssetSize, progress, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }

        string actualHash = ComputeSha256(destinationPath);
        if (!ShaSidecar.Equals(actualHash, expectedHash))
        {
            TryDelete(destinationPath);
            throw new UpdateDownloadException(
                $"SHA-256 mismatch: expected {expectedHash}, got {actualHash}");
        }
    }

    private async Task DownloadStreamingAsync(
        string url,
        string destinationPath,
        long expectedSizeFromApi,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        using HttpResponseMessage resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        try
        {
            resp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new UpdateDownloadException($"Could not download {url}: {ex.Message}", ex);
        }

        long? totalBytes = resp.Content.Headers.ContentLength ?? (expectedSizeFromApi > 0 ? expectedSizeFromApi : null);

        await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream dst = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        byte[] buffer = new byte[81920];
        long copied = 0;
        DateTime lastReport = DateTime.MinValue;
        while (true)
        {
            int read = await src.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            copied += read;

            // Throttle progress reports to ~10/s to keep the UI thread happy.
            DateTime now = DateTime.UtcNow;
            if (progress is not null && (now - lastReport).TotalMilliseconds >= 100)
            {
                progress.Report(new DownloadProgress(copied, totalBytes));
                lastReport = now;
            }
        }
        progress?.Report(new DownloadProgress(copied, totalBytes ?? copied));
    }

    private static string ComputeSha256(string path)
    {
        using FileStream fs = File.OpenRead(path);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(fs, hash);
        StringBuilder sb = new(64);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
