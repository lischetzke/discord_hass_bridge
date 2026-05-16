using System;
using System.Linq;

namespace DiscordHass.Discord;

/// <summary>
/// Single source of truth for the OAuth2 scopes DiscordHass requests during AUTHORIZE.
/// When this set changes between app versions, any cached refresh token becomes stale
/// — Discord's refresh flow only re-issues the original scope set, so the user must
/// re-authorize. <see cref="CurrentKey"/> is persisted in config and compared on
/// startup so we can detect that and prompt them.
/// </summary>
internal static class DiscordScopes
{
    public const string Rpc          = "rpc";
    public const string RpcVoiceRead = "rpc.voice.read";
    // Intentionally NOT in Required as of v0.2.0 — kept here only to recognize legacy cached
    // scope sets. Camera state is read from the Windows Capability Access Manager registry
    // (see WindowsCameraWatcher); Discord's RPC never exposed self_video to user-registered
    // apps, so requesting this scope previously was dead weight.
    public const string RpcVideoRead = "rpc.video.read";
    public const string Identify     = "identify";

    public static readonly string[] Required = { Rpc, RpcVoiceRead, Identify };

    /// <summary>Stable, ordered representation of <see cref="Required"/> for equality checks.</summary>
    public static string CurrentKey()
        => string.Join(",", Required.OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>True iff the cached scope set matches what the current build needs.</summary>
    public static bool Matches(string? cachedKey)
        => string.Equals(cachedKey, CurrentKey(), StringComparison.Ordinal);
}
