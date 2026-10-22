# R1 notes: ship grids, vessel prototypes, finding grids, the mode bar

Round R1 of owner feedback. What was built for each report, and the choices the brief left open.

## 1. Grid files and download names

- Every save (manual, autosave, upload) writes `z<D>.grid.yml` with `MapLoaderSystem.TrySaveGrid` beside each level
  that holds exactly one grid, and `z<D>.vessel.yml` beside it. A failed grid file fails the save like a failed level;
  a vessel prototype that fails to generate is only logged, and its grid file still counts.
- The scan reads the `grids:` list at the head of each level file (no YAML parse, at most 4096 lines) to know each
  level's grid count. A version is a ship (`WFMappingLibraryVersion.Ship`) when it holds exactly one grid in all and
  has that grid's file. The listing shows "ship grid" on such versions.
- Download names: one level is `<MapName>.yml` (the grid file when the level has one, else the map file); several are
  `<MapName>_z<D>.yml`. A ship is `<MapName>.yml` plus `<MapName>.vessel.yml`. The client still writes under
  `/Mapping/<MapName>/<version>/` and accepts only those three name shapes for the map it is downloading
  (`WFMappingLibraryNames.IsDownloadFile`).
- Ship refusals: no grid (`wf-mapping-ship-no-grid`), several (`wf-mapping-ship-several-grids`, with the count), one
  grid but no grid file because the version predates R1 (`wf-mapping-ship-no-grid-file`, "open it and save again"),
  and an unreadable level header (the existing library-read-failed message). A ship whose vessel file is missing sends
  the grid alone.
- Ship buttons show on every library row, not only ship versions, so pressing one on a version that isn't a ship
  explains why. The Save tab's Download as ship downloads the newest saved version of the name in the Save as box (or
  the session name); it doesn't save first.
- Reference lists now show level files only, so the grid and vessel files beside them don't appear twice or as maps.
- Engine quirk found on the way: the tests' in-memory data folder reports a file's `Length` as wherever any stream on
  it last read up to. The header read leaves the stream at the end so later size reads (and the download) stay right;
  real files are unaffected.

## 2. Vessel prototype generator

- `WFMappingVesselYaml` (pure, unit tested) writes the text; `WFMappingVesselSystem` scans the grid and localizes the
  header and the description placeholder. Generated at save time from the live grid, so the file matches the grid
  file saved with it.
- Fields: `id` `WF` + the map name's letter/digit runs in PascalCase (`WFShip` if none), `parent: BaseVessel` (the IFF
  every shipyard vessel gets), `name` the map name, `description` a placeholder, `price`, `category`,
  `group: Shipyard`, `shuttlePath: /SharedMaps/_WF/Community/<file>.yml`, `guidebookPage: null`, `class`, `engine`
  (left out when nothing matched). A `gameMap` of the same id follows, as every vessel in the game has one, so a
  bought ship gets its station, name and late join. Strings are single-quoted.
- `<file>` is the map name's words in lower case joined by `_`, matching the repo's lower-case grid files. The
  download is still named `<MapName>.yml`; the header says to save it as `<file>.yml`.
- Price: `AppraiseGrid` x 1.25, rounded up to 500 below 20,000, 1,000 below 100,000, 5,000 above, minimum 1,000.
  The markup keeps the price over a resale with headroom for container fills, which an uninitialised grid doesn't have
  yet (the arbitrage test appraises after map init; `minPriceMarkup` is 1).
- Size thresholds, from the tile counts of the 228 vessel grids in the repo (a script decoding their tile chunks):
  medians Micro 31, Small 132, Medium 338, Large 744; Small 10-90 % 60-258, Medium 217-519, Large 414-1788. Picked
  Micro < 50, Small < 200, Medium < 500, Large above. About a quarter of existing vessels sit on the other side of a
  threshold from their hand-picked size, which overlaps that much anyway.
