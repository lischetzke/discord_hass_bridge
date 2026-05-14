using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordHass.Discord;

internal sealed record DiscordTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

internal sealed class DiscordOAuth
{
    private const string TokenEndpoint = "https://discord.com/api/oauth2/token";

    private readonly HttpClient _http;

    public DiscordOAuth(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
    }

    public Task<DiscordTokens> ExchangeCodeAsync(string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
        };
        return PostTokenAsync(form, ct);
    }

    public Task<DiscordTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };
        return PostTokenAsync(form, ct);
    }

    private async Task<DiscordTokens> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using HttpRequestMessage req = new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        using HttpResponseMessage resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new DiscordIpcCommandException($"OAuth token request failed: {(int)resp.StatusCode} {resp.ReasonPhrase} — {body}");
        }
        TokenResponse? tr = await resp.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct).ConfigureAwait(false);
        if (tr is null || string.IsNullOrEmpty(tr.AccessToken))
        {
            throw new DiscordIpcCommandException("OAuth token response missing access_token");
        }
        return new DiscordTokens(
            tr.AccessToken!,
            tr.RefreshToken ?? "",
            DateTimeOffset.UtcNow.AddSeconds(tr.ExpiresIn > 0 ? tr.ExpiresIn : 604800));
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}
