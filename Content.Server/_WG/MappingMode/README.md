# MappingMode

A server-wide mode that turns the server into a shared mapping workshop. Rounds never start, the lobby becomes a
mapping menu, and every player gets the full MappingTools toolset on their own map without admin rights. Maps save into
a per-player library on the server, with versions and autosave, and can be downloaded to the player's computer.

Every grid on a session level sits on whole world tiles at a right angle, so levels line up: a sweep each tick
snaps any grid that isn't, whatever placed or loaded it. Tiles laid off every grid, by the spawn menu, Fill, Shape or
Paste, join the nearest grid on that level unless the mapper pressed New grid (`WGMappingModeSystem.WorldGrid.cs`).

A host turns it on with `wg.mapping_mode.enabled true` (replicated, off by default). It forces `game.lobbyenabled`
on, `events.enabled`, `vote.autovote_enabled`, `vote.map_enabled` and `vote.preset_enabled` off and
`shuttle.auto_call_time` to 0 and `physics.grid_splitting` off (put back when the mode turns off), holds the ticker in
the lobby and blocks late join. The other cvars are server only unless noted, all under `wg.mapping_mode.`:

| Cvar | Default | What it limits |
|---|---|---|
| `map_size` (replicated) | 256 | Side of the square, in tiles and centred on the origin, that tiles must lie in |
| `autosave_interval` | 120 | Seconds between autosaves of a dirty session, until the owner picks a step |
| `autosave_min_interval` / `autosave_max_interval` | 60 / 900 | Range the owner's autosave step is clamped to, in seconds |
| `max_versions` | 25 | Versions kept per library map |
| `max_maps` | 50 | Maps per player library |
| `max_library_bytes` | 256 MiB | Size of a player library |
| `max_sessions` | 32 | Live sessions on the server |
| `max_sessions_per_player` | 1 | Live sessions one player may own, empty ones in their grace included |
| `max_members` | 8 | Members per session, absent ones included |
| `max_levels` | 3 | Z-levels per session |
| `empty_session_grace` | 300 | Seconds a session whose members are all away survives; one with no members closes at once |
| `grids_per_player` / `max_grids_per_session` | 6 / 8 | Grids each member adds to the session cap, and the cap itself |
| `max_entities_per_tile` / `max_decals_per_tile` | 30 / 16 | Entities and decals on one tile |
| `max_entities` | 25000 | Entities across all of a session's maps |
| `max_reference_bytes` | 1.5 MiB | Size of a file loaded as a reference grid; admits every ship, refuses stations |
| `reference_cooldown` | 2 | Seconds after any reference load before anyone on the server may load another |
| `max_upload_bytes` (replicated) | 1.5 MiB | Size of an uploaded map file; clamped to a fixed 8 MiB |
| `upload_cooldown` | 30 | Seconds after an accepted upload before the same player may start another |
| `upload_confirm_seconds` | 120 | Seconds an upload with unknown prototypes waits for Load anyway before it is dropped |
| `template_max_bytes` (replicated) | 8 MiB | Size of a PNG template shared with a session; clamped to a fixed 8 MiB |
| `template_cooldown` | 10 | Seconds after a share starts before the same player may share another template |
| `max_edits_per_second` | 60 | Placement, tile, erase and decal requests per second per mapper |
| `max_selection_cells` | 4096 | Cells one tools selection or area erase may cover (a whole-grid delete is checked by entities) |
| `max_edit_entities` | 2000 | Entities, contents included, one copy, paste, delete, area erase or undo step may touch |
| `max_edit_decals` | 4096 | Decals one copy or paste may carry |
| `max_history` / `max_history_bytes` | 30 / 16 MiB | Undo steps per mapper, and their estimated saved size; the oldest go first |
| `test_range` | `/Maps/_NF/POI/cargodepot.yml` | Grid file placed 60 tiles off the ship in a live test, to fly and dock at; empty is space only |
| `test_max_minutes` | 30 | Minutes a live test runs before everyone is sent back to mapping |
| `test_cooldown` | 10 | Seconds after a session's test ends before the next may start |
| `test_global_cooldown` | 5 | Seconds after any test starts before the server builds another |
| `test_max_running` | 8 | Tests that may run at once across every session |

Players see a mapping panel in the lobby instead of the ready button. New map takes a name and makes a session with a
fresh map and a six character join code. Join takes a code. Your sessions lists the ones you can rejoin after a
reconnect, and My maps lists the library: Open starts a new session from a version, Export opens the export wizard on
it, the smaller Download copies its plain level files to the client, Delete asks for a second click. In game, the
Mapping entry of the escape menu
opens the Mapping window. Its Session tab shows the name, join code (Copy, and New code for the owner, which revokes
the old one), the live limits, members (the owner can remove them for good and set the autosave step), Reset layout
(the mode bar) and Leave. Grids lists the grids on your level (tiles, entities and Go to, with Next grid to cycle
through them largest first) and Delete the grid under me. Reference lists the files you can place and previews the
picked one's floor plan, read from its yaml without loading it (`WGMappingPreviewParser`). Save saves into your own library under a name (blank uses the
session name), and Export... opens the export wizard on the newest saved version of that name; Library lists, exports,
downloads and deletes. Opening a map, rejoining and placing a reference put you at the centre of the largest grid
instead of the origin.

The export wizard (`WGMappingExportWizard`, opened from the Save tab, a library row's Export or the lobby's library)
packages a library version as one zip laid out like the repository, so it is unzipped on the repository root. Three
steps, with Back and Next:

- Check runs the issues checker (`WGMappingIssuesSystem`, `WGMappingIssuesRequest`) on the session for the chosen kind,
  Ship, Map or Grid (a grid runs the map rules, with the map lint taking it for a point of interest), and lists what it found with the severity, the title, the fix hint and Go to, which moves the observer
  to the entity or tile (`WGMappingGoToIssueRequest`). Export as ship is off while an error stands, and the hint says
  to fix them or export as a map; Check again re-runs it. The server answers one check per three seconds and says
  so (`WGMappingIssuesRefusedEvent`) rather than staying silent; the client holds a check asked inside that gap
  (`WGMappingIssuesScheduler`, the latest scope only) and sends it when the gap has passed, and a check with no
  answer after six seconds gives up with Check again enabled. From a library row outside the map's session nothing
  can be checked, and the wizard says so.
- Details, for a ship, is the vessel form: name (empty uses the map name; the id and file names preview live), the
  description (up to 2000 characters), the price (empty takes the generated one; the hint shows the appraisal, the
  shipyard floor and the suggestion), size, shipyard group, classes (none ticked takes the scan's), engines (from the
  grid or picked), access and requires crew. A map export skips this step.
- Package names the files the zip will hold, picks Download (into `Mapping/<map>/<version>/<map>.zip` of the client
  data folder, with Open folder) or Save to... (one save dialog for the one zip), shows the progress row and, from the
  Save tab with unsaved changes, saves a version first and exports that.

