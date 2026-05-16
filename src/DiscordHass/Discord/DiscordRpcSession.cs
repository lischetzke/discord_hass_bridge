using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.App;

namespace DiscordHass.Discord;

internal sealed class DiscordRpcSession : IAsyncDisposable
{
    private readonly DiscordIpcClient _ipc = new();

    private string? _currentUserId;
    private string? _currentChannelId;
    private VoiceState _state = VoiceState.Empty;

    public VoiceState CurrentState => _state;
    public string? CurrentUserName { get; private set; }
    public bool IsConnected => _ipc.IsConnected;

    public event EventHandler<VoiceState>? VoiceStateChanged;
    public event EventHandler? Disconnected;

    public DiscordRpcSession()
    {
        _ipc.EventReceived += OnIpcEvent;
        _ipc.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task ConnectAsync(string clientId, string accessToken, CancellationToken ct)
    {
        await _ipc.ConnectAndHandshakeAsync(clientId, ct).ConfigureAwait(false);

        JsonElement authResult = await _ipc.SendCommandAsync("AUTHENTICATE", new { access_token = accessToken }, ct).ConfigureAwait(false);
        if (authResult.ValueKind == JsonValueKind.Object && authResult.TryGetProperty("user", out JsonElement userEl))
        {
            if (userEl.TryGetProperty("id", out JsonElement idEl))
            {
                _currentUserId = idEl.GetString();
            }
            if (userEl.TryGetProperty("username", out JsonElement nameEl))
            {
                CurrentUserName = nameEl.GetString();
            }
        }

        // Seed initial state
        try
        {
            JsonElement vs = await _ipc.SendCommandAsync("GET_VOICE_SETTINGS", new { }, ct).ConfigureAwait(false);
            if (vs.ValueKind == JsonValueKind.Object)
            {
                if (vs.TryGetProperty("mute", out JsonElement m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _state = _state.WithSelfMute(m.GetBoolean());
                }
                if (vs.TryGetProperty("deaf", out JsonElement d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _state = _state.WithSelfDeaf(d.GetBoolean());
                }
            }
        }
        catch (DiscordIpcCommandException)
        {
            // Older clients may not support this command; tolerate.
        }

        try
        {
            JsonElement selected = await _ipc.SendCommandAsync("GET_SELECTED_VOICE_CHANNEL", new { }, ct).ConfigureAwait(false);
            if (selected.ValueKind == JsonValueKind.Object && selected.TryGetProperty("id", out JsonElement chIdEl))
            {
                string? channelId = chIdEl.GetString();
                if (!string.IsNullOrEmpty(channelId))
                {
                    _currentChannelId = channelId;
                    _state = _state.WithChannel(channelId);
                    ExtractSelfVoiceStateFromChannel(selected);
                }
            }
        }
        catch (DiscordIpcCommandException)
        {
            // Tolerate
        }

        // Subscribe to relevant events
        await _ipc.SubscribeAsync("VOICE_CHANNEL_SELECT", new { }, ct).ConfigureAwait(false);
        await _ipc.SubscribeAsync("VOICE_SETTINGS_UPDATE", new { }, ct).ConfigureAwait(false);

        if (_currentChannelId is not null)
        {
            await _ipc.SubscribeAsync("VOICE_STATE_UPDATE", new { channel_id = _currentChannelId }, ct).ConfigureAwait(false);
        }

        RaiseStateChanged();
    }

    public async Task<string> AuthorizeAsync(string clientId, CancellationToken ct)
    {
        await _ipc.ConnectAndHandshakeAsync(clientId, ct).ConfigureAwait(false);

        JsonElement result = await _ipc.SendCommandAsync(
            "AUTHORIZE",
            new { client_id = clientId, scopes = DiscordScopes.Required },
            ct).ConfigureAwait(false);

        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("code", out JsonElement codeEl))
        {
            throw new DiscordIpcCommandException("AUTHORIZE response missing code");
        }
        return codeEl.GetString() ?? throw new DiscordIpcCommandException("AUTHORIZE code was null");
    }

    private void OnIpcEvent(object? sender, DiscordIpcEvent ev)
    {
        AppMetrics.IncrementDiscordEvent();
        switch (ev.EventName)
        {
            case "VOICE_CHANNEL_SELECT":
                HandleChannelSelect(ev.Data);
                break;
            case "VOICE_SETTINGS_UPDATE":
                HandleVoiceSettingsUpdate(ev.Data);
                break;
            case "VOICE_STATE_UPDATE":
                HandleVoiceStateUpdate(ev.Data);
                break;
            case "VOICE_STATE_CREATE":
            case "VOICE_STATE_DELETE":
                HandleVoiceStateUpdate(ev.Data);
                break;
        }
    }

    private void HandleChannelSelect(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? newChannelId = null;
        if (data.TryGetProperty("channel_id", out JsonElement chIdEl) && chIdEl.ValueKind == JsonValueKind.String)
        {
            newChannelId = chIdEl.GetString();
        }

        string? oldChannelId = _currentChannelId;
        if (string.Equals(oldChannelId, newChannelId, StringComparison.Ordinal))
        {
            return;
        }

        // Best-effort un/subscribe (fire-and-forget; failures are tolerable)
        if (oldChannelId is not null)
        {
            _ = _ipc.UnsubscribeAsync("VOICE_STATE_UPDATE", new { channel_id = oldChannelId }, CancellationToken.None);
        }
        if (newChannelId is not null)
        {
            _ = _ipc.SubscribeAsync("VOICE_STATE_UPDATE", new { channel_id = newChannelId }, CancellationToken.None);
        }

        _currentChannelId = newChannelId;
        VoiceState updated = _state.WithChannel(newChannelId);
        // Camera state is managed by WindowsCameraWatcher (Discord RPC does not expose
        // self_video to user-registered apps), so we deliberately do not touch it here.
        SetState(updated);
    }

    private void HandleVoiceSettingsUpdate(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        VoiceState updated = _state;
        if (data.TryGetProperty("mute", out JsonElement m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            updated = updated.WithSelfMute(m.GetBoolean());
        }
        if (data.TryGetProperty("deaf", out JsonElement d) && d.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            updated = updated.WithSelfDeaf(d.GetBoolean());
        }
        SetState(updated);
    }

    private void HandleVoiceStateUpdate(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || _currentUserId is null)
        {
            return;
        }

        // Voice state may be either flat (data IS the voice state) or wrapped under "voice_state"
        JsonElement vs = data;
        if (data.TryGetProperty("voice_state", out JsonElement wrappedVs) && wrappedVs.ValueKind == JsonValueKind.Object)
        {
            vs = wrappedVs;
        }

        // User filter: only react to local user updates
        string? userId = null;
        if (data.TryGetProperty("user", out JsonElement userEl) && userEl.ValueKind == JsonValueKind.Object
            && userEl.TryGetProperty("id", out JsonElement uidEl))
        {
            userId = uidEl.GetString();
        }
        else if (vs.TryGetProperty("user_id", out JsonElement uid2El))
        {
            userId = uid2El.GetString();
        }

        if (userId is not null && !string.Equals(userId, _currentUserId, StringComparison.Ordinal))
        {
            return;
        }

        DiscordVoiceStateDto? dto = vs.Deserialize<DiscordVoiceStateDto>();
        if (dto is null)
        {
            return;
        }

        // SelfVideo is intentionally not consumed here — Discord's RPC voice_state object
        // never includes self_video for user-registered applications, so the DTO field is
        // always false and would clobber the OS-detected camera state managed in
        // BridgeService. See CapabilityAccessParser for the OS-side mechanism.
        VoiceState updated = _state
            .WithSelfMute(dto.SelfMute)
            .WithSelfDeaf(dto.SelfDeaf)
            .WithServerMute(dto.Mute)
            .WithServerDeaf(dto.Deaf);
        SetState(updated);
    }

    private void ExtractSelfVoiceStateFromChannel(JsonElement channelObj)
    {
        if (_currentUserId is null
            || !channelObj.TryGetProperty("voice_states", out JsonElement vss)
            || vss.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement entry in vss.EnumerateArray())
        {
            string? uid = null;
            if (entry.TryGetProperty("user", out JsonElement userEl) && userEl.ValueKind == JsonValueKind.Object
                && userEl.TryGetProperty("id", out JsonElement uidEl))
            {
                uid = uidEl.GetString();
            }
            if (uid != _currentUserId)
            {
                continue;
            }
            if (entry.TryGetProperty("voice_state", out JsonElement vs)
                && vs.Deserialize<DiscordVoiceStateDto>() is DiscordVoiceStateDto dto)
            {
                // SelfVideo intentionally skipped — see HandleVoiceStateUpdate for context.
                _state = _state
                    .WithSelfMute(dto.SelfMute)
                    .WithSelfDeaf(dto.SelfDeaf)
                    .WithServerMute(dto.Mute)
                    .WithServerDeaf(dto.Deaf);
            }
            break;
        }
    }

    private void SetState(VoiceState updated)
    {
        if (updated == _state)
        {
            return;
        }
        _state = updated;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        VoiceStateChanged?.Invoke(this, _state);
    }

    public ValueTask DisposeAsync() => _ipc.DisposeAsync();
}
