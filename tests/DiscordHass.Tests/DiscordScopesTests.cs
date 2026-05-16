using System.Linq;
using DiscordHass.Discord;
using Xunit;

namespace DiscordHass.Tests;

public class DiscordScopesTests
{
    [Fact]
    public void Required_DoesNotIncludeRpcVideoRead()
    {
        // Regression guard: v0.2.0 dropped rpc.video.read because camera detection moved to
        // the Windows Capability Access Manager registry. Re-introducing the scope would force
        // every user through a fresh AUTHORIZE for no benefit.
        Assert.DoesNotContain(DiscordScopes.Required, s => s == DiscordScopes.RpcVideoRead);
    }

    [Fact]
    public void Required_IncludesRpcRpcVoiceReadAndIdentify()
    {
        Assert.Contains(DiscordScopes.Rpc, DiscordScopes.Required);
        Assert.Contains(DiscordScopes.RpcVoiceRead, DiscordScopes.Required);
        Assert.Contains(DiscordScopes.Identify, DiscordScopes.Required);
    }

    [Fact]
    public void CurrentKey_IsStableSortedComma()
    {
        // Two calls should always return the same string.
        Assert.Equal(DiscordScopes.CurrentKey(), DiscordScopes.CurrentKey());
        // Specifically: scopes joined with comma, no spaces, ordinal-sorted.
        string key = DiscordScopes.CurrentKey();
        Assert.DoesNotContain(' ', key);
        string[] parts = key.Split(',');
        Assert.Equal(parts.OrderBy(s => s, System.StringComparer.Ordinal).ToArray(), parts);
    }

    [Fact]
    public void Matches_TrueForCurrentKey()
    {
        Assert.True(DiscordScopes.Matches(DiscordScopes.CurrentKey()));
    }

    [Fact]
    public void Matches_FalseForLegacyKeyContainingRpcVideoRead()
    {
        // Simulate a v0.1.x cached key (which included rpc.video.read).
        string legacy = "identify,rpc,rpc.video.read,rpc.voice.read";
        Assert.False(DiscordScopes.Matches(legacy));
    }

    [Fact]
    public void Matches_FalseForNullOrEmpty()
    {
        Assert.False(DiscordScopes.Matches(null));
        Assert.False(DiscordScopes.Matches(""));
    }
}
