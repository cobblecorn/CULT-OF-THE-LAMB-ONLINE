# COTL Online Research

Fresh research workspace for Cult of the Lamb online-world feasibility.

This is intentionally separate from the old COTLMP fork. The fork may be used for reference, but this workspace starts with diagnostics and server/world research rather than ghost-player synchronization.

## Projects

- `src/COTLOnline.Diagnostics` - BepInEx diagnostics plugin for tracing game lifecycle, save, combat, spawn, and packet-shaped `sync.*` events.
- `src/COTLOnline.ServerLedger` - command-line prototype that consumes diagnostics JSONL traces and reconstructs server-owned run/world/player/equipment state.
- `docs/HEADLESS_FEASIBILITY.md` - running notes on what can plausibly move to a command-prompt server versus what is Unity-bound.

## First Milestone

1. Build and load `COTLOnline.Diagnostics.dll`.
2. Capture a trace from normal play.
3. Use the trace to identify save/world, room, combat, and lifecycle choke points.
4. Only then design the server-owned world protocol.

## Build

The diagnostics plugin needs local BepInEx, Unity, and Cult of the Lamb managed assemblies. If your game is not installed at the default path used by this workspace, set `COTL_GAME_DIR` first:

```powershell
$env:COTL_GAME_DIR = "D:\SteamLibrary\steamapps\common\Cult of the Lamb"
```

Diagnostics plugin:

```powershell
dotnet build ".\src\COTLOnline.Diagnostics\COTLOnline.Diagnostics.csproj" -c Release
```

Built DLL:

```text
.\src\COTLOnline.Diagnostics\bin\Release\net481\COTLOnline.Diagnostics.dll
```

Server ledger prototype:

```powershell
dotnet run --project ".\src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj"
```

Useful options:

- `--trace "path\to\cotlonline-trace.jsonl"` - parse a specific trace instead of the newest trace.
- `--watch` - keep tailing the selected trace after the initial replay.
- `--listen-udp` - receive live events directly from `COTLOnline.Diagnostics` over localhost UDP instead of tailing a trace file.
- `--udp-port 37622` - override the local UDP receive port.
- `--worlds-dir "path\to\server_worlds"` - override the server-owned world state directory used in live UDP mode.
- `--server-save-slot 4` - override the reserved online/server-world save slot. Default is raw file slot `4`, which COTL shows as UI slot 5.
- `--json-out "path\to\ledger.json"` - write the reconstructed ledger as JSON.

The ledger now prints both raw observed game events and the first server-authority projection:

- `Generated Rewards` - reward options produced by the game.
- `Reward Claims` - which player actually interacted with each reward.
- `Server Decisions` - command-shaped decisions such as `register_reward`, `claim_reward`, and `assign_equipment`.
- `Authoritative Players` - the compact server-owned equipment view derived from those decisions.

At this stage those decisions are diagnostic only. They are intentionally not sent back into the game yet.

