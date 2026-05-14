using System.Collections.Generic;
using System.Text;
using DiscordHass.Config;

namespace DiscordHass.App;

internal static class FlagResolver
{
    public static EffectiveStateFlag Resolve(StateFlagDefinition def, AppConfig config)
    {
        FlagOverride? ov = config.FlagOverrides.GetValueOrDefault(def.FlagId);
        string suffix = string.IsNullOrWhiteSpace(ov?.NameSuffix) ? def.DefaultNameSuffix : ov!.NameSuffix!.Trim();
        string icon = string.IsNullOrWhiteSpace(ov?.Icon) ? def.DefaultIcon : ov!.Icon!.Trim();
        string prefix = (config.HelperPrefix ?? "").Trim();
        string friendlyName = prefix.Length == 0 ? suffix : $"{prefix} {suffix}";
        string slug = Slugify(friendlyName);
        return new EffectiveStateFlag(def.FlagId, slug, friendlyName, icon, def.ValueSelector);
    }

    public static IEnumerable<EffectiveStateFlag> ResolveAll(AppConfig config)
    {
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            yield return Resolve(def, config);
        }
    }

    public static IEnumerable<EffectiveStateFlag> ResolveEnabled(AppConfig config)
    {
        foreach (StateFlagDefinition def in StateFlagDefinitions.All)
        {
            if (config.EnabledFlags.Contains(def.FlagId))
            {
                yield return Resolve(def, config);
            }
        }
    }

    // Mirrors Home Assistant's slug behavior closely enough for ASCII inputs:
    // lowercase, non-alphanumeric → '_', collapse repeats, trim leading/trailing '_'.
    public static string Slugify(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '_')
            {
                sb.Append('_');
            }
        }
        return sb.ToString().Trim('_');
    }
}
