# MappingMode review

Review of `git diff efb86c2d22..HEAD` (branch `clanker/server-mapping-mode-961535`, 85 files). Read-only: nothing was
built or run. Every finding below was traced in the code; line numbers are at `15c4a9069a`.

## Findings

### 1. High: a no-op tile placement reverts the last tile change on any map, another session's included

- `Content.Server/_WF/MappingMode/WFMappingModeSystem.Guard.cs:65` stores every tile change on the server in
  `_lastTileChange`, tool edits (`MappingToolsSystem.Apply`) included, and nothing clears it until the next
  `PlacementTileEvent`.
- `OnPlacementTile` (`:99`) takes that stored change as the tile the event placed, without checking that it is the
  event's grid and cell. When the stored change is on a map the placer can't edit, `:117` sets it back to its old tile.
- The engine raises `PlacementTileEvent` even when the tile didn't change
  (`RobustToolbox/Robust.Server/Placement/PlacementManager.cs` `PlaceNewTile`). `MapChunk.TrySetTile` returns false
  for an identical tile, and `SetTile` returns early for an empty tile in a missing chunk, so no `TileChangedEvent` is
  raised in those cases.
- Exploit: `MsgPlacement { PlaceType = RequestPlacement, IsTile = true, TileType = 0, NetCoordinates = (own grid,
  any empty cell) }`. `AllowPlace` passes it, since it is the mapper's own grid and `TileType` 0 makes no grid. Each
  message reverts the most recent tile change on the server. If that change made a new grid in another session, the
  revert empties that grid and the engine deletes it. At the edit rate this is 60 reverts a second.
- It also happens by accident: dragging the tile eraser over gaps, or laying floor over identical floor, reverts other
  sessions' latest tile. When the stale change is in the placer's own session, `:125` can delete an unrelated
  single-tile grid.
- Fix: resolve `ev.Coordinates` to its grid and cell and act only when `_lastTileChange` matches both. Clear it every
  tick, as `MappingToolsSystem.Update` does with its own copy.
- Resolution: the stored change carries its tick and is cleared every tick; `OnPlacementTile` resolves the event's
  coordinates the way the engine does (`IsPlacementCell`) and touches the change only on the same grid, cell and
  tick, so a no-op placement reverts nothing. MappingTools' history claims a tile change the same way.
  `WFMappingPermissionTest` covers it.

### 2. High: one player can hold every session slot

- `TryCreateSession` (`WFMappingModeSystem.Sessions.cs:99`) and `TryOpenSession` (`:123`) end the player's old
  membership with `LeaveSession`. That leaves a session with no members, which only marks `EmptySince` (`:358-359`).
- `UpdateSessions` (`:466-473`) closes such a session only after `empty_session_grace` (300 s), although no member
  is left to come back.
- Exploit: create, then leave (`WFMappingLeaveRequest` has no throttle), then create again. The join throttle allows
  one create per 3 s, so 32 sessions (`max_sessions`) are reached in about 96 s. Repeating it every 300 s keeps the
  cap full, and every other player, admins included, gets "too many sessions" (`CheckSessionCap`, `:148`).
- Fix: close a session at once (autosaving if dirty) when its last member leaves or is kicked. Keep the grace for
  absent members only. Also cap live sessions per owner.
- Resolution: `LeaveSession` closes a session left with no members at once, autosaving if dirty, so create, open
  and join close the player's previous session when nobody else is in it; the grace now only covers sessions whose
  members are all away. `max_sessions_per_player` (default 1) caps live sessions a player owns, and the server cap
  counts the slot the player's old session frees. `WFMappingSessionTest.EmptySessionsKicksAndCodes` covers it.

### 3. High: copy and paste grow decals without bound

- `CheckEditLimits` (`WFMappingModeSystem.Limits.cs:208-246`) checks area, grids, entities and entities per tile.
  It never checks decals, and there is no decal cap per session.
- `Copy` takes every decal in the area (`MappingToolsSystem.cs:388-391`), and `Paste` adds all of them back
  (`:476-481`) through `TryAddDecal`, which only needs a floor tile under them.
