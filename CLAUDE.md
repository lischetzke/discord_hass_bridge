# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Windows tray app (.NET 10, WinForms, self-contained single-file exe) that watches the local Discord desktop client over its RPC named pipe and mirrors voice-session state into `input_boolean` helpers in Home Assistant. End-user docs and the architecture diagram live in [README.md](README.md).

## Commands

```powershell
# Debug build + run the test suite (90 tests)
dotnet build src/DiscordHass/DiscordHass.csproj -c Debug
dotnet test  tests/DiscordHass.Tests/DiscordHass.Tests.csproj -nologo --verbosity minimal

# Single test class
dotnet test tests/DiscordHass.Tests/DiscordHass.Tests.csproj --filter "FullyQualifiedName~FlagResolverTests"

# Cut a release: runs tests, publishes single-file win-x64 exe, drops .exe + .sha256 into release/
pwsh tools/build-release.ps1 -Version 0.1.5
pwsh tools/build-release.ps1 -Version 0.1.5 -SkipTests       # iteration shortcut
pwsh tools/build-release.ps1 -Version 0.1.5 -KeepIntermediate

# Regenerate the embedded tray.ico from src/DiscordHass/Ui/Icons/tray.ico
pwsh tools/generate-icon.ps1

# Release workflow
git tag -a vX.Y.Z -m "..."
git push origin master vX.Y.Z
# Then upload release/DiscordHass-vX.Y.Z-win-x64.exe + .sha256 to a GitHub release
```

**Common gotcha when building**: if `DiscordHass.exe` is running (e.g. you launched the tray app earlier), the publish step will fail with "file in use". Kill the process first:

```powershell
Get-Process -Name DiscordHass -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Architecture — the non-obvious bits

### Two signal sources, one merged `VoiceState`

[BridgeService](src/DiscordHass/App/BridgeService.cs) is the orchestrator. It keeps **two independent source-of-truth fields**:

- `_voiceFromDiscord` — set from [DiscordRpcSession](src/DiscordHass/Discord/DiscordRpcSession.cs)'s `VoiceStateChanged` event. Carries `IsInCall`, `MicMuted`, `SpeakerMuted`, `ServerMuted`, `ServerDeafened`.
- `_cameraFromOs` — set from [WindowsCameraWatcher](src/DiscordHass/App/WindowsCameraWatcher.cs)'s `CameraStateChanged` event. Carries `CameraOn`.

The public `CurrentVoiceState` is a **computed** property: `_voiceFromDiscord with { CameraOn = _cameraFromOs }`. Both `OnDiscordVoiceStateChanged` and `OnCameraStateChanged` trigger `PublishCurrentAsync`, which diffs against `_lastPublishedState` and only calls HA for changed flags.

**Critical invariant**: [DiscordRpcSession](src/DiscordHass/Discord/DiscordRpcSession.cs) must not touch `CameraOn`. Discord's local RPC does not expose `self_video` to user-registered apps (the `voice_state` payload is a stripped 5-field subset; `rpc.video.read` only authorizes the write side, `TOGGLE_VIDEO`). The DTO's `SelfVideo` field is always false because the JSON doesn't contain it; writing it into `_state` would clobber the OS-detected value. There are explicit comments in `HandleVoiceStateUpdate` / `ExtractSelfVoiceStateFromChannel` — don't re-introduce a `.WithSelfVideo(dto.SelfVideo)` call there.

[WindowsCameraWatcher](src/DiscordHass/App/WindowsCameraWatcher.cs) polls `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged\` once per second, enumerating subkeys whose decoded name basename is `Discord.exe` / `DiscordPTB.exe` / `DiscordCanary.exe`. While the camera is active, `LastUsedTimeStop == 0`. Pure parsing logic is in [CapabilityAccessParser](src/DiscordHass/App/CapabilityAccessParser.cs) for testability.

### Flag resolution is dynamic, not static

[StateFlagDefinitions.All](src/DiscordHass/App/StateFlagDefinitions.cs) is a static catalog of `(FlagId, DefaultNameSuffix, DefaultIcon, ValueSelector)`. At runtime, [FlagResolver.Resolve](src/DiscordHass/App/FlagResolver.cs) applies the user's `HelperPrefix` + per-flag `NameSuffix` / `Icon` overrides from config and produces an `EffectiveStateFlag` carrying the live `EntityIdSlug` and `FriendlyName`. `StateMapper` and the publish path work with `EffectiveStateFlag`, never the static definitions directly. When the user changes the prefix or a flag's name, the next reconnect picks up the new effective names and [HaHelperManager.EnsureAndSyncAsync](src/DiscordHass/HomeAssistant/HaHelperManager.cs) handles the in-place rename via `config/entity_registry/update` + `input_boolean/update`. The previous slug per flag is persisted in `FlagOverride.LastEntityIdSlug` so we know what to rename.

### Discord OAuth scope drift is auto-handled

[DiscordScopes.Required](src/DiscordHass/Discord/DiscordScopes.cs) is the single source of truth for the OAuth scope set; `CurrentKey()` produces a stable comma-joined ordered string. `AppConfig.DiscordAuthorizedScopeKey` records the scope set the cached refresh token was issued for. Before every token refresh, [BridgeService.EnsureDiscordAccessTokenAsync](src/DiscordHass/App/BridgeService.cs) compares them; on mismatch it clears the cached tokens and throws — Settings → Discord then shows a red "Re-authorize required" banner. Discord's refresh flow only re-issues the *original* scopes, so this is the only way to propagate scope additions between versions. **If you change `DiscordScopes.Required`, every existing install will be forced through a fresh AUTHORIZE on next launch. That's intentional.**

### Auto-update swap dance

The exe replaces itself in place. [UpdateInstaller.SwapAndRelaunchAsync](src/DiscordHass/Update/UpdateInstaller.cs):

1. `File.Move(currentExe, currentExe + ".old")` — Windows lets you rename a running PE; you cannot overwrite it.
2. `File.Move(stagedNewExe, currentExe)`.
3. `Process.Start(currentExe, "--post-update", "--wait-for-pid", "<ourPid>")`.
4. Caller (`UpdateProgressForm`) `Application.Exit()`s.

The relaunched instance's [Program.HandlePostUpdateArgs](src/DiscordHass/Program.cs) parses those flags and `Process.WaitForExit`s up to 15 s for the old PID before claiming the singleton mutex. [UpdateInstaller.CleanupOldSibling](src/DiscordHass/Update/UpdateInstaller.cs) deletes any leftover `.old` file on each startup (best-effort). Without the `--wait-for-pid` handoff the new instance would race the old one's mutex release and exit immediately.

### GitHub coordinates and the redirect URI are hardcoded

[AppConstants](src/DiscordHass/App/AppConstants.cs):

- `GitHubOwner` / `GitHubRepo` — drives the auto-updater's `/releases/latest` query. Forking the project requires updating these.
- `DiscordOAuthRedirectUri = "http://127.0.0.1:64064/discord/callback"` — used in the OAuth2 token exchange. Discord requires the exact URI to be registered on the application; the URL itself is never loaded (the AUTHORIZE code arrives over the RPC pipe, not via redirect). Changing the port number breaks every existing user — see the warning comment on the constant.

### Config storage + DPAPI

[ConfigStore](src/DiscordHass/Config/ConfigStore.cs) reads/writes `%APPDATA%\DiscordHass\config.json` atomically (write `.tmp`, then `File.Replace`). Any field on [AppConfig](src/DiscordHass/Config/AppConfig.cs) ending in `Protected` is a base64-encoded DPAPI blob; pass through [SecretProtector](src/DiscordHass/Config/SecretProtector.cs) on read/write. `DataProtectionScope.CurrentUser` — copying `config.json` to another machine or user account won't decrypt.

### DPI scaling

WinForms forms must declare their DPI baseline before child controls are added or autoscale is a no-op. The pattern (used in every form):

```csharp
SuspendLayout();
AutoScaleDimensions = new SizeF(96F, 96F);
AutoScaleMode = AutoScaleMode.Dpi;
// ...sizing + InitializeUi()...
ResumeLayout(performLayout: true);
```

Use `ClientSize` instead of `Width`/`Height` for predictable layouts at non-100% scaling.

## Testing patterns

xUnit. Internals exposed via `<InternalsVisibleTo Include="DiscordHass.Tests" />` in the main csproj. **Don't try to mock the Registry, ClientWebSocket, NamedPipeClientStream, or HttpClient directly** — the codebase pulls the pure logic out:

- [FlagResolver](src/DiscordHass/App/FlagResolver.cs) and [StateMapper](src/DiscordHass/App/StateMapper.cs) — pure functions, the test seam.
- [CapabilityAccessParser](src/DiscordHass/App/CapabilityAccessParser.cs) — tested with synthetic `CapabilityAccessEntry` records, not real registry reads.
- [DiscordIpcProtocol](src/DiscordHass/Discord/DiscordIpcProtocol.cs) — frame encode/decode tested against `MemoryStream`.
- [UpdateChecker.SelectExeAsset](src/DiscordHass/Update/UpdateChecker.cs) and `BuildUpdate` — `internal static` so they can be tested with synthetic `GitHubRelease` objects, no HTTP.
- [ReleaseVersion](src/DiscordHass/Update/ReleaseVersion.cs) and [ShaSidecar](src/DiscordHass/Update/ShaSidecar.cs) — pure parsing.

When adding logic with side effects (registry, network, file I/O), keep the parsing/decision in a static class and let the wrapper class handle the I/O.

## Release-note format

`release/vX.Y.Z.md` files follow a strict template — copy the style of [release/v0.1.0.md](release/v0.1.0.md) and [release/v0.1.4.md](release/v0.1.4.md), not the prose I write in chat:

- One-sentence intro.
- Section order, emit only if non-empty: **Upgrade notes** → **Bug fixes** → **New features**.
- Bullets are terse: `* **Short bold title** - one-line description.`. No motivation, no internals.
- A previously-broken thing now working is a **bug fix**, not a new feature.
- No download section; that lives on the GitHub release page.
