# SOL Online Authority Audit

## Snapshot and scope

This branch starts from `COTLOnlineResearch` 0.5.30 and is intended for comparison and selective integration. It does not deploy into the game install.

The immediate implementation is a sequenced, server-relayed spell-cast event. The broader audit below defines the authority boundaries needed to approach couch co-op behavior without running incompatible simulations on both machines.

## Concrete 0.5.30 spell gap

`Phase11SpellPatches` repaired and initialized bridge `PlayerSpells` instances, but it did not serialize a completed local cast or replay it on the other machine. Remote input could therefore move the bridge body while the spell projectile, curse prefab, audio, and cast animation existed only on the source client.

The SOL branch adds this flow:

1. A real local `PlayerFarming.Instance` cast emits `sync.spell_cast` with client/session sequence, curse, level, aim, relative target, scene, location, and room.
2. `COTLOnline.ServerLedger` deduplicates the event and retains it for 3.5 seconds.
3. The server repeats recent casts in `server.spell_casts`; UDP loss does not require the original packet to arrive on the first attempt.
4. The receiving client maps `host-lamb` or `remote-p2` to the correct vanilla P2 bridge body.
5. The receiver restores curse and aim state and calls `PlayerSpells.CastSpell` once per `source|session|sequence`.
6. Old curse-button emulation is suppressed for bridge bodies while explicit relay is enabled, preventing duplicate casts.

This is cast-completion replication. It does not yet stream the remote charge bar or pre-cast aiming presentation.

## Authority model

The command-line server should own identity, ordering, validation, durable mutations, and retained event windows. The host game instance should own Unity simulation. Reimplementing enemy or follower AI in `ServerLedger` would duplicate game logic and drift whenever the game updates.

| Domain | Command-line server | Host game | Remote game |
| --- | --- | --- | --- |
| Session and roles | Assigns world, host, remote role, session, sequence windows | Accepts assignment | Accepts assignment |
| Player input | Relays ordered input and rejects stale sessions | Simulates Lamb and reserved P2 | Simulates local responsiveness, then reconciles |
| Room/run graph | Retains seed, room token, encounter identity, rewards | Generates authoritative graph and encounter | Loads/matches host decisions |
| Spells and attacks | Relays commands/events, deduplicates | Executes both players' authoritative combat actions | Replays presentation; predicted damage is non-authoritative |
| Enemies | Owns network IDs and latest snapshots | Owns spawn, AI, position, HP, status, death | Passive replicas corrected from host snapshots |
| Followers | Owns stable IDs and durable accepted mutations | Owns brain, task choice, pathing, production completion | Passive replicas and UI commands |
| Jobs/structures/resources | Orders validated commands and persists accepted result | Executes mutation and reports result | Requests mutation and applies accepted result |
| VFX/audio | Relays short-lived sequenced events when state alone cannot recreate them | Emits source events | Replays once |

## Verified game choke points

### Damage and death

`Health.DealDamage(...)` is the central acceptance point. It rejects invincible, untouchable, inactive, dodging, immune, and other invalid states before applying damage. The same method sets `HP = 0`, invokes `OnDie`, `OnDieAny`, callbacks, and destroys or disables the object.

Implication: capture authoritative damage after `DealDamage` returns true, but replicate enemy state by stable enemy ID. Do not rebroadcast every remote client's locally observed `DealDamage`, because that doubles hits and lets divergent local AI author combat outcomes.

### Enemy targeting

`UnitObject.GetClosestTarget` uses `Health.playerTeam` and already considers multiple player-team health objects. The host's reserved vanilla P2 is therefore the correct combat body: enemies can target it through existing game logic without team spoofing.

Implication: remote P2 input must reach the host at gameplay frequency. Enemy AI and collision remain host-only. The remote game should not independently choose enemy targets once replica mode is active.

### Enemy enumeration

`Health.allUnits` and team lists (`team2`, `playerTeam`, and others) are maintained by the game. Existing `BridgeCombatAuthority` uses `Health.allUnits` at a throttled interval.

Implication: use maintained lists and spawn/death hooks. Do not add `FindObjectsOfTypeAll` or scene-wide object scans to Update/gameplay loops.

### Follower identity and jobs

