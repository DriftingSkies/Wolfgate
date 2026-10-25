# MappingMode design

A server-wide mode (`wf.mapping_mode.enabled`) that turns the server into a shared mapping workshop. Rounds never
start, the lobby becomes a mapping menu, and every player gets the full mapping toolset on their own map without
admin rights. Maps are saved into a per-player library on the server with versioned autosaves and can be downloaded
to the client. Several players can map together through a join code.

Module: `MappingMode` (`Content.Server/_WF/MappingMode`, `Content.Client/_WF/MappingMode`,
`Content.Shared/_WF/MappingMode`, tests under `Tests/_WF/MappingMode`, locale `Resources/Locale/en-US/_WF/MappingMode`,
prototypes `Resources/Prototypes/_WF/MappingMode`). It builds on `MappingTools` (the editor, undo, selection, z-stacks)
and must not duplicate it; edits inside `MappingTools` are allowed since both are Wolfgate modules.

## Decisions

### Mode

- `wf.mapping_mode.enabled` (bool, `SERVER | REPLICATED`, default false). The client reads it to pick the lobby and
  escape-menu layout. Other cvars, all `SERVERONLY`: `autosave_interval` (seconds, default 120), `max_versions`
  (per map, default 25), `max_maps` (per player, default 50), `max_members` (per session, default 8),
  `empty_session_grace` (seconds a session with no members survives, default 300).
- While enabled the server forces `game.lobbyenabled true`, `events.enabled false`, `shuttle.auto_call_time 0`,
  and keeps `GameTicker.PauseStart(true)` asserted (re-asserted after any round restart). Late join is disallowed
  through `RefreshLateJoinAllowedEvent`. Ready toggles are ignored. The ticker stays in `PreRoundLobby`; nobody can
  join a round. `GameTicker.StartRound` and `LoadMaps` refuse while the mode is on (`startround`, the lobby button
  and the server API included), so no game map or preset rule ever builds the sector. `restartround` is not blocked;
  a restart ends every session after a best-effort autosave.
- Everything is native: no admin flag is needed by a mapper. Admins keep their normal tools on top.

### Admins

- Admins keep everything they have: `aghost`, `tp`, `tpto`, verbs, the admin menu, VV, `mapping` and every
  MappingTools window. The permission predicates treat an admin with `AdminFlags.Mapping` as allowed on any map,
  and the registry never limits them. A mapper's observer appears in the admin player list, and `tpto <name>`
  reaches it, so an admin can go to any session's map to fix a problem.
- The lobby shows an `Observe` button only to admins (the existing `ObserveButton` of `LobbyGui`, kept visible when
  `IClientAdminManager.HasFlag(AdminFlags.Admin)`; everyone else never sees it). Observing spawns the normal admin
  ghost on a private admin map, not in any session; from there admins use `tpto` or the sessions list.
