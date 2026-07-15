# COTL Online Diagnostics LAN Client Setup

This package is for the second PC/client test. It does not make online co-op playable yet. It sends diagnostics, heartbeat, player identity, motion, reward, equipment, and world identity events to the server ledger on the host PC, then receives roster/world, remote-motion, and host-P2 reservation replies.

## Install

1. Make sure the target game already has BepInEx installed.
2. Copy `BepInEx/plugins/COTLOnline.Diagnostics.dll` from this package into the target game's `BepInEx/plugins` folder.
3. Launch the game once, then close it. This creates:

```text
BepInEx/config/com.codex.cotlonline.diagnostics.cfg
```

4. Open that config file on the second PC and set:

```ini
UdpEventStream = true
PlayerMotionStream = true
UdpHost = <host-pc-ip>
UdpPort = 37622
ClientId = auto
```

Use the host PC's reachable IP for `UdpHost`.

Common options:

- normal LAN IP, for example `192.168.1.x`
- VPN IP, for example your Radmin/ZeroTier/Tailscale host address

Use the VPN IP if normal LAN ping/UDP does not work.

5. On the host PC, run:

```powershell
dotnet run --project ".\src\COTLOnline.ServerLedger\COTLOnline.ServerLedger.csproj" -c Release -- --listen-udp
```

6. Launch the game on the second PC.

## Expected Result

The host server console should show a second client id, for example:

```text
client-aaaaaaaaaa:p0
client-bbbbbbbbbb:p0
```

This proves LAN routing and separate client identity. It will not render remote players in-game yet.

With `0.4.4+`, each client should also receive server roster replies. On each game PC, its local trace should contain:

```text
bridge.roster
```

Those lines prove the server can send state back to that client.

With `0.4.5+`, press `F8` in-game to toggle the bridge overlay. The overlay should show both real client ids when both machines are connected to the same server. The first active client should read `role=host-lamb hostSlot=0`; the second should read `role=remote-p2 hostSlot=1`.

With `0.4.6+`, the overlay should also show a shared `world=<worldId>` and per-client `match=True` or `match=False`.

With `0.4.7+`, the overlay should show the reserved online/server-world save slot and `slotOk=True` for each client using it. Current default is `serverSaveSlot=4`, which COTL shows as UI slot 5.

With `0.4.9+`, the overlay should also show `remote relay`. When one client moves or dodges, the other client should receive a `server.remote_players` packet, write a local `bridge.remote_players` trace line, and show that remote client's latest position/state in the overlay.

With `0.5.0+`, the overlay also shows `host p2 reserve`. On the assigned host machine only, press `F9` after both clients appear in the roster to request a real vanilla P2 spawn through `CoopManager.SpawnCoopPlayer(1, true, -1f)`. Press `F10` on the host to clear that reserved P2.

With `0.5.1+`, the overlay also shows `remote p2 driver`. In `0.5.1`, this was a direct coordinate driver: when the host had a reserved P2 and the laptop was the `remote-p2`, the host moved/snapped the reserved P2 body toward the laptop player's latest relayed position.

With `0.5.2+`, the driver switches to a better test path. Nearby remote movement becomes virtual input axes for the reserved vanilla P2, so COTL's own `PlayerController` handles movement state and animation. Large distance errors still snap. The first attack/dodge test is derived from the remote player's observed state, not from true button packets yet.

With `0.5.3+`, the laptop sends `sync.player_input` packets with actual local axes and attack/dodge button edges. The host uses those before coordinate-derived axes. The host also pauses the remote P2 driver if the laptop row shows `match=False`; recopy the same fresh raw slot `4` save to both machines before testing again.

With `0.5.4+`, `match=False` is no longer a hard movement pause by default because live save hashes drift when menus/save activity happen. The host should show `worldWarn=mismatch` instead. Bad save slot still pauses. The host also blocks remote P2 from entering buildings/doors, so movement tests should not change rooms until transition authority is added.

With `0.5.5+`, the plugin reduces live world-identity hitches by caching the startup save-file hash and spacing persistent snapshots farther apart. The host driver also adds `input-correct`; if the host P2 gets a few units away from the laptop position, it should be nudged back instead of waiting for a hard snap.

With `0.5.6+`, high-frequency live packets still go to the server but are no longer written to the JSONL trace file by default. Leave `TraceHighFrequencyPackets=false` for movement testing; only turn it on for short captures where packet-level disk logs matter more than hitch-free play.

With `0.5.7+`, the server can pin the intended host by client id. Start ServerLedger with `--host-client-id <host-client-id>` so the desktop remains `host-lamb` even if the laptop connects first. The plugin also draws lightweight remote-player world markers by default; the laptop should see a `P1` marker for the host's relayed position.

With `0.5.8+`, the laptop/remote-p2 client also has `RemoteHostVisualMirror=true` by default. Once the server assigns the laptop `role=remote-p2` and relays host-lamb motion/input, the laptop should automatically spawn a local vanilla P2 visual body and drive it from the host's relayed state. This is not authoritative gameplay yet; it is a visual mirror so the laptop can see host P1 as an actor instead of only a marker.

