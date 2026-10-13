# CMU military vehicles

The military half of the CMU package: four vehicles, their parts, the AEV's interior and CMU's five tank guns. Ported from
Colonial Marines Universe (`cmu` in `port-manifest.md`, AGPL-3.0); none of CMU's C# is copied. The civilian half is in
`cmu-civilian.md`. The guns sit on the armed roster's framework (`armed.md`).

## The vehicles

`Prototypes/_WF/Vehicles/CMU/military_vehicles.yml`. Each keeps its CMU id, name and description. All four use the handling of
the family they sit in (speed, acceleration, turning, weight class, overcharge and spotlight), not CMU's numbers:

| Id | Family | Differs from the family by | CMU's own numbers, not used |
|---|---|---|---|
| `VehicleTankPMC` | tank | `wytank.rsi` skin, `VehicleTankTurretPMC` | 2.5 max speed, 1.8 acceleration |
| `VehicleTankTWE` | tank | FV150 sprites, smaller hull (1.6 by 2.4), TWE turret and treads, slots that take TWE parts only | 8 max speed, 1.556 acceleration, 0.7 turn delay |
| `VehicleAev` | unarmed tank | `aev_base` sprite, 1500 frame integrity, 8 passengers, armor and support slots only | 1.8 max speed, 0.9 acceleration |
| `VehicleAPCPMC` | APC | `hull_wy` hull from `apc_pmc.rsi` | 5 max speed, 4 acceleration, 20 passengers |

The PMC APC takes the APC's capacity of 18, which is the seat count of its interior; CMU raised it to 20. The PMC tank and the
APC reuse the RMC tank and APC interiors, as in CMU, and so does the TWE tank (CMU pointed all three at RMC's maps).

The prices (80000 PMC tank, 60000 TWE tank, 70000 AEV, 40000 PMC APC) are placeholders for the owner to set.

### The FV150 (`VehicleTankTWE`)

CMU's TWE declared an armor slot with no armor sprite layer and no item slot, and nothing with its compatibility id fits an
armor slot. It is left out, so the FV150 has a turret, a secondary, a support and a treads slot. The hull is the smaller fixture
pair CMU gives it, with the `Vehicle` layer and mask every vehicle here carries. `secondary` and `support` borrow the standard
tank's empty layer states, as CMU does, since the FV150 sheet has none.

### The AEV (`VehicleAev`)

An unarmed carrier: no `primary` or `secondary` slot and no `VehicleWeapons` component. RMC's `RMCVehicleBase` carries that
component and the weapons window, and a child cannot remove a component (CMU used `RemoveComponents`, which is not ported), so
the base file is split: `WFVehicleUnarmedBase` is everything the old base had except the weapons component and window, and
`RMCVehicleBase` is that base plus both. Every other vehicle is unchanged. The AEV sits on the unarmed root and carries
its own copy of the tank family's hull, handling and spotlight (`VehicleTankBase` is armed and cannot be its parent).

- Plow. `VehicleTankSnowplow` is the existing armor plate with no behaviour: CMU's plow cleared structures and smashed walls,
  and plows and wall smashing are deferred. The AEV starts with the plow in its armor slot and the overdrive enhancer in its
  support slot, as in CMU.
- Integrity. The root sets `integrity` as well as `maxIntegrity`, so a frame with a larger maximum has to set both or it spawns
  worn; the AEV sets both to 1500.
- Interior. `cmu_arv.yml`. The tank driver's seat is a weapons seat, and sitting in one gives its vehicle the weapons component,
  so the converter puts the plain `RMCSeatArmoredDriver` there (`map_prototypes` in `interior_map_mapping.yml`, a per-map
  rename). CMU's passenger seat `RMCSeatVehiclePassenger` becomes `CMSeatPassenger`; the radio set and the wall phone are
  deleted; the crates become Wolfgate's steel, steel sheet and plasteel sheet crates.

## Parts

`Prototypes/_WF/Vehicles/CMU/military_hardpoints.yml`.

- `VehicleTankTurretPMC`: the standard tank turret in the PMC skin, with its handling (60 degrees a second, holds its aim while
  the hull turns). CMU's own tank turret turned at 80 and did not hold; the port's `VehicleTankTurret` is RMC's.
- `VehicleTankTurretTWE`: CMU's 95 a second, not holding its aim. It is a turret body (`VehicleTurret`, empty nested cannon
  and launcher slots whose compatibility is TWE), as the RMC bodies are.