The server (`WGMappingExportSystem`, `WGMappingExportRequest`, one export per five seconds per player, the download
cap of 32 MiB on the zip) builds the zip in memory with `System.IO.Compression`. A ship (a version holding one grid on
one level; refused otherwise, saying why) holds `Resources/SharedMaps/_WG/Community/<file>.yml` (the grid file),
`Resources/Prototypes/_WG/Community/<file>.yml` (the vessel and its gameMap, generated by `WGMappingVesselYaml` with
the mapper's details over the generator's guesses, the header saying which is which) and `README.txt`. A map holds
`Resources/Maps/_WG/Community/<file>.yml` for one level, or `Resources/Maps/_WG/Community/<file>/z<D>.yml` per level,
a `gameMap` with a plain station or a `zMap` listing the levels bottom-up (the ground level's index in a comment), and
the README. A grid export (for POIs and other grids that aren't ships) is the version's one grid file as saved, sent
bare as `<file>.yml` under the ship grid's name, with no prototype, README or zip, and refused for several grids or none
the way a ship is. The README says what the package is, where to unzip it, what each file is, what to check (guessed fields,
the price against the appraisal, an id already in the game) and the issues report at export time by severity, or that
the session wasn't checked. A ship export in the map's session is refused while an error stands, the price in the details counted
against the appraisal floor. The check only runs when the version is the map's newest and the session has no changes
since, so the live grids are the version's; outside the session, from an older version or with unsaved changes nothing
is checked and the README says so.

Every export also runs the map lint on the version's saved files, in or out of the session, and is refused when it
fails. `WGMappingMapLint` holds the prototype rules of the CI map checker (`.github/mapchecker/config.py`, to be kept in
step by hand): the always-illegal matches and the DoNotMap category for every kind, plus the point-of-interest-only ones
for a ship or map but not a grid. The live check reports the same rule as `map-lint`.

The details are kept as `ship.yml` in the version folder, sanitized (name alphabet, description length, price range,
known enum values, lists deduplicated and capped at 8), sent with the library listing so the wizard pre-fills, copied
into every later save of the map and applied to its generated vessel file. A save also keeps the generator's reading
of each single grid as `z<D>.scan.yml` beside the grid file, so an export outside the session still has the appraisal
and the engines to generate from; a version saved before that is exported from an empty reading and the README says
so. The old ship download (`WGMappingDownloadRequest` with `ship: true`) still works but has no button.

Live testing is the step before the export: Test map in the menu bar (`WGMappingTestStartRequest`) snapshots the
session's levels with the library's save code into a scratch folder, loads the copy back onto fresh maps registered to
the session as test maps (a CE z-network when there are several), runs `FixAtmos` on the copy, gives the ship grid
what the shipyard gives a purchased one (`ShuttleComponent`, IFF, the session's name) and places the test range named
by `test_range` 60 tiles east of it, then initializes and unpauses the maps so power, atmos, doors and gravity run. The
mapper is spawned as their selected character (`StationSpawningSystem.SpawnPlayerMob`) at a spawn point on the ship,
else the tile in front of the first shuttle console, else the free floor tile nearest the grid's centre, and their mind
moves across; the observer is parked in nullspace with its place recorded. The tester carries
`WGMappingTesterComponent`: the protection system still shields them, the guard refuses them every edit
(`wg-mapping-test-no-edit`) and so do the engine placement hook and the decal gate, and the server refuses a check, a
save, Go to and an export from them (`wg-mapping-export-while-testing`), which the wizard shows greyed with that hint.
Back to mapping (the banner's button, the `WGMappingTestReturn` key when bound, or `WGMappingTestStopRequest`),
death (3 s later), a disconnect, leaving or closing the session, the owner ending the test for everyone or
`test_max_minutes` put the player back on the observer where they left it; the test maps are deleted once nobody is
in them, and the scratch folder right after the load, so nothing of a test is kept. One test per session: another
member's Test map joins it at the same spot, the button's tooltip names the testers and the Session tab shows who is
testing and the time left. A test is refused while the session is over 60% of `max_entities` (the copy counts against
it, so the limits line shows the doubled count), during `test_cooldown` or the server-wide `test_global_cooldown`,
while `test_max_running` tests already run, and while an export or download is still coming in. Test maps are never session maps: saves, autosaves and the export skip them. A tester's own save, check,
Go to and export are refused because they act on the live levels the tester has left; the autosave and the other
members' saves keep running, since they read only the live levels and never the copy. The copy is kept from reaching
past the test: its ships can't FTL (`wg-mapping-test-no-ftl`; FTL space is shared and other maps may take arrivals),
radio doesn't cross between a test's levels and any other map, comms consoles lose their server-wide announcement,
and the character is spawned without the profile's persistent items and components, which spawning would otherwise
use up from the real character.

Downloads land in the client's `Mapping/<map>/<version>/` data folder, with an Open folder button. "Downloads go to"
beside the library, in the Library tab and in the wizard's Package step switches to Save to..., which asks where to
save each file through the game's save dialog, the row naming the file it asks for (an export is one zip, so one
dialog). The dialog doesn't add the extension and the client can't see or fix the name it was given, so the row and
the Package step's hint say to type the whole name, `.zip` or `.yml` included. The choice is kept in the client cvar `wg.mapping_mode.download_save_to`. A folder can't be picked once for
every download: the client sandbox only writes outside its data folder through a stream the save dialog hands back,
one file per dialog, and the engine's dialog takes no starting folder or file name. The plain Download of a map of one
level arrives as `<map>.yml`: its grid file when the level holds exactly one grid, so it loads straight into the game
as a ship, else the map file. A map of several levels arrives as `<map>_z<D>.yml` per level. `<file>` in the export's
paths is the ship or map name in lower case with `_` between words, and the id is `WG` and the name in PascalCase.

Reference grids are game ships (`/SharedMaps`), game maps (`/Maps`) or a level of your own library, loaded onto your
session's map to copy from. Pick one, move the outline, R turns it, click puts it down, Escape or right click drops
it. They count against the grid, entity and area limits like anything else.

Upload, in the lobby panel and the Mapping window's Library tab, sends a `.yml` map file from the player's computer
into their library as a new `upload` version, named after the file. The server refuses anchors and aliases and
anything over the caps of a new session, strips denied components, and stores its own re-save of the map; the raw file
never touches disk. A file using prototypes this server lacks is held unloaded while the upload row warns how many
prototypes and entities that is, naming up to 25; Load anyway stores it without those entities and anything parented
to them, Cancel or `upload_confirm_seconds` without an answer drops it. Open then starts a session from it.

In a session the player gets the mapping screen instead of the game layout. The palette (Entities, Tiles and Decals
tabs, with search, favourites, recents and a strip of the last 12 entries; the Decals tab also has the decal placer's
colour sliders, colour palettes, rotation, snap, cleanable and z-index) is docked on the left; the viewport sits in
the middle with the mode bar, the quick-select bar at the bottom and the cursor readout bottom left; the
Properties, Tools, Layers, Template and Keys tabs sit over the chat on the right. Each docked panel has an Undock
button that moves it into its own window, which the window pop-out can take out of the game; closing the window docks
it again. Once every panel of a column is undocked (the palette on the left, all five tabs on the right) that column
collapses and the viewport takes its width; while the right column is gone the chat floats at the viewport's top
right. Docking any of its panels brings the column back. `ui.layout` is ignored while in a session and back in force on
leaving.

The menu bar over the viewport keeps only what helps while mapping: the escape menu, the guidebook, the admin and
sandbox buttons for those who have them, the bug report and AHelp. The gameplay buttons (sector map, character, crew
setup, emotes, crafting, actions, language, and the consent cog or anatomy panel) are hidden and their keys do nothing.
Mapping (map icon) opens and closes the Mapping window and shows pressed while it is open. Quicksave (floppy icon,
Ctrl+S) saves the session to your own library under the session name in one click, as Save with a blank name does;
the server's answer shows as a popup, and the button stays off for 10 seconds, the server's save throttle. Test map
(play icon) starts the live test or joins the running one, and waits while a download is coming in. Export (tray icon)
opens the export wizard on the session's name, as Export... in the Save tab does with a blank name. The escape menu's
Mapping entry stays. The other screens' menu bars are untouched.

The mode bar starts bottom left, above the quick bar and the readout and below the menu bar, lying flat when the
viewport is wide enough and standing upright otherwise. Its grab handle drags it anywhere over the viewport; let go
near an edge and it snaps there, standing upright on the left or right edge alone and lying flat on the top or bottom
edge alone. Its place is kept in `Mapping/screen.yml` of the client data folder and restored with the screen; Reset
layout in the Session tab puts it back and forgets the file.

Build modes, on keys 1 to 9 or the mode bar:

- Place (1): left click places the selected entry as the engine does, right click erases the entity or tile under the
  cursor (a decal entry leaves it to the decal eraser), middle click eyedrops, Ctrl+wheel or R turns it, Escape
  drops the entry. What a map file leaves out, such as a mob, still places, but a popup and status line say it won't
  be saved (once per prototype) and name the spawner to use; the palette marks it, and its right-click menu and the
  Properties panel offer Use spawner.
- Erase (2): click or drag a rectangle to remove what its sub-choice in Properties says: Everything (the default:
  entities, decals and tiles; a click takes the entity under the cursor, or else the tile), Entities, Tiles or Decals.
  A click takes the entity hit exactly, else the one sprite whose bounds hold the cursor (so a big sprite like a
  2x2 machine is hit anywhere on it, not only on its own cell); where several overlap it opens Delete under cursor, a
  list of them by name and prototype, which a right click opens at any time. A drag may start in space: on a grid it
  removes the entities whose position is in its cells, off every grid the loose entities whose bounds centre is in
  the box, as one undo step. Players, ghosts and observers are never erased. The cursor readout names the current
  target.
- Select (3): the MappingTools selection. A click on an entity selects it; a click on a tile with no entity there
  selects that cell with everything on it. Shift+click adds or takes out an entity or a cell (a cell taken out of a
  dragged box splits the box into single cells, up to 4096). A box drag selects every tile in it and may start in space;
  it lands on the first grid it reaches. Move, rotate, mirror, delete, copy, cut and paste carry the cells' tiles.
- Fill (4): flood-fills the tile under the click with the selected tile, up to 4096 cells.
- Shape (5): drag a line, rectangle or circle (the ellipse fitted to the dragged box) of the selected tile or entity,
  with a preview; Fill or Border in Properties, or a right click with nothing held, picks whether rectangles and circles
  are whole or only their edge. Shift keeps them square or round, Ctrl+wheel turns entities. The shape goes on the
  grid under the drag's start, else under its end, else a grid whose tiles touch or overlap the box, and the preview
  snaps to that grid's cells; tiles past its edge grow it. With no grid near, a tile shape makes a new grid (counted
  against the grid cap). Entity shapes need a grid and skip cells with no floor, saying how many.
- Pick (6): the eyedropper; the pick becomes the entry and Place comes back.
- Template (7): drag moves the template, Ctrl+drag scales it, Shift+drag turns it, the wheel scales it and the arrows
  nudge it by a pixel (eight with Shift).
- Pipe (8): drag a path over a grid's floor and plain gas pipes are laid along it: halves at loose ends, straights,
  bends, Ts and four-ways where the path and the pipes around it meet. Properties picks the colour (Distro, Waste, Air
  and Mix as the spray painter has them, a Custom hex, or Auto to match the first pipe the path joins), the layer (1 to
  3), Auto-join (connect to pipes pointing at the path, and turn the pipe past either end into a T or four-way) and
  Replace (rebuild a plain pipe on the path with the new connections, or leave it). A single click lays a half: towards
  a pipe beside it with Auto-join, else towards the facing, which R or Ctrl+wheel turns (north to start). A draw that
  skips cells says how many and why (pipes kept with Replace off, devices in the way); one that changes nothing says
  that instead. Middle click, or Paint a network then a left click, paints the clicked pipe and everything connected to
  it on its layer. Properties' Atmos devices box (vents, scrubbers, ports, injectors, pumps, valves, filters, mixers,
  thermomachines, radiators, sensors and the half as a cap) arms a device: a left click places it on the hovered cell
  on the current layer and in the current colour, turned so its ports meet the pipes around it when Auto-join is on
  (else to the facing), and a plain pipe beside it gains the side it needs. With Replace on it swaps a plain pipe on the
  cell; right click or Escape goes back to drawing. Each draw, paint or device is one undo step.
- Link (9): wires a map up without hands, so it works once downloaded. Three tools, picked in Properties or cycled with
  Tab:
  - Signals: click a signal source (button, lever, sensor), then a sink (door, light, machine); a window lists the
    source's ports against the sink's as checkboxes, with Link defaults (the game's default pairs) and Unlink all, and
    Apply sets exactly the ticked pairs. Right click a sink unlinks it from the source. The source's links are drawn
    while it is selected. The game's own checks apply (the source's range, link refusals).
  - Devices: click a device list (air alarm, fire alarm, sensor console), then click network devices to add or take
    them out, or drag a box to add every one in it. Devices must have a device network, be on the same map and not be
    the list itself, as the network configurator asks; the list's own limit (32) holds. Lines go to its devices.
  - Access: click a door or other reader to open a window of access levels grouped by access group; Apply sets the
    reader to accept any one of the ticked levels (none ticked opens it to all), Apply to selection sets every reader in
    the MappingTools selection, Copy from last ticks what was applied last. An airlock's access lives in its
    electronics, which only appear at map init; until then its own reader is set and marked, and map init copies it
    across.
  - Alarms: click a vent, scrubber, sensor or air alarm to open its alarm thresholds (temperature, pressure, gases) in
    the air alarm's own threshold controls, with a preset picker per kind. An air alarm stands for every monitor it
    lists. Apply stores the values on each monitor, so they save with the map and map init keeps them over the
    prototype's (a freezer vent in an engine room no longer alarms above 268 K); a monitor only takes the kinds it
    already checks. Apply to selection sets every monitor and listed device in the MappingTools selection.

  Each change is one undo step. Escape drops the selected source or list. The Links layer draws every signal link
  (amber) and device list (blue) on the level.

Right click or Escape drops a drag; Escape with nothing to drop goes back to Place. The wheel zooms whenever a mode
doesn't take it.

The template is a PNG drawn under the grids (or over everything) at a chosen position, scale, rotation and opacity,
on the level it was loaded on, for tracing. It is refused over 16 MiB, 8192 pixels a side or 4096² pixels. A player's
own template lives on their computer, in `Mapping/Templates/<session name>/` of the client data folder with its
settings, and comes back whenever a session of that name is entered. Share with session sends it (up to
`template_max_bytes`) to the server, which checks the PNG header against the same caps without decoding it and sends
it to every member present, now or later; the latest share replaces the one before, and Unshare removes it for
everyone. While a template is shared it is the one every member sees, "Shared by" its sharer, in place of their own:
its position, scale, pixels per tile, rotation, layer and level are session state any member moves, and the server
clamps; opacity, show and lock stay with each player. A save writes it into the version as `template.png` and
`template.yml` (counted in the library size), and opening the version shares it again; downloads and uploads leave it
out.

The quick-select bar holds named sets of pages of ten slots. Click a slot to place it, drag to swap, right click to
rename, remove or move it to another page; the palette's star and menu add entries, and so can every eyedropper pick.
The first set is filled from `/mapping_actions.yml`. It is stored in `Mapping/quickselect.yml` of the client data
folder; an unreadable file is kept as `quickselect.bad.yml` and the defaults are used.

Every limit is enforced on the server. A refusal reaches the player as a cursor popup in game and a status line in the
Mapping window or lobby, naming the limit. Placement past a limit is reverted or dropped, a paste or reference load
that would cross one is refused whole, and requests over their rate are dropped. A save past `max_maps`, or into a
library over `max_library_bytes`, is refused until something is deleted.

Admins keep everything they have. An admin with the Mapping flag may edit any map and is never limited. Admins see an
Observe button in the lobby, which puts them on a private admin map (Return to lobby in the Mapping window brings
them back); from there `tpto <name>` reaches any mapper. Console commands, all `AdminFlags.Mapping` and admin logged:
`wg_mappingmode_list` (sessions with code, owner, members, maps, unsaved state, last save, a running test and its
testers), `wg_mappingmode_tp <code>`,
`wg_mappingmode_close <code>` (autosaves, then closes), `wg_mappingmode_kick <player>`, `wg_mappingmode_save <code>`
and `wg_mappingmode_purge <code> <prototype>` (deletes every instance of a prototype on the session's maps, for what
slipped past the limits; players, ghosts and observers stay).
Turning the cvar off closes every session after an autosave and sends everyone back to the lobby.

The guidebook has a mapping guide for players: `WGMappingGuide` and its children in
`Resources/Prototypes/_WG/MappingMode/guidebook.yml`, with the pages in `Resources/ServerInfo/_WG/MappingMode/Guidebook`.
The lobby panel's Guide button, the Keys tab's Guide button and the Mapping window's ? open it showing only its pages
(`WGMappingGuideLauncher`). While the mode is on it sets `server.default_guide` to the guide, so the menu bar's
guidebook button and key open there unless the player last read another page; turning the mode off puts the default
back. Keep the pages in step with the keys and labels when they change.

Entry points: `WGMappingModeSystem` (server; sessions, registry, guard, limits, reference loads, autosave and admin
parts), `WGMappingLibrarySystem` (library, versions, open, delete), `WGMappingDownloadSystem` (chunked downloads),
`WGMappingModeCommands` (console), `WGMappingObserverSystem` (shared; what an observer can't do),
`MappingProtectionSystem` (MappingTools; who no edit touches),
`WGMappingClientSystem` (client state and setup), `WGMappingLobbyUIController` and `WGMappingLobbyPanel` (lobby),
`WGMappingMenuUIController` and `WGMappingMenuWindow` (Mapping window), `WGMappingReferenceSystem` (reference
placement), `WGMappingBoundsOverlay` (map limit border), `WGMappingUploadSystem` and `WGMappingUploadValidator`
(server upload), `WGMappingVesselSystem` and the pure `WGMappingVesselYaml` (server; the vessel prototype written
beside a grid file), `WGMappingUploadClientSystem` (client upload), `WGMappingTemplateShareSystem` (server; the
template shared with a session), `WGMappingIssuesSystem` (server; the checker, `Issues/`), `WGMappingExportSystem`
and the pure `WGMappingExportPackage` (server; the zip, `Export/`) with `WGMappingExportUIController` and
`WGMappingExportWizard` (client), `WGMappingTestSystem` (server; the live test, `Test/`) with
`WGMappingTestClientSystem` and `WGMappingTestBanner` (client). The mapping screen is `WGMappingScreen` and
`WGMappingScreenUIController` (`Screen/`, with the mode bar `WGMappingModeBar` and its saved place
`WGMappingScreenLayout`), the modes `WGMappingBuildModeSystem` and its handlers (`Modes/`), the
palette `WGMappingPalette` (`Palette/`), the template `WGMappingTemplateSystem` (`Template/`) and quick select
`WGMappingQuickSelectSystem` (`QuickSelect/`) and Link mode's `WGMappingLinkSystem` (`Link/`), all client; Link mode's
requests are handled in MappingTools (`MappingToolsSystem.Links.cs`). Cvars are in `WGMappingModeCVars` and key functions in
`WGMappingKeyFunctions`. The `WGMappingObserver` prototype is in `Resources/Prototypes/_WG/MappingMode`.

## Design notes

- Sessions and registry: a session (`WGMappingSession`) has an id, a code from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, an
  owner, members (one observer each; absent members keep their place) and its levels. A player is in one session at a
  time; create, join and open end the old membership first. The owner passes to the longest-present member when they
  leave. A session whose last member leaves or is kicked closes at once, autosaving if dirty; one whose members are
  all away closes after `empty_session_grace`. A kicked player can't join that session again, and the owner can
  replace the join code. The server makes every map a
  session uses and records map to session in a registry (`WGMappingSessionMapComponent` mirrors it); a request is
  checked against the sender's session, never against a map the client names. Z-levels are a CE z-network private to
  the session and are registered when added.
- Permissions: `CanMap(session)` is true for a present member or a Mapping admin, and `CanEdit(session, map)` for a
  level of the player's session or any map for a Mapping admin. MappingTools asks through `WGMappingCanUseEvent` and
  `WGMappingCanEditEvent` (`WGMappingPermissionEvents.cs`), which nothing cancels outside the mode. Engine placement
  goes through the placement check in `SandboxSystem`, which calls `WGMappingModeSystem.AllowPlacement` for
  non-admins. Decal requests have their own handlers, ordered between MappingTools and the decal system. The client
  learns it is mapping from `WGMappingObserverComponent` on its entity, never from admin data.
- Guard: the placement gate refuses off-session and over-limit requests before the engine acts. A backstop on
  `PlacementEntityEvent` and `PlacementTileEvent` deletes an entity or reverts a tile that still landed off the
  session's maps, and deletes a grid made past the cap. Erasures can't be reverted there: the engine deletes the
  entity on the line after the event, so the gate is the only place to refuse them. The engine's area erase
  (`RequestRectRemove`) is refused for mappers outright, since it takes everything in its box but actors; Erase mode
  sends `MappingDrawOp.EraseEntities` instead. Admins keep the engine's.
- Protected entities: `MappingProtectionSystem` (in MappingTools) is the one rule for what no edit touches: a player
  (`ActorComponent`), a ghost (admin ghosts included), a mapping observer with or without its player, a body whose
  `MindContainer` holds a mind, and anything parented under one. Single erase, every MappingTools selection, area
  erase, pipe replacement, undo and redo, purge and the map entity are refused or skip them, and a grid, z-level or
  entity being deleted hands any of them on to the map, the next level or the grid underneath first.
- Deny set: a prototype or entity carrying a component that reaches past the session or its paused map is refused at
  placement, draw and paste (the clipboard's prototypes, contents included) and stripped at reference load, upload
  and library open, with a count shown. Always: `SingularityDistortion`, `Singularity`, `EventHorizon`,
  `GravityWell`, `PointManager`, `GameRule`, `StationEvent`, `StarSystemMap`, `AnnounceOnSpawn`, `GhostRole` and
  `GhostRoleMobSpawner`. With data: `ActiveRadio` with `globalReceive` or `receiveAllChannels`, `WarpPoint` with
  `follow` (Nar'Sie, Ratvar, the singularity, tesla ball, nuke, disk, rod, vehicles), and `SpawnOnDespawn` whose
  prototype is denied (Nar'Sie's spawn animation), followed four deep. Plain warp markers stay. Map markers with
  `GlobalPvs` (radar edge markers, biome sources) are ordinary ship content, so on a paused map in mapping mode they
  only lose their global PVS override; a real round adds it back. `R3-NOTES.md` has the reasons.
- Limits: the entity total is recounted at most once a second and grows by one per allowed placement; tool edits and
  reference loads recount. Per-tile counts take every entity whose world position falls in the cell, whatever its
  parent (container contents aren't counted), and apply to placements, pastes, moves and mirrors; mappers can't place
  entities off a grid. Pastes check decals per cell the same way. Undo and redo are checked against the entity and
  grid caps like a paste. Area is checked on the tile under a placement, or the AABB of a tool edit. Reference picks
  are refused by file size before loading, checked against the caps (counting other members' pending references)
  straight after, and one loads at a time per `reference_cooldown`. Throttles per player: edits as above, tool
  requests 10/s, create, join and open 1 per 3 s, manual save 1 per 10 s, kick 1/s, reference list 2/s, pick 1 per 5 s.
- Library on disk: `/Mapping/Library/<NetUserId>/<MapName>/v<NNNN>_<yyyyMMdd-HHmmss>_<manual|auto|upload>/z<D>.yml`
  in the server data folder, one file per level with 0 the ground. Every save (manual, auto or upload) also writes
  `z<D>.grid.yml` (`MapLoaderSystem.TrySaveGrid`) and `z<D>.vessel.yml` beside each level that holds exactly one grid;
  they count towards the library size. Manual saves and autosaves first run `fixgridatmos` on every session grid with
  an atmosphere, so saved tiles hold their roundstart air (markers respected); uploads are stored as sent. There is no index; listings are read from folder names, plus the `grids:` list
  at the head of each level file, and cached for 30 s. A version is listed as a ship when it holds exactly one grid in
  all and has that grid's file. Reference lists show level files only. Map names are 1 to 48 letters, digits, spaces, dashes or underscores, and not a Windows device
  name. Pruning keeps `max_versions`, removes `auto` versions before `manual` and `upload` ones and never the newest. Only the
  owner's manual save or an autosave clears the dirty flag, as autosaves go to the owner's library. Autosave steps
  are Off, 1, 2, 5, 10 and 15 minutes, clamped to the cvar range; at most one session is saved per tick, and the
  first check is offset at random so sessions don't line up. A round restart saves every dirty session at once.
- Download: the server sends each file as its own transfer in chunks of up to 256 KiB, two in flight, each acked in
  order; 32 MiB per file, one download at a time per player, dropped after 30 s without an ack. The client assembles
  with `WGMappingDownloadAssembler` (pure and shared, so a unit test can cover it), checks the map name, version
  folder and file name (`<map>.yml`, `<map>_z<D>.yml` or `<map>.vessel.yml`) before writing, and deletes what it wrote
  if a chunk is damaged or the server cancels. The server reads each file to its end rather than trusting its stream
  length, refuses an empty one (`wg-mapping-mode-download-empty`) and sends the file's size with every chunk; the
  client refuses chunks that don't add up to it, reads a written file back and drops the download when the size
  differs. Save to holds the files in memory until the last arrives, then opens one save dialog per file;
  `R5-NOTES.md` has the details.
- Vessel prototype: written at save time from the live grid, so the appraisal sees it as saved (map-uninitialised,
  so random container fills aren't counted). `id` is `WG` and the map name's words in PascalCase, `name` the map name,
  `description` a placeholder, `parent: BaseVessel`, `group: Shipyard`, `guidebookPage: null`, `shuttlePath`
  `/SharedMaps/_WG/Community/<file>.yml`, with a `gameMap` of the same id as the shipyard vessels have. `price` is
  the `PricingSystem.AppraiseGrid` value plus 25 %, rounded up to 500 below 20,000, 1,000 below 100,000 and 5,000
  above, at least 1,000. `category` goes by tiles: Micro under 50, Small under 200, Medium under 500, else Large
  (the Small, Medium and Large medians of the existing vessels are 132, 338 and 744 tiles). `class` is Cargo for a
  cargo pallet, Salvage for mining gear (ore processor, ore box, ore magnet, salvage magnet, pickaxe, mining drill)
  and Mercenary for an anchored gun, or Civilian when none of them is on the grid. `engine` comes from the prototype
  ids of what stands on the grid: AME controller, P.A.C.M.A.N.s (plasma, uranium, welding fuel, bananium), pinch
  generators, reactors, RTGs, APUs, solar panels, TEG, supermatter, tesla and singularity generators.
- Grids list and Go to: the client asks for the grids of its level every 3 s while the Session tab shows; the server
  answers for the level the observer is on if it is a session map, and moves the observer to a grid's bounds centre
  (`SetMapCoordinates`) only when the grid is on one of the player's session maps. Entity counts leave out players.
- Upload: 128 KiB chunks, two in flight, put together by the pure `WGMappingUploadAssembler`. Every begin is
  answered, and a refused one costs no cooldown. The file is scanned for anchors, aliases, nesting over 64 and more
  than one document, parsed, its prototype ids checked through the loader's renames, then loaded onto a paused scratch
  map under the shared reference load slot, stripped, checked against a new session's caps wherever it was sent from,
  saved with the engine and the scratch map deleted. Unknown prototypes hold the bytes in a pending entry
  (`PendingConfirmation`) and send `WGMappingUploadNeedsConfirmEvent`; `WGMappingUploadConfirmRequest` with
  `DropMissing` re-validates the bytes and runs the pure `WGMappingUploadValidator.StripMissing` before the load.
- Mapping screen: `GameplayState.LoadMainScreen` asks `WGMappingScreenUIController.WantsMappingScreen` first (a marked
  edit). Setup and teardown reload the screen while the gameplay state is up; the controller builds the docked panels
  on every load of the screen. The screen holds the widgets other controllers need (`MainViewport` in
  `ViewportContainer`, a plain `ChatBox`, `GameTopMenuBar`) and no hotbar. The mode keys are added to `common` only
  while a session is set up and win over the hotbar keys by priority (a marked block in `keybinds.yml`). A mode
  handler that doesn't claim a click lets it fall through to the reference placement, MappingTools and the placement
  manager; the wheel goes to the mode first and zooms when the mode doesn't take it.
- Draws: Fill, Shape and area erase (`EraseEntities`, `EraseDecals` and a Space `SetTiles`) send one `MappingToolsDrawEvent`, checked as a single tool edit
  (`max_selection_cells`, per-tile caps, denied and hidden prototypes) and undone as one step, instead of one
  placement per cell, which the edit throttle would cut short. A tile draw with no grid names the map and makes a grid
  there (`NewGrid` on the edit check), undone as one root. Every refusal pops up at the cursor and is logged at debug
  level on both ends.
- Observer: `WGMappingObserver` doesn't inherit `AdminObserver` or `MobObserverBase`, which bring hands, pulling,
  stripping, consoles, access, global radio and unrestricted examine. It is `Incorporeal` and `BaseMob` with the
  `aghost` input context. `WGMappingObserverSystem` cancels interaction, use, pickup, drop, emote, speak, throw,
  attack and pointing, and the marked `SharedVerbSystem` edit gives it no verbs unless its player has admin data. It isn't a ghost, so it has no warp
  list or dead chat, and `GhostAttemptHandleEvent` is handled so a mapper can't become one.
- Admin only: the `mapping` command, the Maps window of MappingTools (its client button needs `AdminFlags.Mapping`),
  anyone else's library, `tp`, `loadgrid` and the rest of the admin tools. Nothing a mapper sends is run as a console
  command.
- Nothing but mapping: the lobby hides ready, character, round setup, vote and observe (observe stays for admins),
  the game menu hides Character, Round setup and Sector map, and the round setup handler drops its requests. Nothing
  loads a station, sector, planet or vessel, because no round starts.
- Known gaps: Delete the grid under me goes through a MappingTools delete over the grid's tile box and refuses grids
  over 256 x 256 cells. Mobs (`BaseMob` is `save: false`) can be placed but never save, so they vanish from the
  library and an erased one can't be brought back by undo. The client can't tell the admin map from a session, so every admin
  sees the Mapping button. The decal window stays open when a mapper leaves. A planet file loaded as a reference makes
  the engine log an error and loads without its planet grid. Opening a large library map loads synchronously, with no
  size cap beyond `max_library_bytes`. A decal removal dropped by the edit throttle is still recorded in MappingTools
  history. The forced cvars aren't restored when the mode is turned off. The mapping screen, modes, template, quick
  bar, dock collapse and a tile click in Select are covered by a headless client test; the pop-out and drawing over a
  real viewport haven't been driven by mouse. Erase's Everything sends its entity, decal and tile parts as separate
  requests, so undo takes them back one at a time.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/MappingMode/AtmosphereSystem.WGMappingFix.cs`](AtmosphereSystem.WGMappingFix.cs)
- [`Content.Server/_WG/MappingMode/Commands/WGMappingModeCommands.cs`](Commands/WGMappingModeCommands.cs)
- [`Content.Server/_WG/MappingMode/Export/WGMappingExportPackage.cs`](Export/WGMappingExportPackage.cs)
- [`Content.Server/_WG/MappingMode/Export/WGMappingExportSystem.cs`](Export/WGMappingExportSystem.cs)
- [`Content.Server/_WG/MappingMode/GameTicker.MappingMode.cs`](GameTicker.MappingMode.cs)
- [`Content.Server/_WG/MappingMode/Issues/WGMappingIssuesSystem.cs`](Issues/WGMappingIssuesSystem.cs)
- [`Content.Server/_WG/MappingMode/Issues/WGMappingIssuesSystem.Map.cs`](Issues/WGMappingIssuesSystem.Map.cs)
- [`Content.Server/_WG/MappingMode/Issues/WGMappingIssuesSystem.Ship.cs`](Issues/WGMappingIssuesSystem.Ship.cs)
- [`Content.Server/_WG/MappingMode/Issues/WGMappingMapLint.cs`](Issues/WGMappingMapLint.cs)
- [`Content.Server/_WG/MappingMode/Test/WGMappingTest.cs`](Test/WGMappingTest.cs)
- [`Content.Server/_WG/MappingMode/Test/WGMappingTestSystem.cs`](Test/WGMappingTestSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingDownloadSystem.cs`](WGMappingDownloadSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingLibrarySystem.cs`](WGMappingLibrarySystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Admin.cs`](WGMappingModeSystem.Admin.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Autosave.cs`](WGMappingModeSystem.Autosave.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.cs`](WGMappingModeSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Grids.cs`](WGMappingModeSystem.Grids.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Guard.cs`](WGMappingModeSystem.Guard.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Limits.cs`](WGMappingModeSystem.Limits.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Reference.cs`](WGMappingModeSystem.Reference.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Registry.cs`](WGMappingModeSystem.Registry.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Riders.cs`](WGMappingModeSystem.Riders.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Sessions.cs`](WGMappingModeSystem.Sessions.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Spawners.cs`](WGMappingModeSystem.Spawners.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.Test.cs`](WGMappingModeSystem.Test.cs)
- [`Content.Server/_WG/MappingMode/WGMappingModeSystem.WorldGrid.cs`](WGMappingModeSystem.WorldGrid.cs)
- [`Content.Server/_WG/MappingMode/WGMappingPreviewParser.cs`](WGMappingPreviewParser.cs)
- [`Content.Server/_WG/MappingMode/WGMappingSession.cs`](WGMappingSession.cs)
- [`Content.Server/_WG/MappingMode/WGMappingSessionMapComponent.cs`](WGMappingSessionMapComponent.cs)
- [`Content.Server/_WG/MappingMode/WGMappingTemplateShareSystem.cs`](WGMappingTemplateShareSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingUploadSystem.cs`](WGMappingUploadSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingUploadValidator.cs`](WGMappingUploadValidator.cs)
- [`Content.Server/_WG/MappingMode/WGMappingVesselSystem.cs`](WGMappingVesselSystem.cs)
- [`Content.Server/_WG/MappingMode/WGMappingVesselYaml.cs`](WGMappingVesselYaml.cs)

### Shared

- [`Content.Shared/_WG/MappingMode/Export/WGMappingExportEvents.cs`](../../../Content.Shared/_WG/MappingMode/Export/WGMappingExportEvents.cs)
- [`Content.Shared/_WG/MappingMode/Export/WGMappingShipDetailsYaml.cs`](../../../Content.Shared/_WG/MappingMode/Export/WGMappingShipDetailsYaml.cs)
- [`Content.Shared/_WG/MappingMode/Export/WGMappingShipNaming.cs`](../../../Content.Shared/_WG/MappingMode/Export/WGMappingShipNaming.cs)
- [`Content.Shared/_WG/MappingMode/Issues/WGMappingIssueCatalog.cs`](../../../Content.Shared/_WG/MappingMode/Issues/WGMappingIssueCatalog.cs)
- [`Content.Shared/_WG/MappingMode/Issues/WGMappingIssuesEvents.cs`](../../../Content.Shared/_WG/MappingMode/Issues/WGMappingIssuesEvents.cs)
- [`Content.Shared/_WG/MappingMode/Test/WGMappingTesterComponent.cs`](../../../Content.Shared/_WG/MappingMode/Test/WGMappingTesterComponent.cs)
- [`Content.Shared/_WG/MappingMode/Test/WGMappingTestEvents.cs`](../../../Content.Shared/_WG/MappingMode/Test/WGMappingTestEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingAdminEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingAdminEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingDownloadAssembler.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingDownloadAssembler.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingFootprint.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingFootprint.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingGridEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingGridEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingGuide.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingGuide.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingKeyFunctions.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingKeyFunctions.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingLibraryEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingLibraryEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingLibraryNames.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingLibraryNames.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingModeCVars.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingModeCVars.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingModeEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingModeEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingObserverComponent.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingObserverComponent.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingObserverSystem.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingObserverSystem.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingPerspectiveComponent.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingPerspectiveComponent.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingPngHeader.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingPngHeader.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingPrune.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingPrune.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingReferenceEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingReferenceEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingSessionCodes.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingSessionCodes.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingSpawnerIndex.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingSpawnerIndex.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingTemplateCVars.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingTemplateCVars.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingTemplateEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingTemplateEvents.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingTemplateSettings.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingTemplateSettings.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingThrottle.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingThrottle.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingUploadAssembler.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingUploadAssembler.cs)
- [`Content.Shared/_WG/MappingMode/WGMappingUploadEvents.cs`](../../../Content.Shared/_WG/MappingMode/WGMappingUploadEvents.cs)

### Client

- [`Content.Client/_WG/MappingMode/Export/WGMappingClientSystem.Export.cs`](../../../Content.Client/_WG/MappingMode/Export/WGMappingClientSystem.Export.cs)
- [`Content.Client/_WG/MappingMode/Export/WGMappingExportUIController.cs`](../../../Content.Client/_WG/MappingMode/Export/WGMappingExportUIController.cs)
- [`Content.Client/_WG/MappingMode/Export/WGMappingExportWizard.xaml`](../../../Content.Client/_WG/MappingMode/Export/WGMappingExportWizard.xaml)
- [`Content.Client/_WG/MappingMode/Export/WGMappingExportWizard.xaml.cs`](../../../Content.Client/_WG/MappingMode/Export/WGMappingExportWizard.xaml.cs)
- [`Content.Client/_WG/MappingMode/Export/WGMappingIssuesScheduler.cs`](../../../Content.Client/_WG/MappingMode/Export/WGMappingIssuesScheduler.cs)
- [`Content.Client/_WG/MappingMode/Link/WGMappingAccessWindow.cs`](../../../Content.Client/_WG/MappingMode/Link/WGMappingAccessWindow.cs)
- [`Content.Client/_WG/MappingMode/Link/WGMappingLinkOverlay.cs`](../../../Content.Client/_WG/MappingMode/Link/WGMappingLinkOverlay.cs)
- [`Content.Client/_WG/MappingMode/Link/WGMappingLinkPortsWindow.cs`](../../../Content.Client/_WG/MappingMode/Link/WGMappingLinkPortsWindow.cs)
- [`Content.Client/_WG/MappingMode/Link/WGMappingLinkSystem.cs`](../../../Content.Client/_WG/MappingMode/Link/WGMappingLinkSystem.cs)
- [`Content.Client/_WG/MappingMode/Link/WGMappingThresholdWindow.cs`](../../../Content.Client/_WG/MappingMode/Link/WGMappingThresholdWindow.cs)
- [`Content.Client/_WG/MappingMode/LobbyGui.MappingMode.cs`](../../../Content.Client/_WG/MappingMode/LobbyGui.MappingMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/IWGMappingModeHandler.cs`](../../../Content.Client/_WG/MappingMode/Modes/IWGMappingModeHandler.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGAlignWorldTile.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGAlignWorldTile.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingBuildMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingBuildMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.PipeDevices.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.PipeDevices.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.Spawners.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.Spawners.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.Tools.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingBuildModeSystem.Tools.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingDragModeBase.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingDragModeBase.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingEraseMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingEraseMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingFillMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingFillMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingLinkMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingLinkMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingModeHandlerBase.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingModeHandlerBase.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingPickMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingPickMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingPipeDeviceOverlay.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingPipeDeviceOverlay.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingPipeMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingPipeMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingPlaceMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingPlaceMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingSelectMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingSelectMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingShapeMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingShapeMode.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingShapeOverlay.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingShapeOverlay.cs)
- [`Content.Client/_WG/MappingMode/Modes/WGMappingWrenchMode.cs`](../../../Content.Client/_WG/MappingMode/Modes/WGMappingWrenchMode.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingDecalPalette.xaml`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingDecalPalette.xaml)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingDecalPalette.xaml.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingDecalPalette.xaml.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingPalette.xaml`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingPalette.xaml)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingPalette.xaml.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingPalette.xaml.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingPaletteEntry.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingPaletteEntry.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingPaletteIcons.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingPaletteIcons.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingPaletteStore.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingPaletteStore.cs)
- [`Content.Client/_WG/MappingMode/Palette/WGMappingTilePalette.cs`](../../../Content.Client/_WG/MappingMode/Palette/WGMappingTilePalette.cs)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingCursorReadout.cs`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingCursorReadout.cs)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingKeysPanel.cs`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingKeysPanel.cs)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingLayersPanel.xaml`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingLayersPanel.xaml)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingLayersPanel.xaml.cs`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingLayersPanel.xaml.cs)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingPropertiesPanel.xaml`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingPropertiesPanel.xaml)
- [`Content.Client/_WG/MappingMode/Panels/WGMappingPropertiesPanel.xaml.cs`](../../../Content.Client/_WG/MappingMode/Panels/WGMappingPropertiesPanel.xaml.cs)
- [`Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickBar.cs`](../../../Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickBar.cs)
- [`Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSelectStore.cs`](../../../Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSelectStore.cs)
- [`Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSelectSystem.cs`](../../../Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSelectSystem.cs)
- [`Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSlot.cs`](../../../Content.Client/_WG/MappingMode/QuickSelect/WGMappingQuickSlot.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingDockSlot.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingDockSlot.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingDockWindow.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingDockWindow.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingModeBar.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingModeBar.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingScreen.xaml`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingScreen.xaml)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingScreen.xaml.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingScreen.xaml.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingScreenLayout.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingScreenLayout.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingScreenUIController.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingScreenUIController.cs)
- [`Content.Client/_WG/MappingMode/Screen/WGMappingScreenUIController.TopBar.cs`](../../../Content.Client/_WG/MappingMode/Screen/WGMappingScreenUIController.TopBar.cs)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplateMode.cs`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplateMode.cs)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplateOverlay.cs`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplateOverlay.cs)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplatePanel.xaml`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplatePanel.xaml)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplatePanel.xaml.cs`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplatePanel.xaml.cs)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplateSystem.cs`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplateSystem.cs)
- [`Content.Client/_WG/MappingMode/Template/WGMappingTemplateSystem.Share.cs`](../../../Content.Client/_WG/MappingMode/Template/WGMappingTemplateSystem.Share.cs)
- [`Content.Client/_WG/MappingMode/Test/WGMappingTestBanner.cs`](../../../Content.Client/_WG/MappingMode/Test/WGMappingTestBanner.cs)
- [`Content.Client/_WG/MappingMode/Test/WGMappingTestClientSystem.cs`](../../../Content.Client/_WG/MappingMode/Test/WGMappingTestClientSystem.cs)
- [`Content.Client/_WG/MappingMode/WGMappingBoundsOverlay.cs`](../../../Content.Client/_WG/MappingMode/WGMappingBoundsOverlay.cs)
- [`Content.Client/_WG/MappingMode/WGMappingClientSystem.cs`](../../../Content.Client/_WG/MappingMode/WGMappingClientSystem.cs)
- [`Content.Client/_WG/MappingMode/WGMappingClientSystem.Download.cs`](../../../Content.Client/_WG/MappingMode/WGMappingClientSystem.Download.cs)
- [`Content.Client/_WG/MappingMode/WGMappingClientSystem.Grids.cs`](../../../Content.Client/_WG/MappingMode/WGMappingClientSystem.Grids.cs)
- [`Content.Client/_WG/MappingMode/WGMappingClientSystem.Layers.cs`](../../../Content.Client/_WG/MappingMode/WGMappingClientSystem.Layers.cs)
- [`Content.Client/_WG/MappingMode/WGMappingClientSystem.WorldGrid.cs`](../../../Content.Client/_WG/MappingMode/WGMappingClientSystem.WorldGrid.cs)
- [`Content.Client/_WG/MappingMode/WGMappingDeletePreviewOverlay.cs`](../../../Content.Client/_WG/MappingMode/WGMappingDeletePreviewOverlay.cs)
- [`Content.Client/_WG/MappingMode/WGMappingDownloadRow.cs`](../../../Content.Client/_WG/MappingMode/WGMappingDownloadRow.cs)
- [`Content.Client/_WG/MappingMode/WGMappingDownloadTargetPicker.cs`](../../../Content.Client/_WG/MappingMode/WGMappingDownloadTargetPicker.cs)
- [`Content.Client/_WG/MappingMode/WGMappingGuideLauncher.cs`](../../../Content.Client/_WG/MappingMode/WGMappingGuideLauncher.cs)
- [`Content.Client/_WG/MappingMode/WGMappingLibraryList.cs`](../../../Content.Client/_WG/MappingMode/WGMappingLibraryList.cs)
- [`Content.Client/_WG/MappingMode/WGMappingLobbyPanel.xaml`](../../../Content.Client/_WG/MappingMode/WGMappingLobbyPanel.xaml)
- [`Content.Client/_WG/MappingMode/WGMappingLobbyPanel.xaml.cs`](../../../Content.Client/_WG/MappingMode/WGMappingLobbyPanel.xaml.cs)
- [`Content.Client/_WG/MappingMode/WGMappingLobbyUIController.cs`](../../../Content.Client/_WG/MappingMode/WGMappingLobbyUIController.cs)
- [`Content.Client/_WG/MappingMode/WGMappingMenuUIController.cs`](../../../Content.Client/_WG/MappingMode/WGMappingMenuUIController.cs)
- [`Content.Client/_WG/MappingMode/WGMappingMenuWindow.xaml`](../../../Content.Client/_WG/MappingMode/WGMappingMenuWindow.xaml)
- [`Content.Client/_WG/MappingMode/WGMappingMenuWindow.xaml.cs`](../../../Content.Client/_WG/MappingMode/WGMappingMenuWindow.xaml.cs)
- [`Content.Client/_WG/MappingMode/WGMappingReferenceOverlay.cs`](../../../Content.Client/_WG/MappingMode/WGMappingReferenceOverlay.cs)
- [`Content.Client/_WG/MappingMode/WGMappingReferencePreviewControl.cs`](../../../Content.Client/_WG/MappingMode/WGMappingReferencePreviewControl.cs)
- [`Content.Client/_WG/MappingMode/WGMappingReferenceSystem.cs`](../../../Content.Client/_WG/MappingMode/WGMappingReferenceSystem.cs)
- [`Content.Client/_WG/MappingMode/WGMappingUploadClientSystem.cs`](../../../Content.Client/_WG/MappingMode/WGMappingUploadClientSystem.cs)
- [`Content.Client/_WG/MappingMode/WGMappingUploadRow.cs`](../../../Content.Client/_WG/MappingMode/WGMappingUploadRow.cs)
- [`Content.Client/_WG/MappingMode/WGMappingWorldGridOverlay.cs`](../../../Content.Client/_WG/MappingMode/WGMappingWorldGridOverlay.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingAdminTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingAdminTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingAtmosFixTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingAtmosFixTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDenyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDenyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDockPopoutTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDockPopoutTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDownloadTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingDownloadTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingExportTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingExportTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGhostTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGhostTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGridDeleteTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGridDeleteTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGuideTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingGuideTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingIssuesTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingIssuesTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLibraryListingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLibraryListingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLibraryTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLibraryTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLimitsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLimitsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLinksLayerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingLinksLayerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingMenuLayoutTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingMenuLayoutTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingModeBarTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingModeBarTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingModeTestUtil.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingModeTestUtil.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingPermissionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingPermissionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingPlaceSpamTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingPlaceSpamTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingProtectionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingProtectionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingReferenceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingReferenceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingScreenTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingScreenTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingSessionTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingSessionTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingShipTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingShipTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTemplateShareTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTemplateShareTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTestModeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTestModeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTickerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingTickerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingToolToggleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingToolToggleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUnsavedTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUnsavedTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUploadClientTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUploadClientTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUploadTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingUploadTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingWorldGridTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingWorldGridTest.cs)
- [`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingWrenchTest.cs`](../../../Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingWrenchTest.cs)

### Unit tests

- [`Content.Tests/_WG/MappingMode/WGMappingDownloadAssemblerTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingDownloadAssemblerTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingExportPackageTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingExportPackageTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingFootprintTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingFootprintTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingIssueCatalogTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingIssueCatalogTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingIssuesSchedulerTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingIssuesSchedulerTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingLibraryNamesTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingLibraryNamesTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingPreviewParserTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingPreviewParserTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingPruneTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingPruneTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingQuickSelectStoreTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingQuickSelectStoreTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingScreenLayoutTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingScreenLayoutTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingSessionCodesTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingSessionCodesTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingSpawnerIndexTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingSpawnerIndexTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingTemplateSettingsTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingTemplateSettingsTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingThrottleTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingThrottleTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingUploadAssemblerTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingUploadAssemblerTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingUploadValidatorTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingUploadValidatorTest.cs)
- [`Content.Tests/_WG/MappingMode/WGMappingVesselYamlTest.cs`](../../../Content.Tests/_WG/MappingMode/WGMappingVesselYamlTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/MappingMode/guidebook.yml`](../../../Resources/Prototypes/_WG/MappingMode/guidebook.yml)
- [`Resources/Prototypes/_WG/MappingMode/observer.yml`](../../../Resources/Prototypes/_WG/MappingMode/observer.yml)

### Localization

- [`Resources/Locale/en-US/_WG/MappingMode/mapping-export.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-export.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-guide.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-guide.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-issues.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-issues.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-link.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-link.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-mode.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-mode.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-quickselect.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-quickselect.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-screen.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-screen.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-ship.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-ship.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-template.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-template.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-test.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-test.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-tools-mode.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-tools-mode.ftl)
- [`Resources/Locale/en-US/_WG/MappingMode/mapping-upload.ftl`](../../../Resources/Locale/en-US/_WG/MappingMode/mapping-upload.ftl)