In live UDP mode, the server also sends a throttled `server.roster` packet back to each client's observed UDP endpoint. The diagnostics plugin records those replies locally as `bridge.roster`, proving the server-to-client half of the bridge without mutating game state.
With `0.4.5+`, the diagnostics plugin also draws a read-only in-game bridge overlay when roster packets arrive. Press `F8` to toggle it. The roster includes provisional server roles: first active client is `host-lamb`, second active client is `remote-p2`, and extra clients are `pending`.
With `0.4.6+`, the server also creates a server-owned world record under `server_worlds/<worldId>/world.json`. The first client's save/world hash becomes the baseline. Later clients are marked `worldMatch=True` or `worldMatch=False` against that baseline. This is a dry-run for future save rerouting; it does not redirect COTL saves yet.
With `0.4.7+`, the server reports the reserved online/server-world save slot and per-client `saveSlotOk=True/False`. Current default is raw file slot `4`, which COTL shows as UI slot 5.
With `0.5.0+`, the host can test a vanilla co-op body reservation. When the server roster marks this client as `host-lamb` and another active client as `remote-p2`, press `F9` on the host to request `CoopManager.SpawnCoopPlayer(1, true, -1f)`. Press `F10` on the host to clear that reserved P2 through the normal co-op removal path. The overlay and server roster report `p2Wanted`, `p2Active`, `p2Hold`, and `p2NoController`.
With `0.5.2+`, the host no longer uses direct transform motion as the main reserved-P2 driver. The host derives a virtual movement axis from the remote-p2 relay and feeds it into `RewiredGameplayInputSource` for the reserved vanilla P2. This lets COTL's normal `PlayerController` path handle movement state and animation. Large distance errors still snap as a correction fallback.
With `0.5.17+`, clients also send `sync.player_input` packets containing local axes plus attack, dodge, curse, and heavy-attack button edges. The server relays those as `server.remote_inputs`, and the host P2 driver prefers them over coordinate-derived axes. The driver also pauses if the remote-p2 roster row reports `worldMatch=False`, because mismatched saves can put building/door triggers in different places.
With `0.5.4+`, the first active reserved P2 frame force-snaps to the latest remote position before accepting input, so the goat should not start several units away from the laptop. Save-hash drift is now treated as a warning by default; raw slot mismatch still pauses. Reserved remote P2 is also blocked from triggering host-side building/door transitions during movement tests.
With `0.5.5+`, persistent world snapshots are throttled and save-file hashes are cached after the startup sample to avoid live disk-hash hitches. The host P2 driver also adds `input-correct` soft correction so several-unit drift is nudged down without constant hard teleports.
With `0.5.6+`, high-frequency live packets stay on the UDP/server path but no longer write every packet to the JSONL trace by default. This keeps `sync.player_input`, `sync.player_motion`, `bridge.remote_inputs`, and `bridge.remote_players` from doing thousands of synchronous file appends during movement tests.
With `0.5.7+`, the server can pin a preferred host client id so the same machine stays `host-lamb` even if another machine connects first. The plugin also draws lightweight world-space markers for remote players, so the remote-p2 client can see host P1's relayed position before a real remote actor mirror exists.
With `0.5.8+`, live UDP packets are timestamped at server receive time before roster/relay age calculations, so overlay `ageMs` no longer depends on PC clock skew. Persistent world snapshots default to a lightweight identity hash instead of a reflective DataManager scan. Remote-p2 clients also get an experimental Phase7 host visual mirror: when host-lamb motion arrives, the remote client auto-spawns a local vanilla P2 body and drives it from the host's relayed input/position so P2 can see an approximate host actor instead of only a marker.
With `0.5.9+`, the Phase7 host visual mirror holds COTL's no-controller P2 removal before spawning the local mirror body. This fixes the remote-p2 client repeatedly showing the goat spawn animation, then removing it and spawning again.
With `0.5.10+`, the Phase7 mirror also blocks vanilla co-op removal calls while the remote-p2 visual body is intentionally held without a controller. This targets the later freeze/respawn case where `RemovePlayerFromMenu` repeatedly fired after the mirror was already visible.
With `0.5.11+`, clients emit a compact `sync.cult_snapshot` containing active follower/task/faith state. The server treats the pinned host's snapshot as the active cult baseline and reports per-client `cultMatch=True/False` in the roster/overlay. This is the first merged-world authority layer; it detects live follower/world divergence separately from raw save-file hash matching.
With `0.5.12+`, the host-side remote P2 driver gains a remote-away gate. If the remote-p2 client reports a different scene/location, the host hides the reserved P2 body and stops feeding input until the remote returns. This targets teleporters, altar/building/private transitions, and other cases where keeping a visible P2 in the host scene is misleading. `0.5.13+` normalizes scene/location tokens before comparison, so `Base Biome 1` and `Base_Biome_1` do not false-hide P2.
With `0.5.20+`, the relay includes facing/look/aim/faith-ammo frame data so receiving clients can orient remote attacks and spells more accurately. The camera split also gets a late-frame pass so vanilla co-op camera recentering is corrected after player movement.
With `0.5.21+`, remote attack/curse/heavy button edges are sequence-aware so a repeated relay packet cannot re-fire the same click every diagnostics tick. This targets blunderbuss/gun spam and stuck cast loops. The remote host visual mirror now hides during scene/location mismatch and transition states, and the server/client bridge has an experimental `server.reward_claims` path that marks matching remote-claimed weapon/curse podiums inactive on the other client.

## Clean Test Setup

For online-functionality research, test with a fresh COTL install/profile where this diagnostics DLL is the only gameplay/plugin DLL added by us.

Before save-slot experiments:

- back up normal saves;
- remove unrelated BepInEx gameplay mods;
- launch once with only this diagnostics plugin;
- check the trace for `bepinex.plugin.loaded` entries to confirm what actually loaded.

