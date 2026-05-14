using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiscordHass.HomeAssistant;

internal sealed class HaServerMessage
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("id")] public int? Id { get; set; }
    [JsonPropertyName("success")] public bool? Success { get; set; }
    [JsonPropertyName("result")] public JsonElement? Result { get; set; }
    [JsonPropertyName("error")] public HaError? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("ha_version")] public string? HaVersion { get; set; }
}

internal sealed class HaError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal sealed class HaInputBooleanItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
}

internal sealed class HaCommandException : System.Exception
{
    public string? Code { get; }

    public HaCommandException(string message, string? code = null) : base(message)
    {
        Code = code;
    }
}
