# Stage 2 notes (plan 3): save & export wizard

One zip per export, laid out like the repository, through the existing chunked download; a three-step wizard on the
client; ship details kept per library map. Integrates the stage 1a issues checker.

## Files

Shared (`Content.Shared/_WG/MappingMode/Export/`):

- `WGMappingExportEvents.cs`: `WGMappingExportKind`, `WGMappingShipDetails`, `WGMappingExportLimits` (bounds and
  `Sanitize`), `WGMappingExportRequest`, `WGMappingGoToIssueRequest`.
- `WGMappingShipDetailsYaml.cs`: `ship.yml` read/write, hand-written YAML through YamlDotNet's representation model
  like `WGMappingTemplateSettings` (no `[DataDefinition]`: the engine serializer isn't needed for nine fields and the
  reader sanitizes anyway).
- `WGMappingShipNaming.cs`: `VesselId`, `FileName`, `ShuttlePath`, `SizeFor`, `PriceFor`, `PriceFloor`, moved out of
  the server's `WGMappingVesselYaml` (which now delegates) so the wizard previews live.
- Edited: `WGMappingLibraryNames` (`<Map>.zip`, `IsZipFile`, `z<D>.scan.yml`, `ship.yml` paths),
  `WGMappingLibraryEvents` (`WGMappingLibraryMap.Ship` details).

Server:

- `Export/WGMappingExportSystem.cs`: the request, the check against the session, the zip, the Go to request.
- `Export/WGMappingExportPackage.cs`: pure entry paths, `gameMap`/`zMap` text, README text, `Zip`/`Unzip`.
- Edited: `WGMappingVesselYaml` (`Resolve` gives `WGMappingVesselFields` with the guessed keys; `Build` takes fields;
  `WGMappingVesselScan.Write/Read`), `WGMappingVesselSystem.Generate(scan, mapName, details, out fields)`,
  `WGMappingLibrarySystem` (scan and ship files in the version listing, `ReadShipDetails`, `ReadScan`,
  `TryWriteShipDetails`, details carried into the next save, `Ship` in the listing),
  `WGMappingDownloadSystem` (`DownloadFile` with in-memory bytes, `TryStartData`, `IsDownloading`).

Client:

- `Export/WGMappingExportWizard.xaml(.cs)`, `Export/WGMappingExportUIController.cs`,
  `Export/WGMappingClientSystem.Export.cs` (issues, Go to, `RequestExport`, `ShipDetailsOf`, `IsShipVersion`).
- Edited: `WGMappingClientSystem.Download.cs` (`Zip` flag, zip filter group), `WGMappingDownloadRow` (zip kind),
  `WGMappingLibraryList` (Export replaces Download + Ship; Download stays as a small secondary button),
  `WGMappingMenuWindow.xaml` (Export... replaces the two download buttons and the Save tab's target picker),
  `WGMappingMenuUIController`, `WGMappingLobbyUIController`.

Locale: `mapping-export.ftl` (new), `mapping-ship.ftl` (header keys: `-guesses` takes `$fields`, new `-set`; the two
Save tab download keys removed), `mapping-mode.ftl` (`download-kind-zip`; the two menu download keys removed).

Tests: `Content.Tests/_WG/MappingMode/WGMappingExportPackageTest.cs` (8 tests),
`Content.IntegrationTests/Tests/_WG/MappingMode/WGMappingExportTest.cs` (2 tests). `WGMappingDownloadTest`,
`WGMappingShipTest` and `WGMappingVesselYamlTest` were left as they were: the ship download and `Build(mapName, scan,
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
- The id and file name follow the ship name from the details when set, else the map name (`WGMappingShipNaming`).
- A map of several levels gets a `zMap` with `maps:` bottom-up; the prototype has no ground field, so the ground
  level's index is a YAML comment and a README line. One level gets a `gameMap` with `StandardNanotrasenStation`.
- `WGMappingDownloadAssembler.MaxFileBytes` stays 32 MiB: level YAML deflates several times over, and three levels of
  1.5 MiB each are far under it.
- The wizard skips Details for a map export (Next from Check goes to Package, Back comes back to Check).
- Throttles: one export per five seconds per player, Go to two per second; the issues request keeps its three seconds
  and the wizard greys Check again while a reply is pending.
- The old `WGMappingDownloadRequest(ship: true)` path stays on the server and in `WGMappingClientSystem` (the tests use
  it) but has no button any more.
- TODO (orchestrator, after the merge with stage 1b): a top bar Export button in `WGMappingScreenUIController.TopBar.cs`
  calling `UIManager.GetUIController<WGMappingExportUIController>().Open(session.Name, null, fromSaveTab: true)`.

## Verified

- `dotnet build Content.IntegrationTests -c DebugOpt -p:WarningsAsErrors=nullable` and `Content.Tests`: clean.
- `Content.Tests` `class =~ WGMapping`: 240 passed. `Content.IntegrationTests` `class =~ WGMappingExport`: 2 passed
  (ship zip entries byte for byte, details in the prototype and README, refusal with a price under the floor, details
  persisted, rewritten vessel file, carried into the autosave, in the listing, Go to; one-level gameMap, two-level zMap
  with both level files).

## Not verified / open

- The wizard was never rendered in a real client; the XAML loads only through the compile. Layout, the TextEdit
  description box and the checkbox grids are unseen.
- No headless client sandbox run after these changes (nothing new outside YamlDotNet, which shared already used).
- `wg-mapping-export-*` Fluent strings are checked only by the integration server loading them without a parse error.
- A Save-first export whose save is refused (library full) shows the refusal in the Package status and stops; a save
  that never answers times out after 20 s.
- The lobby's library Export can only export as a map or as an unchecked ship; the wizard says so but can't check.
