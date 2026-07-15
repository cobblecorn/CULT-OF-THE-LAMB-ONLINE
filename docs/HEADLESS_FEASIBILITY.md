# Headless Feasibility Notes

## Current Position

The first server should own persistence and control-plane state, not full Unity gameplay simulation.

Likely server-owned early:

- world IDs and join codes
- player authorization
- active host lease
- checkpoint metadata
- snapshot blob storage
- event journal
- protocol compatibility data
- hash exchange records

Needs evidence before extracting:

- `DataManager` serialization/deserialization
- save-slot isolation
- inventory and follower mutations
- structure mutations
- world/day progression

Likely Unity-bound initially:

- scene loading
- live `GameObject` lifecycle
- enemy AI
- combat collision
- projectiles
- object pooling
- room generation/runtime manifests
- Rewired/player input
- animation, effects, and audio

## Diagnostic Plugin Purpose

`COTLOnline.Diagnostics` records the first hard evidence we need:

- scene loads
- save/load calls
- player/co-op creation
- player attack starts
- player attack damage calls
- `Health.DealDamage` results
- `UnitObject.OnDie`
- enemy spawn/init paths
- `ObjectPool.Spawn` and `ObjectPool.Recycle`
- biome room activation events

The plugin does not synchronize anything. It only traces.