### Textures

- [`Resources/Textures/_WG/MappingMode/Interface/attributions.yml`](../../../Resources/Textures/_WG/MappingMode/Interface/attributions.yml)
- [`Resources/Textures/_WG/MappingMode/Interface/export.png`](../../../Resources/Textures/_WG/MappingMode/Interface/export.png)
- [`Resources/Textures/_WG/MappingMode/Interface/mapping.png`](../../../Resources/Textures/_WG/MappingMode/Interface/mapping.png)
- [`Resources/Textures/_WG/MappingMode/Interface/quicksave.png`](../../../Resources/Textures/_WG/MappingMode/Interface/quicksave.png)
- [`Resources/Textures/_WG/MappingMode/Interface/test.png`](../../../Resources/Textures/_WG/MappingMode/Interface/test.png)

### Guidebook

- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Admins.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Admins.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/BuildModes.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/BuildModes.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/GettingStarted.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/GettingStarted.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Limits.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Limits.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/MappingServer.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/MappingServer.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Pipes.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Pipes.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Placing.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Placing.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Saving.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Saving.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Selecting.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Selecting.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Shapes.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Shapes.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Templates.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Templates.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Together.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Together.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/Wiring.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/Wiring.xml)
- [`Resources/ServerInfo/_WG/MappingMode/Guidebook/ZLevels.xml`](../../../Resources/ServerInfo/_WG/MappingMode/Guidebook/ZLevels.xml)

