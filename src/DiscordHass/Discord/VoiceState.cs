namespace DiscordHass.Discord;

internal readonly record struct VoiceState(
    bool IsInCall,
    bool MicMuted,
    bool SpeakerMuted,
    bool CameraOn,
    bool ServerMuted,
    bool ServerDeafened)
{
    public static VoiceState Empty => new(false, false, false, false, false, false);

    public bool Busy => IsInCall;

    public VoiceState WithChannel(string? channelId) => this with { IsInCall = !string.IsNullOrEmpty(channelId) };

    public VoiceState WithSelfMute(bool selfMute) => this with { MicMuted = selfMute };

    public VoiceState WithSelfDeaf(bool selfDeaf) => this with { SpeakerMuted = selfDeaf };

    public VoiceState WithSelfVideo(bool selfVideo) => this with { CameraOn = selfVideo };

    public VoiceState WithServerMute(bool mute) => this with { ServerMuted = mute };

    public VoiceState WithServerDeaf(bool deaf) => this with { ServerDeafened = deaf };
}
