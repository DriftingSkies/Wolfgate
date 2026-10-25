# Stage 1a notes: issues checker

Server + shared only, no UI. A present member sends `WFMappingIssuesRequest(scope)`; `WFMappingIssuesSystem` answers
with `WFMappingIssuesEvent(scope, issues, summary)`, throttled to one per 3 s per player. The export stage calls
`Check(session, scope, out summary, price?)` and `HasErrors(session, scope, price?)` directly; `price` is the ship
price to hold against the appraisal floor (the generated `WFMappingVesselYaml.PriceFor` when null).

Files:

- `Content.Shared/_WF/MappingMode/Issues/WFMappingIssuesEvents.cs`: scope, severity, `WFMappingIssue`, summary,
  request and reply.
- `Content.Shared/_WF/MappingMode/Issues/WFMappingIssueCatalog.cs`: every rule with id, severity and scope; the
  title key is `wf-mapping-issue-<id>`, the hint `wf-mapping-issue-<id>-hint`; `Compare` gives the reported order
  (errors, warnings, notes; by rule id, depth, then tile position); `MaxIssues` = 200 and the closing `more` note.
- `Content.Server/_WF/MappingMode/Issues/WFMappingIssuesSystem.cs` (+ `.Map.cs`, `.Ship.cs`).
- `Resources/Locale/en-US/_WF/MappingMode/mapping-issues.ftl`.
- Tests: `Content.Tests/_WF/MappingMode/WFMappingIssueCatalogTest.cs`,
  `Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingIssuesTest.cs`.

## Rules

Map rules run for both scopes, ship rules only for `Ship`.

| Id | Severity | Fires when |
|---|---|---|
| `ship-grid-count` | Error | not exactly one grid on one level (the other ship rules are skipped) |
| `ship-no-name` | Warning | grid name empty or the engine default `grid` |
| `ship-no-floor` | Error | no tile whose definition isn't space |
| `ship-no-console` | Error | no anchored `ShuttleConsole` |
| `ship-no-dock` | Error | no `Docking` |
| `ship-no-thruster` | Error | no linear `Thruster` |
| `ship-no-gyroscope` | Error | no angular `Thruster` |
| `ship-no-power-source` | Error | no `PowerSupplier` (generators, APU, solars, AME) |
| `ship-no-apc` | Error | no `Apc` |
| `ship-no-battery` | Error | no `Smes` and no non-APC `PowerNetworkBattery` (substation) |
| `ship-no-atmos` | Note | no `AirAlarm` and no entity with a `PipeNode` |
| `ship-no-spawn-point` | Warning | no `SpawnPoint` |
| `ship-gun-off-hardpoint` | Error | anchored `HardpointAnchorableOnly` with no `Hardpoint` of a matching class and size on its tile (mirrors `SharedHardpointSystem.TryAnchorToHardpoint`, which session maps never run) |
| `ship-appraisal-zero` | Error | `PricingSystem.AppraiseGrid` ≤ 0 |
| `ship-price-floor` | Error | price < ceil(appraisal × 1.0), the shipyard arbitrage test's default `minPriceMarkup` |
| `map-level-empty` | Warning | a level with no grid (`Map` scope only; `ship-grid-count` covers ships) |
| `map-doubled` | Warning | two anchored entities of one prototype, or two cables of one voltage, on a tile |
| `map-anchored-no-tile` | Error | an anchored entity over an empty tile |
| `map-loose-entity` | Warning | an entity parented to the map, not a grid (observers excluded) |
| `map-unsaved` | Error | a `save: false` prototype; `$spawner` is the `WFMappingSpawnerIndex` suggestion or `none` |
| `map-denied` | Error | `WFMappingModeSystem.HasDeniedComponent` |
| `more` | Note | closes a list cut at 200; `$count` left out |

Entities are walked through containers; the session's mapping observers are skipped everywhere.

## Verified

- `dotnet build Content.IntegrationTests -c DebugOpt -p:WarningsAsErrors=nullable` and `Content.Tests` build clean.
- `WFMappingIssueCatalogTest`: 3 passed. `WFMappingIssuesTest`: 2 passed (ship rules clear part by part, price
  floor, grid count, request smoke; doubles, loose, unsaved with spawner, denied, cap note).

## Not verified / open

- `map-anchored-no-tile` has no test: spawning an anchorable entity on an empty tile doesn't anchor it, and the test
  didn't try clearing a tile under one.
- The Fluent file was only checked by the unit test (every key present, no strays) and by the integration server
  loading without a logged parse error; not rendered in a client.
- `ship-no-battery` as an Error follows the plan; a generator wired straight to an APC without an SMES or substation
  is flagged.
- No client code: the reply event is registered but nothing renders it until stage 2.