### Docs

- [`Docs/_WG/MappingMode/DESIGN.md`](../../../Docs/_WG/MappingMode/DESIGN.md)
- [`Docs/_WG/MappingMode/P1-NOTES.md`](../../../Docs/_WG/MappingMode/P1-NOTES.md)
- [`Docs/_WG/MappingMode/P2-NOTES.md`](../../../Docs/_WG/MappingMode/P2-NOTES.md)
- [`Docs/_WG/MappingMode/P3-NOTES.md`](../../../Docs/_WG/MappingMode/P3-NOTES.md)
- [`Docs/_WG/MappingMode/P6-NOTES.md`](../../../Docs/_WG/MappingMode/P6-NOTES.md)
- [`Docs/_WG/MappingMode/P7-NOTES.md`](../../../Docs/_WG/MappingMode/P7-NOTES.md)
- [`Docs/_WG/MappingMode/P8-NOTES.md`](../../../Docs/_WG/MappingMode/P8-NOTES.md)
- [`Docs/_WG/MappingMode/PLAN.md`](../../../Docs/_WG/MappingMode/PLAN.md)
- [`Docs/_WG/MappingMode/PLAN2.md`](../../../Docs/_WG/MappingMode/PLAN2.md)
- [`Docs/_WG/MappingMode/PLAN3.md`](../../../Docs/_WG/MappingMode/PLAN3.md)
- [`Docs/_WG/MappingMode/R1-NOTES.md`](../../../Docs/_WG/MappingMode/R1-NOTES.md)
- [`Docs/_WG/MappingMode/R2-NOTES.md`](../../../Docs/_WG/MappingMode/R2-NOTES.md)
- [`Docs/_WG/MappingMode/R3-NOTES.md`](../../../Docs/_WG/MappingMode/R3-NOTES.md)
- [`Docs/_WG/MappingMode/R4-NOTES.md`](../../../Docs/_WG/MappingMode/R4-NOTES.md)
- [`Docs/_WG/MappingMode/R5-NOTES.md`](../../../Docs/_WG/MappingMode/R5-NOTES.md)
- [`Docs/_WG/MappingMode/REVIEW.md`](../../../Docs/_WG/MappingMode/REVIEW.md)
- [`Docs/_WG/MappingMode/REVIEW3.md`](../../../Docs/_WG/MappingMode/REVIEW3.md)
- [`Docs/_WG/MappingMode/S1-NOTES.md`](../../../Docs/_WG/MappingMode/S1-NOTES.md)
- [`Docs/_WG/MappingMode/S1A-NOTES.md`](../../../Docs/_WG/MappingMode/S1A-NOTES.md)
- [`Docs/_WG/MappingMode/S1B-NOTES.md`](../../../Docs/_WG/MappingMode/S1B-NOTES.md)
- [`Docs/_WG/MappingMode/S2-EXPORT-NOTES.md`](../../../Docs/_WG/MappingMode/S2-EXPORT-NOTES.md)
- [`Docs/_WG/MappingMode/S2-NOTES.md`](../../../Docs/_WG/MappingMode/S2-NOTES.md)
- [`Docs/_WG/MappingMode/S3-NOTES.md`](../../../Docs/_WG/MappingMode/S3-NOTES.md)
- [`Docs/_WG/MappingMode/S5-NOTES.md`](../../../Docs/_WG/MappingMode/S5-NOTES.md)
- [`Docs/_WG/MappingMode/S6-NOTES.md`](../../../Docs/_WG/MappingMode/S6-NOTES.md)

