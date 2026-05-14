using DiscordHass.Discord;
using Xunit;

namespace DiscordHass.Tests;

public class VoiceStateTests
{
    [Fact]
    public void Empty_HasAllFalseAndNotBusy()
    {
        VoiceState s = VoiceState.Empty;
        Assert.False(s.IsInCall);
        Assert.False(s.MicMuted);
        Assert.False(s.SpeakerMuted);
        Assert.False(s.CameraOn);
        Assert.False(s.ServerMuted);
        Assert.False(s.ServerDeafened);
        Assert.False(s.Busy);
    }

    [Fact]
    public void WithChannel_NonNull_SetsInCall()
    {
        VoiceState s = VoiceState.Empty.WithChannel("123");
        Assert.True(s.IsInCall);
        Assert.True(s.Busy);
    }

    [Fact]
    public void WithChannel_Null_ClearsInCall()
    {
        VoiceState s = VoiceState.Empty.WithChannel("123").WithChannel(null);
        Assert.False(s.IsInCall);
    }

    [Fact]
    public void Records_AreEqualByValue()
    {
        VoiceState a = VoiceState.Empty.WithChannel("123").WithSelfVideo(true);
        VoiceState b = VoiceState.Empty.WithChannel("999").WithSelfVideo(true).WithChannel("123");
        Assert.Equal(a, b);
    }
}