## Trace Output

Logs are JSONL files written under:

```text
D:\SteamLibrary\steamapps\common\Cult of the Lamb\BepInEx\worldtrace
```

Each line is one event with:

- `ts` - UTC timestamp
- `category` - event category
- `message` - compact event payload

The first config file is generated by BepInEx after launch:

```text
D:\SteamLibrary\steamapps\common\Cult of the Lamb\BepInEx\config\com.codex.cotlonline.diagnostics.cfg
```

Useful config values:

- `VerboseSnapshots` - enables throttled runtime/persistence snapshots.
- `SyncEvents` - emits compact `sync.scene`, `sync.player_state`, `sync.damage`, `sync.death`, and `sync.world_hash` events.
- `TraceRawHookEvents` - enables raw hook logs such as `health.damage.before`/`after`; disabled by default because `sync.*` events are the lower-noise packet candidates.
- `TraceObjectPoolEvents` - enables the raw ObjectPool spawn/recycle firehose; disabled by default because it is very noisy.
- `TraceHighFrequencyPackets` - writes every high-frequency live packet to disk when enabled. Default is `false`; leave it off for movement testing so UDP packets are not also synchronous trace writes.
- `PersistentSnapshotPreview` - includes the long `DataManager` field preview in persistent snapshots; disabled by default so only the hash is written.
- `DeepPersistentWorldHash` - reflectively scans many `DataManager` fields for forensic persistent hashes. Default is `false`; leave it off during movement tests because it can hitch.
- `RuntimeSnapshotIntervalSeconds` - scene/player/health/team snapshot frequency.
- `PersistentSnapshotIntervalSeconds` - selected `DataManager` persistence hash frequency. Runtime clamps this to at least 30 seconds.
- `SaveFileHashIntervalSeconds` - re-hashes save slot files after the first world identity sample. Default is `0`, which keeps the startup file hash cached and reports `~dirty` instead of doing live disk hashing after save metadata changes.
- `AppliedDamageOnly` - under `[Sync]`, records `sync.damage` only when damage is actually applied.
- `IgnoreNeutralDamage` - under `[Sync]`, skips neutral victims such as grass, sprites, and many breakables.
- `CoopDiagnostics` - under `[Phase2]`, records co-op join/leave and player-count presence hooks.
- `RoomDiagnostics` - under `[Phase2]`, records world generation, room changes, and player placement hooks.
- `RewardDiagnostics` - under `[Phase2]`, records weapon, curse, relic, tarot, and pickup selection hooks.
- `AuthorityDiagnostics` - under `[Phase3]`, records run seed, dungeon graph, direct weapon/curse assignment, and short equipment caller stacks.
- `UdpEventStream` - under `[Live]`, sends low-noise event JSON to `127.0.0.1:37622` for the server ledger prototype.
- `PlayerMotionStream` - under `[Live]`, emits higher-frequency `sync.player_motion` events for player coordinate streaming tests.
- `PlayerMotionIntervalSeconds` - under `[Live]`, controls the `sync.player_motion` interval. Default is `0.1` seconds, or 10Hz. Existing older configs set to `0.2` are capped to an effective `0.1` by the plugin unless you lower the value further.
- `PlayerInputStream` - under `[Live]`, emits `sync.player_input` events with local movement axes and attack/dodge/curse/heavy button edges. Default is `true`.
- `PlayerInputIntervalSeconds` - under `[Live]`, controls the `sync.player_input` interval. Default is `0.05` seconds, or 20Hz.
- `ClientId` - under `[Live]`, stable per-install identity for LAN/server tests. Leave as `auto`; the plugin generates and persists a local id on launch.
- `RosterOverlay` - under `[Bridge]`, draws a read-only server roster overlay in-game. Press `F8` to toggle it.
- `RemotePlayerWorldMarkers` - under `[Bridge]`, draws lightweight world-space markers for remote players in the latest relay packet. Default is `true`.
- `HostP2ReservationDiagnostics` - under `[Phase5]`, records host-side vanilla P2 reservation state for remote co-op emulation tests.
- `HostP2ManualSpawnHotkey` - under `[Phase5]`, enables the host-only `F9` reserved-P2 spawn request.
- `HostP2ClearHotkey` - under `[Phase5]`, enables the host-only `F10` reserved-P2 removal request.
- `HoldReservedP2WithoutController` - under `[Phase5]`, temporarily disables COTL's no-controller P2 removal while a remote P2 reservation is active.
- `RequireRemoteP2ForReservation` - under `[Phase5]`, refuses the `F9` reservation test until the server roster contains an active `remote-p2`.
- `RemoteP2MotionDriver` - under `[Phase6]`, lets the host drive the reserved vanilla P2 body from the latest remote-p2 relay. In `0.5.2+`, nearby movement is translated into virtual input axes; direct snap is only a correction fallback.
- `RemoteP2MaxAgeMs` - under `[Phase6]`, rejects stale remote motion packets. Default is `1000`.
- `RemoteP2SnapDistance` - under `[Phase6]`, base snap distance from earlier prototypes. In `0.5.5+`, hard snaps are internally guarded to much larger errors so normal drift uses soft correction first.
- `RemoteP2MoveSpeed` - under `[Phase6]`, retained from the direct-motion prototype for future smoothing experiments. Current `0.5.2` movement uses the vanilla controller's speed once axes are injected.
- `RemoteP2CorrectionSpeed` - under `[Phase6]`, controls host-side soft correction toward the latest remote-p2 position while input relay is active. Default is `6`.
- `RequireWorldMatchForRemoteP2Driver` - under `[Phase6]`, pauses host P2 driving on `worldMatch=False` only when enabled. Default is `false` because live save hashes can drift during normal play.
- `BlockRemoteP2Transitions` - under `[Phase6]`, blocks reserved remote P2 from triggering host-side building and door transitions. Default is `true`.
- `RemoteP2HideWhenSceneMismatch` - under `[Phase6]`, hides the host reserved P2 body while the remote-p2 client reports a different Unity scene. Default is `true`.
- `RemoteP2HideWhenLocationMismatch` - under `[Phase6]`, hides the host reserved P2 body while the remote-p2 client reports a different `PlayerFarming.Location`. Default is `true`.
- `RemoteP2HideDuringTransitionStates` - under `[Phase6]`, hides the host reserved P2 body while the remote-p2 client remains in transition states such as `InActive` or `CustomAnimation`. Default is `false` in `0.5.13+` because those states occur during normal co-op spawn.
- `RemoteP2AwayGraceSeconds` - under `[Phase6]`, controls how long a mismatch/transition must persist before hiding P2. Default is `0.45`.
- `RemoteHostVisualMirror` - under `[Phase7]`, lets a `remote-p2` client auto-spawn a local vanilla P2 body as a non-authoritative visual mirror of the host-lamb player. Default is `true`.
- `RemoteHostMirrorAutoSpawn` - under `[Phase7]`, automatically spawns the host visual mirror once host-lamb relay data arrives. Default is `true`.
- `RemoteHostMirrorMaxAgeMs` - under `[Phase7]`, rejects stale host-lamb mirror packets. Default is `1000`.
- `RemoteHostMirrorSnapDistance` - under `[Phase7]`, base snap distance for the host visual mirror. Default is `4`.
- `RemoteHostMirrorCorrectionSpeed` - under `[Phase7]`, controls soft correction for the host visual mirror. Default is `8`.
- `RemoteHostMirrorHoldGraceSeconds` - under `[Phase7]`, keeps the mirror's no-controller hold alive through brief host-relay gaps. Default is `10`.
- `BlockRemoteHostMirrorTransitions` - under `[Phase7]`, blocks the visual mirror from triggering local doors/buildings. Default is `true`.
- `WorldAuthorityDiagnostics` - under `[Phase8]`, emits compact cult/follower snapshots for host-owned world-state comparison. Default is `true`.
- `CultSnapshotIntervalSeconds` - under `[Phase8]`, controls how often `sync.cult_snapshot` is emitted. Default is `2`.
- `ServerLoadoutRelay` - under `[Phase9]`, lets ServerLedger relay observed remote weapon/curse assignments back to the other client bodies. Default is `true`.
- `LoadoutOverrideRemoteBodies` - under `[Phase9]`, applies server-relayed weapon/curse loadouts only to bridge-owned remote bodies, never the local real player. Default is `true`.
- `LocalCameraSplit` - under `[Phase9]`, switches the local camera to the local player only when bridge-owned bodies get far apart, then returns to vanilla co-op framing when they are close again. Default is `true`.
- `CameraSplitDistance` / `CameraReturnDistance` - under `[Phase9]`, control local camera split and return thresholds. Defaults are `14` and `8`.
- `ServerRunAuthority` - under `[Phase10]`, receives server-owned dungeon seed and reward-choice packets. Default is `true`.
- `ForceServerDungeonSeed` - under `[Phase10]`, lets non-host clients replace locally generated dungeon seeds with the host/server seed when the packet is available. Default is `true`.
- `ForceServerRewards` - under `[Phase10]`, lets non-host clients force weapon/curse podium randomizers to follow host/server reward order when the packet is available. Default is `true`.

