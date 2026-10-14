# Vehicles design notes

The plan and the decisions behind the Vehicles module. Where each file came from is in `port-manifest.md`.

## What it is

RMC-14's ground vehicle framework (MIT) plus Colonial Marines Universe's extra vehicles (AGPL-3.0), brought over as one
module under `_WF`. The framework drives a vehicle on a grid-locked mover, gives it a walkable interior on its own map,
and builds it from hardpoints (wheels, treads, armour, turrets, guns) that carry the integrity. CMU's vehicles are
re-authored as thin YAML children of RMC's bases; CMU's C# is copied only for emergency lights and the Bison cargo
carrier.

## Decisions

- One module, `Vehicles`, entirely under `_WF`. RMC and CMU prototype ids, component registration names, BUI classes,
  UI key enums and existing Fluent keys are kept as in the source. New Wolfgate-authored prototypes take a `WF` prefix.
- Wolfgate's ride-on vehicles (`Content.Shared/Vehicle`, `Content.Server/Vehicle`, `Content.Client/Vehicle` and the
  nine `- type: Vehicle` prototype blocks) are left alone. RMC's base is renamed on import: `VehicleComponent` is
  `RMCVehicleComponent` (registered as `RMCVehicle`), `VehicleOperatorComponent` is `RMCVehicleOperatorComponent`,
  the base `VehicleSystem` is `RMCVehicleSystem`, `VehicleVisuals` is `RMCVehicleVisuals`, and RMC's interior system
  `VehicleSystem` is `VehicleInteriorSystem`. The prefix also keeps the names clear of the `VehicleOperator` component
  upstream SS14's new vehicle system registers.
- Everything that needs xenonids, marines, skills, squads, the tactical map, requisitions, areas, RMC power, the power
  loader or IFF is stripped, not stubbed. Skill checks become one by-ref attempt event, `VehicleSeatAttemptEvent`,
  that nothing cancels yet; driving and gunning are gated by the vehicle key and lock only.
- No mob blocks or pushes a vehicle. Crushing is allowed (owner decision): a vehicle smashes the Wolfgate equivalents of
  what RMC marks `VehicleSmashable` (tables, lockers, crates, fences, windows, unpowered doors and so on) by RMC's
  weight-class rules, through data rules that leave Wolfgate's prototypes alone, and runs mobs over as RMC does. Doors
  open for a driver with access. A ship's hull parts (reinforced and shuttle windows, external and docking airlocks) break
  under a tank only (owner decision): vans, humvees, APCs, the Bison and the civilian vehicles are stopped by them. Walls,
  safes, a ship's machines and the economy's fixtures are never crushed. `crushing.md` has the rules, the mapping and how
  to add one.
- Vehicles are obtained by admin spawn. The supply console, lift, vendor and roundstart code are not ported.
- Vehicles work on planet ground and on ship and station decks, including a deck that is moving, turning, docking or
  jumping. A vehicle is stopped at the edge of the floor, except where a CE z-level lies below: there it drives off,
  falls one level and takes damage (owner decision). Space, a ship in flight and the bottom level keep the plain
  refusal. `falling.md` has the rule and what is not verified.
- Leaving a vehicle puts the user on free floor they can walk to from the hull, never behind a wall, a closed door or a
  deck edge; a vehicle walled in on every side keeps them inside (`interiors.md`).
- The guns keep their sources' damage, fire rates and speeds (53 tiles a second for RMC's, 62 for CMU's, the few RMC
  guns that set one excepted) and the heavy shells keep RMC's explosion types. A round that flies to an aimed point
  stops at the first wall, closed door, window or hull it meets (`armed.md`).
- RMC weapon damage numbers and lore text are kept as in the source for now.
- Deferred: flamers, the Humvee ARC deploy turret (the ARC ships as a plain variant), tank-roof riding, the action
  hotbar override, roof hiding while peeking, IFF, CMU's gameplay systems (failures, cook-off, maintenance, plows, wall
  smashing), toxic water, squeezing under vehicles.

