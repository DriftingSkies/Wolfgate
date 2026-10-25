# MappingMode plan 3: export wizard, issues checker, live testing

Branch `clanker/mapping-wizard-live-testing-bc1261`, on main at e3d92cc9f2 (engine v291). Three features, built as
three stages. Every new file goes under the `MappingMode` module (`Content.*/_WF/MappingMode/...`,
`Resources/Locale/en-US/_WF/MappingMode`, tests under `Tests/_WF/MappingMode`). Edits outside `_WF` are marked
`WOLFGATE(MappingMode)`. Read `Content.Server/_WF/MappingMode/README.md` and `DESIGN.md` first: the mode's rules
(server enforces everything, mappers hold no admin data, limits on every request, nothing but mapping loads) hold
for all three.

## What is wrong today

- Saving and downloading are two separate ideas in two tabs, and "Download as ship" sends two files through two save
  dialogs; a map of several levels sends one file per level. Nothing tells the mapper what is wrong with the ship
  before it reaches the repo, and the generated vessel prototype is a guess the mapper has to hand-edit.
- There is no way to try a ship: the observer can't open doors, power never runs, atmos is fixed up only on save.

## Stage 1a: issues checker (server + shared, no UI)

`Content.Server/_WF/MappingMode/Issues/WFMappingIssuesSystem.cs` (+ partials for rule groups),
`Content.Shared/_WF/MappingMode/Issues/WFMappingIssuesEvents.cs`, locale `mapping-issues.ftl`.

- Request `WFMappingIssuesRequest(IssueScope scope)` from a present member; throttled 1 per 3 s per player. Scope is
  `Ship` (the session must be one level with one grid; the checks are the ones a shipyard vessel needs) or `Map`
  (every level, the checks that hold for any map). The reply `WFMappingIssuesEvent` carries a list of
  `WFMappingIssue(Severity, LocKey, LocArgs, NetEntity? Entity, NetCoordinates? Where, int? Depth, string RuleId)`
  plus the scan summary (grids, tiles, entities, appraisal, levels). Severity: `Error` (ship export is refused while
  one stands), `Warning` (export goes ahead), `Note`.
- Also a server API `List<WFMappingIssue> Check(WFMappingSession, IssueScope)` and `bool HasErrors(...)` for the
  export stage, and `WFMappingIssueCatalog` (shared, pure) describing each rule: id, severity, loc key for the title
  and the fix hint, so the client can render without knowing the rules.
- Rules, each a small method; port what the repo already checks. Look at `Content.IntegrationTests/Tests/_WF/Shipyard/VesselShipyardTest.cs`
  (hardpoint-only guns on hardpoints, square helm), `Tests/_NF/ShipyardTests.cs` (price floor vs appraisal, grid loads
  as a grid), `Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs` (what a purchased ship needs), the upload
  validator `WFMappingUploadValidator.cs` (denied components, `save: false` prototypes) and the save-time atmos fix in
  `WFMappingLibrarySystem.FixAtmos`. Ship rules at least: exactly one grid on one level; a shuttle console; at least
  one docking port; at least one thruster and a gyroscope; a power source (generator/APU/solar/AME...) and at least
  one APC and SMES or substation; an air alarm or a gas distribution pipe network is a Note only; a tile with two
  anchored entities of the same prototype (doubled walls, doubled cables of one layer); anchored entities with no tile
  under them; entities not on the grid (loose in space on the session map) as a Warning; prototypes the map file
  won't save (`save: false`) as Errors naming the spawner (see `WFMappingSpawnerIndex`); denied components (the
  mode's deny set) as Errors; hardpoint-only guns off hardpoints; appraisal 0 or a price under the appraisal floor
  the shipyard test uses; no spawn point / no floor tile a crew could stand on; grid name empty. Map rules: the
  subset that applies to any level plus a level with zero grids.
- Deterministic order: errors, warnings, notes, each by rule id then grid position. Cap the list at 200 with a
  final Note saying how many more.
- Tests: `Content.Tests/_WF/MappingMode/WFMappingIssueCatalogTest.cs` (every rule has loc keys, ids unique) and
  `Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingIssuesTest.cs`: build a bare grid in a session and assert
  the expected rule ids fire and clear as the pieces are added (console, dock, thruster, gyro, power).

## Stage 1b: live testing (server + shared + client button)

`Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs` (+ partials), shared
`Content.Shared/_WF/MappingMode/Test/WFMappingTestEvents.cs`, client `Content.Client/_WF/MappingMode/Test/*`,
locale `mapping-test.ftl`, cvars in `WFMappingModeCVars` (`wf.mapping_mode.test_*`).

- One click, "Test map", from the mapping screen's top bar (`WFMappingScreenUIController.TopBar.cs`, a play icon next
  to Quicksave) and the Mapping window is NOT touched in this stage (stage 2 owns it). The request
  `WFMappingTestStartRequest` from a present member starts the session's test, or joins a running one.
- Server flow: snapshot the session's levels with the library's save code into a scratch folder under
  `/Mapping/Test/<sessionId>/` (reuse `MappingZLevelsSystem.TrySaveLevels` / `TrySaveGrid`; never the live
  entities), then build the test stack: one new map per level, a CE z-network when there are several (see how
  `TryOpenSession`/`MappingZLevelsSystem` open a stack), registered to the session in the map registry
  (`WFMappingModeSystem.Registry.cs`) with a `Test` flag so PVS and guards know them. Load the snapshot grids onto
  them, and on the ground level also the test range: the grid file named by cvar `wf.mapping_mode.test_range`
  (default: pick a small existing POI/station grid from `Resources/Maps/_NF/POI` or `_WF` with a dock and power,
  under ~1 MB; say which and why in the notes; empty string = space only), placed 60 tiles off the ship so the
  mapper can fly to it. Give the ship grid what the shipyard gives a purchased one (`ShuttleComponent`, IFF, name
  from the session) by reading `ShipyardSystem.TryPurchaseShuttle`/`TryAddShuttle` and the systems it relies on;
  don't call the shipyard's purchase (no station exists). Then `MapSystem.InitializeMap` + unpause every test map so
  power, atmos, doors and gravity run.