- Exploit: on one floored cell, send `MappingToolsCopyEvent { Selection.Area = that cell }`, then
  `MappingToolsPasteEvent { Target = that cell }`. Each pair doubles the decals on the cell. At the tools rate of
  10 requests a second that is five doublings a second, so the count passes millions within seconds. Every dirty
  decal chunk is sent whole to every viewer, and each step's lists stay in the undo history, so server memory,
  bandwidth and CPU run away.
- Fix: give the paste check a decal count per cell (as `EntityCells` does for entities) and refuse past
  `max_decals_per_tile`. Add a decal cap per session.
- Resolution: a paste passes the cell of every decal it adds (`DecalCells`) and is refused whole when any cell would
  pass `max_decals_per_tile`; a copy or paste carrying more than `max_edit_decals` (4,096) is refused. Reference
  grids were already checked per cell. No separate per-session decal cap: pastes are the only way to multiply
  decals, and the per-cell cap bounds them. `WFMappingLimitsTest.ToolLimits` covers it.

### 4. High: a reference pick loads a whole station before any limit is checked

- `OnReferencePick` (`WFMappingModeSystem.Reference.cs:94`) only checks the file size against
  `max_reference_bytes` (8 MiB) before loading it.
- All 560 files under `Resources/Maps` and `Resources/SharedMaps` are under 8 MiB. The largest is
  `Maps/_Mono/Crocus/jianghui_industrial_0.yml` at 5.9 MB, and there are stations of 4 to 5 MB.
- The file is loaded synchronously inside the network handler (`:183`). The entity and grid caps are checked only
  after the load (`:220-222`), so a station far over `max_entities` is fully loaded, then refused and deleted.
- The throttle is per player (1 per 5 s, `Limits.cs:27`), so several mappers picking `/Maps/convex.yml` in turn keep
  the tick blocked. A pick that passes also holds up to the session's remaining entity cap on a scratch map for 60 s,
  for every present member at once.
- Fix:
  - Lower `max_reference_bytes`, or keep an index of entity and grid counts per listed file, built once, so a pick
    over the caps is refused before loading.
  - Add a server-wide reference-load budget (one load at a time, a global rate).
  - Count pending references against the session's entity cap.
- Resolution: `max_reference_bytes` defaults to 1.5 MiB, which admits every file under `/SharedMaps` (largest 1.2 MB)
  and refuses 21 of the 31 `/Maps` files over 1 MiB, every station from 1.6 MB up. Right after the load the grid
  and entity caps are checked on everything loaded, counting other members' pending references, before anything is
  stripped or measured. The load stays synchronous, but `reference_cooldown` (2 s) refuses any pick on the server
  until that long after the last load. `WFMappingReferenceTest` covers the cooldown.

### 5. High: the cost of a mapping tools request is limited by count, not by size

Before this branch the mapping tools were admin-only. Now every whitelisted mapper reaches them, limited only to 10
requests a second.

- `Copy`, `Delete`, `Paste` and undo or redo serialise or deserialise the whole selection. A selection can be the
  whole session, up to 25,000 entities (`MappingToolsSystem.cs:393-399`, `Apply.cs:201-283`), so 10 of these a second
  is enough to stall the server.
- `RequestRectRemove` has no size limit:
  - `AllowRectErase` (`WFMappingModeSystem.Limits.cs:368-380`) checks only the map, and the engine uses
    `msg.RectSize` as sent. One message with `RectSize = (1e6, 1e6)` erases the whole session in one tick.
  - For each erased entity, `MappingToolsSystem.OnPlacementEntity` (`History.cs:244-272`) builds a new
    `EntitySerializer` (`SaveGroup`) and adds a separate group to one merged edit.
  - Undoing that edit runs `RestoreGroup` once per group, and each run creates a holding map and calls
    `TryLoadGeneric` (`Apply.cs:241-283`). That is 25,000 map creations and loads in one undo, which can be repeated
    by undo and redo.
- History memory has no byte budget: `MaxHistory = 100` edits per mapper (`History.cs:25`), and each edit can hold a
  snapshot of a whole session. Alternating paste and delete fills it with about 50 full snapshots per mapper.
- Fix:
  - Cap `RectSize` for mappers (for example 32 x 32 tiles), and refuse a rect erase or tool selection over a set
    entity count.
  - In MappingTools, serialise all erasures of one tick in one group, and restore a group with one holding map.
  - Give each mapper's history a byte or entity budget, and drop the oldest edits past it.
  - Throttle by work (entities touched) rather than by request count.
