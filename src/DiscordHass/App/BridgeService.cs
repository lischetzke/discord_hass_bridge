using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscordHass.Config;
using DiscordHass.Discord;
using DiscordHass.HomeAssistant;

namespace DiscordHass.App;

internal enum ConnectionPhase
{
    Idle,
    Connecting,
    Connected,
    Reconnecting,
    Faulted,
}

internal sealed class ConnectionStatus
{
    public ConnectionPhase Phase { get; set; } = ConnectionPhase.Idle;
    public string? LastError { get; set; }
    public DateTimeOffset? ChangedAt { get; set; }
}

internal sealed class BridgeService : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly ConfigStore _configStore;
    private readonly DiscordOAuth _oauth = new();

    private CancellationTokenSource _cts = new();
    private Task? _discordRunner;
    private Task? _haRunner;

    private DiscordRpcSession? _discord;
    private HaWebSocketClient? _ha;
    private HaHelperManager? _haHelpers;
    private WindowsCameraWatcher? _cameraWatcher;

    private VoiceState _voiceFromDiscord = VoiceState.Empty;
    private bool _cameraFromOs;

    private VoiceState? _lastPublishedState;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly LatencyHistogram _publishLatency = new(capacity: 256);

    /// <summary>Read-only handle for the Settings → Performance section.</summary>
    public LatencyHistogram PublishLatency => _publishLatency;

    public ConnectionStatus DiscordStatus { get; } = new();
    public ConnectionStatus HaStatus { get; } = new();
    /// <summary>
    /// Merged state from both signal sources: voice fields (in-call, mic/speaker mute, server
    /// mute/deaf) come from Discord RPC, while camera comes from the Windows Capability
    /// Access Manager registry — Discord's RPC does not expose self_video to user-registered
    /// applications.
    /// </summary>
    public VoiceState CurrentVoiceState => _voiceFromDiscord with { CameraOn = _cameraFromOs };
    public string? DiscordUserName { get; private set; }

    public event EventHandler? StatusChanged;
    public event EventHandler? VoiceStateChanged;

    public BridgeService(AppConfig config, ConfigStore configStore)
    {
        _config = config;
        _configStore = configStore;
    }

    public void Start()
    {
        if (_discordRunner is not null || _haRunner is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();

        _cameraWatcher = new WindowsCameraWatcher();
        _cameraWatcher.CameraStateChanged += OnCameraStateChanged;
        _cameraWatcher.Start();

        _discordRunner = Task.Run(() => RunDiscordLoopAsync(_cts.Token));
        _haRunner = Task.Run(() => RunHaLoopAsync(_cts.Token));
    }

    public async Task RestartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Start();
    }

    public async Task StopAsync()
    {
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }

        Task? discordRunner = _discordRunner;
        Task? haRunner = _haRunner;
        _discordRunner = null;
        _haRunner = null;

        if (discordRunner is not null)
        {
            try { await discordRunner.ConfigureAwait(false); } catch { /* shutdown */ }
        }
        if (haRunner is not null)
        {
            try { await haRunner.ConfigureAwait(false); } catch { /* shutdown */ }
        }

        if (_cameraWatcher is not null)
        {
            _cameraWatcher.CameraStateChanged -= OnCameraStateChanged;
            await _cameraWatcher.DisposeAsync().ConfigureAwait(false);
            _cameraWatcher = null;
            _cameraFromOs = false;
        }
        if (_discord is not null)
        {
            await _discord.DisposeAsync().ConfigureAwait(false);
            _discord = null;
        }
        if (_ha is not null)
        {
            await _ha.DisposeAsync().ConfigureAwait(false);
            _ha = null;
            _haHelpers = null;
        }

        DiscordStatus.Phase = ConnectionPhase.Idle;
        HaStatus.Phase = ConnectionPhase.Idle;
        DiscordStatus.ChangedAt = HaStatus.ChangedAt = DateTimeOffset.UtcNow;
        RaiseStatusChanged();
    }

    private async Task RunDiscordLoopAsync(CancellationToken ct)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(_config.DiscordClientId)
                || string.IsNullOrEmpty(_config.DiscordClientSecretProtected)
                || string.IsNullOrEmpty(_config.DiscordRefreshTokenProtected))
            {
                SetStatus(DiscordStatus, ConnectionPhase.Faulted, "Discord not configured — open Settings to authorize.");
                await SafeDelayAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                SetStatus(DiscordStatus, ConnectionPhase.Connecting, null);
                string accessToken = await EnsureDiscordAccessTokenAsync(ct).ConfigureAwait(false);
                DiscordRpcSession session = new();
                session.VoiceStateChanged += OnDiscordVoiceStateChanged;
                await session.ConnectAsync(_config.DiscordClientId, accessToken, ct).ConfigureAwait(false);
                _discord = session;
                DiscordUserName = session.CurrentUserName;
                _voiceFromDiscord = session.CurrentState;
                SetStatus(DiscordStatus, ConnectionPhase.Connected, null);
                backoff = TimeSpan.FromSeconds(2);

                TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler handler = (_, _) => closed.TrySetResult();
                session.Disconnected += handler;
                using (ct.Register(() => closed.TrySetCanceled()))
                {
                    try { await closed.Task.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
                session.Disconnected -= handler;
                session.VoiceStateChanged -= OnDiscordVoiceStateChanged;

                _discord = null;
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetStatus(DiscordStatus, ConnectionPhase.Faulted, ex.Message);
            }

            if (ct.IsCancellationRequested) break;

            try { await PublishAllOffAsync(ct).ConfigureAwait(false); } catch { /* best effort */ }

            SetStatus(DiscordStatus, ConnectionPhase.Reconnecting, DiscordStatus.LastError);
            AppMetrics.IncrementDiscordReconnect();
            await SafeDelayAsync(backoff, ct).ConfigureAwait(false);
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task RunHaLoopAsync(CancellationToken ct)
    {
        TimeSpan backoff = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(_config.HaBaseUrl) || string.IsNullOrEmpty(_config.HaTokenProtected))
            {
                SetStatus(HaStatus, ConnectionPhase.Faulted, "Home Assistant not configured — open Settings.");
                await SafeDelayAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            string? token = SecretProtector.Unprotect(_config.HaTokenProtected);
            if (string.IsNullOrEmpty(token))
            {
                SetStatus(HaStatus, ConnectionPhase.Faulted, "Stored HA token could not be decrypted.");
                await SafeDelayAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                continue;
            }

            HaWebSocketClient? client = null;
            try
            {
                SetStatus(HaStatus, ConnectionPhase.Connecting, null);
                client = new HaWebSocketClient(_config.HaBaseUrl, token!);
                await client.ConnectAndAuthenticateAsync(ct).ConfigureAwait(false);

                HaHelperManager helpers = new(client);
                await EnsureAllEnabledHelpersAsync(helpers, ct).ConfigureAwait(false);

                _ha = client;
                _haHelpers = helpers;
                SetStatus(HaStatus, ConnectionPhase.Connected, null);
                backoff = TimeSpan.FromSeconds(2);

                _lastPublishedState = null;
                await PublishCurrentAsync(ct).ConfigureAwait(false);

                TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler handler = (_, _) => closed.TrySetResult();
                client.Disconnected += handler;
                using (ct.Register(() => closed.TrySetCanceled()))
                {
                    try { await closed.Task.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
                client.Disconnected -= handler;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                SetStatus(HaStatus, ConnectionPhase.Faulted, ex.Message);
            }
            finally
            {
                _ha = null;
                _haHelpers = null;
                if (client is not null)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
            }

            if (ct.IsCancellationRequested) break;
            SetStatus(HaStatus, ConnectionPhase.Reconnecting, HaStatus.LastError);
            AppMetrics.IncrementHaReconnect();
            await SafeDelayAsync(backoff, ct).ConfigureAwait(false);
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    private async Task EnsureAllEnabledHelpersAsync(HaHelperManager helpers, CancellationToken ct)
    {
        bool configDirty = false;
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            if (!_config.EnabledFlags.Contains(def.FlagId)) continue;
            EffectiveStateFlag eff = FlagResolver.Resolve(def, _config);
            FlagOverride ov = _config.GetOrCreateOverride(def.FlagId);
            string? lastSlug = ov.LastEntityIdSlug;
            string actualSlug = await helpers.EnsureAndSyncAsync(
                eff.EntityIdSlug, eff.FriendlyName, eff.Icon, lastSlug, ct).ConfigureAwait(false);
            if (!string.Equals(actualSlug, lastSlug, StringComparison.Ordinal))
            {
                ov.LastEntityIdSlug = actualSlug;
                configDirty = true;
            }
        }
        if (configDirty)
        {
            try { _configStore.Save(_config); } catch { /* don't fail bridge over save error */ }
        }
    }

    private void OnDiscordVoiceStateChanged(object? sender, VoiceState state)
    {
        _voiceFromDiscord = state;
        VoiceStateChanged?.Invoke(this, EventArgs.Empty);
        _ = PublishCurrentAsync(_cts.Token);
    }

    private void OnCameraStateChanged(object? sender, bool cameraOn)
    {
        _cameraFromOs = cameraOn;
        VoiceStateChanged?.Invoke(this, EventArgs.Empty);
        _ = PublishCurrentAsync(_cts.Token);
    }

    private async Task PublishCurrentAsync(CancellationToken ct)
    {
        HaHelperManager? helpers = _haHelpers;
        if (helpers is null) return;

        await _publishLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<EffectiveStateFlag> effective = FlagResolver.ResolveEnabled(_config).ToList();
            List<HelperUpdate> updates = StateMapper.ComputeUpdates(_lastPublishedState, CurrentVoiceState, effective);
            bool configDirty = false;
            foreach (HelperUpdate u in updates)
            {
                FlagOverride ov = _config.GetOrCreateOverride(u.FlagId);
                try
                {
                    string actualSlug = await helpers.EnsureAndSyncAsync(
                        u.EntityIdSlug, u.FriendlyName, u.Icon, ov.LastEntityIdSlug, ct).ConfigureAwait(false);
                    if (!string.Equals(actualSlug, ov.LastEntityIdSlug, StringComparison.Ordinal))
                    {
                        ov.LastEntityIdSlug = actualSlug;
                        configDirty = true;
                    }
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    await helpers.SetStateAsync(actualSlug, u.DesiredOn, ct).ConfigureAwait(false);
                    sw.Stop();
                    _publishLatency.Record(sw.Elapsed);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    SetStatus(HaStatus, ConnectionPhase.Faulted, $"Publish failed: {ex.Message}");
                    return;
                }
            }
            if (configDirty)
            {
                try { _configStore.Save(_config); } catch { /* tolerate */ }
            }
            _lastPublishedState = CurrentVoiceState;
        }
        finally
        {
            try { _publishLock.Release(); } catch { /* disposed */ }
        }
    }

    /// <summary>
    /// Forces a single state publish to HA for one flag, ignoring Discord's view of reality.
    /// Used by the "Test publish" button on Settings → States. Throws if HA isn't connected
    /// or the flag id is unknown — callers should grey the button when <see cref="HaStatus"/>
    /// isn't Connected.
    /// </summary>
    public async Task PublishTestAsync(string flagId, bool desiredOn, CancellationToken ct)
    {
        HaHelperManager? helpers = _haHelpers;
        if (helpers is null)
        {
            throw new InvalidOperationException("Home Assistant is not connected.");
        }
        StateFlagDefinition def = StateFlagDefinitions.FindByFlagId(flagId)
            ?? throw new ArgumentException($"Unknown flag id: {flagId}", nameof(flagId));

        EffectiveStateFlag eff = FlagResolver.Resolve(def, _config);
        FlagOverride ov = _config.GetOrCreateOverride(flagId);

        await _publishLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Make sure the helper exists / is renamed before we publish to it. EnsureAndSync
            // will create-or-rename and return the slug that's actually in HA right now.
            string actualSlug = await helpers.EnsureAndSyncAsync(
                eff.EntityIdSlug, eff.FriendlyName, eff.Icon, ov.LastEntityIdSlug, ct).ConfigureAwait(false);
            if (!string.Equals(actualSlug, ov.LastEntityIdSlug, StringComparison.Ordinal))
            {
                ov.LastEntityIdSlug = actualSlug;
                try { _configStore.Save(_config); } catch { /* tolerate */ }
            }
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            await helpers.SetStateAsync(actualSlug, desiredOn, ct).ConfigureAwait(false);
            sw.Stop();
            _publishLatency.Record(sw.Elapsed);

            // Keep diff bookkeeping consistent so the next real Discord event publishes the
            // correct value rather than thinking nothing changed.
            if (_lastPublishedState is null)
            {
                _lastPublishedState = CurrentVoiceState;
            }
            AppMetrics.IncrementTestPublish();
        }
        finally
        {
            try { _publishLock.Release(); } catch { /* disposed */ }
        }
    }

    private async Task PublishAllOffAsync(CancellationToken ct)
    {
        HaHelperManager? helpers = _haHelpers;
        if (helpers is null) return;

        await _publishLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _voiceFromDiscord = VoiceState.Empty;
            _cameraFromOs = false;
            List<EffectiveStateFlag> effective = FlagResolver.ResolveEnabled(_config).ToList();
            List<HelperUpdate> offs = StateMapper.ComputeAllOff(effective);
            foreach (HelperUpdate u in offs)
            {
                FlagOverride ov = _config.GetOrCreateOverride(u.FlagId);
                string slug = ov.LastEntityIdSlug ?? u.EntityIdSlug;
                try { await helpers.SetStateAsync(slug, false, ct).ConfigureAwait(false); } catch { /* best effort */ }
            }
            _lastPublishedState = VoiceState.Empty;
            VoiceStateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            try { _publishLock.Release(); } catch { /* disposed */ }
        }
    }

    private async Task<string> EnsureDiscordAccessTokenAsync(CancellationToken ct)
    {
        // Cached tokens were issued for a specific scope set. Discord's refresh flow only
        // re-issues the original scopes, so a stale scope key here means the cached tokens
        // were granted for a different permission set than this version requires. Clear them
        // so the user is forced through a fresh AUTHORIZE.
        if (!string.IsNullOrEmpty(_config.DiscordRefreshTokenProtected)
            && !DiscordScopes.Matches(_config.DiscordAuthorizedScopeKey))
        {
            _config.DiscordAccessTokenProtected = null;
            _config.DiscordAccessTokenExpiresAtUnix = 0;
            _config.DiscordRefreshTokenProtected = null;
            _config.DiscordAuthorizedScopeKey = null;
            try { _configStore.Save(_config); } catch { /* tolerate */ }
            throw new DiscordIpcCommandException(
                "Discord permissions changed in this version — re-authorization required. " +
                "Open Settings → Discord → Authorize.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAt = DateTimeOffset.FromUnixTimeSeconds(_config.DiscordAccessTokenExpiresAtUnix);
        string? cached = SecretProtector.Unprotect(_config.DiscordAccessTokenProtected);
        if (!string.IsNullOrEmpty(cached) && expiresAt - now > TimeSpan.FromMinutes(5))
        {
            return cached!;
        }

        string? refreshToken = SecretProtector.Unprotect(_config.DiscordRefreshTokenProtected);
        string? clientSecret = SecretProtector.Unprotect(_config.DiscordClientSecretProtected);
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientSecret))
        {
            throw new DiscordIpcCommandException("No Discord refresh token / client secret — re-authorize in Settings.");
        }

        DiscordTokens tokens = await _oauth.RefreshAsync(_config.DiscordClientId, clientSecret!, refreshToken!, ct).ConfigureAwait(false);
        _config.DiscordAccessTokenProtected = SecretProtector.Protect(tokens.AccessToken);
        _config.DiscordAccessTokenExpiresAtUnix = tokens.ExpiresAt.ToUnixTimeSeconds();
        if (!string.IsNullOrEmpty(tokens.RefreshToken))
        {
            _config.DiscordRefreshTokenProtected = SecretProtector.Protect(tokens.RefreshToken);
        }
        if (!string.IsNullOrEmpty(tokens.GrantedScopes))
        {
            _config.DiscordGrantedScopes = tokens.GrantedScopes;
        }
        _configStore.Save(_config);
        return tokens.AccessToken;
    }

    private void SetStatus(ConnectionStatus status, ConnectionPhase phase, string? error)
    {
        status.Phase = phase;
        status.LastError = error;
        status.ChangedAt = DateTimeOffset.UtcNow;
        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
        _publishLock.Dispose();
    }
}
