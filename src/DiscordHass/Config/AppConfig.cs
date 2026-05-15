using System.Collections.Generic;

namespace DiscordHass.Config;

internal sealed class AppConfig
{
    public string HaBaseUrl { get; set; } = "";

    public string? HaTokenProtected { get; set; }

    public string DiscordClientId { get; set; } = "";

    public string? DiscordClientSecretProtected { get; set; }

    public string? DiscordAccessTokenProtected { get; set; }

    public long DiscordAccessTokenExpiresAtUnix { get; set; }

    public string? DiscordRefreshTokenProtected { get; set; }

    /// <summary>Scope set the cached Discord tokens were issued for; null means "never authorized".</summary>
    public string? DiscordAuthorizedScopeKey { get; set; }

    /// <summary>Scopes that Discord actually granted in the most recent token response (space-separated).</summary>
    public string? DiscordGrantedScopes { get; set; }

    public string HelperPrefix { get; set; } = "Discord";

    public Dictionary<string, FlagOverride> FlagOverrides { get; set; } = new();

    public HashSet<string> EnabledFlags { get; set; } = new(DefaultEnabledFlags);

    public bool AutostartEnabled { get; set; } = false;

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public bool CheckUpdatesAutomatically { get; set; } = true;

    public long LastUpdateCheckUnix { get; set; } = 0;

    public static readonly string[] DefaultEnabledFlags =
    {
        StateFlagIds.InCall,
        StateFlagIds.MicMuted,
        StateFlagIds.SpeakerMuted,
        StateFlagIds.CameraOn,
        StateFlagIds.Busy,
    };

    public FlagOverride GetOrCreateOverride(string flagId)
    {
        if (!FlagOverrides.TryGetValue(flagId, out FlagOverride? ov))
        {
            ov = new FlagOverride();
            FlagOverrides[flagId] = ov;
        }
        return ov;
    }
}

internal sealed class FlagOverride
{
    public string? NameSuffix { get; set; }
    public string? Icon { get; set; }
    public string? LastEntityIdSlug { get; set; }
}

internal static class StateFlagIds
{
    public const string InCall = "in_call";
    public const string MicMuted = "mic_muted";
    public const string SpeakerMuted = "speaker_muted";
    public const string CameraOn = "camera_on";
    public const string ServerMuted = "server_muted";
    public const string ServerDeafened = "server_deafened";
    public const string Busy = "busy";

    public static readonly string[] All =
    {
        InCall, MicMuted, SpeakerMuted, CameraOn, ServerMuted, ServerDeafened, Busy,
    };
}