- Spawn the player as their character: `IServerPreferencesManager.GetPreferences(user).SelectedCharacter` as a
  `HumanoidCharacterProfile`, `StationSpawningSystem.SpawnPlayerMob(coords, job: null, profile, station: null,
  session)` at the ship's best spot: a `SpawnPointComponent` on the ship, else the tile in front of a shuttle
  console, else the free floor tile nearest the grid centre. Make a mind and `TransferTo`. Detach the observer but
  keep it (parked in nullspace or hidden on the session map) so returning is a re-attach at the spot they left.
  Give the mob `WFMappingTesterComponent` so the protection system still shields it and the guard knows it may not
  edit: a mapper in a test can't place, erase, select or run any tools request (`WFMappingCanUseEvent` refused with
  `wf-mapping-test-no-edit`), and the engine placement hook ignores them.
- Return: `WFMappingTestStopRequest` (Back to mapping), death (`MobStateChangedEvent` to Dead → 3 s then return),
  disconnect, session close, the owner ending the test, or `wf.mapping_mode.test_max_minutes` (default 30) elapsed.
  Returning deletes the test maps once nobody is in them and re-attaches the observer. Leaving the session from a test
  goes through the normal leave. Tell other members "X is testing" via the session state; they can join the running
  test with the same button and land at the same spot.
- Client: `WFMappingTestClientSystem` tracks `WFMappingTestState` (testing, since, who); the top bar button toggles
  label/tooltip (Test map / Back to mapping). While testing the normal gameplay HUD shows (the player is a mob, not a
  mapping observer), so add a small always-visible strip at the top of the gameplay screen (new client control
  `WFMappingTestBanner`, mounted from the mapping client system when the local player carries
  `WFMappingTesterComponent`): "Testing <map>: Back to mapping [button]  Time left mm:ss". Also a hotkey
  `WFMappingTestReturn` in `WFMappingKeyFunctions` (default: none bound; show the button).
- Limits: test maps count in the session's entity cap (`max_entities`); refuse the start when the session is
  already over 60% of it, naming the limit. One test per session; `test_cooldown` (default 10 s) between tests.
  Test maps are never saved: check `GetLevels(session.RootMap)` and the autosave path skip them (they're a
  separate z-network; assert in a test). Admin `wf_mappingmode_list` shows a testing session.
- Tests: `WFMappingTestModeTest.cs`: start a test in a session with a two-tile grid holding a shuttle console; the
  player is attached to a humanoid on a grid of the test map, the test map is initialized, the session's own map
  is not; the player can't place through the guard; stop returns them to the observer and deletes the test maps;
  the library save after a test holds only the session's own level.

## Stage 2: save & export wizard (server packaging + client wizard, integrates 1a)

- Server `WFMappingExportSystem` builds ONE zip in memory (`System.IO.Compression`, server only) for a library
  version and streams it through the existing download system as `<Map>.zip` (one file, so one save dialog). Entries
  mirror the repo so the archive is dropped on the repo root:
  - Ship (one level, one grid): `Resources/SharedMaps/_WF/Community/<file>.yml`,
    `Resources/Prototypes/_WF/Community/<file>.yml` (vessel + gameMap, from `WFMappingVesselYaml` extended with the
    wizard's fields), `README.txt` (what it is, where it was dropped, which fields were guesses, the issues report).
  - Map (any level count): `Resources/Maps/_WF/Community/<file>/z<D>.yml` (one level: `<file>.yml`), a `zMap`
    prototype in `Resources/Prototypes/_WF/Community/<file>.yml` when there are several levels (format:
    `Resources/Prototypes/_CE/ZLevels/zmaps.yml`), else a `gameMap`, and the README.
- Ship details the mapper fills: name, description, price (default the appraisal rule, editable with the floor
  shown), category (auto with override), classes (multi-select), group, engines (auto with override), access,
  `requireCrew`. Stored per library map as `ship.yml` in the version folder next to the vessel file (so re-export
  and newer versions keep them), sent to the client with the library listing.
- Client wizard window `WFMappingExportWizard.xaml` (opened from Export… in the Save tab, from a library row's
  Export, and from a top bar button): step 1 Check (runs the issues request, lists errors/warnings with Go to;
  ship export disabled while errors stand, with a "Export as map instead" path); step 2 Details (the form above,
  live id/file name preview and the price hint); step 3 Package (Save a version now if dirty, then Download the zip
  to the data folder or Save to…, progress row, Open folder). The Save tab keeps the name box, Save and the save
  times; its Download/Download-as-ship buttons go, replaced by Export… (the plain level download stays in the
  Library rows). Library rows: Export (zip) replaces Download + Ship.
- Tests: zip contents for a ship and a two-level map (`WFMappingExportTest`), details round trip, download of a zip
  through the client assembler, the README names the right paths.

## Stage 3: gate

Build Server/Client/Shared/Tests (`-c DebugOpt`, with `-p:WarningsAsErrors=nullable`), run the MappingMode tests,
YAML linter in Release, headless client sandbox check, `python Tools/_WF/Ci/modules.py --write`, README update, one
commit per stage.
