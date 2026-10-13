# The armed roster

The guns, rounds, crates, port guns and pre-armed variants of the vehicles. Ported from RMC-14's hardpoint files
(`Vehicles/Tank/Hardpoints`, `APC`, `Humvee`, `Van/spp_hardpoints.yml`), `Vehicles/Common/ammo.yml` and the vehicle
cartridges and projectiles they name. The gun and ammo systems they run on are in `weapons.md` and `ammo.md`.

## What is armed

| Vehicle | Weapons (hardpoint ids are RMC's) |
|---|---|
| Tank | turret `primary` (`VehicleTankTurret`, empty at spawn): cannon `VehicleTankLTBCannon`, `VehicleTankLTAAAPMinigun`, `VehicleTankAceAutocannon`; launcher `VehicleTankGrenadeLauncher`, `VehicleTankTowLauncher`, `VehicleTankM56Cupola`. Support mount: `VehicleTankRocketLauncher` (smoke), `VehicleTankFlareModule` |
| SPP tank | turret starts with `VehicleSPPTankP17702` and `VehicleSPPTankCupola`; also `VehicleSPPTankRailgun`, `VehicleSPPTankHJ35TLauncher`, `VehicleSPPTankRocketLauncher`, `VehicleSPPTankFlareModule` |
| APC | `RMCAPCDualCannon` (primary), `RMCAPCFrontCannon` (secondary), `RMCAPCFlareLauncher` (support), empty at spawn as in RMC; the two port guns are fitted |
| SPP APC | turret starts with `VehicleSPPAPCMinigun`; also `VehicleSPPAPCAutocannon`, `VehicleSPPAPCHJ35Launcher`, `VehicleSPPAPCFlareLauncher`; no port guns |
| Humvee | `VehicleHumvee` starts with `VehicleHumveeTurretArmed` (cannon); `VehicleHumveeTurret` (empty), `VehicleHumveeARCTurret` (ARC cannon `VehicleHumveeARCCannon`), `VehicleHumveeCannon`, `VehicleHumveeLauncher` (smoke) |
| SPP van | `VehicleSPPVanArmed` starts with `VehicleSPPVanTurret` and `VehicleSPPVanCannon` |

Each gun carries what `weapons.md` lists for a mounted gun, a `BallisticAmmoProvider` that cannot be cycled, `VehicleHardpointAmmo`
and `RefillableByBulletBox`. RMC's damage, fire rates, magazine sizes and reserve counts are unchanged. Every gun has explicit
`projectileSpeed` and `recoil: 0`. The speed is the source's: RMC sets one on the ACE autocannon (50), the grenade, TOW, smoke
and humvee launchers (18) and the flare launchers (20), and every other gun flies at RMC's engine default of 53, not Wolfgate's 25
(CMU's guns: `cmu-military.md`). `VehicleArmedDataTest` pins the table. The ten crates are in `Weapons/ammo.yml` under RMC's `VehicleAmmoBox*` ids; each holds one
magazine of the guns that take it.

Left out: the two flamers (`VehicleTankFlamer`, `VehicleTankDragonFlamer`) and their fuel crate, as the brief says; IFF
(`GunIFF`, `UserIFF`), weapon accuracy (`RMCWeaponAccuracy`), `GunIgnorePrediction` and the smart gun chamber.

RMC's own file marks two values of the APC's dual cannon as unchecked, and they are kept as they are: its north-facing muzzle
offset (`0,0`) and its sound (RMC wanted a Mono one, and the gun plays the RMC dual autocannon sound).

## Rounds

Cartridges are `Weapons/projectiles.yml`, on `BaseCartridge` with `deleteOnSpawn: true` (a casing arriving on a client trips an
assert in the test client), and the rounds on `BaseBullet`, `BaseBulletTrigger` and `BulletRocket`. Damage is RMC's.

Not ported with them: accuracy, `CMArmorPiercing`, damage falloff, `RMCAreaDamage` (the ACE flak's), shrapnel and scorch effects,
the knock-out of the TOW rocket. Where RMC's falloff cut a round's damage off at a range, the round has a `TimedDespawn` for that
range at its gun's speed (the cupola's 12 tiles at 53 a second, the minigun's 22 tiles at 53, the ACE flak's 32 at 50);
`VehicleArmedDataTest` checks each against the range of its source.

Blasts. The LTB cannon shell (and the SPP railgun's) keeps RMC's intensity numbers and RMC's explosion type, `RMCLTBCannon`, ported
under that id with RMC's damage per intensity (Blunt 20, Heat 20, Structural 400; Wolfgate's default type is Heat 5, Blunt 5,
Piercing 5, Structural 20) in `Weapons/explosions.yml`, which also holds RMC's `RMC` type for the TOW rocket's small blast. The owner
may want to rebalance the cannon: one shell exploding in the open (measured with the part alone in the open, 650 intensity) now
destroys a shuttle or reinforced wall, a reinforced window and an external airlock up to 4 tiles from the blast, where the default
type destroyed those walls only at 1 tile, the airlock up to 3 tiles and the window, as now, up to 4; a shuttle docking airlock or a
plastitanium wall or window, which the default type never destroyed, falls up to 3 tiles away. Its tile breaking (`tileBreakScale`
4, at most 12 tiles) is unchanged. The TOW's multiplier against vehicles is RMC's 5. The shell goes off on a hard
fixture only: RMC's `ignoreOtherNonHard: false` made it go off on the non-hard fly-by sensor every Wolfgate bullet carries.

The rounds RMC fired as grenade and flare items are Wolfgate's own, fired as projectiles through the same cartridge path:
`WFVehicleGrenadeHEShell` (the tank grenade launcher; goes off 1.5 seconds after it leaves the gun), `WFVehicleSmokeShell` (three
shells a shot, as RMC; they burst into smoke at the aimed point, at most 9 tiles out as in RMC and CMU, 20 seconds of cloud, spread
25 each) and `WFVehicleFlareRound` (stops at the aimed point and burns 45 seconds). The smoke launchers lose the 0.2 second fuse RMC
gave them, which burst the shells 3.6 tiles out whatever the aim.

## Fixed distance

`ShootAtFixedPoint` makes a gun's rounds fly to the point the gunner aims at and stop there (`ProjectileFixedDistance` holds the
end time). `ProjectileMaxFixedRange` on a round and `maxFixedRange` on the gun cap the distance (RMC kept the round's cap in
`Projectile.maxFixedRange`; the module has its own component so the upstream projectile gets no field). `shootArcProj` lets the
rounds fly over everything but impassable fixtures. `TriggerOnFixedDistanceStop` on a round triggers it when it stops, and
`OnShootTriggerAmmoTimer` on a gun starts a trigger timer on each round it fires. The cannon shell, the smoke shells, the
flare, the HE grenade and the humvee smoke launcher use these; the TOW rocket flies on until it hits something.

The gun system raises its ammo event on the server only, and the shooter's client fires its own copy of each projectile
(`weapons.md`). A round meant to go off at a point carries `IgnorePredictionHide`: the client leaves it to the server and the
server's round is not hidden from the shooter. A range-limited round the client can mirror (the autocannon flak) is predicted.
A round comes to rest within a tick of travel of the aimed point: the gun's speed over the tick rate, up to about 0.9 tiles for the
cannon shell at 53 a second with the physics substeps. The module caps every fixed distance at 64 tiles whatever the client sends
(a point that is huge but finite is capped; a point that is zero tiles from the muzzle leaves the round on its own course for that
distance at most), and the gun's and the round's own caps still apply under it. An aim point that is on another map than the muzzle's,
or is not a number, is refused before a round is spawned (`GunAimGuardSystem`, on every hardpoint gun and port gun, ahead of the arc,
the turret and the muzzle handlers, leaving no cooldown); if one reached the ammo event anyway, its rounds are deleted rather than left
to fly with no stop. A stock client never sends one: it aims from the grid under the gun.

The soft rounds (grenade, smoke, flare) have no hard fixture, so they would fly through walls to the aimed point. A round with
`ProjectileFixedDistance` that would meet a hard fixture on the impassable or high-impassable layers (walls, closed doors, windows
and windoors including those on a tile's edge, hulls of any grid, large machines and other vehicles: RMC's item mask) comes to rest
0.55 tiles in front of its surface, so the smoke bursts, the grenade's fuse runs and the flare lies on the near side. A round that is
deleted on its first hit, the cannon shell, is set 0.1 tiles in front of it and hits it through the projectile system, so its damage
is the usual one, and its blast starts there. A round that has come to rest is spent, so a mob walking onto a flare is not hit by it.

The stop is the server's, and it does not wait for a physics contact or look at how any grid is turned. Each tick, ahead of the
physics step (`ShootAtFixedPointSystem.Sweep.cs`), the round's travel for that step is cast as three rays (its middle and the two sides
of its body) in the map's frame, from its position along its map velocity. The first hit that a contact would also have stopped (the
fixture's layers and the round's mask, and `PreventCollideEvent`, so the firing vehicle's own hull and an arcing round's low things are
let past) ends the flight, and the hit wins over the flight clock running out on the same tick. A grid that moves under the round
(a ship drifting or turning, the round's own included) is met in its own frame: the ray is cast against that grid's fixtures as the
round's travel relative to the grid, from the grid's velocity at the round. The same cast covers the muzzle: from the gun to where the
round was spawned, so a muzzle already through a wall delivers its round on the gun's side. A step longer than 12 tiles, a jump, is not
swept. A round that stops in open space in front of a hull is parented to that hull's grid, with grid traversal off, so the ship
carries it and the grenade's fuse goes off where the round lies on it; over a grid's floor it is on that grid already. A diagonal
wall is taken by the box of its tile, which stops a round a little early on its open half.

## Spin-up

`GunSpinup` is on the LTAAAP minigun and, by inheritance, the SPP tank's rotary cannon. While it fires its fire rate climbs
through the rate tiers and its spread closes; it winds down after two seconds of rest. `TrySelectHardpoint` plays the gun's
select sound when a gunner picks it (to everyone nearby, the gunner included: the selection runs on the server only, so a
predicted sound would leave the gunner out), and the stop sound is the server's alone (a client playing it too doubled it for the
shooter). Both sides run the same state from the same shot events, so prediction needs nothing
more. `VehicleGunSpinupTest` runs it on a test gun, once on the server alone and once with the shooter's client firing its
copies: one copy for each of the server's rounds and the same spin level and fire rate on both sides. That test drives the
client by hand; a real client session was not run.