- Class: the brief's Transport, Mining and Combat aren't `VesselClass` values. Used Civilian (the enum's catch-all)
  when nothing is found, Cargo for a `CargoPallet` component, Salvage for mining gear (a `SalvageMagnet` component, or
  an id or parent id containing OreProcessor, OreBox, OreMagnet, MagnetBoxOre, SalvageMagnet, Pickaxe, or exactly
  MiningDrill; GasMiningDrill isn't mining) and Mercenary for an anchored entity with a `Gun` component.
- Engine: first match over the entity's id and parents: AmeController AME, SuperPacman Uranium, DKJr Bananium,
  JrPacman Welding, Pacman Plasma, Pinch Plasma, NuclearReactor NFR, GeneratorRTG RTG, APU APU, SolarPanel Solar,
  TegCenter TEG, Supermatter, TeslaGenerator Tesla, SingularityGenerator Singularity. Derived from which entities the
  existing vessels with each engine carry. Only entities standing on the grid count, not container contents.
- An id that already names a vessel in the game gets a header line saying to rename it.
- Where the files go: `Resources/SharedMaps/_WF/Community/<file>.yml` and
  `Resources/Prototypes/_WF/Community/<file>.yml`, as the brief named. That folder isn't a module yet: the first
  community ship added needs a `Community` module README, or `modules.py --check` fails.

## 3. Finding grids

- `Enter` (create, join, rejoin, open) spawns the observer at the centre of the bounds of the grid with the most tiles
  on the ground level, or the origin when there is none. Join gets it too; it is the same code path.
- Reference confirm moves the observer to the centre of the largest placed grid, as asked. It is a short jump, since
  the reference was placed under the cursor.
- The Session tab's Grids section: a row per grid on the mapper's current level (name, tiles, entities with contents,
  Go to), Next grid (largest first, wrapping) and Refresh. The window asks every 3 s while the Session tab shows, so
  the list follows level changes; requests share a 2/s throttle (burst 4).
- `WFMappingGoToGridRequest` is refused unless the grid is on one of the sender's session maps; the move uses
  `SetMapCoordinates` rather than `SetWorldPosition` so a grid on another level of the session also works.
- Entity counts skip players (anything with an actor or the mapping observer component, and what is under them);
  the observer standing on a grid would otherwise count with its action entities.

## 4. Movable mode bar

- `WFMappingModeBar` holds the 1-8 buttons after a grab handle; `WFMappingScreen` places it. The bar lies flat, or
  stands upright when flat it wouldn't fit the viewport's width (always the case at 1280x720, where the viewport is
  about 700 UI units wide). The default spot is 10 units in from the viewport's left, just above whichever of the quick
  bar and the cursor readout lie under it, and never above the bottom of the menu bar or a floating chat.
- Drop: within 24 units of an edge snaps to it. Upright on the left or right edge alone, flat on the top or bottom
  edge alone, unchanged in a corner or mid view. Saved as a fraction of the free space per axis plus the orientation in
  `/Mapping/screen.yml` (YAML via `ResPath` and the user data folder); unreadable text gives the default spot.
- Reset layout sits in the Session tab under Screen; it restores the default spot and deletes the file.
- Checked by a headless client test at 1280x720 and at UI scales 0.75 and 0.5 (bigger virtual screens): the default
  spot is inside the viewport, bottom left, and clear of the menu bar, quick bar, readout and chat. Numbers seen at
  1280x720: viewport 697x795 UI units, bar upright 113x261 at (10, 428) over the viewport, quick bar from y 698.

## 5. Entities that won't save

- Every mob inherits `save: false`, so a mapper could place one, see it, and lose it from the saved and downloaded
  file. Maps use spawner markers instead. `WFMappingSpawnerIndex` (Shared) maps each prototype with `MapSavable` false
  to one spawner prototype: a spawner of only that prototype with chance 1 first, then a common entry before a rare
  one, then the shortest list, then the lowest id. Spawners that are abstract, hidden, denied for mappers or wait for a
  game rule are left out. Example: `MobCorgiMouse` gets `SpawnMobCorgiMouse`, `MobCorgiIan` gets the shared
  `SpawnMobCorgi`, and `MobCorgi` itself has none.
- The spawner components are server-only (the client ignores them), so the server builds the index lazily, drops it on
  an entity prototype reload, and sends only the unsaveable entries when the client sets up its mapping screen
  (`WFMappingSpawnerIndexRequest`/`Event`, on the list throttle). The client holds it in `WFMappingClientSystem`.
- The warning is a popup plus a result line, shown red, once per prototype per player until they leave their session
  or disconnect: engine placement (`AllowPlace`, and for admins a marked call in `SandboxSystem`), and mapping tools
  draws and pastes (`WFMappingCanEditEvent.Prototypes`/`CarriedPrototypes`, admins included). The edit is never
  refused for this. The text is `wf-mapping-mode-unsaved-spawner` or `wf-mapping-mode-unsaved-none`.
- Client: the Entities tab marks such an entry with an amber `!` and adds the same text to its tooltip; its right-click
  menu gets Use spawner instead when one exists; placing one shows a hint and a Use spawner button in Properties.
  Palette tiles are shared with the spawn window, which passes no note, so it looks as before.
- Not covered: tiles and decals always save; entries in the recent strip and quick bar aren't marked.

## 6. Mapping screen menu bar

- The bar is the screen's own `GameTopMenuBar`; the game's controllers find it through `GetActiveUIWidgetOrNull` and
  `ConstructionMenuPresenter` through `GetActiveUIWidget`, which throws without it (PLAN2 widget table), so no button
  is removed. `WFMappingScreenUIController.TopBar.cs` hides the gameplay ones on load and again every frame, since
  `ConstructionMenuPresenter` (crafting), `AnatomyUIController.UpdateButtons` (consent and anatomy) and others set
  `Visible` themselves later. The controller's `FrameUpdate` runs whatever is hidden, unlike a control's.
- Decisions, from each button's controller:
  - Kept: escape menu (it holds the Mapping entry and leaving), guidebook, bug report (`FeedbackUIController`), AHelp.
  - Kept on their own rules: admin (`IClientConGroupController.CanAdminMenu`) and sandbox (`SandboxSystem.
    SandboxAllowed`). Those controllers only update the bar that is up when the state is entered or admin or sandbox
    status changes, and the mapping screen loads a frame later, so by reading the code its bar keeps the XAML default
    (shown) for everyone; not checked before the change. The mapping screen now applies their rules each frame.
  - Hidden: sector map (the hexagon, `WFSectorMapUIController`, the hex territory map), character, crew setup (the
    `>`-looking `group.svg` icon, `WFRoundSetupUIController`: crews and player lobbies, not an expander), emotes,
    crafting, actions, language, and the consent cog (`ConsentUiController`, the gear in the screenshot; it is the
    consent window, not game options, which stay in the escape menu) with the anatomy panel that takes its slot.
- Keys: a mapper's entity uses the `aghost` context, which inherits `common` and not `human`, so character (C),
  emotes (N), crafting (G) and language (J) never reach it. Actions (K), sector map (Shift+N), consent and anatomy are
  in `common`; they are taken out of it while the screen is up and put back on unload, as the build mode keys are added
  and removed.
- Mapping and Quicksave are `MenuButton`s with `ButtonSquare`, made in code on load and put just before the bug
  report button, and orphaned on unload, so `GameTopMenuBar.xaml` is unchanged. Icons are 64x64 white PNGs drawn for
  this (`Resources/Textures/_WF/MappingMode/Interface/mapping.png`, `quicksave.png`); nothing in `Resources/Textures`
  had a map or floppy in the menu style, which the button tints.
- Mapping has no key. Quicksave is `WFMappingQuicksave` on Ctrl+S: Ctrl+S is otherwise only the engine `SaveMap`,
  which exists only in `MappingState`. Its label under the icon is blank, as `BoundKeyHelper` leaves out keys with a
  modifier, so the tooltip names it.
- Quicksave sends `WFMappingSaveRequest` with no name, so the server saves under the session name into the sender's
  library. The server already answers every save with a cursor popup and a result line (`Notify`), so the client adds
  only a popup for a key press during its cooldown. The cooldown is 10 seconds of real time, matching
  `WFMappingLibrarySystem._saveThrottle` (one save per 10 s), and outlasts screen reloads.
- Test (`WFMappingScreenTest`, connected client): after joining and after a layout reload the hidden buttons and keys
  are gone and Mapping and Quicksave sit before the bug report button; Mapping opens and closes the window with its
  pressed state; Quicksave writes `v0001_*_manual` under the session name, and a second click and the key inside the
  cooldown send nothing (the client's last result stays "saved", not "too soon"); after leaving to a plain entity the
  normal bar shows its gameplay buttons, has no Mapping or Quicksave and the keys are back in `common`.

## Not exercised

- No mouse drag was driven in a real client; the test moves and drops the bar through `MoveBar`/`DropBar`, which the
  handle's drag calls.
- A downloaded ship hasn't been added to the game and bought from a shipyard console. The integration test loads the
  grid file with the shipyard's `TryLoadGrid` call and indexes the vessel prototype, but doesn't buy it.
- The menu bar was checked in a connected headless client only: the icons and their tint weren't looked at in a real
  client, and Quicksave turning back on after 10 seconds isn't waited for in the test.
