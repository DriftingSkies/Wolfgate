# Safety, demolition and the crash effect

How a vehicle's interior coexists with the rest of the game, and the two features that act on whoever is inside. The
safety system is new Wolfgate code (`Content.Server/_WF/Vehicles/Safety`); demolition and the crash effect are RMC-14's,
as partials of `VehicleInteriorSystem` (see `interiors.md`).

## Why it is needed

An interior is a private map and its grid is an ordinary grid, so Wolfgate's own systems act on it as they would on any
other. `VehicleSafetySystem` (server) is the one place that answers for that.

- **Cleanup.** Hull cleanup deletes any empty, unflagged grid, and the interior of a parked vehicle is one. On
  `VehicleInteriorLoadedEvent` the grid gets `CleanupImmune` and a `ProtectedGrid` that stops floor removal, floor
  placement, RCD use and explosions. The vehicle base needs `CleanupImmune` too (and `FTLSmashImmune`, a `StaticPrice` and
  `save: false`); `WFVehicleTestChassis` and `RMCVehicleBase`, and so every vehicle, have them. The same
  cleanup counts only players near the hull, and a player in a vehicle is on another map: while one is inside, the sweep
  below puts the cleanup timer of the hull the vehicle stands on back to zero.
- **Explosions on the interior.** `PreventExplosions` cancels any explosion centred on the interior grid. That keeps the
  floor from being blown out under the occupants, and it also means an explosive grenade dropped through a doorway
  (`VehicleEntranceItemSystem`) goes off with no blast; flash, stun and smoke ones still work. Whether to give that up
  for the grenade drop is an open owner call; `VehicleSafetyTest.ExplosionsOnTheInteriorFloorAreShieldedFromBlasting`
  pins the current behaviour.
- **Deleting the vehicle.** A vehicle's interior map goes with it, and a grid takes its children with it when deleted.
  On `EntityTerminatingEvent` of a `VehicleEnter` everyone inside is moved out first: `VehicleInteriorSystem.EjectAllOccupants`
  takes every mob and ghost on the interior map and the unanchored items lying on its floor (not the vehicle's key) to the
  vehicle's coordinates. When the vehicle's parent is being deleted too (its hull, a container) they are put on the map at
  the vehicle's world position, never on the dying grid. Nothing is done when the whole map is going.
- **Selling a hull.** The shipyard's `FoundOrganics` only walks the hull's transform children, and a vehicle's occupants
  are on another map. It raises `ShipyardOrganicsSearchEvent` on each child (one marked edit in
  `ShipyardSystem.Consoles.cs`), and a vehicle answers with the name of a living, sapient being on its interior map, so the
  sale is refused with the usual "organic aboard" message. The sale also moves the hull's IDs, PDAs and cash to the console
  (`CleanGrid`), but only those it can walk to from the grid; `VehicleSafetySystem` handles `ShipSoldEvent`, raised just
  before the hull goes, and moves the same things found on the floor or in containers of each parked vehicle's interior.
- **Strays.** Once a second, anyone (a mob that is not a ghost) on an interior map who is off the grid or over an empty
  tile is sent back to the entry point, with their pulls kept, and at rest: over the void nothing slows a mob, so one
  thrown out of the cabin would otherwise drift out again and be sent back every second, each teleport breaking whatever
  it was doing (leaving, say). The cabin is every tile that is not empty, `RMCVoid`
  included: RMC builds the humvee cabins entirely out of it and rings the vans' floors with it, and an empty tile
  (`Space`) is what lies outside. Anywhere the game puts someone has to pass this test, or the sweep drags them off it every
  second: `VehicleRosterTest` checks the entry point, each doorway's landing spot, each exit marker and each seat of every
  vehicle.

## FTL

A hull that jumps carries its parked vehicles: they are children of the grid, and the interior map is separate and
untouched, so occupants stay where they are and exit onto the deck wherever the ship has arrived. The arrival sweep
(`ShuttleSystem.Smimsh`) deletes everything parented to the map under the landing footprint; it does not look at entities
parented to another grid, and `FTLSmashImmune` on the vehicle covers one that is loose on the map.

## Demolition

`VehicleDemolition` (on the vehicle) accepts any item with the `RMCVehicleDemolitionCharge` marker (`WFVehicleDemolitionCharge`
is a C4 that has it) once the frame is destroyed (`HardpointIntegrity` at zero). RMC asked for an engineering skill; here
anyone may plant it. The plant do-after (`doAfter`, 45 s) uses the charge up, then the wreck beeps (`beepInterval`) for
`timerDelay` seconds, ejects everyone inside with `EjectAllOccupants`, spawns `explosion` (`WFVehicleWreckExplosion`) and
deletes the vehicle. Those inside stand on another map, so arming also warns each of them and every beep is played on the
interior grid as well as at the vehicle. A planted charge cannot be disarmed. The planting hold, like a doorway's, lapses
when the planter is deleted. The wreck explosion uses RMC's intensity numbers on Wolfgate's `FireBomb` type: it burns and hurts but
does no structural damage and breaks no floor, so a wreck on a ship deck cannot hole the hull.

## Crash effect

`VehicleInteriorSystem` handles the mover's `VehicleCollisionEvent` of kind `Crash` on `VehicleEnter`. At 15% of top speed or
more it plays the vehicle's `CrashSound` (with its cooldown) and, for everyone inside, kicks the camera along the direction
of travel (`SharedCameraRecoilSystem.KickCamera`, 0.2 per strength step instead of RMC's camera shake). Anyone not strapped
in is also paralysed for 1 s, knocked down for 2 s and thrown `2 x strength` tiles along the vehicle's axis at speed 10,
where strength is 1 to 3 by how fast the vehicle was going. These numbers are RMC's. A vehicle without `VehicleEnter` gets
none of it, sound included.