- Admin console commands (`AdminFlags.Mapping`): `wf_mappingmode_list` (sessions, codes, owners, members, maps,
  dirty, last save), `wf_mappingmode_tp <code>` (teleport to a session's root map), `wf_mappingmode_close <code>`
  (autosave to the owner's library, then close), `wf_mappingmode_kick <name>`, `wf_mappingmode_save <code>`.
  Each action is admin-logged.
- Admins can join a session with its code like anyone else, or be invited by the owner (`Invite admin` is not needed;
  an admin asks the owner for the code or teleports in).

### Nothing but mapping

- With the mode on the server loads no station, no sector, no planet, no POI and no vessel: the ticker's
  `game.map`/preset, `StationSystem`, the Wolfgate sector and encounter systems and anything that spawns grids at
  round start must be bypassed. Since rounds never start, nothing in `OnRoundStart`/`PostRoundStart` runs; the plan
  stage checks what runs at `PreRoundLobby` (lobby map loading, `GameTicker.LoadMaps` in `RoundFlow`, lobby
  background, Wolfgate round setup, sector control, NPC crews, planet pre-generation) and names the hook that stops
  each one in the mode. The expected result: on boot with the mode on the server has zero maps until the first
  session is created, and the only maps that ever exist are session maps and the admin observer map.
- Round setup (`_WF/RoundSetup`), ship purchase, traders, the shipyard and the sector map are not reachable: their
  lobby controls are hidden, and their server handlers drop requests while the mode is on.

### Sessions

- A mapping session: `Id`, `Code`, `Name`, `Owner` (NetUserId), `Members` (NetUserId set, one observer entity each),
  `RootMap` (the ground level; a CE z-network may grow from it through the MappingTools Z-levels section), `Dirty`
  flag, `LastSaved`, `Source` (new, or `libraryMap`/`version` it was opened from).
- Codes: 6 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, unique among live sessions, shown only to members.
- A player is in at most one session. Creating one makes a fresh paused map (`CreateMap(runMapInit: false)`) with
  nothing on it; placing the first tile creates the grid, as the `mapping` command does. Opening a library map loads
  that version's level files into a new z-stack instead.
- Joining by code spawns the player's mapping observer on the session's root map at the origin and moves them
  into the game (`GameTicker.PlayerJoinGame`). Leaving despawns the observer and sends them back to the lobby
  (`GameTicker.Respawn` or the equivalent public path; verify). Disconnecting counts as leaving. The lobby lists
  sessions the player is still a member of so they can rejoin after a reconnect.
- When the owner leaves, ownership passes to the longest-present member. When the last member leaves the session
  survives for `empty_session_grace`, then autosaves if dirty and deletes its maps.
- The observer is a new prototype `WFMappingObserver`: `AdminObserver` without `Ghost`, so there is no warp menu
  and no return-to-body, and with a `WFMappingObserverComponent`. Verify what else `AdminObserver` relies on
  (visibility layer, eye, no-clip movement, sprite for other members) and keep only what mappers need. Mappers
  must have no client-initiated way to leave their session's maps.

### Permissions and anti-grief

- One predicate, `WFMappingModeSystem.CanMap(ICommonSession)` (server): true for a member of a live session, or
  for anyone holding `AdminFlags.Mapping` (unchanged behaviour outside the mode). A second predicate
  `CanEdit(ICommonSession, MapId)`: the map belongs to the player's session (root map or any level of its
  z-network), or the player is an admin with `Mapping`.
- Gates to route through them (every one is a marked edit unless it is inside `_WF`):
  - `AdminManager.CanAdminPlace` (engine placement, tiles and the eraser).
  - `DecalSystem` placement and removal requests (`AdminFlags.Mapping` check).
  - `MappingToolsSystem.CanUse`, `MappingMapsSystem.CanUse`, `MappingZLevelsSystem.CanUse` and the `Spawn`
    check in `MappingToolsSystem.History.cs`.
  - Every client-side gate the mapping action bar (`mapping_actions.yml`), the entity/tile/decal spawn menus and
    the eraser use (client `IClientAdminManager.CanAdminPlace()` or `HasFlag`). Enumerate them; the client must
    learn it is a mapper from a networked state, never from admin data.
- A guard in MappingMode watches `PlacementEntityEvent`, `PlacementTileEvent`, tile changes and decal requests from
  a mapper and reverts anything that lands on a map `CanEdit` refuses (delete the entity, restore the tile, drop the
  decal request) with a log line. MappingTools raises a local `WFMappingCanEditEvent(session, mapId)` before it
  applies any request (select, move, delete, paste, save, z-level changes); MappingMode cancels it when `CanEdit` is
  false. Outside the mode nothing cancels it.
- Other sessions' maps are out of PVS range, mappers have no teleport, and the chat they share is OOC. Admin
  commands remain admin-only (`mapping`, `tp`, `loadgrid`, ...).

### Isolation and hardening

The mode is open to the whole whitelisted population, so every rule is enforced on the server and nothing relies on
the client behaving. The toolset stays complete; the fences sit around it.

- Map registry: the server allocates every map a session uses (root and z-levels) and records `MapId -> session`.
  A mapper never names a map; every request is resolved against the registry from the sender's session, and a
  request whose target resolves to no session or another session is dropped and logged. Z-levels are added only
  through the session (`Add above`/`Add below` of MappingTools go through `WFMappingCanEditEvent`, and the new level
  is registered at once); `max_levels` (3) per session. Levels are a CE z-network private to the session, so no two
  sessions ever share or stack over the same maps, and a session's maps are deleted together when it closes.
- Mappers get no admin data, no console permissions and no verbs: the observer has no hands, no pulling, no
  interaction, no examine-based leaks beyond its own map; the client receives `WFMappingSessionState` (in session,
  name, code, members, limits) and nothing about other sessions beyond the lobby's public list (name, owner,
  member count; never codes or maps).
- Request throttles per player, with a log line and a drop when exceeded: placement and tile requests (the engine
  already batches; guard count per tick, `max_edits_per_second` 60), MappingTools requests (10/s), save (1 per
  10 s), library list/preview (2/s), reference load (1 per 5 s), download (1 at a time), session create/join
  (1 per 3 s), kicks. Every string is length-checked (names 48, codes 6, notes 200, paths must be listed ones).
- Nothing a mapper sends is executed as a console command; every former `shell.ExecuteCommand` path (`aghost`,
  `tp`, `changecvar`, `mappingclientsidesetup`) is replaced with direct calls.
- Failures are contained: every save, load and download is wrapped so an exception is logged and reported to the
  player, never thrown into the tick; a session whose map fails to load is torn down cleanly.
