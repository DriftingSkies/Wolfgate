# Plan 3 review: issues checker, export wizard, live testing

Adversarial and maintainer review of `6ca0c821d5..741862a7fc`. High and Medium are fixed with tests; Low are open.
Line numbers are after the fixes.

| Severity | Where | Issue | State |
|---|---|---|---|
| High | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:472` | The test character was spawned from the real profile with the session, so Mono's `LoadPersistentData` handed the profile's persistent items and components to a body the test deletes and saved the profile without the non-sticky ones: every test used them up for good. Spawned from `TestProfile` now (no persistent items or components). | Fixed |
| High | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:617` | A test ship could FTL. The upload validator keeps map root components such as `FTLDestination`, so a tester could jump their running ship and character onto another session's live map (or their own, getting initialised entities saved); a test ending mid-jump also left the ship and its contents in the shared FTL map for good. `ConsoleFTLAttemptEvent` is cancelled for any shuttle on a test level. | Fixed |
| Medium | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:324` | `ComputerComms` and the other comms consoles are `global: true`; on a running copy any tester with an ID card the mapper placed could announce to the whole server. The copy's consoles lose `Global` before the maps start. | Fixed |
| Medium | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:627` | Radio ignores maps (the map check is commented out upstream), so a radio on a test level reached intercoms on every other session's maps and the reverse. Receives across a test's boundary are cancelled; inside one test they still work. | Fixed |
| Medium | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:187` | A build that succeeded followed by a failed join (no mind, spawn threw, not on the observer) left a test with no testers: `Update` skips it while nobody tests, so its maps ran until the session closed. Closed at once now. | Fixed (no test: the failure needs a broken spawn) |
| Medium | `Content.Server/_WF/MappingMode/Export/WFMappingExportSystem.cs:123` | The export checked and scanned the live session for any version of the map's name: an older version, or a newest one with unsaved changes, got the live grid's appraisal, size and engines and was refused or passed on the live grid's issues. The session is used only for the newest version with no changes since; otherwise the export is unchecked and the README says so. | Fixed |
| Medium | `Content.Server/_WF/MappingMode/Export/WFMappingExportSystem.cs:251` | A map export's "id already exists" README line looked the id up as an entity prototype; it now looks in `gameMap` for one level and `zMap` for several. | Fixed (no test) |
| Medium | `Content.Server/_WF/MappingMode/WFMappingLibrarySystem.cs` (`OnSaveRequest`) | A tester's save is refused while the autosave keeps running. Kept on purpose and documented: the refusal is because the tester has left the live levels; the autosave and other members' saves read only those levels and never the copy. | Decided, documented |
| Low | `Content.Server/_WF/MappingMode/WFMappingModeSystem.Guard.cs:50` | The global PVS strip only applies on paused maps; an entity with `GlobalPvs` spawned at run time on a running test level keeps its override and goes to every client. | Open |
| Low | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs` | Other initialised behaviour that reaches past a map was not audited: fax and device network packets to paused maps, random spawners rolling ghost-role mobs on map init, bluespace lockers, nuke and station-event hooks. | Open |
| Low | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:463` | A member joining after the ship was destroyed (spawn coordinates invalid) ends the test for everyone instead of landing on the ground level. | Open |
| Low | `Content.Server/_WF/MappingMode/WFMappingModeSystem.cs:174` | `ghost` from a test character spawns a ghost on the test level; the detach returns the player to their observer, so it works, but by accident. | Open |
| Low | `Content.Server/_WF/MappingMode/Test/WFMappingTestSystem.cs:207` | A test may start at 60% of `max_entities`; the counted copy then puts the session near or over the cap, so other members can't add anything while it runs. Documented, but the share and the cap don't fit together. | Open |
| Low | `Content.Server/_WF/MappingMode/WFMappingLibrarySystem.cs:386` | `ship.yml` (up to about 2 KB) is written without counting against `max_library_bytes`, on export and on every save. Bounded by the sanitizer. | Open |
| Low | `Content.Server/_WF/MappingMode/Export/WFMappingExportSystem.cs:123` | The session match is by name: a member of someone else's session with the same name as an unrelated map of their own gets that session's check and scan on their newest version. | Open |
| Low | `Content.Shared/_WF/MappingMode/Export/WFMappingExportEvents.cs:101` | `Sanitize` walks the whole class and engine lists before cutting at 8; only the network message size bounds them. | Open |
| Low | `Content.Client/_WF/MappingMode/Test/WFMappingTestClientSystem.cs:91` | The return key function is added to the `common` context at run time, so it isn't in the keybind menu until a test runs. | Open |
| Low | `Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingTestModeTest.cs:276` | `ClosingTheSessionEndsTheTest` sets `test_range` to empty and never restores it on the pooled server. | Open |

## Checked and fine

- Every new request is from a present member and throttled; Go to resolves the target and refuses one off the
  sender's session maps; export reads only the sender's library through `TryFindVersion`; the details are sanitized
  before they reach disk or YAML (quoted single-line scalars, enum values, access alphabet).
- The tester's detach, death, disconnect (status change then detach), leave, kick, session close, mode off and round
  restart all return or drop the player and delete the test maps; the observer's detach is skipped while testing, and
  `EndTest` removes the tester before moving the mind so its own detach handler is a no-op.
- `WFMappingTesterComponent` events and the broadcast `ConsoleFTLAttemptEvent` / `RadioReceiveAttemptEvent`
  subscriptions have no duplicate (component, event) pair.
- Test maps are a separate stack, registered as `Test`, so `GetLevels`, the save, the autosave and `EntityCount`'s
  session part skip them; no bank account is added (no job, no loadout), so ATMs and shops refuse the test character.
- Zip entries, vessel required fields (`price`, `category`, `group`, `shuttlePath`) and the `zMap` shape match the
  game's prototypes; the client code uses nothing outside the sandbox and no reserved window control names.
