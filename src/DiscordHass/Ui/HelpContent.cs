using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DiscordHass.App;

namespace DiscordHass.Ui;

internal sealed record HelpLink(string Caption, string Url);

internal sealed record HelpTopic(string Title, string Body, IReadOnlyList<HelpLink> Links)
{
    public static HelpTopic OfBody(string title, string body) => new(title, body, Array.Empty<HelpLink>());
    public static HelpTopic OfLinks(string title, string body, params HelpLink[] links)
        => new(title, body, links);
}

/// <summary>
/// Central catalog of help text shown when the user clicks a (?) icon next to a setting or in
/// the onboarding wizard. Strings are hardcoded — no localisation in v0.2.0 — but kept in one
/// place so they're easy to revise without hunting around UI files.
/// </summary>
internal static class HelpContent
{
    public static class TopicIds
    {
        public const string HaUrl                    = "ha.url";
        public const string HaToken                  = "ha.token";
        public const string DiscordClientId          = "discord.client_id";
        public const string DiscordClientSecret      = "discord.client_secret";
        public const string DiscordAuthorize         = "discord.authorize";
        public const string DiscordRegistrationGuide = "discord.registration_guide";
        public const string StatesPrefix             = "states.prefix";
        public const string StatesIcon               = "states.icon";
        public const string StatesFlags              = "states.flags";
        public const string StatesTest               = "states.test";
        public const string GeneralAutostart         = "general.autostart";
        public const string GeneralAutoupdate        = "general.autoupdate";
        public const string GeneralMinimize          = "general.minimize";
        public const string GeneralRunSetup          = "general.run_setup";
        public const string GeneralPerformance       = "general.performance";
        public const string GeneralDiagnostics       = "general.diagnostics";
        public const string OverviewIntro            = "overview.intro";
    }

    private static readonly Dictionary<string, HelpTopic> s_topics = BuildCatalog();

    public static HelpTopic Get(string topicId)
    {
        if (s_topics.TryGetValue(topicId, out HelpTopic? topic)) return topic;
        throw new KeyNotFoundException($"Unknown help topic: {topicId}");
    }

    public static IReadOnlyCollection<string> AllTopicIds => new ReadOnlyCollection<string>(new List<string>(s_topics.Keys));

