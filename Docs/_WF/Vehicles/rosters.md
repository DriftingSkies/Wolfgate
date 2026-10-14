# Vehicle rosters: base, interiors, vans and humvees

How the base, the interiors and RMC's vehicles came over from RMC-14, and what was changed on the way. Ported from RMC's
`Vehicles` prototype folder, its `Common` interior set, `vehicle_seats.yml` and the interior maps (see
`port-manifest.md`): the vans and humvees first, and the APCs and tanks the same way (below).

## The base

`Base/base_vehicle.yml` is `RMCVehicleBase`: a grid mover, the two fixtures every vehicle has, the hardpoint window, spotlight,
horn and overcharge sounds and the van's sprite, slots and interior. The Wolfgate additions are `save: false` (a saved
vehicle would point at an interior map that is gone), `CleanupImmune`, `FTLSmashImmune` and a `StaticPrice`.

The file is split in two: `WFVehicleUnarmedBase` is everything above, and `RMCVehicleBase` adds `VehicleWeapons` and its UI
key (the weapons seats) on top of it. Every armed family builds on `RMCVehicleBase`; the AEV builds on the unarmed root (see
`cmu-military.md`). The base also carries `VehicleDemolition`. A child that sets `UserInterface` adds its keys to the
parent's: the `interfaces` dictionary merges, and a key the child lists wins. A child that sets `Fixtures`, `ItemSlots`
`slots`, `HardpointSlots` `slots`, `VehicleEnter` `entryPoints` or `Sprite` `layers` replaces the parent's whole, so it has to
list all of it again. RMC's base also had `RMCNightVisionVisible`, `Corrodible`, `RMCIgnoreGhostInteractionLimits`, `RequireProjectileTarget`
and `VehicleXenoSize`; none has a meaning here. The damage modifier set `StructuralMarine` is empty in RMC, so the base names
none.

Placeholder prices, for the owner to set: van family 15000, humvee 25000, base default 20000.

## Families

- Van (`Van/`): the van, CLF van (it still uses the van interior, as in RMC), box van, pizza van and the SPP van with its
  medical, prisoner, logistics and armed variants.
- Humvee (`Humvee/`): humvee, medical, transport and ARC. The ARC is a plain variant: no deployable auto turret, no sensor,
  no deploy-gated hardpoints, no reserved passenger pools.

Wheels are fitted at spawn. RMC leaves the van's wheel loose and gives the humvee its own, but an admin-spawned van that
cannot move is no use. `VehicleSPPVanWheel` is the only new hardpoint; the humvee wheel, hatch, snowplow and overlight were
already in `Hardpoints/`.

The armed roster fits them: `VehicleHumvee`'s `turret` slot starts with `VehicleHumveeTurretArmed` and `VehicleSPPVanArmed`'s
`secondary` with `VehicleSPPVanTurret`, as in RMC (`armed.md`).

## Interiors

- `Interiors/common_interiors.yml`, `viewports.yml`, `markers.yml` and `Seats/seats.yml` hold the shared pieces, and
  `Van/*_interiors.yml` and `Humvee/humvee_interiors.yml` the family ones. Ids are RMC's.
- The interior grid has no power, so nothing needs an APC. The NanoMed and the wall cameras are props; the medical and blood
  vendors, wall phones and the supply vendors are gone and the converter deletes their placements.
- Every ammo loader (`VehicleAmmoLoader` and the humvee, SPP van, SPP APC, SPP tank and tank ones built on it) carries the
  `VehicleAmmoLoader` component and its window (`ammo.md`); `VehicleRosterTest` checks that each one an interior places
  has both. The gunner seats (`RMCSeatArmoredWeaponsOperator`,
  `RMCSeatHumveeWeaponsOperator`) are real weapons seats, with `VehicleWeaponsSeat` and the weapons window.
- The cabins are built of the `RMCVoid` tile: the humvees entirely, the vans as a ring round a few floor tiles. An empty tile
  (`Space`) is what lies outside, and it is the only thing the safety sweep treats as outside (see `safety.md`).
- `VehicleSPPTankSmallLockerAlt` and its base live in `Van/spp_van_interiors.yml` because the SPP logistics van places one.
- RMC removed components from a parent with `RemoveComponents`. Here a child takes the click-through off with
  `InteractionTransparency enabled: false` and the context-menu hiding off with an empty `Tag`.
- RMC's `CMWindoor` (the prisoner van's cell doors) becomes `WFVehicleWindoor`, Wolfgate's windoor with `needsPower: false`: a
  powered windoor on a grid with no power can only be pried open, which would lock in anyone who walks in at the back.
  `VehicleRosterTest` opens every door in the interior by hand before it leaves through the exit marker.
- The converter gives every interior map a `MapAtmosphere` (standard air, immutable). With the grid atmosphere gone the cabin
  reads the map's air, and RMC's maps carry none, which left the cabin in vacuum.

## Click-through and fading

`InteractionTransparency` lets a click pass through a piece while the local player stands inside its sprite, so a cabin roof
or wall does not swallow it. The client system is `InteractionTransparencySystem`; the hook is the marked check in
`GameplayStateBase.GetClickableEntities`. Roofs also carry the stock `SpriteFade` (RMC had its own fade component) and are
`Clickable`, because Wolfgate's fade only fades what it can find as clickable under the player. The fade system asks for the
roofs with `ignoreInteractionTransparency: true`, a marked one-line edit in `SpriteFadeSystem`, so a roof the player stands
under still fades.