- PVS: sessions are different maps, so other sessions' entities are never sent. Verify no system marks mapping maps
  or observers as PVS overrides (`PvsOverrideSystem`), and that local chat, emotes, pointing and the ghost/dead
  channels can't cross maps (observers aren't ghosts, so they get no dead chat and no ghost warp list).
- Kill switches: `wf.mapping_mode.enabled` off stops accepting sessions and returns everyone to the lobby after an
  autosave; admins can `wf_mappingmode_list`, `wf_mappingmode_close <code>` and `wf_mappingmode_kick` from the
  console (admin commands, `Mapping` flag) and every session action goes to the admin log.
- Round restart: on the ticker's pre-restart point (the plan stage names it), every dirty session autosaves before
  its maps are deleted.

### Library, versions, autosave

- Layout in the server data folder: `/Mapping/Library/<NetUserId>/<MapName>/v<NNNN>_<yyyyMMdd-HHmmss>_<kind>/z<D>.yml`,
  `kind` is `manual` or `auto`, `D` the z depth (0 = ground, as MappingTools' z-stack save). One folder per version;
  a single-level map has only `z0.yml`. Every level is saved as a whole map (`TrySaveMap`). The listing is derived
  from folder names; no index file.
- `MapName`: `^[A-Za-z0-9 _-]{1,48}$`, trimmed; the folder uses the same text. `NetUserId` is the GUID string.
- Save (manual): any member can save the session into their own library under a name (default: the session name);
  the next version number continues that map's sequence. Rate limit: one manual save per 10 s per player.
- Autosave: every `autosave_interval`, each dirty session is saved as `auto` into the current owner's library, under
  the session name. Pruning keeps `max_versions` per map, removing the oldest `auto` versions first and only then
  the oldest `manual` ones. `max_maps` caps a player's library; a save beyond it fails with a message.
- Open from library: from the lobby (any of the player's maps, any version). It creates a new session from that
  version. Other players' libraries are never loadable by mappers; game content loads only as a reference grid (below).
  The Maps window of MappingTools is hidden from non-admin mappers.
- Delete a map or a version from one's own library (confirm in the UI).

### Limits

All limits are server cvars under `wf.mapping_mode.*`, enforced on the server (the client only mirrors them in the
UI) and shown live in the Mapping window as a status line (`Grids 1/4 · Entities 2,310/25,000 · Area 256×256`).
Every refusal gives the mapper a popup with the limit it hit.

- Autosave interval: the session owner picks one of fixed steps in the Mapping window (Off, 1, 2, 5, 10, 15 min;
  default 2) and the server clamps it to `autosave_min_interval` (60 s) .. `autosave_max_interval` (900 s). Saves
  are only written when the session is dirty, at most one session is saved per server tick (a queue, so ten
  sessions due at once stagger over ten ticks), and sessions get a random initial offset so intervals don't line up.
- Grids: `grids_per_player` (6) grids per member, so a session's cap is `6 × members` (capped by `max_grids_per_session`,
  8). Placing a tile in empty space makes a grid; above the cap the guard deletes that grid and tells the player.
  Grid splits (erasing tiles until a grid breaks apart) are counted but not reverted; the mapper can't create more
  until they delete one. The Mapping window has a `Delete grid` action for the grid under the mapper (confirm).
  Reference loads count too and are refused above the cap.
- Entities per tile: `max_entities_per_tile` (30): the guard counts entities whose position rounds to the target
  tile on that grid (anchored or not) and reverts a placement at the cap; a paste or reference load that would
  push any tile over it is refused whole. Decals per tile: `max_decals_per_tile` (16), same treatment.
- Entities per session: `max_entities` (25,000) across all of a session's maps; placement, paste and reference load
  are refused at the cap. Counted from the session's maps on each check (cheap enough at this scale; cache per tick
  if profiling says otherwise).
- Map area: `map_size` (256 tiles, clamp 64..512): every grid tile must lie inside the square
  `[-size/2, size/2)` in map coordinates on each level. Tiles placed outside are reverted; a reference load or paste
  that would cross it is refused. The client draws the bound as an obvious dashed border overlay with corner labels
  (`256 × 256 map limit`) whenever the mapper is in a session, and tints the outside area.
- Reference load file size: `max_reference_bytes` (8 MiB) per file; bigger files are refused before loading.
- Library: `max_maps` (50) per player, `max_versions` (25) per map, `max_library_bytes` (256 MiB) per player
  (summed on save; a save that would exceed it fails until versions are deleted), one manual save per 10 s, one
  download at a time with a 32 MiB file cap.
- Sessions: `max_sessions` (32) live sessions, `max_members` (8) per session, names and codes as above.

### Reference grids