With `0.5.9+`, that laptop mirror should no longer loop the goat spawn animation. If the server prints `[hmirror] ... inactive_body`, the mirror body exists but COTL still has it inactive; leave the server running and capture that log instead of repeatedly restarting.

With `0.5.10+`, the laptop mirror should also survive COTL's later co-op cleanup calls. The server should no longer show bursts of `[p2] ... remove_menu` for the laptop while the host visual mirror is active. Occasional `[hmirror] ... remove_blocked` lines are expected and mean the guard is doing work.

With `0.5.11+`, the overlay also shows `world authority` and each roster row includes `cult=<match>/<followers>`. `match=True` can still become `cult=False`; that means both clients may be on the same save slot while their active follower/cult simulation has already diverged.

With `0.5.13+`, the host-side remote P2 body should disappear only while the remote-p2 client reports a different scene/location, then restore when the remote returns. The host overlay should report `remote p2 driver: hidden remote away ...` during that state. If both clients are in Base, repeated `scene_Base_Biome_1!=Base_Biome_1` away logs mean an older `0.5.12` build/config is still loaded.

With `0.5.14+`, the overlay also shows `loadout relay` and `camera split`. The server sends observed remote weapon/curse assignments back to the other client bodies, and each plugin applies those assignments only to bridge-owned remote bodies. If alt-tab or focus changes make one client control both bodies, the bridge-owned body should now ignore local vanilla input and wait for server/remote input again.

With `0.5.15+`, the overlay also shows `run authority`. The server keeps the assigned host-lamb's dungeon seed as authoritative and sends it to the remote-p2 client. When that packet arrives before remote dungeon generation, the laptop should log `phase10.seed.forced` and should generate the same seed as the host. Host-generated weapon/curse podium order is also sent to non-host clients and can force local podium randomizers before they choose their own item.

Expected host-side signs after `F9`:

- host overlay client row reports `p2=True` or `p2Wanted=True/p2Active=True`;
- if no local controller is attached for P2, `p2NoController=True` and `p2Hold=True` are expected;
- host overlay reports `remote p2 driver: input-spawn-snap` once after spawn, then `input` or `input-correct` while the laptop moves; `input-snap` should be rare and means the two bodies were very far apart;
- server console prints `[p2]` lines for the spawn request, hold state, or vanilla remove-menu calls.

Expected laptop-side signs after `0.5.8`:

- laptop overlay reports `remote host mirror: waiting for local visual P2`, then `remote host mirror: input` or `input-correct`;
- host server console may print `[hmirror]` lines from `phase7.remote_host_mirror.spawn.request`, `body_changed`, `hold_enabled`, or `apply`;
- after `0.5.10`, repeated visible respawns or one-second age spikes should be checked against `[hmirror] remove_blocked` versus old `[p2] remove_menu` bursts;
- if it causes local camera/co-op issues on the laptop, set `[Phase7] RemoteHostVisualMirror = false` and fall back to the marker-only `0.5.7` behavior.

Expected Phase9 signs:

- in a dungeon, taking or assigning a weapon or curse on one real client should produce server-side `server.loadouts` packets and local `bridge.loadouts` trace lines on the other client;
- the bridge body on the opposite client should pick up the relayed weapon/curse without changing the local real player's loadout;
- if players move far apart, overlay `camera split` should switch from `vanilla` to `local p...`, then return to vanilla when the bodies are close again;
- if focus changes or alt-tab causes the wrong body to accept local input, the bridge-owned body should stop moving locally instead of letting one keyboard/controller drive both bodies.

Expected Phase10 signs:

- server console seed lines should show host observations as `accepted=True`; laptop seed observations after a host seed exists should be `accepted=False`;
- laptop trace should contain `bridge.run_authority` followed by `phase10.seed.forced` before or during dungeon entry;
- both overlays should show the same `runSeed` once the host seed is accepted;
- if reward authority lands in time, laptop trace should show `phase10.reward.forced` before `phase2.reward.*.postfix`, and podium choices should match the host's weapon/curse order;
- if the laptop generates the dungeon before a host seed packet arrives, restart that dungeon attempt instead of judging seed authority from that run.

For this phase, avoid follower interactions on the remote P2. Follower positions and interaction state are still local per game instance, so follower interaction sync needs a separate authority pass.

Expected Phase8 signs:

- host server console prints `[cult]` lines from both clients every few seconds when the active cult scene is loaded;
- host client should become the cult baseline when started with `--host-client-id <host-client-id>`;
- laptop may show `cult=False` even when `slotOk=True`, which confirms live follower/task divergence instead of install/config failure.

Known dungeon limitation:

- dungeon weapon/curse podium choices are still not synchronized by seed/room authority yet;
- `0.5.14` only relays observed loadouts after one client has received or chosen gear, which should help missing-item door readiness but will not make podium options identical by itself;
- the next authority pass should force the dungeon seed/reward roll source so both local games offer the same room graph and item choices before pickup.

The host server writes the current dry-run world state under:

```text
D:\Desktop\COTLMP\new mods\online functionality attempts\COTLOnlineResearch\server_worlds
```

The `world.json` file includes the baseline host hash, each connected client's hash, and a dry-run `SaveRedirectDryRunPath`. This is not active save redirection yet.

## Firewall

If the host does not receive packets, allow inbound UDP port `37622` on the host PC firewall.
