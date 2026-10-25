# Stage 2 notes (plan 3): save & export wizard

One zip per export, laid out like the repository, through the existing chunked download; a three-step wizard on the
client; ship details kept per library map. Integrates the stage 1a issues checker.

## Files

Shared (`Content.Shared/_WF/MappingMode/Export/`):

- `WFMappingExportEvents.cs`: `WFMappingExportKind`, `WFMappingShipDetails`, `WFMappingExportLimits` (bounds and
  `Sanitize`), `WFMappingExportRequest`, `WFMappingGoToIssueRequest`.
- `WFMappingShipDetailsYaml.cs`: `ship.yml` read/write, hand-written YAML through YamlDotNet's representation model
  like `WFMappingTemplateSettings` (no `[DataDefinition]`: the engine serializer isn't needed for nine fields and the
  reader sanitizes anyway).
- `WFMappingShipNaming.cs`: `VesselId`, `FileName`, `ShuttlePath`, `SizeFor`, `PriceFor`, `PriceFloor`, moved out of
  the server's `WFMappingVesselYaml` (which now delegates) so the wizard previews live.
- Edited: `WFMappingLibraryNames` (`<Map>.zip`, `IsZipFile`, `z<D>.scan.yml`, `ship.yml` paths),
  `WFMappingLibraryEvents` (`WFMappingLibraryMap.Ship` details).

Server:

- `Export/WFMappingExportSystem.cs`: the request, the check against the session, the zip, the Go to request.
- `Export/WFMappingExportPackage.cs`: pure entry paths, `gameMap`/`zMap` text, README text, `Zip`/`Unzip`.
- Edited: `WFMappingVesselYaml` (`Resolve` gives `WFMappingVesselFields` with the guessed keys; `Build` takes fields;
  `WFMappingVesselScan.Write/Read`), `WFMappingVesselSystem.Generate(scan, mapName, details, out fields)`,
  `WFMappingLibrarySystem` (scan and ship files in the version listing, `ReadShipDetails`, `ReadScan`,
  `TryWriteShipDetails`, details carried into the next save, `Ship` in the listing),
  `WFMappingDownloadSystem` (`DownloadFile` with in-memory bytes, `TryStartData`, `IsDownloading`).

Client:

- `Export/WFMappingExportWizard.xaml(.cs)`, `Export/WFMappingExportUIController.cs`,
  `Export/WFMappingClientSystem.Export.cs` (issues, Go to, `RequestExport`, `ShipDetailsOf`, `IsShipVersion`).
- Edited: `WFMappingClientSystem.Download.cs` (`Zip` flag, zip filter group), `WFMappingDownloadRow` (zip kind),
  `WFMappingLibraryList` (Export replaces Download + Ship; Download stays as a small secondary button),
  `WFMappingMenuWindow.xaml` (Export... replaces the two download buttons and the Save tab's target picker),
  `WFMappingMenuUIController`, `WFMappingLobbyUIController`.

Locale: `mapping-export.ftl` (new), `mapping-ship.ftl` (header keys: `-guesses` takes `$fields`, new `-set`; the two
Save tab download keys removed), `mapping-mode.ftl` (`download-kind-zip`; the two menu download keys removed).

Tests: `Content.Tests/_WF/MappingMode/WFMappingExportPackageTest.cs` (8 tests),
`Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingExportTest.cs` (2 tests). `WFMappingDownloadTest`,
`WFMappingShipTest` and `WFMappingVesselYamlTest` were left as they were: the ship download and `Build(mapName, scan,
text)` still work.

## Decisions

- The check runs against the session the player is present in when its name is the map's (the Save tab path, where
  the wizard saves first). An export from the library outside that session runs no check, is never refused for
  issues, and its README says "No check ran". The ship price in the details is held against the appraisal floor
  through `Check(session, Ship, price)`.
- The vessel scan (tiles, appraisal, flags, engines) is saved as `z<D>.scan.yml` at save time, so an export outside
  the session generates from the version's own reading instead of loading the grid aside. In the session the live grid
  is scanned. A version saved before stage 2 has no scan: the generator runs on an empty reading (price 1000, Micro) and
  the README says to open and save again.
- `ship.yml` lives in the version folder; the listing shows the newest version's; `SaveVersion` copies the newest
  details into the new version and generates its vessel file with them; `TryWriteShipDetails` on export also rewrites
  that version's vessel file. The few hundred bytes aren't counted against `max_library_bytes` before the write.
- The id and file name follow the ship name from the details when set, else the map name (`WFMappingShipNaming`).
- A map of several levels gets a `zMap` with `maps:` bottom-up; the prototype has no ground field, so the ground
  level's index is a YAML comment and a README line. One level gets a `gameMap` with `StandardNanotrasenStation`.
- `WFMappingDownloadAssembler.MaxFileBytes` stays 32 MiB: level YAML deflates several times over, and three levels of
  1.5 MiB each are far under it.
- The wizard skips Details for a map export (Next from Check goes to Package, Back comes back to Check).
- Throttles: one export per five seconds per player, Go to two per second; the issues request keeps its three seconds
  and the wizard greys Check again while a reply is pending.
- The old `WFMappingDownloadRequest(ship: true)` path stays on the server and in `WFMappingClientSystem` (the tests use
  it) but has no button any more.
- TODO (orchestrator, after the merge with stage 1b): a top bar Export button in `WFMappingScreenUIController.TopBar.cs`
  calling `UIManager.GetUIController<WFMappingExportUIController>().Open(session.Name, null, fromSaveTab: true)`.

## Verified

- `dotnet build Content.IntegrationTests -c DebugOpt -p:WarningsAsErrors=nullable` and `Content.Tests`: clean.
- `Content.Tests` `class =~ WFMapping`: 240 passed. `Content.IntegrationTests` `class =~ WFMappingExport`: 2 passed
  (ship zip entries byte for byte, details in the prototype and README, refusal with a price under the floor, details
  persisted, rewritten vessel file, carried into the autosave, in the listing, Go to; one-level gameMap, two-level zMap
  with both level files).

## Not verified / open

- The wizard was never rendered in a real client; the XAML loads only through the compile. Layout, the TextEdit
  description box and the checkbox grids are unseen.
- No headless client sandbox run after these changes (nothing new outside YamlDotNet, which shared already used).
- `wf-mapping-export-*` Fluent strings are checked only by the integration server loading them without a parse error.
- A Save-first export whose save is refused (library full) shows the refusal in the Package status and stops; a save
  that never answers times out after 20 s.
- The lobby's library Export can only export as a map or as an unchecked ship; the wizard says so but can't check.
