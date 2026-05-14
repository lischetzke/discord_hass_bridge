using DiscordHass.App;
using DiscordHass.Config;
using Xunit;

namespace DiscordHass.Tests;

public class FlagResolverTests
{
    [Fact]
    public void Defaults_GiveDiscordPrefixedNames()
    {
        AppConfig cfg = new();
        EffectiveStateFlag in_call = ResolveFlag(cfg, StateFlagIds.InCall);

        Assert.Equal("Discord In Call", in_call.FriendlyName);
        Assert.Equal("discord_in_call", in_call.EntityIdSlug);
        Assert.Equal("mdi:phone-in-talk", in_call.Icon);
    }

    [Fact]
    public void EmptyPrefix_DropsThePrefixSpace()
    {
        AppConfig cfg = new() { HelperPrefix = "" };
        EffectiveStateFlag in_call = ResolveFlag(cfg, StateFlagIds.InCall);

        Assert.Equal("In Call", in_call.FriendlyName);
        Assert.Equal("in_call", in_call.EntityIdSlug);
    }

    [Fact]
    public void WhitespacePrefix_IsTrimmed()
    {
        AppConfig cfg = new() { HelperPrefix = "  Voice  " };
        EffectiveStateFlag in_call = ResolveFlag(cfg, StateFlagIds.InCall);

        Assert.Equal("Voice In Call", in_call.FriendlyName);
        Assert.Equal("voice_in_call", in_call.EntityIdSlug);
    }

    [Fact]
    public void Override_NameSuffix_OverridesDefault()
    {
        AppConfig cfg = new();
        cfg.GetOrCreateOverride(StateFlagIds.InCall).NameSuffix = "Calling";
        EffectiveStateFlag in_call = ResolveFlag(cfg, StateFlagIds.InCall);

        Assert.Equal("Discord Calling", in_call.FriendlyName);
        Assert.Equal("discord_calling", in_call.EntityIdSlug);
    }

    [Fact]
    public void Override_Icon_OverridesDefault()
    {
        AppConfig cfg = new();
        cfg.GetOrCreateOverride(StateFlagIds.CameraOn).Icon = "mdi:webcam";
        EffectiveStateFlag camera = ResolveFlag(cfg, StateFlagIds.CameraOn);

        Assert.Equal("mdi:webcam", camera.Icon);
    }

    [Fact]
    public void Override_BlankFields_FallBackToDefault()
    {
        AppConfig cfg = new();
        FlagOverride ov = cfg.GetOrCreateOverride(StateFlagIds.InCall);
        ov.NameSuffix = "   ";
        ov.Icon = "";
        EffectiveStateFlag in_call = ResolveFlag(cfg, StateFlagIds.InCall);

        Assert.Equal("Discord In Call", in_call.FriendlyName);
        Assert.Equal("mdi:phone-in-talk", in_call.Icon);
    }

    [Theory]
    [InlineData("Discord In Call", "discord_in_call")]
    [InlineData("Voice  In  Call", "voice_in_call")] // collapse repeats
    [InlineData("   leading", "leading")]
    [InlineData("trailing!!!", "trailing")]
    [InlineData("___", "")]
    [InlineData("Discord-Mic-Muted", "discord_mic_muted")]
    [InlineData("123 numbers OK", "123_numbers_ok")]
    public void Slugify_MatchesExpected(string input, string expected)
    {
        Assert.Equal(expected, FlagResolver.Slugify(input));
    }

    private static EffectiveStateFlag ResolveFlag(AppConfig cfg, string flagId)
    {
        StateFlagDefinition def = StateFlagDefinitions.FindByFlagId(flagId)!;
        return FlagResolver.Resolve(def, cfg);
    }
}
