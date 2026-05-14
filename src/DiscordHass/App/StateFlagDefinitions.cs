using System;
using System.Collections.Generic;
using DiscordHass.Config;
using DiscordHass.Discord;

namespace DiscordHass.App;

internal sealed record StateFlagDefinition(
    string FlagId,
    string DefaultNameSuffix,
    string DefaultIcon,
    Func<VoiceState, bool> ValueSelector);

internal static class StateFlagDefinitions
{
    public static readonly IReadOnlyList<StateFlagDefinition> All = new[]
    {
        new StateFlagDefinition(StateFlagIds.InCall,         "In Call",          "mdi:phone-in-talk",   s => s.IsInCall),
        new StateFlagDefinition(StateFlagIds.MicMuted,       "Mic Muted",        "mdi:microphone-off",  s => s.MicMuted),
        new StateFlagDefinition(StateFlagIds.SpeakerMuted,   "Speaker Muted",    "mdi:volume-off",      s => s.SpeakerMuted),
        new StateFlagDefinition(StateFlagIds.CameraOn,       "Camera On",        "mdi:camera",          s => s.CameraOn),
        new StateFlagDefinition(StateFlagIds.ServerMuted,    "Server Muted",     "mdi:microphone-off",  s => s.ServerMuted),
        new StateFlagDefinition(StateFlagIds.ServerDeafened, "Server Deafened",  "mdi:headphones-off",  s => s.ServerDeafened),
        new StateFlagDefinition(StateFlagIds.Busy,           "Busy",             "mdi:do-not-disturb",  s => s.Busy),
    };

    public static StateFlagDefinition? FindByFlagId(string flagId)
    {
        foreach (StateFlagDefinition def in All)
        {
            if (string.Equals(def.FlagId, flagId, StringComparison.Ordinal))
            {
                return def;
            }
        }
        return null;
    }
}