## Versions

- `0.5.33` - starts Phase14 enemy authority in observe-only mode. ServerLedger now sends `server.enemy_authority` packets based on the assigned host-lamb combat roster, and the overlay shows the host room, enemy hash/count, and whether the local client matches it. This does not suppress remote enemies or drive AI yet; it is the first host-authority feed needed before enemy spawn/death/room-clear commands. The server reducer also no longer lets a first-seen client presence line swallow useful `sync.*` events such as spell casts.
- `0.5.32` - changes spell relay from zero-damage replay to normal visual replay plus a guarded damage suppressor. Relayed casts now spawn with their normal visual parameters, while `[Phase11] SpellRelayVisualOnly=true` makes `Health.DealDamage` skip damage from bridge-owned relayed spell objects. This should let projectiles/area effects appear without letting the receiving client author enemy health. `phase11.*` receive-side relay diagnostics are now also sent to ServerLedger and printed as `[spell]` lines so misses such as `space_mismatch`, `bridge_body_unavailable`, or `apply_error` are visible in the server console.
- `0.5.31` - selectively imports the useful SOL spell-relay work into the active bridge without replacing this project with the SOL fork. Completed local spell casts now emit sequenced `sync.spell_cast` events after a successful `PlayerSpells.CastSpell`, ServerLedger relays recent casts as `server.spell_casts`, and receivers replay each cast once on the opposite bridge-owned body. Replay is visual-only by default through `[Phase11] SpellRelayVisualOnly=true`, so the receiving client does not intentionally author enemy damage until enemy authority is ready. The SOL authority audit is kept in `docs/SOL_AUTHORITY_AUDIT.md`, and `scripts/Test-SpellRelay.ps1` smoke-tests the packet path.
- `0.5.20` - fixes Phase9 loadout relay thrashing by relaying/applying only each host/remote client's real `playerID=0` loadout, not its local mirror body. It also adds Phase12 remote frame state on the input relay: facing/look/aim angles plus faith ammo are mirrored onto bridge-owned bodies before virtual attack/spell input is consumed, and camera split now has a late pass plus server-relay fallback when the visual bridge body is hidden or missing.
- `0.5.19` - adds Phase11 bridge-owned spell cast diagnostics/recovery. Remote P2 and remote-host mirror bodies now call `PlayerSpells.Init()` before `CastSpell`, log the curse/prefab/faith context, and recover from bridge-only `CastSpell` exceptions so failed remote spell attempts do not leave the actor stuck in Aiming/Casting.
- `0.5.18` - fixes the Phase9 camera split regression when the remote player is intentionally hidden for a scene/location mismatch. The split camera now keeps forcing local-player focus while the roster says the other client is away, instead of releasing back to vanilla co-op camera targets.
- `0.5.17` - adds curse/spell and heavy-attack input relay. `sync.player_input`, `server.remote_inputs`, the host P2 driver, and the remote host visual mirror now carry `curseDown`, `curseHeld`, `curseUp`, `heavyDown`, and `heavyHeld` so dungeon spells can be driven through the same game input APIs as local play.
- `0.5.15` - adds the first Phase10 dungeon authority pass. ServerLedger now treats the assigned `host-lamb` as the authoritative dungeon seed source, sends `server.run_authority` packets, and tags generated weapon/curse rewards with the source client/role. Non-host clients can replace `DataManager.AddNewDungeonSeed` with the server seed and can force weapon/curse podium randomizers through the game's existing `ForceEquipmentType` path. This is intentionally best-effort: if the remote client generates its dungeon before the host seed packet arrives, that run can still diverge and should be restarted for the next test.
- `0.5.14` - adds Phase9 loadout relay, local-input blocking for bridge-owned P2/mirror bodies, and an experimental local camera split. ServerLedger now sends `server.loadouts` packets from observed host/remote weapon and curse assignments, and the diagnostics plugin applies them only to non-local bridge bodies to reduce dungeon missing-item softlocks. Bridge-owned bodies now return neutral local axes/buttons if vanilla input leaks through after focus changes or alt-tab. The camera split is local-only: when the remote body is far away, each client can focus its own player instead of inheriting the vanilla co-op midpoint camera.
- `0.5.13` - fixes the bad `0.5.12` remote-away false positive. The host compared raw local scene names such as `Base Biome 1` against packet-safe remote scene tokens such as `Base_Biome_1`, then logged both cleaned, making the mismatch look impossible. Scene/location comparison is now token-normalized before deciding remote-away, and transition-state hiding is disabled by default so normal P2 spawn `InActive` / `CustomAnimation` states do not make the host P2 invisible.
- `0.5.12` - adds a Phase6 remote-away gate for host-side reserved P2. The host now hides the reserved P2 body and clears virtual input while the remote-p2 client is in a different scene/location or sustained transition state, then restores/snaps it when the remote returns. The current dungeon-room testing also confirms a separate next authority target: weapon/curse podium and loadout assignments must be server-owned or mirrored, because vanilla co-op doors can softlock if either local instance believes a player is missing required starting items.
- `0.5.11` - adds Phase8 cult/world authority diagnostics. The plugin emits `sync.cult_snapshot` with stable follower IDs, follower task/state/position, follower needs, active follower count, structure count, cult faith, and a compact hash. ServerLedger stores the host-lamb snapshot as the active cult baseline, persists it in `server_worlds/<worldId>/world.json`, and adds `cultMatch`/`cultFollowers` to roster rows so save-slot matching and active cult simulation matching can be evaluated separately.
- `0.5.10` - blocks vanilla `CoopManager.RemovePlayerFromMenu`, `RemoveCoopPlayer`, and `RemoveCoopPlayerStatic` for the Phase7 remote host visual mirror while the local client is assigned `remote-p2`. The bad `0.5.9` signature was a visible host mirror followed by repeated `remove_menu` logs, respawns, and roughly one-second indicator age jumps. This build keeps the no-controller hold through brief relay gaps and records throttled `phase7.remote_host_mirror.remove_blocked` lines instead.
- `0.5.9` - fixes the Phase7 remote host visual mirror spawn/remove loop. The mirror now enables `CoopManager.temporaryDisableRemoval` before calling `SpawnCoopPlayer`, keeps that hold while host relay data is valid, and waits on an existing inactive mirror body instead of repeatedly spawning new ones. It also logs `phase7.remote_host_mirror.inactive_body` if the visual body is present but inactive.
- `0.5.8` - normalizes live UDP timestamps on server receipt so relay age is not distorted by clock skew, changes persistent snapshots to a lightweight default hash path to avoid reflective DataManager hitches, and adds the first experimental remote-p2 host visual mirror. The mirror auto-spawns a local vanilla P2 body on the remote-p2 client and drives it from host-lamb relay data; it is visual/non-authoritative and may still look like the goat until skin ownership is solved.
- `0.5.7` - adds `--host-client-id` / `--preferred-host` to ServerLedger so a known desktop client can stay `host-lamb` regardless of connect order. It also adds optional `RemotePlayerWorldMarkers` so non-host clients can see relayed remote-player positions in-world without spawning gameplay bodies.
- `0.5.6` - separates hot live packet transport from trace-file logging. `sync.player_input`, `sync.player_motion`, `bridge.remote_inputs`, and `bridge.remote_players` still update UDP/live state, but no longer write every packet to disk unless `TraceHighFrequencyPackets=true`. `phase6.remote_p2.input` diagnostics are throttled to periodic samples and mode changes.
- `0.5.5` - reduces live hitching by clamping persistent snapshots to 30+ seconds and caching save-file hashes after the startup world identity sample. If the save files change later, the reported hash is marked `~dirty` instead of immediately re-reading and SHA-hashing the slot files on the Unity thread. Host P2 movement also gains `input-correct` soft drift correction and raises hard snap fallback to larger errors.
- `0.5.4` - adds a one-time spawn snap so newly reserved host P2 is moved to the latest remote position before input is accepted. It changes `worldMatch=False` from a hard movement pause to an overlay warning by default, while still pausing on bad save slot. It also blocks remote reserved P2 from host-side building/door transitions until transition authority is designed.
- `0.5.3` - adds `sync.player_input` and `server.remote_inputs` so host P2 can be driven from real remote axes and button edges instead of delayed position/state inference. The host still uses remote position as a large-error correction fallback, but input packets are preferred. The remote P2 driver now pauses on explicit `worldMatch=False` to avoid walking a mismatched save into host-only building or door triggers.
- `0.5.2` - changes host-side reserved P2 driving from direct transform follow to virtual input-axis injection through `RewiredGameplayInputSource.GetHorizontalAxis` / `GetVerticalAxis` for the reserved P2 only. The host still snaps P2 when too far from the remote coordinate. The build also spoofs first-pass dodge/attack button edges from remote state changes and reduces the server remote-player reply throttle to 50ms. This is still experimental and not true remote button transport yet.
- `0.5.1` - adds the first experimental host-side remote P2 motion driver. When host P2 is reserved and a remote-p2 relay packet arrives, the host moves/snaps the reserved P2 body toward the laptop player's latest coordinates. This is coordinate sync only, not real remote attack/input injection yet. P2 reservation now ignores noisy `worldMatch=False` and only treats `saveSlotOk=False` as a hard remote-p2 mismatch.
- `0.5.0` - added Phase 5 host P2 reservation diagnostics. The host can press `F9` to spawn/reserve a real vanilla P2 body when a remote-p2 client is present, `F10` to clear it, and the bridge reports whether P2 is wanted, active, held from no-controller removal, or missing controller input. Rosters now include the current observed dungeon seed.
- `0.4.9` - added read-only server-to-client remote player relay through `server.remote_players` and the F8 overlay.
- `0.4.8` - changed world matching to prefer stable slot save-file hashes so copied reserved-slot saves can match across PCs.
- `0.4.7` - added the reserved server save slot policy. The server advertises `serverSaveSlot` and marks each client `saveSlotOk=True/False`.
- `0.4.6` - added `sync.world_identity`, server-owned `server_worlds/<worldId>/world.json`, baseline world hash matching, and dry-run save redirect path reporting.
- `0.4.5` - added a read-only F8-toggle bridge overlay that parses `server.roster` replies and shows current clients/roles inside the game. Server rosters now mark the first active client as `host-lamb` and the second as `remote-p2`.
- `0.4.4` - added the first bidirectional bridge: ServerLedger replies to clients with a throttled roster packet, and the diagnostics plugin records server replies as `bridge.roster`.
- `0.4.3` - added stable per-install `ClientId`, per-launch `SessionId`, identity-prefixed trace events, and server-side `client:pN` player keys for LAN testing.
- `0.4.2` - added configurable higher-frequency `sync.player_motion` coordinate streaming and server-side motion sample ingestion.
- `0.4.1` - added a low-frequency live heartbeat so a UDP listener started mid-game can recover plugin version and current scene/run context.
- `0.4.0` - added localhost UDP live event streaming from the diagnostics plugin and `--listen-udp` mode in the server ledger prototype.
- `0.3.0` - added Phase 3 authority diagnostics for run seed lifecycle, dungeon room graph mutation points, direct weapon/curse assignment, and P2 mid-dungeon equipment attribution.
- `0.2.0` - added Phase 2 read-only co-op, room generation, and reward/pickup diagnostics for testing local co-op as a network-player emulation target.
- `0.2.1` - added modern `MMBiomeGeneration` / `MMRoomGeneration` dungeon hooks and throttled repeated remove-menu logging.
- `0.1.5` - reduced runtime lag by making raw hook logs opt-in, filtering default `sync.damage` to applied non-neutral hits, and making persistent snapshots hash-only by default.
- `0.1.4` - changed trace writes to open/append/close per event and added startup checkpoint logs for easier BepInEx diagnosis.
- `0.1.3` - replaced async trace queue with synchronous auto-flushed writes after `0.1.2` produced an empty live trace.
- `0.1.2` - added packet-shaped `sync.*` events and disabled ObjectPool tracing by default behind config.
- `0.1.1` - removed direct `PlayerWeapon` hooks after `AttackDealDamage` proved unsafe; attack state is now observed through runtime snapshots and resolved damage through `Health.DealDamage`.
- `0.1.0` - initial diagnostics build.
