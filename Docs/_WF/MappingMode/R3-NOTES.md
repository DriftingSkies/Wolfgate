# R3 notes: protected entities, the deny set and Nar'Sie

Two live bugs from the owner: "You can delete other ghosts" and "A narsie was spawned, and now can't be deleted."

## Protected entities

`MappingProtectionSystem` (`Content.Server/_WF/MappingTools`) is the one rule, used by MappingTools and MappingMode.
An entity is protected when it, or anything it is parented to, is a player (`ActorComponent`), a ghost
(`GhostComponent`, admin ghosts included), a mapping observer (`WFMappingObserverComponent`, attached or not) or a body
whose `MindContainer` holds a mind. An entity may be edited (`CanTouch`) when it is neither protected nor holds a
protected entity at any depth. It lives in MappingTools because MappingMode already depends on it; it reads the
observer component from Shared MappingMode.

Paths that were open, and how each is closed:

| Path | Before | Now |
|---|---|---|
| Engine single erase (`RequestEntRemove`) | `HoldsPlayer`: actors, observers, anything an actor is under. A left-behind admin ghost (no actor), a body with a mind and whatever a detached observer carried could go | `AllowErase` asks `CanTouch` |
| Engine area erase (`RequestRectRemove`) | Deletes everything in the box but grids, actors and actors' children: detached observers, left-behind admin ghosts, mind bodies, a locker with a player in it | Refused for mappers (`wf-mapping-mode-erase-area-tools`); Erase mode sends `MappingDrawOp.EraseEntities`. Admins keep the engine's |
| MappingTools select, delete, cut, copy, move, rotate, mirror | `IsSelectable` left out ghosts and actor trees only, so a member's observer mid-reconnect (no ghost component, no actor) and mind bodies could be selected | `IsSelectable` asks `CanTouch` |
| Undo and redo | A restored or removed root took whatever had come to sit under it since (a ghost following a placed wall); moves carried it | Protected roots are skipped, protected entities under a root are moved off before the delete, and moves skip a root that now holds one |
| Grid delete (tools delete, clearing every tile, emptied grids, the grid cap backstop) | Only direct children that were actors, ghosts or observers were moved off; MappingTools moved whole unselectable children instead. A ghost following a wall went with the grid | Every topmost protected entity at any depth is moved to the map, out of any container, where it stands |
| Z-level removal | Moved actors only | Moves every protected entity to the next level |
| Pipe draw with Replace | Deleted the replaced pipe with anything under it | Refused when a replaced pipe holds a protected entity |
| Copy and paste | Excluded ghosts and actor trees | Same rule as selection; nothing protected is copied |
| `wf_mappingmode_purge` | (new) | Skips protected entities and moves protected ones off what it deletes |

`EraseEntities` takes the entities whose world position lies in the cells of the grid, plus loose entities off every
grid whose world AABB centre lies in them; with no grid the cells are map tiles of `Map`. Only topmost entities are
roots (the rest go with their parent), protected ones are left and counted, and the whole erase is one `Deleted`
group, checked against `max_selection_cells` and `max_edit_entities`.

## Nar'Sie

Reproduced in a throwaway integration test (since folded into `WFMappingDenyTest`), as the test client so the
engine's own `PlacementManager.HandleNetMessage` ran with the sandbox gate:

- Placement was allowed: `AllowPlacement` returned true for `MobNarsie` and `MobNarsieSpawn`. Neither carries
  anything in the old deny set (`SingularityDistortion`, `PointManager`, `GameRule`, `StarSystemMap`).
- Every erase path failed on `MobNarsie`, each with `Exception: Nar'Sie (…) is not serializable`: engine single
  erase, engine area erase and MappingTools delete. `MobNarsieSpawn` erased fine.

