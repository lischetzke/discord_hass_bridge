using System.Collections.Generic;
using DiscordHass.Discord;

namespace DiscordHass.App;

internal readonly record struct HelperUpdate(
    string FlagId,
    string EntityIdSlug,
    string FriendlyName,
    string Icon,
    bool DesiredOn);

internal static class StateMapper
{
    public static List<HelperUpdate> ComputeUpdates(
        VoiceState? previous,
        VoiceState current,
        IEnumerable<EffectiveStateFlag> effectiveFlags)
    {
        List<HelperUpdate> result = new();

        foreach (EffectiveStateFlag eff in effectiveFlags)
        {
            bool desired = eff.ValueSelector(current);
            if (previous is VoiceState prev && eff.ValueSelector(prev) == desired)
            {
                continue;
            }

            result.Add(new HelperUpdate(eff.FlagId, eff.EntityIdSlug, eff.FriendlyName, eff.Icon, desired));
        }

        return result;
    }

    public static List<HelperUpdate> ComputeAllOff(IEnumerable<EffectiveStateFlag> effectiveFlags)
    {
        List<HelperUpdate> result = new();
        foreach (EffectiveStateFlag eff in effectiveFlags)
        {
            result.Add(new HelperUpdate(eff.FlagId, eff.EntityIdSlug, eff.FriendlyName, eff.Icon, false));
        }
        return result;
    }
}
