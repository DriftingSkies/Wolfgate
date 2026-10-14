# Interiors

How a vehicle becomes enterable, what happens at the door and who counts as driving. Ported from RMC-14's
`Vehicle/Core` and `Vehicle/Viewport` with the xenonid, skill, power loader, area and construction code stripped (see
`design.md`).

## Model

- A vehicle with `VehicleEnter` can be walked into. `interiorPath` names a map under
  `Resources/Maps/_WF/Vehicles/Interiors`; it is loaded on the server the first time someone enters and kept until the
  vehicle goes. The map is private: the vehicle and the map entity point at each other (`VehicleInterior` on the vehicle,
  server only; `VehicleInteriorLink` on the map entity, networked).
- `entryPoints` are the doorways: an `offset` in the vehicle's own frame, a `radius` to stand within, and the
  `interiorCoords` on the interior grid where an entrant lands. A user is at a doorway when their position, taken through
  the vehicle's world matrix, is within the radius. Nothing is cached between uses, so a vehicle on a turning or moving
  ship deck works the same as one on the ground.
- `VehicleExit` markers (prototype `VehicleExitArea`) inside the map lead back out. `entryIndex` picks the doorway the
  exit lands at; with no match the `exitOffset` is used. Exit coordinates are taken in the vehicle's parent frame, so an
  exit from a vehicle on a ship deck lands on the deck wherever the ship is by then.
- Entering and leaving run a do-after of `enterDoAfter` and `exitDoAfter` seconds. The enter do-after is not aimed at the
  vehicle, so on completion it checks again that the user is still at the doorway. A doorway or exit is held while its
  do-after runs; the hold names its user and lapses when they are deleted (a do-after of a deleted user reports nothing)
  or a second after the do-after should have ended. Whoever is pulled comes along:
  `VehiclePullingSystem.HandlePulling` stops the pull, moves both and starts it again on the other side, and
  `VehicleInteriorTest.PulledEntityComesAlong` checks that the pull is intact after going in and after coming out.
- `maxPassengers` caps the living occupants: mobs that are not dead. Dead bodies and anything dragged in (a crate) take no
  seat. There is one occupant set; the xeno set and the job-reserved pools of RMC are gone.
- Leaving has somewhere to land, and never on the far side of anything. The exit marker names a doorway; if a wall, door,
  furniture or another vehicle stands there, or there is no floor under it, the vehicle's other doorways are tried, then
  the free floor nearest that doorway that can be walked to from the hull (`VehicleExitSpotSystem`). A place is free
  only if nothing hard blocks it on any grid or on the map, and its floor is the topmost one there: on a planet, where a
  ship lies as a grid over the ground, a tile under the ship's wall or floor is the ship's, not the ground's. The walk
  starts at the tiles that touch the hull, goes over free tiles one step at a time (not across a corner), stays within 5
  tiles of the hull and stops at walls, closed doors, windows, furniture and the edge of a deck or ship, so a sealed room, a
  brig or another ship beside the vehicle is never reached. Of the places it reaches the vehicle's own grid's come first,
  then the nearest to the hull, then the nearest to the doorway. Only when there is no floor to stand on at all does the
  exit put the user over open space, at the doorway. A vehicle walled in on every side, with every tile around it
  taken, keeps the user inside; the exit works again when a tile opens. Whoever is pulled is put with the user. Ghosts go
  where the marker says.
- Everyone inside carries `VehicleInteriorOccupant` and the `ExtraTransformEvents` meta flag, so a map change untracks
  them however they leave.

## The interior map

- `EnsureInterior` loads the map with `MapLoaderSystem.TryLoadMap`. It treats the interior as loaded only while its map
  exists and its grid is not terminating; otherwise it puts anyone still on the old map back beside the vehicle, deletes
  it and loads a fresh one. A map without a grid is an error and is deleted again.
- After a load it raises `VehicleInteriorLoadedEvent(Vehicle, MapUid, Grid)` on the vehicle and broadcasts it. The safety
  system subscribes to it to protect the grid from cleanup and edits; `VehicleInteriorComponent` and the other
  interior components are readable from other systems for that.
- Deleting the vehicle (`VehicleEnter` shutting down) deletes the interior map. The safety system moves whoever is inside
  out first, when the vehicle starts terminating.