## The test

`VehicleRosterTest` visits every concrete vehicle that has `RMCVehicle` and `VehicleEnter` and an interior under
`/Maps/_WF/Vehicles/Interiors` (the test interior is excluded): starting parts fitted, can run, interior loads with a driver
seat, an exit marker for every doorway and breathable air, every landing spot, exit marker and seat is over a tile the
out-of-bounds sweep leaves alone, every ammo loader it places has its component and window, every doorway takes a person in
and the matching exit marker puts them back, and deleting the vehicle takes its interior map with it. It expects at least 50 vehicles: RMC's 21 (9 vans, 4 humvees, 5 APCs
and 3 tanks), the 17 civilian and 4 military CMU vehicles (`cmu-civilian.md`, `cmu-military.md`) and the 8 pre-armed variants
(6 on RMC's bases, `armed.md`, and the 2 CMU tanks), and that at least one of them starts with an armed turret. The Bison
(`bison.md`) has no interior and is not in the walk.

`VehicleSeatPrototypeTest` checks that every seat with `VehicleWeaponsSeat` has the `VehicleWeaponsUiKey.Key` interface.

## APCs and tanks

Ported from RMC's `Vehicles/APC` and `Vehicles/Tank` folders and their seven interior maps the same way as the vans and humvees.
Ids, handling numbers, hardpoint delays, lore names and descriptions are RMC's.

- APC (`APC/`): `VehicleAPC`, `VehicleAPCTracked`, `VehicleAPCMed`, `VehicleAPCCommand` and `VehicleSPPAPC`. RMC's tracked APC
  only added the tactical map tracker, so here it is the plain APC. The medical and command APCs have the two side doorways
  only: RMC gave them a third, at the rear, copied from the van's, but their interior maps have no exit marker for it.
- Tank (`Tank/`): `VehicleTank`, `VehicleSPPTank` and `VehicleSPPTankCommand`. The debug autodrive tank is not ported, nor the
  roof riding surface (deferred).
- Prices are placeholders for the owner to set: the APC family 40000, the tank family 80000.
- No xeno caps or push fields, job pools, tactical map, roof riding, port gun slots or `RemoveComponents`. The mover has no
  `pushCooldown` or `blockingMobBypassNudgeLimit`, so the tank's two numbers for them are gone; the rest of the handling is RMC's.

### Parts

The wheels (`RMCAPCWheel`, `VehicleSPPAPCWheel`) and treads (`VehicleTankTreads`) are fitted at spawn, as are the turrets:
`VehicleTankTurret` on the tank, `VehicleSPPTankTurret` on the SPP tank and `VehicleSPPAPCTurret` on the SPP APC. A turret is a
`VehicleTurret` that turns toward the gunner's cursor (see `weapons.md`) and has nested slots for its guns, which start empty.
The tank turret is heavy: no power loader, it goes in with the slot's 3 second delay and comes out the same way with a
prying tool. The SPP armour and support parts that carry no gun (`VehicleSPPTankReactiveArmor`, `VehicleSPPTankWarningArray`,
`VehicleSPPTankOverdriveEnhancer`, `VehicleSPPTankArtilleryModule`) are in `Hardpoints/spp_hardpoints.yml`.

The armed roster (`armed.md`) fits the weapons. The APC's primary, secondary and support slots start empty, as in RMC, and
`APC/armed.yml` has the armed twins; the APC's `port-gun-north` and `port-gun-south` slots hold the two port guns; the SPP APC
and tank turrets start with their guns. The APC comms relay (`RMCAPCCommsRelay`) is left out for good: its only effect was the
tactical map sensor.

### Interiors

- The ids and structure are RMC's. `RMCSpriteFade` became `SpriteFade` on clickable roofs; `RemoveComponents` became a disabled
  `InteractionTransparency` or an empty `Tag`. The tank's exit hatches (`VehicleTankExitHatch`, `VehicleSPPTankExitHatch`) are
  built without the wall's body instead of removing its fixtures.
- The APC exit doors (`VehicleAPCExteriorDoor`, `VehicleAPCExitDoor`, `VehicleSPPAPCExitDoor`) are `WallMount`. The medical and
  command maps put the door on the same tile as a wall piece, and an interaction ray from the doorway stops at that wall unless
  the user is almost touching the door, which the 0.05 tile offset of the medical doorway made impossible.
- The tank seats (`Seats/armored_seats.yml`) carry `VehicleWeaponsSeat` and the weapons window: the driver also works the support
  hardpoints from the hotbar, the gunner is the primary operator and the commander takes the secondary and launcher types.
  `RMCSeatArmoredPortGunOperator` carries `VehiclePortGunSeat` and the two port gun mounts (`VehicleAPCM56FPWInteriorNorth` and
  `South`) `VehiclePortGunController`; the three ammo loaders (`VehicleTankAmmoLoader`, `VehicleSPPAPCAmmoLoader` and
  `VehicleSPPTankAmmoLoader`) carry the loader component and its window (`ammo.md`).
- The driver and weapon consoles of the SPP APC are decoration, as in RMC. The overwatch and ground-side consoles, the tactical map
  table, the wall phones, the supply vendor and the medical and blood vendors are gone with their placements.
