# COTL Online Research Status

This repository is an experimental online-play research workspace for Cult of the Lamb. It is not a finished multiplayer mod.

## Current Shape

- `COTLOnline.Diagnostics` is the BepInEx plugin loaded by each game client.
- `COTLOnline.ServerLedger` is a UDP/server-ledger prototype that assigns host/remote roles and relays compact server packets.
- Save slot 4 is currently treated as the shared online test slot.
- The most recent build line is `0.5.33`, which starts observe-only host enemy authority through `server.enemy_authority`.

## What Works Best So Far

- Two local/LAN clients can register with the server roster.
- Player motion, input, remote P2 reservation, and remote host visual mirroring are functional enough for testing.
- Dungeon seed/reward/loadout authority is partially working and can produce matching podium items.
- Camera split can let players move farther apart without forcing the vanilla co-op midpoint camera.
- Spell cast relay has a guarded visual replay path, with server-visible `phase11.*` diagnostics.

## Known Gaps

- Enemy authority is not active yet. Enemies, evil follower events, room clear, HP, and death timing can still diverge.
- Follower/cult AI is still locally simulated and diverges quickly after matching save loads.
- Death/revive/game-over needs an explicit online rule instead of relying on vanilla local transitions.
- Spell visuals are still experimental and may fail per curse type or body/room mismatch.
- The server-owned save/world model is not implemented yet; slot 4 is only a shared baseline.

## Next Milestones

1. Promote `server.enemy_authority` from observe-only to a host-owned enemy entity registry.
2. Add authority commands for enemy spawn, HP/death, and room clear.
3. Gate special encounters so the host/server decides when an evil follower or event starts.
4. Add an online death rule, likely downed/spectator first, before any revive mechanics.
5. Convert cult/follower sync from save-hash comparison to host-authored follower commands.

## Build Notes

The diagnostics project references local BepInEx, Unity, and game assemblies. Set `COTL_GAME_DIR` to your Cult of the Lamb install folder if it is not at the default local path.

Example:

```powershell
$env:COTL_GAME_DIR = "D:\SteamLibrary\steamapps\common\Cult of the Lamb"
dotnet build .\src\COTLOnline.Diagnostics\COTLOnline.Diagnostics.csproj -c Release
dotnet build .\src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj -c Release
```
