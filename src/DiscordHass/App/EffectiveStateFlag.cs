using System;
using DiscordHass.Discord;

namespace DiscordHass.App;

internal sealed record EffectiveStateFlag(
    string FlagId,
    string EntityIdSlug,
    string FriendlyName,
    string Icon,
    Func<VoiceState, bool> ValueSelector);