- The maps are converted from RMC's by `Tools/_WF/Vehicles/convert_interior_map.py`, never edited by hand, so a re-run keeps
  every fix. Besides renaming ids and dropping what Wolfgate has no use for, it lays `RMCVoid` (`cabin_tile` in
  `interior_map_mapping.yml`) under any entity the source stands over an empty tile that touches the cabin. RMC has no
  out-of-bounds sweep: its SPP APC has two alcove seats, two lockers, an extinguisher, a NanoMed and two invisible walls over
  gaps in the wall ring, and the sweep would send anyone buckled in such a seat back to the entry point every second. A tile
  that touches nothing is left alone and reported.
- RMC's mappers also left those eight and one invisible wall of the humvee parented to the map entity, not the grid. A child
  of the map has no `GridUid` and is not anchored, and nothing that walks the grid's children sees it, so the converter
  parents every such entity over a cabin tile to the grid, turning its position into the grid's frame, and deletes the one
  that stands over no cabin tile (the humvee's wall, four tiles outside the cabin). `verify` and the universal roster test
  fail on an interior entity that is still a child of the map.
- Props that differ from RMC, none of which matters in play because the interior grid is protected and cleanup-immune: the
  seats are indestructible (RMC's passenger seat broke at 50 damage into a sheet of metal; its driver seat could not be
  damaged); the interior lockers sit on Wolfgate's `ClosetBase`, so they break at its thresholds (150 and 300, leaving a sheet
  of steel); and the medical humvee locker holds no surgical tray, because `RMCSurgicalTray` does not exist here.
- `Maps/_WF/Vehicles/Interiors/wf_test_interior.yml` is a 5 by 4 room with two exit markers and a driver seat. The tests use
  it; the vehicles use the maps converted from RMC's and CMU's.

## Locks and keys

- `VehicleLock` holds the lock state and the key id (a GUID made when the vehicle spawns). A locked vehicle refuses
  entry at the door and exit at the marker. Ghosts ignore it.
- A key is `VehicleKey` (`RMCVehicleKey`, `...Blank`, `...Duplicator`). One is spawned at the driver seat when the interior
  first loads. Using a key on the vehicle or on an exit marker toggles the lock; a blank key imprints on first use (not
  on a locked vehicle, unless the user is inside it) and a duplicator copies another key.
- A driver can lock and unlock from the seat with the `ActionVehicleLock` action. Cutting the lock (a `Cutting` tool,
  60 s, with the horn and light as an alarm) breaks it, a `Screwing` tool repairs it, and a destroyed frame unlocks it.
  The pry-open path for xenos is not ported.

## Driving

- Buckling into a `VehicleDriverSeat` makes the user the vehicle's operator, points their eye at the vehicle and gives them
  the lock and view-toggle actions; unbuckling takes all of it back. Before the buckle goes through, the seat raises
  `VehicleSeatAttemptEvent` (occupant, user, whether to pop up, a `Cancelled` flag). Nothing cancels it yet; it is where
  access rules for seats go, and the weapon and port gun seats raise it too.
- Someone strapped in while unable to act (critical, or asleep) is not made operator. They carry
  `VehicleDriverRecoveryComponent` and become the operator, with the lock action, when they wake or are revived, unless
  they were unstrapped or another operator has the wheel by then.
- `VehicleViewToggle` is one action per user, shared by every source that grants it (the driver seat, a viewport). It swaps
  the eye between the vehicle's outside and the previous target.
- `VehicleViewport` entities in the interior show the outside when used, and `Look inside` on a doorway shows the interior
  from outside (server system, client part hands the eye back when the user moves). A view also ends once a second check
  finds the viewer off the interior map (viewport) or no longer at a doorway of the vehicle (peek), and when the viewer is
  deleted; a driver cannot use a viewport, which would overwrite the seat's view toggle. A locked vehicle can still be
  peeked into, as in RMC.
- A primed grenade used on a doorway lands on the interior floor (`VehicleEntranceItemSystem`). Flash, stun and smoke
  ones work there; an explosive one goes off on a grid that is shielded from explosions (see `safety.md`) and does nothing.

## What is not here

- The crash effect on occupants, demolition and `EjectAllOccupants` are partials of `VehicleInteriorSystem`
  (`Crash`, `Demolition`, `Eject`); cleanup immunity, floor protection, ejection when the vehicle is deleted, the sale
  guard and the out-of-bounds sweep are `VehicleSafetySystem`. All are described in `safety.md`.
- The doorway overlay (`rmc_vehicle_entries`, admin only) draws the doorways, landing spots and exit markers. It is an
  overlay of its own, apart from the mover's debug overlays.
