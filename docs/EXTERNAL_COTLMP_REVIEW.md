# External COTLMP Review

Reviewed sources:

- https://github.com/GeoB99/COTLMP/pull/9
- https://github.com/GeoB99/COTLMP/pull/21
- https://github.com/firebirdjsb/COTLMP

Date reviewed: 2026-07-17

## Verdict

`firebirdjsb/COTLMP` is the useful reference. It contains the visible "Open to LAN" pause-menu flow, LAN discovery, save transfer, client save blocking, world-state snapshots, dungeon seed handling, and several co-op lifecycle patches that map directly to issues seen in `COTLOnline.Diagnostics`.

`GeoB99/COTLMP#9` is useful historical context for player sync and async server structure, but most of that work is either already merged into the later code or superseded by the firebird fork.

`GeoB99/COTLMP#21` is low value for our current sync problems. It is command-line parser scaffolding for a future dedicated server, not a server-authoritative world simulation.

## Useful Ideas To Port Carefully

1. Pause-menu UX:
   - Repurpose the co-op pause button into "Open to LAN" when offline.
   - Show stop/disconnect states once hosting or joined.
   - Add direct connect and LAN scan later, after authority behavior is stable.

2. LAN discovery:
   - UDP broadcast discovery is straightforward and fits our existing ServerLedger model.
   - Good for local testing across machines without manually typing IPs.

3. Host save transfer:
   - Host reads the active save, compresses it, and gives it to joining clients.
   - Joining clients use a disposable/temp save copy.
   - Clients should not write authoritative saves while connected.
   - This supports our slot-4 "server world" idea, but should become explicit server-world state rather than ad hoc slot swapping.

4. Co-op lifecycle guards:
   - Block remote/co-op bodies from becoming `SetMainPlayer` targets.
   - Keep remote/co-op bodies out of camera target lists.
   - Stop vanilla co-op cleanup paths from hiding/removing network bodies.
   - Block remote bodies from firing door/scene transitions.
   - These should be scoped to bridge-owned bodies in our project, not global "all non-lamb" bodies.

5. Scene transition hygiene:
   - Debounce scene changes.
   - Skip transient scenes such as splash/buffer/loading scenes.
   - Resume transitions with retries after load.
   - This maps to our dungeon-door and 5000m run-loop bugs.

6. World-state snapshot checklist:
   - Dungeon seed and room coordinates.
   - Equipment, curse, relics, and pickups.
   - Followers, follower jobs, faith, hunger, sickness, weather.
   - Enemies, drops, resources, critters, NPCs, structures.
   - Useful as a capture checklist, but not as a final authority model.

7. Follower/world suppression on clients:
   - The fork blocks follower tick/task changes on clients and applies host follower state.
   - This matches our follower drift problem.
   - The safer route is host-authored follower command/state packets, not permanent blind suppression until we know all side effects.

## Do Not Copy Wholesale

The fork still appears to use relay and snapshot heuristics rather than a true authoritative entity model. Enemy/resource/NPC entries include Unity instance IDs, type names, positions, and health, then clients try to apply or match those snapshots. Instance IDs are not stable across machines, so this can reduce visible drift but does not fully solve deterministic enemy/event authority.

The global patches are also broad. Blocking every non-lamb player from updates, interactions, camera targets, and doors may fix one architecture while breaking ours. Our bridge has host-lamb, remote-p2, and remote-host mirror roles, so patches should check bridge ownership and role before suppressing game behavior.

## Best Next Port Order

1. Port scoped co-op lifecycle guards:
   - Block bridge-owned remote bodies from `SetMainPlayer`.
   - Block bridge-owned remote bodies from camera target lists.
   - Block bridge-owned remote bodies from door/scene transition triggers.
   - Guard vanilla co-op hide/remove paths only while bridge reserve is active.

2. Port save authority as protocol:
   - Add explicit `server.save_snapshot`, `server.save_chunk`, and `client.save_ack` events.
   - Keep slot 4 as the current test save, but treat the server copy as authoritative.
   - Block or warn on non-host save writes while connected.

3. Convert world snapshot ideas into host commands:
   - `enemy.spawn`, `enemy.damage`, `enemy.die`, `room.event`, `room.clear`, `pickup.claimed`, `follower.task`.
   - Use host-assigned stable network IDs, not Unity instance IDs.

4. Add LAN UI only after the above:
   - The "Open to LAN" UI is valuable, but it should not hide sync bugs behind a cleaner button.

## Current Project Implication

The external fork supports the direction we are already taking: save/world authority plus scoped manipulation of the vanilla co-op slot. It does not replace `COTLOnline.ServerLedger`. The most important lesson is that movement mirroring is the easy layer; enemy events, follower jobs, room transitions, and save ownership need host/server authority instead of trying to reconcile two independent simulations after they drift.

The reported "building placement breaks sync" behavior is consistent with a snapshot/reconciliation design rather than true live world authority. Save transfer can make clients enter the same world, and periodic snapshots can repair some drift, but construction, follower work, resource deposits, faith changes, and room/event triggers are live mutations. Our hybrid path should therefore treat the fork's save work as an entry/rejoin baseline and implement building/follower/enemy changes as ordered commands with one authority source.