- Resolution: MappingTools fills in each request's selected cells, entities touched (contents included) and decals
  on `WFMappingCanEditEvent`, and MappingMode refuses mappers past `max_selection_cells` (4,096),
  `max_edit_entities` (2,000) and `max_edit_decals`; a whole-grid delete is checked by its entities, so Delete the
  grid under me still works. An area erase is refused past the same cell and entity caps before the engine acts.
  Each mapper's history is trimmed to `max_history` (30) steps and `max_history_bytes` (16 MiB, estimated from the
  saved data), oldest first, through a `Limits` kind of the same event. An undo restores all its groups on one
  holding map. Admins keep the old behaviour. Erasures are still saved one group per entity, now at most 2,000.

### 6. Medium: undo and redo skip the entity and grid caps

- `StepHistory` (`MappingToolsSystem.History.cs:196-215`) asks `WFMappingCanEditEvent` with kind `Edit` and no
  counts. `CheckEditLimits` (`WFMappingModeSystem.Limits.cs:208-246`) therefore passes it, so a redo of a paste or an
  undo of a delete restores everything regardless of the current totals.
- A solo mapper can do it: paste 20,000 entities, undo, place a 20,000-entity reference (which doesn't touch the
  history, so the redo stays), then redo. The session ends at 40,000 against a cap of 25,000.
- With several members, each one's redo stack adds on top, so the total can reach the cap times the member count.
  Grids work the same way, through a redo of a paste that made a new grid.
- Fix: have the history step fill `Entities` and `NewGrid` from the edit (the entity count of the groups it
  recreates, and the grids among them), so `CheckEditLimits` applies.
- Resolution: groups record their entity count and whether a root is a grid when saved; a step passes the entities
  it would add net of those it removes, the entities it touches and whether it brings a grid back, so the caps and
  the popup apply as for a paste. `WFMappingLimitsTest.ToolLimits` covers it.

### 7. Medium: `colornetwork` lets any player recolour pipes on any map

- `Content.Server/Sandbox/Commands/ColorNetworkCommand.cs:24-27` (upstream, `[AnyCommand]`) writes "no access" and
  carries on: the `return` is missing.
- Node groups form on paused maps, since `NodeContainerSystem` queues a reflood on `ComponentStartup`, and this
  command is meant for paused mapping maps.
- Exploit: `colornetwork <NetEntity id> Pipe #ff0000`, with any pipe's id (the ids are sequential), repaints that
  pipe network in another session. The colour is saved into that session's library versions.
- This is an upstream bug that also affects normal rounds, but in mapping mode it breaks isolation.
- Fix: a marked edit that adds `return;` after the error. If mappers should keep the command on their own maps, add
  a hook that allows it when `WFMappingModeSystem.CanEdit(player, map of target)` is true.
- Resolution: the `return` is added under a standalone `WOLFGATE` marker, since it fixes normal rounds too. Mappers
  don't get the command.

### 8. Medium: a kicked member can rejoin at once

- `OnKickRequest` (`WFMappingModeSystem.Sessions.cs:304-328`) only calls `LeaveSession`.
- `OnJoinRequest` and `TryJoinSession` (`:180-215`) keep no record of the kick, and codes never change. A kicked
  griefer can send `WFMappingJoinRequest(code)` 3 s later and is back in with full edit rights. The owner can't keep
  them out, and the session autosaves their damage into the owner's library.
- Fix: keep a set of kicked users per session and refuse their join. Optionally let the owner regenerate the code.
- Resolution: a kick (by the owner or `wf_mappingmode_kick`) adds the player to the session's kicked set, and a join
  from them is refused for the session's life (Mapping admins excepted). `WFMappingNewCodeRequest` lets the owner
  replace the code, from New code on the Session tab. `WFMappingSessionTest.EmptySessionsKicksAndCodes` covers both.

### 9. Low: the per-tile entity cap only counts direct grid children at a cell

- `AllowPlace` (`WFMappingModeSystem.Limits.cs:332-341`) checks the cap only when a grid is found. A placement in
  space, with the map as parent, is never counted, so entities can be stacked without limit on one point (up to
  `max_entities`).
