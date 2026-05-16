using DiscordHass;
using DiscordHass.Config;
using Xunit;

namespace DiscordHass.Tests;

public class UpgradeBackfillTests
{
    private static AppConfig Configured() => new()
    {
        HaBaseUrl                    = "http://homeassistant.local:8123",
        HaTokenProtected             = "dpapi-blob-1",
        DiscordClientId              = "1234567890",
        DiscordRefreshTokenProtected = "dpapi-blob-2",
    };

    [Fact]
    public void LooksConfigured_True_WhenAllFourCredentialsPresent()
    {
        Assert.True(Program.LooksConfigured(Configured()));
    }

    [Fact]
    public void LooksConfigured_False_WhenHaUrlMissing()
    {
        AppConfig c = Configured();
        c.HaBaseUrl = "";
        Assert.False(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_WhenHaTokenMissing()
    {
        AppConfig c = Configured();
        c.HaTokenProtected = null;
        Assert.False(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_WhenClientIdMissing()
    {
        AppConfig c = Configured();
        c.DiscordClientId = "";
        Assert.False(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_WhenRefreshTokenMissing()
    {
        AppConfig c = Configured();
        c.DiscordRefreshTokenProtected = null;
        Assert.False(Program.LooksConfigured(c));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void LooksConfigured_False_WhenAnyFieldIsWhitespaceOnly(string blank)
    {
        AppConfig c = Configured();
        c.HaBaseUrl = blank;
        Assert.False(Program.LooksConfigured(c));
    }

    [Fact]
    public void LooksConfigured_False_OnFreshConfig()
    {
        Assert.False(Program.LooksConfigured(new AppConfig()));
    }
}