`FollowerBrain.Info.ID` is stable save-backed identity. `FollowerBrain` exposes explicit current/saved task state and transitions such as `CheckChangeTask`, `HardSwapToTask`, and task `Tick`. `FollowerManager.FindFollowerByID` resolves a live body from that stable ID.

Implication: network follower state by `Info.ID`, not runtime Unity instance ID. Run task selection, pathing, production timers, needs, aging, and relationship changes on the host. Send commands such as assign job, interact, collect, imprison, heal, or rename to the host and return an accepted mutation/result.

## Required network identities

- Player: `worldId + clientId + sessionId + role`.
- Dungeon room: authoritative run ID plus biome/room coordinates and room seed.
- Enemy: `runId + roomId + hostSpawnSequence + prefab/type key`. Runtime Unity instance IDs are diagnostics only.
- Projectile or transient attack: source player identity plus attack/cast sequence and child spawn index.
- Follower: save-backed `FollowerInfo.ID`.
- Structure: persistent structure ID from save data; never position alone.
- Reward/podium: run ID, room ID, source spawn sequence, reward type, and claim state.

## Ordered implementation phases

### 1. Host enemy identity and snapshot

- Assign a network enemy ID at host spawn hooks already covered by encounter diagnostics.
- Send a 10-15 Hz host snapshot containing room epoch, enemy ID, type/prefab key, position, facing/state, HP/max HP, status flags, active/dead, and snapshot sequence.
- Emit immediate reliable-window events for spawn, damage accepted, status applied, and death.
- Keep periodic full snapshots as recovery from packet loss.

### 2. Remote enemy replica mode

- Match local encounter objects to host enemy IDs during room setup.
- Disable or gate remote enemy decision-making, attacks, reward drops, room-clear decisions, and authoritative damage.
- Interpolate ordinary movement; snap on room entry, teleport/burrow, large error, or death.
- Apply host HP/status/death idempotently without triggering a second reward/drop path.

### 3. Host player damage and lifecycle

- Treat both host Lamb and host reserved P2 health as authoritative.
- Relay accepted damage, dodge/invulnerability state, knockdown, revive, death, and room-transition locks.
- Reconcile remote local prediction to the host's HP and state sequence.

### 4. Follower baseline before live AI

- Build a compact follower baseline keyed by `FollowerInfo.ID`: name/skin, age, traits, needs, illness/death state, location, job/task type, assigned structure, and durable relationship fields.
- Compare baseline hashes first. Transfer a full baseline only on join, reconnect, scene load, or mismatch.
- Stream only active follower transforms/state at a low rate while clients share the cult scene.

### 5. Follower and structure commands

- Define request/accept/result messages with command ID, actor, follower/structure ID, expected revision, and mutation.
- Host validates proximity, interaction lock, resource cost, task eligibility, and current revision.
- Server records accepted order and latest durable revision; all clients apply the result once.

## Protocol rules needed before broad state sync

- Every event needs `worldId`, source session, monotonic sequence, and scene/room epoch.
- Reset dedupe windows when a session or room epoch changes.
- Use server receive time for retention; do not compare clocks from separate PCs.
- Commands need acknowledgement and retry. Snapshots may be lossy and replace older snapshots.
- Reject future-room, stale-room, wrong-world, unknown-role, and out-of-window commands.
- Cap packet counts and retained windows to prevent malformed clients from growing memory.
- Late join requires a baseline plus the latest sequence/revision, not a replay of the whole session.

## Known spell-relay boundaries

- The relay is compiled and protocol-smoke-tested, but still needs a real two-game test over the VPN.
- The remote client currently executes the complete spell against its local enemy simulation. That is necessary for present visuals, but enemy damage becomes host-only when replica mode is introduced.
- Teleport curses intentionally move the mapped bridge body. Test collision and room-boundary agreement on both machines.
- Charge/aim presentation before release is not replicated yet.
- Curse availability should ultimately be validated against the server-authoritative loadout before accepting a cast.

## Recommended next code slice

Implement host enemy IDs and read-only `server.enemy_snapshot` first. Initially display match/miss/error metrics without suppressing remote AI. Once identity matching is proven across repeated rooms and enemy variants, add remote replica gating behind a disabled-by-default configuration flag. This produces evidence before changing combat authority and keeps rollback straightforward.
