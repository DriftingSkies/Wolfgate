# S1 notes: assumptions made while building the server core

## Feedback

- Refusals and results go out twice: a cursor popup when the player is in game, and a `WFMappingResultEvent`
  always. S3 should show the event only in the lobby (or drop the server popup) so nothing doubles.
- Whoever cancels `WFMappingCanEditEvent` tells the player; MappingTools shows nothing itself. `Reason` holds the
  Fluent id (for tests); `Quiet` asks without feedback (the decal-removal check uses it).
- Map entity names go through Fluent (`wf-mapping-mode-map-name`, `wf-mapping-mode-admin-map-name`).

## Sessions

- A player is a member of at most one session. Create or join quietly ends the old membership first.
- The member cap counts absent members.
- "Longest-present member" for handover: earliest-joined present member, else earliest-joined absent one.
- Someone joining a session everyone left becomes its owner. The owner can't kick themselves.
- Disconnect: the observer is deleted and the mind wiped on `Disconnected`, so a reconnect lands in the lobby;
  membership stays for rejoin. An admin ghosting out of their observer also counts as absent.
- The state event carries the player's own session (only while present) and the one session they can rejoin;
  there is no public session list. Limits ride on it and are re-sent at most once a second when they change.
- A session whose ground level vanished (an admin deleted it) closes without saving.
- `TileChangedEvent` marks a session dirty, loads included; S2 should clear `Dirty` after opening from the library.

## Limits

- Throttles: edits (placement, tiles, erase, decals) share `max_edits_per_second` as rate and burst; tool requests
  10/s; create, join and rejoin share 1 per 3 s; kick 1/s, burst 3; reference list 2/s; pick 1 per 5 s; confirm 1/s
  (its own bucket, so pick then confirm works). Drops log at Debug.
- Area: a single placement checks the tile under the placement point in map coordinates; tool edits check the
  grid-local box turned into a map AABB (so a rotated grid is checked by its AABB). `map_size` is clamped 64..512.
- Entities per tile count direct grid children found by lookup (container contents aren't counted). Paste counts
  root entities per destination cell. Move checks area only; mirror, delete and undo check map ownership only (undo
  of a delete can pass the entity cap).
- The session entity total includes observers, is recounted at most once a second, grows by one per allowed
  placement, and is recounted after tool edits, erasures and reference loads.
- Refused prototypes: unknown, abstract, `HideSpawnMenu`, or with a deny-set component. Unknown tile ids are refused.
- Erase is refused for maps, actors, mapping observers and anything a player is parented under (a grid someone
  stands on).
- A decal removal that the edit throttle drops is still recorded in MappingTools history (undo could re-add it).

## Reference loads

- The scratch load is `TryLoadGeneric` merged onto a paused scratch map, so grid and map files both work. A planet
  file (grid-map) makes the engine log an error and loads without its planet grid.
- Loose non-grid entities on the scratch map and deny-set entities are deleted before counting.
- Anchor: the floor of the centre of the union of the grids' AABBs. Each footprint grid is its origin and rotation
  relative to the anchor; confirm's offset is the map position for the anchor on the mapper's current level.
- Per-tile caps are checked at pick (grid-local, placement doesn't change them); area and grid/entity caps again at
  confirm.
- Own library files offered as references: any `.yml` under `/Mapping/Library/<NetUserId>/`, four folders deep. S2
  may narrow this to its version layout.

## Mode, admins, S2 hooks

- Turning the mode off closes every session (with autosave), returns admin observers, deletes the admin map and
  unpauses the ticker. The forced cvars (`game.lobbyenabled`, `events.enabled`, `shuttle.auto_call_time`) aren't
  restored.
- Admin observe needs `AdminFlags.Admin` and a player in the lobby. Admin leave works for any admin entity that isn't
  a session observer.
- `WFMappingLibrarySystem.AutosaveAll` and `Autosave(session)` are S2 stubs; `wf_mappingmode_save` reports a failure
  and `wf_mappingmode_close` closes without saving until S2 fills them.
- `WFMappingStackChangedEvent` also carries the ground index, which the session uses as its root map.
- `MappingMapsSystem.FindSaved` still walks `/Mapping/Library` (S2 item). Non-admin reference lists never include
  `Saved` files from the shared folder.
- No README or `modules.py --write` yet (S4), so `modules.py --check` fails until then.