Cause: `MobNarsie` inherits `save: false` from `BaseMob`. MappingTools snapshots everything it deletes for undo
(`SaveGroup`, from the engine's `PlacementEntityEvent` and from `RemoveGroup`), and
`EntitySerializer.SerializeEntityRecursive` throws for a root whose prototype doesn't save. The throw left the
engine's erase handler before its `DeleteEntity`, stopped the engine's area erase loop at that entity (leaving the
rest of the box too), and aborted tools deletes and cuts. Not the cause: the sprite or click hit test (the server
refused before any of that mattered), `HoldsPlayer`/`IsSelectable` (its `MindContainer` holds no mind and it has no
actor), `EventHorizon`/`GravityWell` (nothing runs on a paused map), or `TimedDespawn`/`SpawnOnDespawn` (paused, so
the spawn never despawns). Every `BaseMob` had the same problem, so any mob placed as decor was stuck too.

Fix: a snapshot takes only the roots that serialize, and a failed snapshot is logged and the delete still happens;
copy leaves non-saving roots out. An erased mob can't be undone, which matches the library: it never saves.

## Deny set

Audited by component type. Added, always denied:

| Component | Why |
|---|---|
| `Singularity` | Grows and drains energy once unpaused; its distortion already adds a global PVS override |
| `EventHorizon` | Eats entities and tiles, and can breach containment |
| `GravityWell` | Pulls everything in range |
| `AnnounceOnSpawn` | A server-wide announcement and sound at map init |
| `GhostRole` | Registers a role for every ghost on the server at startup (hidden while paused, but registered) |
| `GhostRoleMobSpawner` | Spawns a mob for whoever takes the role |
| `StationEvent` | Round event; goes with `GameRule`, already denied |

Denied only with certain data (`DeniesComponent`):

| Component | When | Why |
|---|---|---|
| `ActiveRadio` | `globalReceive` or `receiveAllChannels` | Hears every channel on the server (Nar'Sie, Ratvar, observers); ordinary radios stay |
| `WarpPoint` | `follow` | Ghost follow targets (Nar'Sie, Ratvar, singularity, tesla ball, nuke, disk, immovable rod, vehicles) that `GhostSystem.GetLocationWarps` lists from every map, paused ones included |
| `SpawnOnDespawn` | Its prototype is denied, or the chain is over four deep | Nar'Sie's spawn animation leaves Nar'Sie behind |

Checked and left allowed:

- Plain `WarpPoint` markers: 208 `/SharedMaps` files carry warp points; stripping them would cut them out of every ship
  opened or uploaded. The upstream warp list does list them from paused maps, but mapping mode runs no round, so only
  admins have a warp list.
- `GlobalPvs` (radar edge markers on 82 maps, biome sources): ordinary content, so instead of denying it the override
  is removed on startup when the entity is on a paused map in mapping mode (`OnGlobalPvsStartup`). A real round adds it
  back when the map loads.
- Other global PVS overrides: `CEPvsOverride` is put on z-level map entities by CE itself; `GlobalTimeManager` and the
  station-wide ones run at map init or from round systems, and session maps are never initialised; the rest (explosions,
  FTL visuals, ropes, shields, holopads, laser pointers, tractor beams) start only from runtime actions.
- `IntrinsicRadioReceiver` alone only relays what its own radios hear; it is harmless without a global `ActiveRadio`.
- Shuttle consoles, thrusters and FTL: normal ship content. They act only through a console, which an observer can't
  use, and the session map stays paused.

The set is refused at placement (`IsDenied`), draws (`Prototypes`), pastes (`CarriedPrototypes`, every entity in the
clipboard, contents included, checked for denied components only, so hidden contents still paste) and stripped at
reference load, upload and library open (`HasDeniedComponent`); all three now tell the player how many were removed
(`wf-mapping-mode-denied-stripped`).

## Erase mode

- A click erases the entity hit exactly; otherwise the sprite tree is queried at the cursor (as the engine eraser
  finds entities), so a sprite larger than its cell is hit anywhere on it. One hit erases it; several open Delete
  under cursor, a list by name and prototype (twelve at most). A right click with no drag opens the list.
- The area erase sends `EraseEntities`, on a grid or off every grid (the drag now resolves its grid from both ends, as
  Shape does), then the decal and tile parts as before.
- Players, ghosts and observers are left out on the client too; the server checks again.

## Admin

`wf_mappingmode_purge <code> <prototype>` (`AdminFlags.Mapping`, admin logged) deletes every instance of a prototype
on the session's maps, nested ones included, and marks the session dirty.

## Tests

- `WFMappingProtectionTest`: two mappers, an admin ghost, an observer with no player, a body with a mind and an item
  under B's observer. A tries engine erase, engine area erase, tools delete, cut, move, rotate, mirror, copy and paste,
  area erase, undo of a placement a ghost sits under, z-level removal, grid delete and clearing every tile; every
  target stays and doesn't move. The area erase removes the walls, undo restores them.
- `WFMappingDenyTest`: Nar'Sie, its spawn and five test prototypes (one per new rule) refused at placement, by the gate
  and through the engine; refused in a draw and a paste; stripped at reference load, upload and library open; a
  spawned Nar'Sie and spawn erased by engine click, area erase and tools delete; purge.
- `WFMappingLimitsTest`: the engine area erase is refused at any size; an `EraseEntities` over
  `max_selection_cells` is refused.

## Not covered

- The client hit test and the Delete under cursor list were built and sandbox-checked, not clicked in a client.
- No test for the `GlobalPvs` override removal or the pipe-replace refusal.
- A body with a mind on a session map fails the session autosave (its `Consent` component has no serializer); the
  protection test's body is `save: false` to keep autosave quiet. Real players' bodies don't reach session maps.