## Layout

| Area | Folder |
|---|---|
| Shared C# | `Content.Shared/_WF/Vehicles/{Base,Bison,Compat,Core,Crushing,Falling,Grid,Hardpoint,Turret,Viewport,Weapons}` |
| Server C# | `Content.Server/_WF/Vehicles/{Bison,Core,Crushing,Falling,Hardpoint,Safety,Viewport,Weapons}` |
| Client C# | `Content.Client/_WF/Vehicles/{Falling,Grid,Interaction,Overlays,Turret,Ui,Visuals}` |
| Prototypes | `Resources/Prototypes/_WF/Vehicles/{Actions,APC,Base,Bison,CMU,Crushing,Damage,Demolition,Hardpoints,Humvee,Interiors,Seats,Tank,Tiles,Van,Weapons}` |
| Interior maps | `Resources/Maps/_WF/Vehicles/Interiors` (loaded server-side only, so Maps rather than SharedMaps) |
| Assets | `Resources/Textures/_WF/Vehicles`, `Resources/Audio/_WF/Vehicles`, `Resources/Locale/en-US/_WF/Vehicles` |
| Tools | `Tools/_WF/Vehicles` |

Namespaces follow the folders, as AGENTS.md asks: `Content.Shared/_WF/Vehicles/Core/X.cs` declares
`Content.Shared._WF.Vehicles.Core`, and the tests follow their folder the same way
(`Content.IntegrationTests.Tests._WF.Vehicles`). The one file that keeps an upstream namespace is the client
`Turret/GunSystem.AimOrigin.cs`, a partial of the upstream `GunSystem`. `Tools/_WF/Vehicles/fix_namespaces.py` puts any
file that arrives in the old flat namespace where its folder says (see The tools). The engine finds BUIs, components and
YAML type tags by type name and enum keys by their short name, so a namespace never appears in a prototype.

The client's debug overlays and commands live in `Overlays`, not `Debug`: the repository `.gitignore` ignores every
`Debug/` folder, so files there are never committed.

Edits outside `_WF`, seven files, each marked `WOLFGATE(Vehicles)`: `CollisionGroup.Vehicle`; the shot origin and the
extra `AttemptShootEvent` members in `SharedGunSystem`; the coordinate entity and muzzle flash tracking in the client
`GunSystem`; the click-through check in `GameplayStateBase` and the call in `SpriteFadeSystem` that passes its flag
(`rosters.md`); the organics search over a hull's children in `ShipyardSystem.Consoles` (`safety.md`); and the event probe
system that `TestPair` loads for the tests. Each keeps the upstream text beside it, commented, inside the marked block.

## Compat helpers

