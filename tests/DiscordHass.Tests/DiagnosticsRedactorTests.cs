using System.Text.Json;
using DiscordHass.App;
using Xunit;

namespace DiscordHass.Tests;

public class DiagnosticsRedactorTests
{
    [Fact]
    public void RedactsEveryProtectedField()
    {
        const string input = """
        {
          "HaBaseUrl": "http://homeassistant.local:8123",
          "HaTokenProtected": "DPAPI-BLOB-1",
          "DiscordClientId": "1234567890",
          "DiscordClientSecretProtected": "DPAPI-BLOB-2",
          "DiscordAccessTokenProtected": "DPAPI-BLOB-3",
          "DiscordRefreshTokenProtected": "DPAPI-BLOB-4"
        }
        """;

        string output = DiagnosticsRedactor.Redact(input);

        using JsonDocument doc = JsonDocument.Parse(output);
        JsonElement root = doc.RootElement;
        Assert.Equal("http://homeassistant.local:8123", root.GetProperty("HaBaseUrl").GetString());
        Assert.Equal("<redacted>", root.GetProperty("HaTokenProtected").GetString());
        Assert.Equal("1234567890", root.GetProperty("DiscordClientId").GetString());
        Assert.Equal("<redacted>", root.GetProperty("DiscordClientSecretProtected").GetString());
        Assert.Equal("<redacted>", root.GetProperty("DiscordAccessTokenProtected").GetString());
        Assert.Equal("<redacted>", root.GetProperty("DiscordRefreshTokenProtected").GetString());
    }

    [Fact]
    public void PreservesNonProtectedFieldsAndStructure()
    {
        const string input = """
        {
          "HelperPrefix": "Discord",
          "EnabledFlags": ["in_call", "mic_muted"],
          "FlagOverrides": { "in_call": { "Icon": "mdi:phone", "LastEntityIdSlug": "discord_in_call" } },
          "AutostartEnabled": true,
          "LastUpdateCheckUnix": 1700000000,
          "HasCompletedOnboarding": true
        }
        """;

        string output = DiagnosticsRedactor.Redact(input);
        using JsonDocument doc = JsonDocument.Parse(output);
        JsonElement root = doc.RootElement;
        Assert.Equal("Discord", root.GetProperty("HelperPrefix").GetString());
        Assert.Equal(2, root.GetProperty("EnabledFlags").GetArrayLength());
        Assert.Equal("mdi:phone", root.GetProperty("FlagOverrides").GetProperty("in_call").GetProperty("Icon").GetString());
        Assert.True(root.GetProperty("AutostartEnabled").GetBoolean());
        Assert.Equal(1700000000, root.GetProperty("LastUpdateCheckUnix").GetInt64());
        Assert.True(root.GetProperty("HasCompletedOnboarding").GetBoolean());
    }

    [Fact]
    public void NestedProtectedFieldsAreAlsoRedacted()
    {
        const string input = """
        {
          "OuterField": "kept",
          "Nested": { "InnerProtected": "secret", "InnerPlain": "kept" }
        }
        """;
        string output = DiagnosticsRedactor.Redact(input);
        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.Equal("<redacted>", doc.RootElement.GetProperty("Nested").GetProperty("InnerProtected").GetString());
        Assert.Equal("kept", doc.RootElement.GetProperty("Nested").GetProperty("InnerPlain").GetString());
    }

    [Theory]
    [InlineData("HaTokenProtected", true)]
    [InlineData("DiscordRefreshTokenProtected", true)]
    [InlineData("HaBaseUrl", false)]
    [InlineData("DiscordClientId", false)]
    [InlineData("Protected", true)]    // exactly "Protected" still ends with "Protected"
    [InlineData("protected", false)]   // case-sensitive
    [InlineData("", false)]
    public void IsProtectedFieldName_Truthtable(string name, bool expected)
    {
        Assert.Equal(expected, DiagnosticsRedactor.IsProtectedFieldName(name));
    }

    [Fact]
    public void ThrowsOnMalformedJson()
    {
        // System.Text.Json throws JsonReaderException (a subclass of JsonException) — accept either.
        Assert.ThrowsAny<JsonException>(() => DiagnosticsRedactor.Redact("{not json"));
    }

    [Fact]
    public void ThrowsOnNonObjectRoot()
    {
        Assert.Throws<JsonException>(() => DiagnosticsRedactor.Redact("[1,2,3]"));
    }

    [Fact]
    public void ThrowsOnEmpty()
    {
        Assert.Throws<System.ArgumentException>(() => DiagnosticsRedactor.Redact(""));
    }
}
