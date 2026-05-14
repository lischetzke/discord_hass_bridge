namespace DiscordHass.App;

internal static class AppConstants
{
    public const string ProductName = "DiscordHass";
    public const string DisplayName = "Discord ↔ Home Assistant Bridge";
    public const string SingletonMutexName = "Global\\DiscordHass-singleton-2026";
    public const string AutostartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AutostartValueName = "DiscordHass";
    public const string EntityIdPrefix = "discord_";

    // Discord's /api/oauth2/token endpoint requires a redirect_uri that matches one of the
    // URIs registered on the application — even when the code came from the RPC AUTHORIZE
    // flow (where no redirect actually happens). Users must register exactly this URI in
    // their Discord app's OAuth2 → Redirects settings.
    //
    // Why http://127.0.0.1:<port>/discord/callback rather than https://localhost/ :
    //   - Discord allows the http scheme only for loopback URIs (127.0.0.1, localhost).
    //   - A specific high port + path avoids collisions with any local web app the user
    //     might be running and matches the loopback redirect convention used by other
    //     OAuth desktop clients.
    //   - The port (64064) was picked once from the IANA ephemeral range and is now
    //     stable; if it ever needs to change, every existing user must re-register the
    //     new URI in their Discord application, so do not change this lightly.
    public const string DiscordOAuthRedirectUri = "http://127.0.0.1:64064/discord/callback";
}