- `VehicleTankTreadsTWE`, `VehicleTankSnowplowTWE` (a support part that slows the tank to 0.7 and does nothing else) and
  `TWEVehicleTankArtilleryModule`.
- `VehicleTankMountedFlag`: rides on the turret and turns with it. CMU named `mountedflag_2` as its damaged state; the sheet
  has `mountedflag_0` and `mountedflag_1`, so the damaged state is `mountedflag_1`.
- `HardpointCompatibilityTWETank` restricts TWE parts to the TWE tank, as CMU's string compatibility did: every TWE part and
  every slot of the FV150 (and of the TWE turret) carries it, so a TWE part fits nothing else and the FV150 takes nothing else.

The plow, artillery module and flag are drawn by the turret overlay (`VehicleTurret` with `showOverlay`), because the FV150 and
flag sprites live in sheets other than the vehicle's own. An overlay part never shows its `HardpointVisual` damaged state, so each
names its damaged overlay state as well.

## Guns

`Prototypes/_WF/Vehicles/CMU/military_weapons.yml`, written the way the armed roster writes the RMC guns (`armed.md`, `weapons.md`,
`ammo.md`) from CMU's edited tank hardpoint files. CMU's damage, fire rate, magazine and reserve numbers are kept as they are (the
owner may rebalance later); IFF, weapon accuracy, armour piercing, damage falloff and area damage, the gun predict opt-out and the
smart gun chamber are gone, as in the armed package, and a round CMU cut off at a distance has a `TimedDespawn` for that range at
its gun's speed. Every gun has explicit `projectileSpeed` (CMU's: the hull machine gun and the RE700 set none in CMU and so fly at
CMU's engine default of 62, not Wolfgate's 25) and `recoil: 0`, casing-less cartridges and a crate; none is a starting
item of a CMU vehicle or turret (checked against CMU's vehicle and hardpoint files, and its vendor loadout puts them in the slots below).

| Gun | Fits | Type | Fire rate | Magazine / reserve | Crate |
|---|---|---|---|---|---|
| `VehicleTankAutocannonTWE`, L26A2 45mm autocannon | FV150 turret, cannon slot | cannon | 1.6 | 40 / 2 | `VehicleAmmoBoxAceAutocannon` |
| `VehicleTankHeavyElectrolaserTWE`, L215A1 | FV150 turret, cannon slot | cannon | 0.3 | 520 / 1 | `WFVehicleAmmoBoxElectrolaser` |
| `VehicleTankCupolaTWE`, L98 hull machine gun | FV150 hull, secondary slot | secondary | 3.5 | 500 / 1 | `VehicleAmmoBoxCupola` |
| `VehicleTankRocketLauncherTWE`, L-39T smoke discharger | FV150 hull, support slot | support | 0.6 | 6 / 2 | `VehicleAmmoBoxSmokeLauncher` |
| `VehicleTankRERE700`, RE700 cupola | standard tank turret, launcher slot (the Longstreet's) | launcher | 6.45 | 500 / 2 | `VehicleAmmoBoxCupola` |

The four FV150 guns carry `HardpointCompatibilityTWETank`, so they fit the FV150 only and its slots take nothing else. The FV150
turret's own launcher slot therefore takes nothing: CMU's cupola is a secondary-type part for the hull, and the standard launcher
guns (M56 cupola, RE700, grenade and TOW launchers) have no compatibility id. The RE700 has none either and fits any tank turret
with a launcher slot. The autocannon and electrolaser share the one cannon slot.

- Sounds. CMU's weapon audio has no attribution and was not copied. The autocannon fires the RMC autocannon (`autocannon_fire.ogg`),
  the electrolaser `dual_autocannon_fire.ogg`, the hull machine gun and the RE700 `gun_ml66a_2.ogg` (the M56 cupola's) and the
  smoke discharger the stock grenade launcher sound; `cmu_translation.yml` has the same substitutions.
- Autocannon round. CMU's ACE flak hits for 75 where RMC's does 60, and the shared id is RMC's here, so the FV150 fires a copy,
  `WFVehicleCartridgeAceAutocannonTWE` and `WFVehicleProjectileAceFlakTWE`, with CMU's 75 and the same 32 tile limit. The flak's
  shrapnel payload and area damage are not ported (nor are the RMC ACE's).
- Electrolaser round. `VehicleCartridgeElectrolaser` and `RMCProjectileElectrolaser` keep CMU's ids and damage (piercing 10,
  heat 160, asphyxiation 15, 15 stamina, electrified) on `BulletLaser`, with CMU's tesla ball sprite, and are cut off at 32 tiles
  (0.5333 seconds at 60 a second). CMU gave the gun no crate and no way to refill; `WFVehicleAmmoBoxElectrolaser` holds its one
  magazine of 520.
- Smoke discharger. It fires the module's smoke shells (`WFVehicleCartridgeSmoke`) to the aimed point, as the RMC smoke launchers
  do, and takes the smoke launcher crate: CMU named the grenade launcher crate, which holds HE grenades here. CMU also set
  `useBarrelDirectionForShots` and a 0.2 second fuse on it; neither is kept (they would fire the shells straight ahead of the
  hull and burst them 3.6 tiles out whatever the aim).
- Sentry round. The hull machine gun and the RE700 name `VehicleCartridgeSentry10x50mm`, an id RMC and CMU both define: RMC's
  round does 30 piercing and is cut off at 12 tiles, CMU's (`CMBulletSentry10x50mm`) does 20 and is not cut off. They fire a CMU
  copy, `WFVehicleCartridgeSentry10x50mmTWE` and `WFVehicleBulletSentry10x50mmTWE`, with CMU's 20 and no range limit; the M56
  cupola keeps RMC's. CMU's armour piercing and accuracy are not ported.
- Hull machine gun. CMU's data is kept: it does not follow the turret, and its shots leave along the hull within the 180 degree arc,
  which is enforced: a gun fixed to the hull is held to its arc even when its turret component says it shoots along the barrel
  (`GunFireArcSystem` skips that check only for a gun on a turret that turns to the cursor).
- Pre-armed variants (`military_armed.yml`, names in `cmu-military.ftl`), for admin spawning, with the loadout as an abstract
  first parent (with two parents the first one's slots win, so a loadout listed second is dropped and the tank spawns unarmed):
  `WFVehicleTankTWEArmed` (the vendor's loadout: autocannon in the turret, hull machine gun, smoke discharger; the
  electrolaser is the alternative for the turret slot and is not fitted) and `WFVehicleTankPMCArmed` (the RMC armed tank's
  set with the RE700 for the M56 cupola: LTB cannon and RE700 on the turret, smoke launcher on the support mount).

The PMC APC inherits the APC family's two port guns, fitted at spawn, and its primary, secondary and support slots, which start
empty as the family's do; CMU has no guns of its own for it and there is no armed PMC APC.

### The AEV's support slot

The AEV is unarmed, but its support slot is the tank's, and the tank's smoke launcher and flare module are support parts with a
gun: they would sit in it inert, with no weapons seat to fire them. The slot has a whitelist for the three components the unarmed
support modules carry (`VehicleSpeedModifierAttachment`, `VehicleWeaponSupportAttachment`, `VehicleGunnerViewAttachment`), so it
takes the overdrive enhancer, warning array and artillery module and no gun; the armor slot still takes armor and the plow. A
compatibility id would do the same but would also take those modules away from the standard tank, whose slot has none. `VehicleArmedDataTest` checks that no vehicle
without the weapons component has a slot that takes a part with a gun or a turret body.

## Tests

`VehicleRosterTest` covers all four (its minimum is 50, after the armed medical and transport humvees were removed). It also checks, for every vehicle, that the frame
spawns at full integrity and that a vehicle without the weapons component places no weapons seat in its interior. The prototype
tests for hardpoints, seats and the spawn defaults (`HardpointPrototypeTest`, `VehicleSeatPrototypeTest`,
`VehicleSmashRuleTest`, `VehicleSpawnDefaultsTest`) go over the new prototypes as well.
