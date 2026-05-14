using System;

namespace DiscordHass.Update;

/// <summary>
/// Parses the "<hex-hash>  <filename>" line format produced by `Get-FileHash`
/// (build-release.ps1) and by sha256sum -b.
/// </summary>
internal static class ShaSidecar
{
    public static bool TryParse(string content, out string hashHex)
    {
        hashHex = "";
        if (string.IsNullOrWhiteSpace(content)) return false;

        // Take the first non-empty line.
        ReadOnlySpan<char> line = default;
        foreach (ReadOnlySpan<char> rawLine in content.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> trimmed = rawLine.Trim();
            if (trimmed.IsEmpty) continue;
            line = trimmed;
            break;
        }
        if (line.IsEmpty) return false;

        // The hash is the leading hex token. Stop at whitespace or '*' (sha256sum binary marker).
        int end = 0;
        while (end < line.Length && IsHex(line[end])) end++;
        if (end == 0) return false;

        // Plausible sha256 length, but accept any-length hex for forward-compat.
        hashHex = new string(line[..end]).ToLowerInvariant();
        return hashHex.Length is 64 or 40 or 32; // sha256, sha1, md5 — sha256 is what we use.
    }

    public static bool Equals(string a, string b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
