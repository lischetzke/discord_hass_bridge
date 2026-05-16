using DiscordHass.Ui;
using Xunit;

namespace DiscordHass.Tests;

public class SetupActionsTests
{
    [Fact]
    public async System.Threading.Tasks.Task TestHa_ReturnsMissingInput_WhenUrlBlank()
    {
        HaTestResult result = await SetupActions.TestHaConnectionAsync("", "token", default);
        Assert.IsType<HaTestResult.MissingInput>(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestHa_ReturnsMissingInput_WhenTokenBlank()
    {
        HaTestResult result = await SetupActions.TestHaConnectionAsync("http://x:8123", "", default);
        Assert.IsType<HaTestResult.MissingInput>(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task TestHa_ReturnsFailure_WhenUrlUnreachable()
    {
        // Use a port on localhost that is overwhelmingly likely to refuse connections so the
        // test runs fast (the inner CTS caps at 10s but a localhost refuse is instant).
        HaTestResult result = await SetupActions.TestHaConnectionAsync(
            "http://127.0.0.1:1/", "any-token", default);
        Assert.IsType<HaTestResult.Failure>(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AuthorizeDiscord_ReturnsMissingInput_WhenClientIdBlank()
    {
        DiscordAuthResult result = await SetupActions.AuthorizeDiscordAsync("", "secret", default);
        Assert.IsType<DiscordAuthResult.MissingInput>(result);
    }

    [Fact]
    public async System.Threading.Tasks.Task AuthorizeDiscord_ReturnsMissingInput_WhenClientSecretBlank()
    {
        DiscordAuthResult result = await SetupActions.AuthorizeDiscordAsync("1234567890", "", default);
        Assert.IsType<DiscordAuthResult.MissingInput>(result);
    }
}
