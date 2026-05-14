using System;

namespace DiscordHass.Update;

/// <summary>
/// Minimal SemVer-style version: MAJOR.MINOR.PATCH plus optional pre-release tag.
/// Pre-release tags are compared lexicographically; "no tag" beats any tag.
/// </summary>
internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<ReleaseVersion>
{
    public static readonly ReleaseVersion Zero = new(0, 0, 0, null);

    public static bool TryParse(string? input, out ReleaseVersion result)
    {
        result = Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;

        ReadOnlySpan<char> s = input.AsSpan().Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s[1..];

        // Strip build metadata (+...) — we don't compare it.
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];

        // Split off pre-release suffix (-...)
        string? pre = null;
        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            pre = s[(dash + 1)..].ToString();
            s = s[..dash];
        }

        Span<Range> parts = stackalloc Range[4];
        int count = s.Split(parts, '.');
        if (count is < 2 or > 3) return false;

        if (!int.TryParse(s[parts[0]], out int major) || major < 0) return false;
        if (!int.TryParse(s[parts[1]], out int minor) || minor < 0) return false;
        int patch = 0;
        if (count == 3 && (!int.TryParse(s[parts[2]], out patch) || patch < 0)) return false;

        result = new ReleaseVersion(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        int c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);     if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);     if (c != 0) return c;

        // No prerelease sorts after any prerelease (SemVer rule).
        bool a = string.IsNullOrEmpty(PreRelease);
        bool b = string.IsNullOrEmpty(other.PreRelease);
        if (a && b)  return 0;
        if (a)       return 1;
        if (b)       return -1;
        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