A mapper can load an existing grid onto their session's map to copy from or build against: any game ship grid
(`/SharedMaps`) or map (`/Maps`), or a level from their own library. `Load reference` in the Mapping window lists
them (reusing `MappingMapsSystem.ListFiles` and its preview read); picking one puts the client in a placement mode
that draws a ghost outline at the cursor, the grid's tile footprint and bounding box in a translucent colour, rotated
with R, confirmed with a click and cancelled with Escape, like the MappingTools paste preview. To know the footprint
the client loads the file into a client-side preview map the way `_WF/ShipPreview` does (negative MapIds) and reads
its tiles, discarding it afterwards; a map file's grids are placed together keeping their relative positions. On
confirm the server loads it at that position and rotation (`TryLoadGrid` with offset/rotation for a grid file, the
map-merge API for a map file; the plan stage names it), checks the grid, entity and area limits first, and the
loaded grids become ordinary editable grids of the session, counted against the caps. Nothing else is loadable:
other players' libraries and the server's `/Mapping` saves stay out of the list for non-admin mappers.

### Download

- A mapper asks for a version; the server streams each level file in `WFMappingDownloadChunkEvent`s (transfer id,
  file name, chunk index, chunk count, up to 256 KiB per chunk; 32 MiB cap per file, 1 transfer at a time per
  player). The client reassembles and writes to its user data folder,
  `/Mapping/<MapName>/v<NNNN>_<stamp>_<kind>/z<D>.yml`, then offers to open that folder (`UserData.OpenOsWindow`,
  the pattern `HumanoidProfileEditor` uses for exports). Put the reassembly logic in Shared as a pure class so a
  unit test can cover it.

### Client

- Lobby: when the mode is on, `WFMappingLobbyUIController` (a `UIController`, `IOnStateEntered<LobbyState>`)
  hides the ready/observe/character/round-setup controls of `LobbyGui` and inserts a `WFMappingLobbyPanel` into the
  lobby menu: New map (name, Create), Join (code, Join), My maps (name, version count, last saved; Open latest,
  Open version, Download, Delete), Your sessions (rejoin). Prefer injecting into the existing `LobbyGui` controls
  from the controller; a `LobbyGui.MappingMode.cs` partial in the module (upstream namespace) is acceptable if a hook
  is unavoidable.
- In game: a `Mapping` button in the escape menu (marked edits in `EscapeMenu.xaml`, `EscapeMenu.xaml.cs` and
  `EscapeUIController`, only visible in the mode) opens `WFMappingMenuWindow`: Session tab (name, code with copy,
  members, owner controls: kick; Leave), Save tab (name, Save now, last save, next autosave), Library tab (as the
  lobby's My maps, plus Download of the current session's latest version). No Load inside a session; opening
  another map goes through Leave then the lobby, or an Open that confirms leaving.
- Entering a session runs the client setup `mappingclientsidesetup` performs, from code rather than the console
  (markers visible, lights off, subfloor shown, `/mapping_actions.yml` assignments, `MappingToolsSystem.SetEnabled`),
  and leaving undoes it. The client keeps `WFMappingClientSystem` state (in session, session info, library list)
  fed by network events.
- Every string in Fluent under `Resources/Locale/en-US/_WF/MappingMode`.

### Tests

- `Content.Tests/_WF/MappingMode`: code generation alphabet and length, map-name validation, version folder name
  parse/format round trip, prune order, download chunk reassembly.
- `Content.IntegrationTests/Tests/_WF/MappingMode`: create/join/leave with codes and member limits; a non-member's
  placement on another session's map is reverted and `CanEdit` refuses it; save creates `v0001`, a second save
  `v0002`, autosave marks `auto`, pruning respects `max_versions`; open-from-library rebuilds the level count; the
  ticker stays in `PreRoundLobby` with the mode on. No `Destructive` pool settings.

## Open points settled by the plan stage

The plan stage verifies and records: the exact GameTicker calls for join and return to lobby; every client admin
gate on the mapping UI; what `AdminObserver` needs without `Ghost`; the CE z-network API for "all levels of this
session"; whether broadcast network events (`RequestDecalPlacementEvent`) may have a second subscriber and how to
order it; the engine's net message size limit for chunk sizing; an event fired before round restart deletes maps.

## Phase 2

Phase 2 adds the mapping screen (docked palette, properties, tools, layers, template and keys panels round the
viewport), seven SDMM-style build modes on keys 1 to 7, a PNG template underlay, a quick-select bar and map upload
into the library. Its verified facts, decisions and stages are in [PLAN2.md](PLAN2.md); the choices made while
building each stage are in `P1-NOTES.md`, `P2-NOTES.md` and `P3-NOTES.md`. Tests: `MappingToolsShapesTest`,
`WFMappingQuickSelectStoreTest`, `WFMappingTemplateSettingsTest` and `WFMappingUploadAssemblerTest` (unit),
`MappingToolsDrawTest`, `WFMappingUploadTest` and `WFMappingScreenTest` (integration, the last with the real client).
