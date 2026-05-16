using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.App;
using DiscordHass.Config;
using DiscordHass.Discord;
using DiscordHass.HomeAssistant;

namespace DiscordHass.Ui;

/// <summary>
/// "Test HA connection" result. <see cref="Success"/> records the number of helpers found,
/// failure records a user-facing error message. Pure record types so they can be unit-tested.
/// </summary>
internal abstract record HaTestResult
{
    public sealed record Success(int InputBooleanCount) : HaTestResult;
    public sealed record Failure(string Message)        : HaTestResult;
    public sealed record MissingInput(string Message)   : HaTestResult;
}

/// <summary>
/// "Authorize with Discord" result. <see cref="Success"/> carries the new tokens (ready to be
/// persisted via DPAPI), <see cref="Failure"/> a user-facing error.
/// </summary>
internal abstract record DiscordAuthResult
{
    public sealed record Success(DiscordTokens Tokens) : DiscordAuthResult;
    public sealed record Failure(string Message)       : DiscordAuthResult;
    public sealed record MissingInput(string Message)  : DiscordAuthResult;
}

/// <summary>
/// Shared connection-test + authorize logic used by both Settings → Discord/HA tabs and the
/// onboarding wizard. Keeping the network/auth flow here means SettingsForm and the wizard
/// only have to render status; nothing duplicated, and the result types are unit-testable.
/// </summary>
internal static class SetupActions
{
    /// <summary>
    /// Opens a HA WebSocket connection with <paramref name="url"/> + <paramref name="token"/>,
    /// authenticates, then calls <c>input_boolean/list</c> to count helpers. Always closes the
    /// connection before returning. Never throws — failures come back as a Failure record.
    /// </summary>
    public static async Task<HaTestResult> TestHaConnectionAsync(
        string url, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
        {
            return new HaTestResult.MissingInput("Enter a URL and token first.");
        }

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            await using HaWebSocketClient client = new(url, token);
            await client.ConnectAndAuthenticateAsync(cts.Token).ConfigureAwait(false);
            JsonElement listResult = await client.SendCommandAsync(
                new { type = "input_boolean/list" }, cts.Token).ConfigureAwait(false);
            int count = listResult.ValueKind == JsonValueKind.Array ? listResult.GetArrayLength() : 0;
            return new HaTestResult.Success(count);
        }
        catch (OperationCanceledException)
        {
            return new HaTestResult.Failure("Timed out waiting for Home Assistant.");
        }
        catch (Exception ex)
        {
            return new HaTestResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Runs the full Discord OAuth flow: AUTHORIZE over the IPC pipe, exchange code for token
    /// via the OAuth endpoint, and return the resulting <see cref="DiscordTokens"/>. The caller
    /// is responsible for encrypting + persisting the tokens. Never throws — failures come
    /// back as a Failure record.
    /// </summary>
    public static async Task<DiscordAuthResult> AuthorizeDiscordAsync(
        string clientId, string clientSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new DiscordAuthResult.MissingInput("Enter both Client ID and Client Secret first.");
        }

        try
        {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            await using DiscordRpcSession session = new();
            string code = await session.AuthorizeAsync(clientId, cts.Token).ConfigureAwait(false);

            DiscordOAuth oauth = new();
            DiscordTokens tokens = await oauth.ExchangeCodeAsync(
                clientId, clientSecret, code, AppConstants.DiscordOAuthRedirectUri, cts.Token).ConfigureAwait(false);
            return new DiscordAuthResult.Success(tokens);
        }
        catch (OperationCanceledException)
        {
            return new DiscordAuthResult.Failure("Authorization timed out — make sure you clicked Authorize in Discord.");
        }
        catch (Exception ex)
        {
            return new DiscordAuthResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Persists a successful Discord authorization into <paramref name="config"/> + saves.
    /// Always encrypts secrets with DPAPI before writing.
    /// </summary>
    public static void PersistDiscordTokens(
        AppConfig config, ConfigStore configStore,
        string clientId, string clientSecret, DiscordTokens tokens)
    {
        config.DiscordClientId = clientId;
        config.DiscordClientSecretProtected = SecretProtector.Protect(clientSecret);
        config.DiscordAccessTokenProtected = SecretProtector.Protect(tokens.AccessToken);
        config.DiscordAccessTokenExpiresAtUnix = tokens.ExpiresAt.ToUnixTimeSeconds();
        config.DiscordRefreshTokenProtected = SecretProtector.Protect(tokens.RefreshToken);
        config.DiscordAuthorizedScopeKey = DiscordScopes.CurrentKey();
        config.DiscordGrantedScopes = tokens.GrantedScopes;
        configStore.Save(config);
    }
}
