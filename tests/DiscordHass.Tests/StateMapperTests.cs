using System.Collections.Generic;
using System.Linq;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;
using Xunit;

namespace DiscordHass.Tests;

public class StateMapperTests
{
    private static List<EffectiveStateFlag> AllEffectiveWithDefaults()
        => FlagResolver.ResolveAll(new AppConfig()).ToList();

    private static List<EffectiveStateFlag> Effective(params string[] flagIds)
    {
        AppConfig cfg = new();
        cfg.EnabledFlags = new HashSet<string>(flagIds);
        return FlagResolver.ResolveEnabled(cfg).ToList();
    }

    [Fact]
    public void NoPreviousState_FirstUpdate_EmitsEveryProvidedFlag()
    {
        List<EffectiveStateFlag> flags = AllEffectiveWithDefaults();
        List<HelperUpdate> updates = StateMapper.ComputeUpdates(null, VoiceState.Empty, flags);
        Assert.Equal(flags.Count, updates.Count);
        Assert.All(updates, u => Assert.False(u.DesiredOn));
    }

    [Fact]
    public void OnlyChangedFlagsAreEmitted()
    {
        VoiceState prev = VoiceState.Empty.WithChannel("123").WithSelfVideo(false);
        VoiceState next = prev.WithSelfVideo(true);

        List<HelperUpdate> updates = StateMapper.ComputeUpdates(prev, next, AllEffectiveWithDefaults());

        HelperUpdate single = Assert.Single(updates);
        Assert.Equal(StateFlagIds.CameraOn, single.FlagId);
        Assert.True(single.DesiredOn);
    }

    [Fact]
    public void DisabledFlags_AreSkipped()
    {
        VoiceState prev = VoiceState.Empty;
        VoiceState next = prev.WithChannel("123").WithSelfMute(true).WithSelfVideo(true);

        List<HelperUpdate> updates = StateMapper.ComputeUpdates(prev, next, Effective(StateFlagIds.InCall));

        HelperUpdate u = Assert.Single(updates);
        Assert.Equal(StateFlagIds.InCall, u.FlagId);
        Assert.True(u.DesiredOn);
    }

    [Fact]
    public void Busy_TracksInCall()
    {
        VoiceState prev = VoiceState.Empty;
        VoiceState next = prev.WithChannel("123");

        List<HelperUpdate> updates = StateMapper.ComputeUpdates(prev, next, AllEffectiveWithDefaults());
        HelperUpdate busy = Assert.Single(updates, u => u.FlagId == StateFlagIds.Busy);
        Assert.True(busy.DesiredOn);

        VoiceState left = next.WithChannel(null);
        List<HelperUpdate> leftUpdates = StateMapper.ComputeUpdates(next, left, AllEffectiveWithDefaults());
        HelperUpdate busy2 = Assert.Single(leftUpdates, u => u.FlagId == StateFlagIds.Busy);
        Assert.False(busy2.DesiredOn);
    }

    [Fact]
    public void ComputeAllOff_ReturnsOneEntryPerEffectiveFlag()
    {
        List<EffectiveStateFlag> flags = Effective(StateFlagIds.InCall, StateFlagIds.CameraOn);
        List<HelperUpdate> off = StateMapper.ComputeAllOff(flags);
        Assert.Equal(2, off.Count);
        Assert.All(off, u => Assert.False(u.DesiredOn));
    }

    [Fact]
    public void CustomPrefix_PropagatesToHelperUpdate()
    {
        AppConfig cfg = new() { HelperPrefix = "Voice" };
        cfg.EnabledFlags = new HashSet<string> { StateFlagIds.InCall };
        List<EffectiveStateFlag> flags = FlagResolver.ResolveEnabled(cfg).ToList();
        VoiceState next = VoiceState.Empty.WithChannel("xyz");

        HelperUpdate u = Assert.Single(StateMapper.ComputeUpdates(null, next, flags));
        Assert.Equal("Voice In Call", u.FriendlyName);
        Assert.Equal("voice_in_call", u.EntityIdSlug);
        Assert.True(u.DesiredOn);
    }
}
