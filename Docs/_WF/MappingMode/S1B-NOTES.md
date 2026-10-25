# Stage 1b notes: live testing

Decisions taken where PLAN3 said pick or decide, with the reason.

## Test range: `/Maps/_NF/POI/cargodepot.yml`

`wf.mapping_mode.test_range` defaults to the Frontier cargo depot. Of the POI grids under 1 MB it is the smallest
that has everything a mapper wants to try against: a shuttle airlock to dock at (`AirlockGlassShuttle` with its
docking port), a full power chain (`GeneratorBasic`, `SolarPanel`, `SMESBasic`, `SubstationBasic`, `APCBasic`), a
mini gravity generator and ordinary doors, at 164 KB and about 1,100 entities. Grifty's (130 KB) has the same dock
but runs on solars alone, the anomalous geode brings an anomaly generator, and McHobo's has no dock. A cargo depot is
also the natural thing to fly a new ship to. The file is a grid file (one `MapGrid` root), so `TryLoadGrid` takes it.

The range sits 60 tiles east of the ship's bounding box, centred on the ship's Y, as the plan asked; `""` means space
only. A missing or unloadable file is logged and the test runs in empty space rather than failing.

## What the ship gets

`ShuttleComponent` (consoles pilot it), `IFFComponent` (radars label it) and the session's name as the grid's name,
which is what the IFF label reads. Not replicated from the shipyard: `ShuttleDeedComponent` and the console locks
(there is no deed holder, and locking the console to a deed nobody carries would make the console unusable; the deed
component is also `Access`-restricted to the shipyard systems), ship access readers, `FTLLockComponent` and the
station/records setup (no station exists in the mode). The `ShipBoughtEvent` isn't raised: its subscribers (ship
repair, the Wolfgate trader) assume a shipyard purchase.

## Snapshot

The levels are saved with `MappingZLevelsSystem.TrySaveLevels` into `/Mapping/Test/<sessionId>/z<D>.yml` in the
server data folder, loaded back with `TryLoadLevels` (paused, uninitialised, restacked when there are several), and
the scratch folder is deleted right after the load; nothing of a test is kept on disk. `WFMappingLibrarySystem.FixAtmos`
(made public) runs on the copy, not the live levels, so a test changes nothing in the session. The copy's maps are
registered with `WFMappingMapKind.Test`: `IsSessionMap` is false for them, so every edit path, the save and the
autosave ignore them, and `TestMaps(sessionId)` lists them for the entity count and deletion.

## Entering and leaving

The observer is kept and parked in nullspace (`TransformSystem.DetachEntity`) with its map coordinates recorded;
returning puts it back there (or at the root map's origin if that level is gone) and moves the mind back with
`TransferTo`. The member stays in `Present` for the whole test, so the session state still lists them and
`TryGetPresent` keeps working; `IsTesting` is checked in `CanMap`, `CanEdit`, the tools gate, the engine placement
gate and the decal gate, each refusing with `wf-mapping-test-no-edit`. `OnObserverDetached` skips a member who is
testing, since the detach is the move onto the character.

The tester's `PlayerDetachedEvent` ends their test (disconnect, or an admin moving them); if nobody is on the observer
afterwards the member is marked absent through `ObserverGone`, which is what a detached observer does. Death sets
`ReturnAt` 3 s later; the test system's `Update` sends them back. `LeaveSession` and `CloseSession` call
`EndTest` first so the normal return-to-lobby finds the player on the observer. `ClearState` forgets the testers on a
round restart.

## Limits

A test is refused when the session is over 60% of `max_entities` (the copy would push it towards the cap), during
`test_cooldown` after the last test ended, and while the player is already testing. Test maps are added to
`EntityCount`, so the live limits line shows the doubled count while a test runs. One test per session: a second
member's Test map joins the running one at the same spot.

## Spawn spot

A `SpawnPointComponent` on the ship wins; else the tile in front of the first shuttle console found
(`LocalRotation.ToWorldVec()` from the console, which is the side a player uses it from) when that tile exists; else
the floor tile with no anchored entity nearest the grid's centre; else the centre itself. A level with no grid spawns
at the map origin.

## Client

The gameplay HUD comes back on its own: `WFMappingClientSystem.InSession` reads the observer component off the local
entity, and the tester has none, so the mapping screen tears down and the chosen layout loads. `WFMappingTestClientSystem`
mounts `WFMappingTestBanner` (a plain `PanelContainer`) on `UIManager.ActiveScreen` anchored top-centre whenever the
local entity carries `WFMappingTesterComponent`, re-parenting it if the screen is reloaded, and binds
`WFMappingTestReturn` into `common` for the duration. Nothing binds the key by default; the banner shows the key when
one is bound. The top bar's Test map button (`test.png`, a play triangle drawn like the other two icons) joins a running
test; its tooltip names the testers while one runs.

## Not verified here

A real client wasn't driven: the banner, button and tooltip are built and compile, the headless client's sandbox
typecheck passes (`Content.Client.exe --headless`, no `Sandbox violation`), and the integration tests cover the server
flow and the bar button. The character's HUD, chat and interaction on the test map, the banner's look, and a second
member joining a running test weren't checked by hand.
