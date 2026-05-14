using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordHass.Discord;

internal sealed class DiscordIpcMessage
{
    [JsonPropertyName("cmd")] public string? Cmd { get; set; }
    [JsonPropertyName("nonce")] public string? Nonce { get; set; }
    [JsonPropertyName("evt")] public string? Evt { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonPropertyName("args")] public JsonElement? Args { get; set; }
}

internal sealed class DiscordVoiceStateDto
{
    [JsonPropertyName("user_id")] public string? UserId { get; set; }
    [JsonPropertyName("channel_id")] public string? ChannelId { get; set; }
    [JsonPropertyName("self_mute")] public bool SelfMute { get; set; }
    [JsonPropertyName("self_deaf")] public bool SelfDeaf { get; set; }
    [JsonPropertyName("self_video")] public bool SelfVideo { get; set; }
    [JsonPropertyName("mute")] public bool Mute { get; set; }
    [JsonPropertyName("deaf")] public bool Deaf { get; set; }
    [JsonPropertyName("suppress")] public bool Suppress { get; set; }
}

internal sealed class DiscordIpcCommandException : System.Exception
{
    public string? Code { get; }
    public DiscordIpcCommandException(string message, string? code = null) : base(message)
    {
        Code = code;
    }
}