- `EntitiesOnTile` (`:154-165`) counts only entities whose parent is the grid. `PlacementManager` spawns with
  `SpawnAttachedTo(coordinates)`, so a placement whose `NetCoordinates` are relative to an unanchored entity already
  on the tile is parented to that entity and not counted.
- `Move` and `Mirror` (`MappingToolsSystem.cs:270-315`) don't pass `EntityCells`, so a selection can be moved onto a
  full tile.
- The session entity cap still holds, so the damage is client render cost for the session's members.
- Fix: count by position within the cell, whatever the parent, and apply the check to off-grid placements and to
  moves.
- Resolution: `EntitiesOnTile` counts every entity found on the cell by its world position, whatever its parent.
  Mappers can't place entities off a grid (tiles still make grids). Move and mirror pass where each selected entity
  lands, with the moving entities left out of the existing count. `WFMappingLimitsTest.ToolLimits` covers it.

### 10. Low: an admin on a mapping observer sees admin verbs but can't use them

- The marked edit in `Content.Shared/Verbs/SharedVerbSystem.cs:76-78` returns no verbs for a
  `WFMappingObserverComponent` user unless `force` is set.
- Listing is forced for admins (`Content.Server/Verbs/VerbSystem.cs`), but execution (`HandleExecuteVerb`,
  `SharedVerbSystem.cs:23-39`) calls `GetLocalVerbs` without `force`. An admin who joined a session therefore gets the
  admin verb menu, but every click finds no verb and does nothing.
- Fix: in the same marked block, let the check through for users with an admin flag, for example through an
  overridable `CanUseVerbs(user)` hook that the server implements with `IAdminManager`.
- Resolution: the marked block also lets through a user with admin data (`ISharedAdminManager.IsAdmin`), so an admin
  in a session lists and runs verbs; mappers without admin data still get none. Not covered by a test.

## Clean or notes only

- Isolation, apart from 1, 7 and 9:
  - Placement, erase and rect erase are pinned to the sender's session maps; maps and anything holding a player
    can't be erased.
  - Tool selections, z-level moves, adds and removes, decals, references, library list, open, delete and download
    are all resolved from the sender's session or their own `NetUserId` folder.
  - Paths are matched against server-built lists or folder names. No traversal was found in map names, version
    names, reference paths or download file names, and the client checks all three before writing.
  - No console command runs for a mapper. `mapping` is reached only through the admin-only Maps window and
    z-stack Open.
  - Ghost roles, follow, `ghost`, `suicide`, `observe` and `joingame` are all refused for a mapping observer
    (not a ghost, no `MobState`, pre-round lobby).
  - Verbs are empty; examine keeps its same-map range check.
- Stability:
  - Exceptions in entity network handlers are caught and logged by `ServerEntityManager` (`EXCEPTION_TOLERANCE`), so
    none of these handlers can take the server down; save, open, pick and download also catch their own.
  - Per tick, the guard costs nothing, `UpdateSessions` recounts each active session's entity tree once a second, and
    autosave writes at most one session a tick. With many sessions near 25,000 entities and the 1-minute step, the
    combined autosave time is high, so watch it once a live server runs.
  - Disconnect during a download or a pick drops the transfer or the scratch map. Round restart and mode-off
    autosave, then clear.
  - The reconnect `Forget` on the join, tools and edit throttles gives at most one extra burst per reconnect, which
    doesn't matter next to 4 and 5.
- Engine traps: no duplicate directed subscription (every directed one is on a new MappingMode component), no client
  or shared sandbox type found by grep, no `DefaultWindow` reserved names, no static IoC caches, no control that has to
  un-hide itself. Nullable warnings were not checked, since nothing was built.
- Correctness otherwise matched the design: codes, handover, versions and pruning, the autosave queue, download acks,
  reference offset and rotation, grid cap per member, the ticker held in the lobby, the admin map made on first
  observe.
- Project rules: every upstream edit carries a `WOLFGATE(MappingMode)` marker, the prototype is `WFMappingObserver`,
  player text goes through Fluent, and comments follow the house style. This file is new under `_WF`, so
  `python Tools/_WF/Ci/modules.py --write` needs running before the module README check passes.