## Port guns

`VehicleAPCM56FPWPortGunRight` and `Left` are in `Hardpoints/port_guns.yml`. `VehicleAPCBase` carries the two locked slots,
the firing port props carry `VehiclePortGunController` with the slot ids, and `RMCSeatArmoredPortGunOperator` carries
`VehiclePortGunSeat`. The SPP APC lists its own slots and so has none.

Pick for the owner to confirm: RMC's gun takes a smart-gun magazine and Wolfgate has no match, so they take the L6 SAW's box
magazine (`Magazine762x39mmBox`) as a child, `WFVehicleMagazine762x39mmBox`, whose rounds (`WFVehicleCartridge762x39mmFMJ`) leave
no casing. The guns do not eject an emptied magazine by themselves.

## Pre-armed variants

RMC fitted weapons from a vendor that is not ported, so each vehicle with weapon slots that RMC spawns bare has an armed twin
for admin spawning (names in `armed.ftl`). The choices are the author's, for the owner to change:

| Variant | Fitted |
|---|---|
| `WFVehicleTankArmed` | `WFVehicleTankTurretArmed` (LTB cannon and M56 cupola), smoke launcher on the support mount |
| `WFVehicleAPCArmed`, `WFVehicleAPCTrackedArmed`, `WFVehicleAPCMedArmed`, `WFVehicleAPCCommandArmed` | dual cannon, frontal cannon, flare launcher, both port guns |
| `WFVehicleHumveeARCArmed` | `VehicleHumveeARCTurret` |