- `VehicleHardCollision.IsHardCollidable`: the engine's `SharedPhysicsSystem.IsHardCollidable(FixturesComponent,
  FixturesComponent)` reads the first body's fixtures twice (Robust v277.2.1, `SharedPhysicsSystem.Fixtures.cs`), so it
  gives wrong answers whenever the two bodies' layers and masks differ. The helper is RMC's v264 per-fixture-pair loop.
  The mover must use it; the import script rewrites `_physics.IsHardCollidable(` to it.
- `VehiclePullingSystem.HandlePulling`: teleports an entity and the one it pulls, stopping the pull first and starting
  it again afterwards, so the joint is not carried across a map change. Used by enter and exit.
- `IRefreshableBui` and `VehicleUserInterfaceSystem.RefreshUIs`: the open hardpoint, ammo loader and weapons windows
  redraw when their component state arrives.

## Damage

The eight `Vehicle*` modifier sets (`VehicleArmorBallistic`, `VehicleArmorCaustic`, `VehicleArmorConcussive`,
`VehicleArmorPaladin`, `VehicleFrameTank`, `VehicleFrameAPC`, `VehicleFrameARC`, `VehicleFrameCLFVan`) are copied
unchanged. Vehicle prototypes use `damageContainer: StructuralInorganic`, which supports the same groups and types as
RMC's `StructuralMarine`. Explosions reach hardpoints through `BeforeExplodeEvent` and
`GetExplosionResistanceEvent`, not through an upstream edit.

## The driver and the grid mover

`RMCVehicleSystem` sets and clears the operator (`TrySetOperator`, `TryRemoveOperator`) and puts `GridVehicleOperator`
and `VehicleWatching` on the driver; RMC's `SetRelay` branch is not ported, so a vehicle whose `movementKind` is not
`Grid` has an operator and no movement. `GridVehicleMoverSystem` is RMC's mover: it does not use the physics engine to
move the vehicle. Speed is integrated along one of four grid-local cardinal facings and the transform is rewritten from
the mover's position at a fixed 1/60 s step. RMC tested collision by hand with world AABB probes every 0.1 tile; Hardening
below describes what Wolfgate changed. The body stays `Dynamic` with two hard fixtures, and `PreventCollide` cancels its contacts with static blockers, downed mobs and
anyone it has run over, so physics only shoves loose things.

Stripped from the mover: xeno blocking, shoving, fortify, squeeze-under and size rules, weeds
and water slowdown, toxic water, ride surfaces, barricade handling, RMC power for doors, the autopilot, the xeno-driven
push-speed path and the bypass around blocking mobs. These `GridVehicleMover` fields went with them and prototypes must
not set them: `weedsSpeedFactor`, `xenoBlockMinimumSize`, `xenoPushMinimumSize`, `canXenosPush`, `pushCooldown`,
`pushImpulseSpeed`, `blockingMobBypassNudgeLimit`, `blockingMobBypassNudgeStep`. A vehicle can still push another
vehicle (`canPushVehicles`). Mobs never block: a standing mob is run over at 1.5 tiles per second or more (Blunt of 8
plus 2 per tile a second, times 1.25 for a light vehicle, 1.75 for a tank and 2.25 above, at most 60; a scream; a
knockdown and `VehicleRunover`), slower than that it is left alone.

Speeds are Wolfgate's (owner, 2026-10-13: fast enough to be transport on open planets; a mob sprints at about 4.5
tiles a second). Top speed and acceleration: humvee 14 and 8, vans and the civilian vehicles 13 and 7, APCs 11 and 6
(SPP 11 and 4.5), tanks 7 and 2.5, the AEV 6 and 2.2, the Bison 9 and 6. RMC's and CMU's were 6, 6, 5, 3, 3 and 5. The
turn rates are unchanged, so a vehicle at speed turns in a wide arc and has to slow down for a tight one.

Steering is CMU's, not RMC's. A vehicle with `freeSteering` (every roster vehicle; the test chassis keeps the four-way
drive) reads forward and back as throttle and left and right as steering. The chassis has a turn rate of its own
(`maxRotationSpeedDegrees`, `rotationAccelerationDegrees`, `rotationDecelerationDegrees`), so it holds any heading and
drives arcs. Wheels only turn it while it rolls; a `turnInPlace` vehicle (tanks, the AEV, the Bison) also turns on the
spot. Steering is mirrored in reverse. `alignmentAssistDegrees` straightens a nearly square heading at parking speed, and
only a vehicle square to the tiles gets the four-way drive's lane steering. The heading (`Heading`) is kept in the
grid's frame and written to the transform every step, like the position; `CurrentDirection` is the cardinal nearest to
it. A turn is tested like a move. The upright box round the turned hull only picks what to test; whether the hull meets
a thing, or lies over an empty tile, is asked of the turned rectangle itself (`HullPose`,
`GridVehicleGeometry.Overlaps`), which is CMU's check made exact. Other things are still their own upright boxes. A
vehicle close to a wall still cannot swing its tail into it, since the hull turns about its middle. On the client `VehicleSteeredSpriteSystem` draws one of the four
sprites turned by what is left of the angle: the side sprites within 22.5 degrees of east and west, the front and
back sprites everywhere else.

The driver can switch a vehicle between the two drives (`WFActionVehicleSteeringMode`, given to whoever drives;
`GridVehicleMoverSystem.TrySetFreeSteering`). The choice is the vehicle's and stays with it. Going to the four-way drive
squares the hull to the nearest direction, which needs room; without it nothing changes.

The mover knows neither wheels nor the cabin. When a probe with effects is stopped by something hard it raises
`VehicleCollisionEvent` (kind `Impact`) on the vehicle, and when a drive step ends blocked it raises the same event with
kind `Crash`, both on the server only. The wheel system takes wheel damage from the first and the interior crash effect
from the second.

## Hardening for Wolfgate

RMC's mover assumed a still, unrotated map and no one else moving its vehicles. Wolfgate's does not.

- **The grid's frame.** Position, facing and speed are stored relative to the grid the vehicle drives on, and nothing
  is kept in world space between ticks. The collision test builds the vehicle's boxes and each candidate's box in the
  grid's frame, where the vehicle is always square to the tiles, and only the broadphase query and the debug drawing go
  through world space (as the bounding box of the grid-frame box). So a grid turned 45 degrees lets a vehicle through the
  same opening as an unturned one, and a grid that is drifting or spinning changes nothing: the ship's motion is the
  parent's, never the vehicle's velocity. A grid entity is never a blocker, since it is only its floor.
- **The floor.** Every tile under the vehicle's movement footprint must be floor on the grid it drives on, floor of
  another grid at that point (so a ramp from a ship to the ground works), or allowed by a handler of
  `VehicleOpenTileAttemptEvent` (raised by ref on the vehicle with the grid, the tile and an `Allow` flag that starts
  false; with no handler the move is refused). A vehicle that already overhangs an empty tile may move so that it
  overhangs no more than it does where it stands, so one stranded at an edge can back off. A planet's unloaded ground
  counts as empty until the terrain streams in. The lane steering that slips a blocked vehicle sideways round an
  obstacle asks the same question, so with a handler that allows the tile it can slide a vehicle off the deck. Once the
  vehicle's origin is over an open tile that a handler allows (asked again, with `HandOver` set, at that moment), the
  mover lets the vehicle go to the map, and from there it is no longer driven; `SharedVehicleFallSystem` is the handler
  and takes it from there (`falling.md`). A test that sets a handler's flag must not leave it set on a pooled pair.
- **Moving a vehicle by hand.** The mover writes its own position into the transform at every step. A vehicle that
  something else has put elsewhere on its own grid (an admin's teleport, a portal) would be put back, so the server
  reads the place back when the vehicle is more than half a tile from where the mover has it and has no velocity (a
  shove from physics leaves velocity and is still undone). Speed is zeroed; the facing is read from the transform.
- **Who can steer.** The driver's held keys are read only while they can act: a dead, critical, asleep or stunned driver
  steers nothing, and the vehicle coasts to rest, even though they still hold a key. A driver strapped in while unable
  to act becomes the operator when they can act again (`interiors.md`).
- **Doors.** See `crushing.md`: a door opens for a driver with access whatever the smash rules say, and for a vehicle with
  no driver it is a wall.
- **Changing grid.** When the vehicle's parent becomes another grid (it drove across a seam, or its grid was moved to
  another map) the speed is kept and the facing is read again from the transform in the new grid's frame, so the
  vehicle goes on in the same direction in the world. The held direction keys are still interpreted in the grid's
  frame (RMC's behaviour; the controls are relative to the deck), so holding a key across a seam between grids turned
  differently asks for a turn: the heading is kept only while the key is let go. `GridVehicleFloorTest` pins both.
- **Who hands a vehicle to a grid.** The engine moves an entity to the grid under its origin every time it moves, and a
  shove from physics (a ship impact throws everything on the hull from inside the physics step, so the bodies are
  integrated before any mover can clear the velocity) can carry the origin past a deck edge for a step. So the mover
  switches the engine's grid traversal off for its vehicle and does the hand-over itself, after its own writes and
  for a vehicle that is on the map: the vehicle goes to another grid when that grid has floor under the origin. Over
  no floor at all it stays a child of the grid it is on, so a vehicle whose tile was destroyed is still carried by its
  ship and can drive back; only a handler of `VehicleOpenTileAttemptEvent` lets it go to the map.
- **Smash settings** are read in one place, `GridVehicleMoverSystem.TryGetSmashable`, which returns a
  `VehicleSmashSettings`. It reads `VehicleSmashableComponent` and, for an entity that carries none, the data rules of
  the crushing rules (`crushing.md`); the mover itself does not know the difference.
- **Other movers.** Explosions, the shockwave shove and ship impacts throw vehicles through `ThrowingSystem`. The mover
  handles `ThrownEvent` and, every step, sets the body back to on-ground with no linear or angular velocity, and holds
  the vehicle's position and facing, so a blast leaves a parked vehicle where it was. A vehicle nobody has driven has no
  facing; the mover holds the rotation it last had while the body had no spin, and puts it back when physics spun the
  body or a throw turned it (a throw turns a body that cannot spin to face where it goes, and bodies do not spin by
  default). A vehicle that is off every grid has its body settled the same way. Two vehicles do not collide in
  the physics solver, which only shoved them off their lanes; the mover blocks or pushes them.
- **What is sent.** The mover state is marked dirty only when a step changed it.
- **Limits.** The push buffers are three deep, so a vehicle pushes at most two others; a longer chain is refused. The
  per-mob run-over cooldowns are cleared when the round restarts.

## The tools

`Tools/_WF/Vehicles` holds the scripts, each with its tests beside it
(`python -m unittest discover -s Tools/_WF/Vehicles -p "test_*.py"`).

- `import_rmc.py` copies source files by an explicit list in `import_files.yml`, grouped by work package, and only does the
  mechanical part: line endings, namespaces, the `RMCVehicle*` renames, `readonly` off every `[Dependency]` field, `partial`
  on every type that holds one, and duplicate usings. It never overwrites a file that exists. Its report lists, per file,
  the usings that still point at RMC code Wolfgate does not have and a count of the tokens that mark stripped systems
  (`Xeno`, `Skill`, `PowerLoader`, ...), which is where the hand edit starts. It keeps the table in `port-manifest.md`
  current, and a real import ends by running `fix_namespaces.py`.
- `fix_namespaces.py` sets every module file's namespace from its folder and repairs the usings of the files that name a
  moved type, the marked upstream hooks included. Run it after any merge that adds files to the module, then build; a second
  run changes nothing. `--check` lists what it would change.
- `check_conventions.py` sweeps the module for the AGENTS.md rules a merge tends to break: licence headers, `readonly` on a
  `[Dependency]` field, Fluent ids defined twice or keys used and not defined, TODO and commented-out code, doubled carriage
  returns.
- `convert_interior_map.py` (see `interiors.md`) and `translate_cmu_yaml.py` (see `cmu-civilian.md`) convert the interior maps
  and CMU's YAML; `copy_assets.py` copies sprites, tiles and audio (`assets.md`); `check_sprite_states.py` checks every sprite
  state, overlay state and sound the vehicle prototypes name.
- The source checkouts are found beside this one, or by `WOLFGATE_RMC_PATH`, `WOLFGATE_CMU_PATH` and `WOLFGATE_ENGINE_PATH`,
  or by the tools' `--rmc`, `--cmu` and `--engine` options (`source_paths.py`).
