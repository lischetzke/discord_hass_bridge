# DiscordHass

**Mirror your Discord voice-call state into Home Assistant, in real time.**

DiscordHass is a small Windows tray app that watches your local Discord desktop
client over its RPC named pipe and reflects what it sees into a set of
`input_boolean` helpers in Home Assistant. Build automations on top:

* turn on a *do-not-disturb* light when you join a voice channel,
* drop the volume of HA-controlled speakers while your mic is hot,
* flip a sign on the door when your camera is on,
* …and anything else you can wire up to a boolean entity.

The app is fully self-contained — a single `DiscordHass.exe` carrying its own
.NET 10 runtime. Nothing else to install.

---

## Table of contents

* [What it tracks](#what-it-tracks)
* [Requirements](#requirements)
* [Install](#install)
* [Configure](#configure)
  * [1. Home Assistant token](#1-home-assistant-token)
  * [2. Discord application](#2-discord-application)
  * [3. Authorize and pick your helpers](#3-authorize-and-pick-your-helpers)
* [Customizing helper names and icons](#customizing-helper-names-and-icons)
* [Automation recipes](#automation-recipes)
* [How it works](#how-it-works)
* [Updates](#updates)
* [Where things live on disk](#where-things-live-on-disk)
* [Troubleshooting](#troubleshooting)
* [Build from source](#build-from-source)
* [Security notes](#security-notes)
* [Limitations](#limitations)
* [License](#license)

---

## What it tracks

| Helper (default name)          | Default entity ID                       | Meaning                                |
|--------------------------------|-----------------------------------------|----------------------------------------|
| **Discord In Call**            | `input_boolean.discord_in_call`         | You're in any voice channel            |
| **Discord Mic Muted**          | `input_boolean.discord_mic_muted`       | You muted your own mic                 |
| **Discord Speaker Muted**      | `input_boolean.discord_speaker_muted`   | You deafened yourself                  |
| **Discord Camera On**          | `input_boolean.discord_camera_on`       | Your webcam is streaming               |
| **Discord Server Muted** \*    | `input_boolean.discord_server_muted`    | A server admin muted you               |
| **Discord Server Deafened** \* | `input_boolean.discord_server_deafened` | A server admin deafened you            |
| **Discord Busy**               | `input_boolean.discord_busy`            | Derived: currently mirrors *In Call*   |

\* Off by default; enable from *Settings → States* if you want them.

All names, icons, and the shared `Discord ` prefix are editable from the
Settings UI — see [Customizing helper names and icons](#customizing-helper-names-and-icons).

---

## Requirements

* Windows 10 21H2 or newer, x64.
* Discord desktop client running on the same Windows user session.
  The web client and mobile apps don't expose the RPC pipe.
* A reachable Home Assistant instance and an admin user on it.
* No separate .NET install — the runtime is bundled into the exe.

---

## Install

Grab the latest `DiscordHass.exe` from the [releases](#) page (or build it
yourself, see [Build from source](#build-from-source)). It's a single ~50 MB
file. Drop it anywhere — `%LOCALAPPDATA%\Programs\DiscordHass\DiscordHass.exe`
is a tidy spot — and run it.

The first launch opens the **Settings** window. Walk through the three
configuration tabs below; once you click *Save & Close*, the app drops into
the system tray and the settings window goes away.

---

## Configure

### 1. Home Assistant token

1. In Home Assistant, click your profile in the bottom-left, go to **Security**,
   scroll to **Long-Lived Access Tokens**, and click **Create Token**.
2. Copy the token (you only see it once).
3. The user that owns this token needs to be able to create helpers — i.e.
   they need to be an admin. If they aren't, helper creation will fail.

In DiscordHass:

1. Open the **Home Assistant** tab.
2. Paste your base URL — `http://homeassistant.local:8123`, `https://your-domain.duckdns.org`, or whatever you actually use.
3. Paste the token.
4. Click **Test connection**. You should see something like *"Connected.
   Found N existing input_boolean helper(s)"*.

### 2. Discord application

The RPC API requires a Discord application that *you* own. It's a 30-second
setup:

1. Go to <https://discord.com/developers/applications> and click **New
   Application**. Name it anything (e.g. "DiscordHass on my PC").
2. From the application's **General Information** tab, copy the **Application ID**.
   That's your **Client ID**.
3. In the **OAuth2** tab, click **Reset Secret** and copy the value. That's
   your **Client Secret**.
4. Still on the **OAuth2** tab, under **Redirects**, add **exactly**:

   ```
   http://127.0.0.1:64064/discord/callback
   ```

   Copy-paste it — the match is case-sensitive and one stray character will
   make the token exchange fail with `invalid_request`. This URL is never
   actually loaded; the RPC AUTHORIZE flow returns the code in-process. But
   Discord's `/api/oauth2/token` endpoint requires the `redirect_uri`
   parameter on every code exchange and rejects the request if it doesn't
   match a registered URI, so it has to be on the application.

In DiscordHass:

1. Open the **Discord** tab.
2. Paste the Client ID and Client Secret.
3. Click **Authorize…**. Discord will pop up an in-client approval modal —
   click *Authorize* there.
4. The status line should say *"Authorized."*. The refresh token is now
   cached locally; you won't need to do this again.

### 3. Authorize and pick your helpers

1. Open the **States** tab.
2. Tick the flags you want to expose. The four call-related ones are on by
   default.
3. *(Optional)* Customize names and icons — see below.
4. Open the **General** tab and tick **Start with Windows** if you want
   DiscordHass to run on every login.
5. Click **Save & Close**.

The bridge will connect to both Discord and Home Assistant in the background,
create the helpers you enabled (if they don't already exist), and start
publishing state changes. Right-click the tray icon and choose **Status…** to
watch it work.

---

## Customizing helper names and icons

The States tab lets you change three things per helper:

* **Helper prefix** (top of the tab, applies to all flags) — defaults to
  `Discord`. Friendly names are built as `"<prefix> <suffix>"`. Set it to
  empty if you want no prefix.
* **Per-flag name suffix** — the part after the prefix. *"In Call"*, *"Mic
  Muted"*, etc.
* **Per-flag icon** — a [Material Design Icon](https://pictogrammers.com/library/mdi/)
  identifier like `mdi:phone-in-talk`.

The **Entity ID** column updates live as you type so you can see exactly
what's about to land in Home Assistant.

### Rename behavior

If you change the prefix or a name suffix and the corresponding helper
already exists in HA, DiscordHass will **rename it in place** the next time
it connects:

1. The entity registry is updated so `input_boolean.discord_in_call` becomes,
   say, `input_boolean.voice_calling`.
2. The friendly name on the helper itself is updated.
3. The new entity ID is remembered, so subsequent renames work the same way.

If the rename fails for any reason (e.g. the entity was deleted out from
under us, or your token user lacks permission), the bridge falls back to
creating a fresh helper with the new name. The old one is left intact and
you can clean it up by hand.

> ⚠️ **Heads up**: renaming changes the `entity_id`. Any automation, script,
> Lovelace card, or template that hard-codes the old `input_boolean.foo`
> entity ID must be updated to point at the new one.

---

## Automation recipes

A few starter ideas. Drop into your `automations.yaml` and tweak.

**Do-not-disturb light while in any voice call**

```yaml
- alias: "Office lamp red while in Discord call"
  trigger:
    - platform: state
      entity_id: input_boolean.discord_in_call
  action:
    - choose:
        - conditions:
            - condition: state
              entity_id: input_boolean.discord_in_call
              state: "on"
          sequence:
            - service: light.turn_on
              target: { entity_id: light.office_lamp }
              data: { rgb_color: [255, 0, 0], brightness_pct: 60 }
        - conditions:
            - condition: state
              entity_id: input_boolean.discord_in_call
              state: "off"
          sequence:
            - service: light.turn_off
              target: { entity_id: light.office_lamp }
```

**Pause the music when your camera turns on**

```yaml
- alias: "Pause Spotify when on camera"
  trigger:
    - platform: state
      entity_id: input_boolean.discord_camera_on
      to: "on"
  action:
    - service: media_player.media_pause
      target: { entity_id: media_player.spotify }
```

---

## How it works

```
Discord desktop ─┐                                ┌─► input_boolean.discord_in_call
   (named pipe)  │                                │   …mic_muted, …camera_on, etc.
                 ▼                                │
        DiscordIpcClient ──► BridgeService ──► HaHelperManager ──► Home Assistant
                                  ▲                                (WebSocket API)
            (tray UI / status) ──┘
```

* **Discord side.** DiscordHass connects to the local `\\.\pipe\discord-ipc-N`
  named pipe, does the standard handshake → AUTHORIZE → token exchange →
  AUTHENTICATE flow, then subscribes to `VOICE_CHANNEL_SELECT`,
  `VOICE_SETTINGS_UPDATE`, and per-channel `VOICE_STATE_UPDATE`. From those
  events it maintains a normalized `VoiceState` for the current user.
* **HA side.** The bridge opens a single WebSocket to
  `/api/websocket`, authenticates with your long-lived token, and uses the
  storage-collection commands `input_boolean/list`, `input_boolean/create`,
  `input_boolean/update`, plus `config/entity_registry/update` for rename,
  and `call_service` to flip the state. Helpers created this way are
  *persistent* — they survive HA restarts, unlike entities created via
  `POST /api/states/`.
* **State mapping.** Every Discord event recomputes the `VoiceState`. The
  `StateMapper` diffs it against the last published snapshot and emits the
  minimum set of HA service calls.
* **Reconnect.** Each side has its own reconnect loop with exponential
  backoff capped at 30 seconds. If Discord disconnects (you quit it),
  DiscordHass flips every enabled helper to `off` so HA doesn't think you're
  still in a meeting.
* **Camera state is OS-detected, not RPC-detected.** Discord's local RPC
  intentionally strips the `voice_state` payload to five fields
  (`mute, deaf, self_mute, self_deaf, suppress`) for user-registered
  applications — there is no `self_video` field and no `VIDEO_STATE_UPDATE`
  event, even with `rpc.video.read` granted. (`rpc.video.read` only
  authorizes the write side, `TOGGLE_VIDEO`. Even Discord's whitelisted
  partners — StreamKit, Streamdeck, Overlayed, Reactive Images — don't read
  camera state via RPC.) So instead, DiscordHass watches HKCU's Capability
  Access Manager registry:
  `Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam\NonPackaged\<encoded-exe-path>`.
  This is the same signal Windows itself uses to drive the "camera in use"
  tray indicator. While the camera is active, `LastUsedTimeStop == 0`;
  once Discord releases the camera, Windows writes the release timestamp
  there. DiscordHass polls the relevant subkeys once per second, enumerates
  any `Discord.exe` / `DiscordPTB.exe` / `DiscordCanary.exe` it finds, and
  flips `input_boolean.discord_camera_on` accordingly. No admin required —
  HKCU is fully readable from a normal user process.

---

## Updates

DiscordHass checks GitHub for new releases on its own.

* **What it does.** Once a day (and 30 s after startup), the app hits
  `https://api.github.com/repos/lischetzke/discord_hass_bridge/releases/latest`,
  compares the tag against its installed version, and if there's something
  newer it lights up a bold *Install update: vX.Y.Z…* item in the tray
  menu and shows a one-time balloon tip.
* **What happens when you click it.** A small download dialog appears.
  The new exe is downloaded to `%LOCALAPPDATA%\DiscordHass\update\`, its
  SHA-256 is verified against the release's `.sha256` sidecar, the running
  exe is moved aside to `<exe>.old`, the new exe is put in place, and the
  new version is launched. The dialog shows "Restarting…" for a beat and
  the app comes back already running the new build. The leftover `.old`
  file is cleaned up on next launch.
* **Settings → General** has *Check for updates automatically* (default
  on), the installed version, the last-check timestamp, a manual *Check
  now* button, and a link to the GitHub releases page.
* **Privacy.** The only request DiscordHass makes to GitHub is the single
  `releases/latest` API call. The asset download is a direct HTTPS GET to
  the URL GitHub returns. Nothing about your HA setup or Discord activity
  leaves the machine.

If you'd rather update by hand, turn off auto-check and download the new
exe from the [releases page](https://github.com/lischetzke/discord_hass_bridge/releases)
when you feel like it. Replacing the file while DiscordHass is running is
fine — close it from the tray first.

---

## Where things live on disk

* **Configuration & cached tokens.**
  `%APPDATA%\DiscordHass\config.json`
  Tokens (HA access token, Discord client secret + refresh token) are
  DPAPI-encrypted before being written. They can only be decrypted by the
  same Windows user account on the same machine.
* **Update staging.**
  `%LOCALAPPDATA%\DiscordHass\update\`
  Downloaded update binaries land here briefly before they're swapped
  into place. Safe to delete at any time.
* **Autostart.**
  Registry value `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DiscordHass`
  pointing at the current `DiscordHass.exe`. Toggle from the tray menu or
  the **General** settings tab.
* **Single-instance lock.**
  A named mutex prevents two copies of DiscordHass from running at once.

---

## Troubleshooting

**Tray menu shows "Discord: fault — Could not connect to any discord-ipc-N pipe"**
The Discord desktop client isn't running, or you're signed into the wrong
Windows user. Start Discord, then right-click the tray icon → **Reconnect**.

**Tray menu shows "HA: fault — auth_invalid"**
The long-lived access token was rejected. Tokens can be revoked from the HA
profile page; you may have deleted yours. Open **Settings → Home Assistant**
and paste a fresh one.

**Authorize button does nothing in Discord**
The approval modal sometimes hides behind other windows. Click the Discord
icon in the taskbar to bring it forward, or wait — DiscordHass times out
after two minutes.

**Helpers don't appear in HA**
Make sure the long-lived token belongs to an admin user. Non-admin tokens
can list `input_boolean` helpers but can't create new ones.

**Helpers don't rename when I change names in settings**
The rename uses `config/entity_registry/update`, which requires admin
permissions. If the rename keeps failing, the bridge falls back to creating
new helpers with the new names — the old ones stay around. Delete them by
hand in HA → Settings → Devices & Services → Helpers.

**I want to start over**
Quit DiscordHass from the tray, delete `%APPDATA%\DiscordHass\config.json`,
and start it again.

---

## Build from source

Requires the .NET 10 SDK (10.0.100 or newer).

```powershell
git clone <this-repo>
cd dc_hass

dotnet build                                                      # debug
dotnet test                                                        # 29 unit tests
dotnet publish src/DiscordHass/DiscordHass.csproj -c Release       # single-file release
```

The release publish produces a single ~50 MB exe at
`src\DiscordHass\bin\Release\net10.0-windows\win-x64\publish\DiscordHass.exe`
that runs on a clean Windows install with no .NET runtime present.

### Project layout

```
src/DiscordHass/
  App/              # BridgeService orchestrator, StateMapper, FlagResolver, AutostartManager
  Config/           # AppConfig + DPAPI-protected config store
  Discord/          # Named-pipe IPC client, RPC session, OAuth token exchange
  HomeAssistant/    # WebSocket client + helper manager (create / update / rename)
  Ui/               # WinForms tray icon, settings dialog, status dialog
tests/DiscordHass.Tests/
  …                 # xUnit tests for state mapping, flag resolution, slug derivation, IPC framing
```

---

## Security notes

* **Secrets**: HA access tokens, the Discord client secret, and the Discord
  refresh / access tokens are encrypted at rest via DPAPI with
  `DataProtectionScope.CurrentUser`. They are bound to the specific Windows
  user account that wrote them — copying `config.json` to another machine
  won't decrypt.
* **Autostart**: per-user `HKCU` only. No UAC prompt, no Task Scheduler, no
  service install.
* **Network**: the app only talks to the Home Assistant URL you configure
  and to `https://discord.com/api/oauth2/token`. No telemetry.
* **Permissions**: helper creation and renaming require an admin-scoped HA
  user. If you'd rather not give it admin, pre-create the helpers in the HA
  UI with the exact entity IDs DiscordHass would generate, then use a
  non-admin token — the bridge will reuse the existing helpers.

---

## Limitations

* Windows only. Discord's IPC pipe paths, the autostart mechanism, and the
  Capability Access Manager registry are all Windows-specific.
* Camera detection only works for the standard (non-UWP) Discord installer,
  i.e. `%LocalAppData%\Discord\`, `%LocalAppData%\DiscordPTB\`,
  `%LocalAppData%\DiscordCanary\`. The (uncommon) Microsoft Store build of
  Discord registers under a different registry root and is not currently
  enumerated.
* Screen-sharing isn't reflected — Discord's RPC voice state doesn't expose
  it cleanly.
* One Discord account at a time per Windows user session (whichever account
  is signed in to the desktop client).
* DiscordHass needs *your own* Discord application registration. There's no
  shared client_id; we don't want to ship Anthropic-or-anyone-else's secret
  with the binary.

---

## License

MIT.
