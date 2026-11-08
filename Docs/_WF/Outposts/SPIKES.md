# Outposts spike results

Results of the F0 spikes in `PLAN.md`, for the design doc's open questions. Each names the test that measured it.

## F0.1 Breach: can raiders get into a separate outpost grid?

Yes, with two things the outpost has to provide. `OutpostBreachSpikeTest` puts a 5 x 5 outpost grid, walled all
round with `WallSolid` and anchored as F1.1 will anchor it (ForceAnchor, PreventGridAnchorChanges, shuttle disabled),
into a hole carved in a planet-layer ground map, and sends a smashing xeno at it from 20 tiles out.

- **The outpost needs a ring of pathfinding portals.** The ground and the outpost are two navmeshes, and a path
  crosses between grids only through a portal (`PathfindingSystem.TryCreatePortal`, which docking uses). With none,
  steering finds no path onto the outpost at all. Even when the NPC breaks the wall by attacking it directly, it can't
  path through the hole it made and stands outside. A portal joining each edge tile to the ground tile outside it fixes
  this, and the ground's lazy navmesh links them as soon as anyone is near. The same portals let NPCs inside an outpost
  (pets, livestock, NPC crew) walk out onto the ground, which they couldn't do otherwise either.
- **The breach must be a walk-up, then an attack on the wall as a target.** Sent straight at the middle, even with
  portals, A* finds no path: a wall costs `10 + HP / 100` tiles' worth (23 for 1,300 HP of solid wall) and A* gives up
  after 512 nodes, which it spends on open ground first. In three stages it works: walk the ground to the tile outside
  the nearest edge tile, melee whatever blocks that tile (`NPCMeleeCombatComponent.Target`, the wall and then its girder),
  then walk in. The xeno stood inside after 15 s, the same as with the walls built straight onto the ground (the control).
  A separate grid costs a raid nothing.
- **Steering's own wall smashing doesn't work on main**, on one grid or two; the breach stages don't use it:
  - A non-crew NPC that reaches a wall node keeps pushing into it. Steering waits for it to slow down before it smashes
    (`LinearVelocity > 0.01` returns early), but only access-aware crew have their input zeroed there
    (`NPCSteeringSystem.Context.cs`, the `NpcCrew` marker that sets `moveMultiplier`). A xeno pinned at full speed never
    attacks. Zeroing it for every NPC made the xeno hit the wall at once in a local trial; that would change how every
    smashing NPC behaves (xenos, carp, hivebots), so it is left for a separate decision.
  - When a smash is on cooldown, the smashing branch of `TryHandleFlags` returns `Failed`, so steering reports no path
    after the first hit (stock upstream; an HTN replan hides it). Melee combat then calls its target unreachable. The
    breach operator has to start steering afresh when that happens, as the test does.

Breach speed is balance for the prefab walls (F2.2) and the raid tiers (F8). The test's xeno bites for 150 Blunt so it
doesn't spend a minute on solid wall.

What this changes in the plan: F1.1 creates the portal ring, F1.2 extends it as plates extend the grid, F1.3 rebuilds
it on load, and F8.3's breach operator has the three stages above.

## F0.2 Save benchmark: how long does saving and loading an outpost take?

`OutpostSaveBenchmarkTest` (explicit; run it with
`Tools/_WF/Testing/test.ps1 -Test OutpostSaveBenchmarkTest -Shipyard`) builds a 40 x 40 grid (1,600 tiles) with an
outpost-like mix: walls, cables, pipes, lights, tables, loose tools and sheets, filled engineering lockers, APCs,
substations, SMES, generators, vents and airlocks. It saves the grid to YAML, gzips it, unpacks it and loads it onto a
fresh map. Measured in DebugOpt (what CI runs) on a 32-thread desktop, after a warm-up save and load:

| Entities | Save | YAML | gzip Fastest | gzip SmallestSize | Unpack | Load |
|---|---|---|---|---|---|---|
| 500 | 119 ms | 259 KiB | 0 ms, 18 KiB | 2 ms, 10 KiB | 0 ms | 96 ms |
| 1,013 | 206 ms | 493 KiB | 0 ms, 29 KiB | 3 ms, 17 KiB | 1 ms | 160 ms |
| 2,006 | 451 ms | 964 KiB | 1 ms, 52 KiB | 7 ms, 29 KiB | 1 ms | 306 ms |
| 4,000 | 861 ms | 1,898 KiB | 1 ms, 97 KiB | 13 ms, 53 KiB | 11 ms | 660 ms |

Every entity came back on load. Save and load scale linearly: about 0.21 ms an entity to save and 0.16 ms to load,
both on the main thread, so they stall the tick. Compression costs nothing next to them, and the packed save is small
enough for the database at any of these sizes (about 25 bytes an entity with gzip Fastest, 14 with SmallestSize).

What this means for the caps:

- **Entity cap 2,000 for v1.** That is a half-second save and a third of a second load. At 4,000 a save stalls the
  server for most of a second, which an autosave every 10 minutes per outpost can't afford.
- **Autosave has to stay staggered and skip unchanged outposts** (F1.4 already says so): two outposts saving in the
  same tick at the cap is a one-second stall.
- **Round-start loads one per two ticks** (F4.2) is one 300 ms stall per outpost at the cap, back to back. Spread
  them a second or more apart instead.
- **Store saves with gzip SmallestSize:** half the size of Fastest, still well under the save itself, and it can run
  off the main thread once the YAML is written.

The mix is heavy on machines with many components, so a real outpost of the same count is likely a little cheaper.
These are dev-machine numbers; a production server's CPU will be slower, so measure there again before raising the cap.

## F0.3 Roof and outdoors: does an outpost go by its roofs?

Yes, once it stops looking like a vessel. `OutpostRoofTest` founds an outpost on a planet's ground and checks its
tiles.

- A grid made at runtime gets `ImplicitRoofComponent` (`ShuttleSystem.OnGridInit`), which roofs every tile, and on a
  planet layer any grid but the ground and a fixed city's levels counts as a vessel, covered throughout
  (`SharedWeatherSystem.WfPlanetExposure`). An outpost would never see rain.
- The fix is a new Planets marker, `WFPlanetTerrainGridComponent`: a grid that is buildings on the ground. The four
  checks that let a city grid go by its roofs accept it too: weather exposure, the roofed-below check for z-levels, the
  planet view's "inside a building" test and the z-level ship shadow. The outpost drops its implicit roof for a
  `RoofComponent` of its own.
- Then a bare outpost tile is outdoors (rained on, struck by lightning), a wall's tile is roofed (walls carry
  `IsRoof`), and a tile under a roof panel (`WFOutpostRoofPanel`, owner only for now) is roofed. Thunder picks its tile
  through the same exposure check, so it never lands on a roofed outpost tile.

Not checked: the overlay itself. The roof bits are what the client's roof overlay draws, but no screenshot was taken;
the test is headless.

Atmosphere needs nothing: a runtime grid of seven tiles or more gets a grid atmosphere of its own
(`AutomaticAtmosSystem`), so an outpost holds air like a landed ship, its open edges sharing the planet's.
