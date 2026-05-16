using System;
using System.Buffers;
using System.Text;
using System.Text.Json;

namespace DiscordHass.App;

/// <summary>
/// Strips secrets out of an <c>AppConfig</c> JSON blob before it's included in a diagnostics
/// bundle shared in an issue report. Replaces every property whose name ends with
/// <c>"Protected"</c> with the literal string <c>"&lt;redacted&gt;"</c>; passes everything else
/// through unchanged. Pure function — no I/O. Tested directly.
/// </summary>
internal static class DiagnosticsRedactor
{
    private const string RedactedMarker = "<redacted>";

    /// <summary>
    /// Returns a redacted copy of <paramref name="configJson"/>. Throws
    /// <see cref="JsonException"/> if the input is not a JSON object at the top level.
    /// </summary>
    public static string Redact(string configJson)
    {
        if (string.IsNullOrEmpty(configJson))
            throw new ArgumentException("Config JSON must not be empty", nameof(configJson));

        using JsonDocument doc = JsonDocument.Parse(configJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Config JSON root must be an object");

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            WriteRedactedObject(writer, doc.RootElement);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteRedactedObject(Utf8JsonWriter writer, JsonElement obj)
    {
        writer.WriteStartObject();
        foreach (JsonProperty prop in obj.EnumerateObject())
        {
            if (IsProtectedFieldName(prop.Name))
            {
                writer.WriteString(prop.Name, RedactedMarker);
            }
            else
            {
                writer.WritePropertyName(prop.Name);
                WriteRedactedValue(writer, prop.Value);
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteRedactedValue(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteRedactedObject(writer, value);
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WriteRedactedValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    /// <summary>Internal so tests can assert the naming convention.</summary>
    internal static bool IsProtectedFieldName(string name)
        => !string.IsNullOrEmpty(name)
           && name.EndsWith("Protected", StringComparison.Ordinal);
}