## Non-modular edits

- [`Content.Client/Gameplay/GameplayState.cs`](../../../Content.Client/Gameplay/GameplayState.cs): mapping observers get the mapping screen
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): session, save and library window while mapping
- [`Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs`](../../../Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs): mappers open the decal placer
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): mapping session window; crew and character entries hidden while mapping
- [`Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs`](../../../Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs): mappers open the spawn menus
- [`Content.Server/GameTicking/GameTicker.RoundFlow.cs`](../../GameTicking/GameTicker.RoundFlow.cs)
  - mapping mode loads no game map or preset rule
  - mapping mode never starts a round
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs): mapping observers move on their paused session maps like admin ghosts
- [`Content.Server/Sandbox/SandboxSystem.cs`](../../Sandbox/SandboxSystem.cs)
  - admins who map get the won't-be-saved warning and tile routing too
  - mappers place, erase and lay tiles on their own session's maps
- [`Content.Shared/Input/ContentKeyFunctions.cs`](../../../Content.Shared/Input/ContentKeyFunctions.cs): Escape drops a reference grid being placed
- [`Content.Shared/Verbs/SharedVerbSystem.cs`](../../../Content.Shared/Verbs/SharedVerbSystem.cs): mapping observers without admin data get no verbs; admins keep theirs
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml)
  - Escape drops a reference grid being placed, ahead of the game menu
  - build mode and quicksave keys for the mapping screen

<!-- WOLFGATE-GENERATED END -->