The plain, CLF, box, pizza and the SPP medical, prisoner and logistics vans have no weapon slot and stay as they are. The medical
and transport humvees have no armed variant: their bodies have no turret hardpoint slot or sprite layer and their interiors no
gunner seat or ammo loader, so a turret could not be mounted or fired on them. A child's `ItemSlots` replace the parent's, so each
variant lists every slot; the APC and CMU tank variants do it once, in an abstract loadout prototype with no parent that they take
as their FIRST parent (`parent: [ Loadout, Vehicle ]`). With two parents the first one's value of a field wins and the slots of a
vehicle are one field, so a loadout listed second was dropped and those variants spawned as bare as their vehicle.

## Hull guns and arcs

A gun with `GunFireArc` refuses a shot outside its arc, measured from the facing of the vehicle it sits in. A gun on a turret that
turns to the cursor is exempt (the turret system holds its shot to the barrel); a gun fixed to the hull is not, even when its
`VehicleTurret` component says `useBarrelDirectionForShots` (the CMU hull machine gun, the SPP APC's HJ35 launcher).

## Two traps in the base

- The hardpoint layer states go through appearance data as one string (`VehicleHardpointLayerCodec`), not as a
  `List<VehicleHardpointLayerState>`: the network serializer does not know that type and it failed the whole game state for
  any player who saw a vehicle.
- The interior maps save empty fixtures for the wall camera, which has no body, and the client logs an error when it is given
  fixtures for one. The camera has a static body with collision off.

## Tests

- `VehicleArmedWeaponsTest`: for every hardpoint with a gun, a vehicle whose slot takes it (found from the family, type, slot type
  and compatibility id) is spawned and entered, a gunner straps into a weapons seat, picks the gun from the hotbar and fires:
  the round or its pellets leave the muzzle on the vehicle's map, the hull is untouched, the ready magazine loses one, the next
  magazine comes up from the reserve, and a fixed-distance round stops where it was aimed. The two APC port guns are fired from
  the port gun seat. The blast of a shell is removed in the test so it does not break the floor; the numbers are data.
- `VehicleArmedDataTest`: every crate names a gun and every gun a crate that holds one of its magazines, with cartridges that
  leave no casing; the port guns start loaded; every pre-armed variant keeps the slots of its vehicle and starts with guns; every
  gun flies at its source's speed; every kinetic round has its source's range cut-off; the heavy rounds keep their blast types,
  multipliers, smoke range and CMU's sentry round.
- `VehicleArmedWeaponsTest` also: every vehicle whose id says Armed spawns with guns mounted and offers a gunner in a weapons seat one
  to pick; every fixed-distance gun is fired, with the firing grid turned 0, 90, 180 and 37 degrees (the turrets laid on the new
  facing) and a second grid turned another way, at a wall, a closed door, a reinforced window, a diagonal wall, a shutter, a blast
  door, a closed firelock, a reinforced directional window and a secure windoor on the tile edge facing the gun, another vehicle, a
  wall on a grid of its own and a wall on a ship lying over the firing grid's floor: it rests on the near side, not short of it, and
  its fuse or blast starts there; an open door, a mob, a loose item and open deck do not stop it; aims from half a tile before the
  face of a wall, a directional window and a windoor to half a tile behind it, in tenths, stop on the near side; a ship that moves and
  turns under the gunner has the wall, and a ship that closes on the gunner has it, and a round that stopped in front of the second
  ship's hull stays on it and its fuse goes off there; every gun refuses an aim on the interior map, not a number or infinite; it is
  spent once at rest and survives an aim 10^14 and 10^20 tiles away (a round without a fixed distance fails); every gun fixed to the
  hull refuses a shot behind it; the cannon shell goes off on a wall and a mob and flies past another bullet's fly-by sensor. Two
  direct tests: a round spawned beyond a wall, windoor or directional window between the gun and the muzzle is stopped on the gun's
  side, and a round whose gun aims at another map or at a point that is not a point is taken away.
- `VehicleGunSpinupTest`, and `VehicleRosterTest`, which also checks the guns a starting turret carries and that the hardpoint
  walk sees every starting part.
- `Tools/_WF/Vehicles/check_sprite_states.py` checks every sprite state, overlay state and sound the vehicle prototypes name.