    private static Dictionary<string, HelpTopic> BuildCatalog() => new()
    {
        [TopicIds.HaUrl] = HelpTopic.OfLinks(
            "Home Assistant URL",
            "The base URL where your Home Assistant instance is reachable from this PC. "
            + "It must include the scheme (http or https) and the port — for example "
            + "http://homeassistant.local:8123 or https://ha.example.com.\n\n"
            + "If you can open this URL in your browser and see the HA dashboard, DiscordHass "
            + "can use it too. Self-signed certificates may need to be trusted by Windows first.",
            new HelpLink("Home Assistant docs: remote access", "https://www.home-assistant.io/docs/configuration/remote/")),

        [TopicIds.HaToken] = HelpTopic.OfLinks(
            "Home Assistant long-lived access token",
            "Open your HA profile (click your name in the lower-left), scroll to the bottom, "
            + "and use \"Create Token\" under Long-Lived Access Tokens. Copy the token immediately "
            + "— HA only shows it once. Paste it here.\n\n"
            + "The token authenticates DiscordHass when it creates input_boolean helpers and "
            + "publishes their states. It is encrypted with Windows DPAPI before being saved.",
            new HelpLink("HA docs: long-lived access tokens", "https://www.home-assistant.io/docs/authentication/")),

        [TopicIds.DiscordClientId] = HelpTopic.OfBody(
            "Discord Client ID",
            "A unique numeric ID that identifies your Discord application. Click the (?) icon "
            + "next to \"How do I get this?\" for the full step-by-step registration guide."),

        [TopicIds.DiscordClientSecret] = HelpTopic.OfBody(
            "Discord Client Secret",
            "A confidential value that pairs with your Client ID and proves to Discord that the "
            + "OAuth token request really came from your application. Generate it on the Discord "
            + "developer portal under OAuth2 → Client Secret → Reset Secret. Discord only shows "
            + "it once; copy it immediately. DiscordHass encrypts it with Windows DPAPI before "
            + "saving."),

        [TopicIds.DiscordAuthorize] = HelpTopic.OfBody(
            "Authorize with Discord",
            "Clicking Authorize asks Discord (the desktop client running on this PC) to grant "
            + "DiscordHass access to your voice-session state. A small approval modal pops up "
            + "inside Discord — click Authorize there to confirm.\n\n"
            + "The cached refresh token Discord returns is encrypted with Windows DPAPI and only "
            + "decryptable by your Windows user account; copying config.json to another machine "
            + "won't work."),

        [TopicIds.DiscordRegistrationGuide] = HelpTopic.OfLinks(
            "How to register a Discord application",
            "DiscordHass needs you to create a small Discord application so it has its own "
            + "Client ID + Secret. This is a one-time step. Follow these in order:\n\n"
            + "1. Open the Discord developer portal (link below) and click \"New Application\".\n\n"
            + "2. Name it \"DiscordHass\" (or anything you like). Accept the developer ToS.\n\n"
            + "3. In the left sidebar choose OAuth2. Copy the Client ID from the top of the "
            + "OAuth2 page and paste it into the Client ID box below.\n\n"
            + "4. Under OAuth2 → Client Secret, click \"Reset Secret\" to generate one. Copy it "
            + "immediately — Discord only shows it once. Paste it into the Client Secret box.\n\n"
            + "5. Under OAuth2 → Redirects, click \"Add Redirect\" and paste exactly:\n"
            + $"      {AppConstants.DiscordOAuthRedirectUri}\n"
            + "   (Use the \"Copy redirect URI\" button in DiscordHass to grab it.)\n\n"
            + "6. Click \"Save Changes\" at the bottom of the Discord page.\n\n"
            + "7. Click Authorize back in DiscordHass — your Discord desktop client will pop up "
            + "an approval modal. Click Authorize there to grant access.\n\n"
            + "That's it. You won't need to revisit the Discord portal unless DiscordHass asks "
            + "you to re-authorize after a permission change.",
            new HelpLink("Discord Developer Portal", "https://discord.com/developers/applications"),
            new HelpLink("Discord OAuth2 docs", "https://discord.com/developers/docs/topics/oauth2")),

        [TopicIds.StatesPrefix] = HelpTopic.OfBody(
            "Helper name prefix",
            "Every HA helper DiscordHass creates uses this prefix in its display name. For "
            + "example with the default \"Discord\" prefix you'll get \"Discord In Call\", "
            + "\"Discord Mic Muted\", etc. Changing the prefix renames every existing helper in "
            + "HA the next time DiscordHass reconnects."),

        [TopicIds.StatesIcon] = HelpTopic.OfLinks(
            "Helper icon",
            "Material Design Icons identifier for the helper's icon in Home Assistant. Must "
            + "include the \"mdi:\" prefix — for example mdi:microphone-off. Leave empty to use "
            + "DiscordHass's default icon for this state. You can browse all available icons on "
            + "the MDI website.",
            new HelpLink("Browse Material Design Icons", "https://pictogrammers.com/library/mdi/")),

        [TopicIds.StatesFlags] = HelpTopic.OfBody(
            "Per-state checkboxes",
            "Uncheck a state to stop publishing it to HA. The existing helper (if any) is left "
            + "untouched — DiscordHass only stops writing new values. Re-enable the checkbox "
            + "to resume publishing. The helper is created on the next reconnect if it doesn't "
            + "already exist."),

        [TopicIds.StatesTest] = HelpTopic.OfBody(
            "Test publish",
            "Forces a single on/off toggle to HA for this flag, ignoring what Discord currently "
            + "reports. Useful for wiring HA automations against the helper without having to "
            + "actually join a voice channel or mute your mic.\n\n"
            + "The button is disabled when HA isn't connected or when there are unsaved changes "
            + "(test uses the saved helper name, not the in-flight UI state). Each click toggles "
            + "the opposite of what HA currently shows."),

        [TopicIds.GeneralAutostart] = HelpTopic.OfBody(
            "Start with Windows",
            "Adds a per-user registry entry so DiscordHass launches when you sign in. No admin "
            + "rights needed; removed cleanly when you uncheck this box."),

        [TopicIds.GeneralAutoupdate] = HelpTopic.OfBody(
            "Check for updates automatically",
            "Once per day DiscordHass quietly queries GitHub for a newer release. You're always "
            + "prompted before anything is installed. Disable this if you'd rather check "
            + "manually from the tray menu."),

        [TopicIds.GeneralMinimize] = HelpTopic.OfBody(
            "Minimize to tray on close",
            "When the close button (X) is clicked on the Overview or Settings window, the window "
            + "is hidden and the app keeps running in the tray. Uncheck to make X actually quit "
            + "the app. The tray icon's Quit menu always exits."),

        [TopicIds.GeneralRunSetup] = HelpTopic.OfBody(
            "Run setup wizard again",
            "Re-launches the first-run setup wizard so you can step through HA connection, "
            + "Discord application registration, and autostart preferences again. Useful after "
            + "wiping your Discord credentials or moving to a new HA instance. Your existing "
            + "settings stay until you change them in the wizard."),

        [TopicIds.GeneralPerformance] = HelpTopic.OfBody(
            "Performance metrics",
            "Live readout of how much work DiscordHass is doing on your PC. Useful for "
            + "diagnosing whether the app is contributing to frame-rate dips in games.\n\n"
            + "Expected idle baseline: CPU near 0%, working set ~30 MB, ~1 timer wake per second "
            + "(the camera-state poll), no HA frames when nothing is changing. Significant "
            + "deviation from that is a regression worth reporting."),

        [TopicIds.GeneralDiagnostics] = HelpTopic.OfBody(
            "Diagnostics bundle",
            "Generates a zip in your TEMP folder containing your RPC log, a redacted copy of "
            + "your config (every secret field replaced with \"<redacted>\"), and an environment "
            + "summary. Useful for attaching to issue reports. No secrets ever leave your PC "
            + "unless you choose to upload the file."),

        [TopicIds.OverviewIntro] = HelpTopic.OfLinks(
            "About DiscordHass",
            "DiscordHass mirrors your Discord voice-session state into Home Assistant "
            + "input_boolean helpers in real time. Use the colored pills above to confirm both "
            + "sides are connected; each tile below shows the live value of one state flag and "
            + "the HA entity_id it publishes to.\n\n"
            + "The action buttons let you re-open Settings, force a Reconnect, jump to Home "
            + "Assistant in your browser, generate a redacted diagnostics bundle for issue "
            + "reports, or open this help.",
            new HelpLink("Project on GitHub", $"https://github.com/{AppConstants.GitHubOwner}/{AppConstants.GitHubRepo}")),
    };
}
