# Turrets and weapon seats

How a gunner works a vehicle's guns. Ported from RMC-14's `Vehicle/Turret`, `Vehicle/Weapons` and the gunner view, with
the IFF, accuracy, skill and auto turret code stripped (see `design.md`); ammo and port guns are in `ammo.md`.

## Weapon seats

- A seat with `VehicleWeaponsSeat` (in a vehicle's interior) makes whoever is strapped into it a weapons operator. Before
  the strap goes through the seat raises `VehicleSeatAttemptEvent`, as the driver seat does; nothing cancels it yet.
- Strapping in adds `VehicleWeaponsOperator` (the vehicle and the picked weapon) to the user, `VehicleWeapons` to the
  vehicle (primary operator, the crew's picks), `VehicleWatching`, the view toggle, and points the user's eye at the vehicle.
  The seat's window opens (`VehicleWeaponsBoundUserInterface`, key `VehicleWeaponsUiKey.Key`). Nothing is picked until the
  gunner picks it.
- A weapon is a mounted gun that is also a `VehicleTurret`. The gunner picks it in the window or with a hotbar action
  (`ActionVehicleSelectHardpoint`, one per selectable weapon, kept by `VehicleWeaponsSystem` as the hardpoints change).
  Picking the picked weapon again drops it. Both go through `TrySelectHardpoint`.
- One operator at a time holds a weapon, except weapons of the support hardpoint type, which any operator can pick. A
  second gunner is not offered a held weapon and is refused if they ask; they get it once the first lets go. A seat can
  limit the types it may use (`allowedHardpointTypes`), allow only one of window or hotbar, and be primary or not; only
  the primary operator's pick is the vehicle's `SelectedWeapon`.
- The operator answers the gun lookup (`MannedTurretGetGunEvent`) with `TryGetSelectedWeaponForOperator`: their own pick,
  else their entry in the crew's picks, else the primary operator's. That is the only subscriber of the pair.
- `VehicleHardpointsRefreshEvent` (damage and repair) redraws the gunner view and the seats' window state.

## Turrets

- `VehicleTurret` marks a turret or a gun on one. A turret with `rotateToCursor` turns at `rotationSpeed` degrees a second
  toward its target; a gun with `VehicleTurretAttachment` is steered through the turret that holds it. The client sends the
  cursor (`VehicleTurretRotateEvent`) and the server checks that the sender operates that weapon from the outside view,
  and that the point names a real entity at a finite spot on the vehicle's map.
- A turret counts as mounted only through hardpoint slots (`VehicleTopologySystem.TryGetMountedVehicle`): every container
  up the chain must be a slot in its owner's `HardpointSlots`. A turret carried in a crate on the vehicle is left alone, and
  a turret part is only re-posed under the parent that holds it, so it is never pulled out of its slot.
- `VehicleTurretSystem.OnAttemptShoot` refuses a shot while the barrel is further than two degrees (plus the gun's grace)
  from the aim, or the turret or its holder is destroyed. A refused shot leaves no cooldown. With `useBarrelDirectionForShots`
  the shot flies along the barrel, within `maxShotCurvatureDegrees` of the aim. On the gunner's client a replay of a request
  (after a server state) judges the aim against a rewound turret, so it repeats the refusal of the request's first pass
  (kept per gun and tick for a few hundred ticks) instead of judging again.
- A destroyed frame refuses the shot on both sides (`OnOperatorShotAttempted`, from the frame's networked integrity); only the
  popup is the server's.
- The aim point the client sends for a mounted gun is measured from the grid under the gun (`GunSystem.GetAimCoordinates`),
  not from the gun, whose parents the gunner's client does not predict. The shot's start is still the muzzle.
- A turret's anchor on the vehicle is a pixel offset, chosen by the way the vehicle's sprite faces and kept in world axes,
  because the sprites are drawn in the world's directions. The facing is the mover's own, turned with the frame the vehicle
  drives on, so a vehicle on a turned ship picks the offset that matches the sprite.
- `GunMuzzleOffsetSystem` starts a turret gun's shot from the turret's pose and along its barrel, with the same frame
  arithmetic as a plain mount: the offsets are added in the grid's own axes. `VehicleTurretMuzzleSystem` alternates the
  shot between a left and a right barrel; its offset is a world one, converted to the grid's axes the same way.
- The pose and the alternation come from networked state only (the vehicle's transform, the turret's `WorldRotation` and
  `UseRightNext`), so the gunner's predicted copy of a shot and the server's shot leave from the same point.
  `VehicleWeaponsTest` checks that on an unturned grid and on one turned 45 degrees.

## Gunner view

`VehicleGunnerViewSystem` widens the gunner's PVS scale and `VehicleGunnerCursorOffsetSystem` lets the view lean toward the
cursor, by the seat's base view plus the vehicle's view modules (`VehicleGunnerView`, from support hardpoints). A view that
is shutting down adds nothing: removing it is deferred, and its own shutdown and its remover refresh the scale while it is
still on the entity.

## Client

`VehicleTurretInputSystem` sends the aim, `VehicleTurretVisualSystem` draws the turret's overlay sprite (the
`VehicleTurretVisual` entity the turret system spawns) and `VehicleTurretMuzzleOffsetSystem` answers where a turret gun's
muzzle is drawn right now, which the debug overlay (`rmc_vehicle_hardpoint`) uses to draw guns, muzzles, fire arcs and
aims.

## What is not here

- The reserve magazine counts in the weapons window, the port gun check and the minigun's spin-up select sound in
  `TrySelectHardpoint` are in; see `ammo.md` and `armed.md`.
- The auto turret switch, IFF, accuracy and the skill checks are gone with the systems they belonged to.
- RMC moved the shot origin and the muzzle flash of a turret gun with `BeforeAttemptShootEvent` and a muzzle flash event.
  Wolfgate's gun code has neither and the shot origin is already right through `AttemptShootEvent`, so the flash is drawn at
  the gun's own position, not at the muzzle. The action hotbar override (weapon actions sorted first) is not ported.
- Weapon seat prototypes need `VehicleWeaponsSeat` and a `UserInterface` with `enum.VehicleWeaponsUiKey.Key` of type
  `VehicleWeaponsBoundUserInterface`; the `skills` fields of RMC's seats are gone. The guns that sit in them are in `armed.md`.

## Flamers

The DRGN flamer (tank cannon slot) and the tank flamer (launcher slot) are CMU's hardpoints without RMC's flamer system,
which is not ported: there is no napalm tank and no tile fire. Each shot is a jet (`WFVehicleFlameJetBase`) that sets
alight what it hits (`IgniteOnCollide`) and ends, where it was aimed or at the first hard thing, in a small
`WFVehicleFlame` explosion: the fire bomb's fire with heat for its only damage, which breaks no tiles and opens no deck
to space. The DRGN keeps CMU's numbers (60 jets, one every two
seconds, 30 heat). The tank flamer's hundred units of napalm are a hundred jets at one a second with nine tiles of
reach; its burst is a little wider and its jet cooler (22 heat). The jets fly at 53 tiles a second like the other guns'
rounds, not CMU's 20: at 20 a jet fired on a moving and turning ship at its own wall ended a fifth of a tile inside it in
one test run of three. They are refilled from `VehicleAmmoBoxDragonFlameTank`
and `WFVehicleAmmoBoxTankFlamer`. No armed variant carries one. The burst sizes and fire stacks are Wolfgate's picks.
