using DiscordHass;
using DiscordHass.Config;
using Xunit;

namespace DiscordHass.Tests;

public class UpgradeBackfillTests
{
    [Fact]
    public void LooksConfigured_True_WhenHaUrlSet()
    {
        AppConfig c = new() { HaBaseUrl = "http://homeassistant.local:8123" };
        Assert.True(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_True_EvenWithMissingDiscordCredentials()
    {
        // Long-time user whose Discord refresh token was cleared by the scope-mismatch
        // logic in BridgeService (or by manually clicking "Clear cached tokens") should
        // still be considered configured: they've used the app before, the wizard would
        // be noise, and the Settings status chips make missing Discord setup obvious.
        AppConfig c = new()
        {
            HaBaseUrl = "http://homeassistant.local:8123",
            HaTokenProtected = "dpapi-blob",
            DiscordClientId = "1234567890",
            DiscordRefreshTokenProtected = null,
        };
        Assert.True(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_WhenHaUrlEmpty()
    {
        AppConfig c = new() { HaBaseUrl = "" };
        Assert.False(Program.LooksConfigured(c));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void LooksConfigured_False_WhenHaUrlIsWhitespaceOnly(string blank)
    {
        AppConfig c = new() { HaBaseUrl = blank };
        Assert.False(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_OnFreshConfig()
    {
        // Default AppConfig has HaBaseUrl = "", so a brand-new install still gets the
        // first-run wizard.
        Assert.False(Program.LooksConfigured(new AppConfig()));
    }
}
