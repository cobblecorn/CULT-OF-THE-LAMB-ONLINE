# Release Package Usage

This document explains how to use the latest packaged test build from this repository. The package is still an experimental diagnostics/bridge build, not a finished online co-op mod.

Current package:

```text
releases/COTLOnline-0.5.33-enemy-authority-observe.zip
```

## What Is Inside

- `BepInEx/plugins/COTLOnline.Diagnostics.dll` - the client-side BepInEx plugin.
- `server/COTLOnline.ServerLedger.dll` - the UDP server/ledger prototype.
- `docs/` - setup notes, current findings, project status, and SOL authority audit.
- `scripts/Test-SpellRelay.ps1` - local smoke test for the spell relay packet path.

The package does not include Cult of the Lamb game assemblies, BepInEx itself, saves, server worlds, generated traces, or private local configuration.

## Install On Each Game Client

1. Install BepInEx for Cult of the Lamb if it is not already installed.
2. Extract the package somewhere temporary.
3. Copy this file into the game install:

```text
BepInEx/plugins/COTLOnline.Diagnostics.dll
```

Target:

```text
<Cult of the Lamb install>/BepInEx/plugins/COTLOnline.Diagnostics.dll
```

4. Launch the game once, then close it so BepInEx creates the config file.
5. Edit:

```text
<Cult of the Lamb install>/BepInEx/config/com.codex.cotlonline.diagnostics.cfg
```

Recommended client settings for LAN testing:

```ini
[Live]
UdpEventStream = true
PlayerMotionStream = true
PlayerInputStream = true
UdpHost = <host-pc-ip-or-vpn-ip>
UdpPort = 37622
ClientId = auto

[Bridge]
RosterOverlay = true
RemotePlayerWorldMarkers = true
```

Leave `ClientId = auto` unless you intentionally need a stable manual id. The overlay will show the generated client id after launch.

## Start The Server

On the host PC, from the extracted package folder:

```powershell
dotnet ".\server\COTLOnline.ServerLedger.dll" --listen-udp --server-save-slot 4
```

If you know the host client's generated id, pin it so the host remains `host-lamb` even if another client connects first:

```powershell
dotnet ".\server\COTLOnline.ServerLedger.dll" --listen-udp --server-save-slot 4 --host-client-id <host-client-id>
```

If you run from the source repo instead of the package:

```powershell
dotnet run --project ".\src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj" -c Release -- --listen-udp --server-save-slot 4 --host-client-id <host-client-id>
```

## Current Test Setup

- Use the same save baseline on both systems.
- Current convention is raw save slot `4`, shown by COTL as UI slot 5.
- Start the server first.
- Launch the intended host game client.
- Launch the second game client.
- Confirm the overlay/server roster shows two clients:
  - one `role=host-lamb`
  - one `role=remote-p2`

## What To Test In 0.5.33

The current priority is enemy authority observation. Enter a dungeon on both clients and watch:

- overlay `enemy authority: host=... room=... enemies=... match=...`
- server `[combat]`, `[spawn]`, `[encounter]`, and `[spell]` lines
- whether both clients report the same room and enemy hash before/after events
- whether evil follower events, extra enemies, room clear, or deaths happen at different times

Useful signs:

- `match=True` means the local combat roster matches the host roster at that moment.
- `match=False` means this is the spot where the next host-authoritative enemy command work should focus.
- Spell relay misses should appear as `phase11.*` lines in the server console.

## Known Limitations

- Enemy authority is observe-only in `0.5.33`; remote enemies are not suppressed or driven by host commands yet.
- Follower/cult AI is still local and can diverge quickly.
- Death/revive/game-over is not server-authoritative yet.
- Dungeon seed/reward/loadout authority is partially working, but not final.
- Spell visuals are experimental and can still fail by curse type, room mismatch, or bridge-body availability.
- This build is for LAN/VPN testing with legitimate local game installs and third-party mod code; it is not a standalone server or a replacement for the game.

## Troubleshooting

- If the second client does not appear, check Windows Firewall and confirm `UdpHost` points to the host PC address reachable from the second PC.
- If host/remote roles swap, restart the server with `--host-client-id <host-client-id>`.
- If movement hitches badly, keep `TraceHighFrequencyPackets=false`.
- If config changes do not seem to apply, close the game, edit the BepInEx config, then relaunch.
- If the overlay is noisy, press `F8` to toggle it.
