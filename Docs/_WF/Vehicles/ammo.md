# Ammo, the ammo loader and port guns

How a vehicle's guns are fed and how a crewman works a port gun. Ported from RMC-14's `Vehicle/Hardpoint`,
`Vehicle/Weapons` and `Weapons/Ranged/Ammo/BulletBox`, with the flamer tank, xeno and skill code stripped (see
`design.md`).

## Hardpoint ammo

- A hardpoint gun carries `BallisticAmmoProvider` (the ready magazine), `VehicleHardpointAmmo` (the magazine size and
  how many magazines the gun holds in all) and `RefillableByBulletBox` (the box prototype that fills it). The ready
  magazine is the gun's own rounds; the reserve is `maxStoredMagazines - 1` slots of up to one magazine each, kept as
  counts in `VehicleHardpointAmmo.StoredRoundSlots`. Nothing in the reserve is an entity.
- `VehicleHardpointAmmoSystem` runs after the gun system takes ammo: the moment the ready magazine is empty it takes
  the first reserve slot that holds rounds and chambers it, then squeezes the reserve together. An empty gun that is
  fired does the same, so a magazine loaded into an empty gun is picked up on the next pull of the trigger. It raises
  `VehicleAmmoChangedEvent` on the gun whenever the counts move.
- `VehicleTopologySystem.GetMountedAmmoProviders` lists every mounted gun with all three components, nested slots
  included; the loader works from that list.

## Bullet boxes

`BulletBox` is a box of loose rounds of one `bulletType` (the box's own prototype id). Using a magazine or gun that is
`RefillableByBulletBox` of the same type on a box takes rounds out of the box into it after a do-after; the box's
alternative verb "Fill Box" moves rounds from the held magazine back into the box. `BulletBoxSystem.TryConsume`, `TryAdd` and `TrySetAmount` are the only writers of a box's
count (`[Access]` on the component), and the box sprite shows how full it is through `BulletBoxLayers.Fill`.

## The ammo loader

- `VehicleAmmoLoader` is a prop in a vehicle's interior (`VehicleAmmoLoader`, `VehicleHumveeAmmoLoader`,
  `VehicleSPPVanAmmoLoader`; the APC and tank loaders take the same component). It needs a `UserInterface` with
  `enum.VehicleAmmoLoaderUiKey.Key` of `VehicleAmmoLoaderBoundUserInterface`. Using it empty-handed or with a bullet
  box opens the window; both `hardpointType` and `bulletType` can narrow the guns it lists.
- The window shows one panel per gun with its ready magazine and reserve slots. A slot takes a click to load the held box
  into it or a right click to unload it, each after a `loadDelay` do-after. A box of another type is not offered a slot
  and is refused if sent anyway; a full slot is refused; a box that runs empty is dropped at the loader. Unloading spawns
  new boxes of the gun's `RefillableByBulletBox` type, one per box-full, into the user's hands or at their feet.
- The loader keeps the window state in `VehicleAmmoLoaderComponent.Ui`, redrawn when the user's hands change, a box in
  their hand is selected or dropped, the gun's rounds change or a hardpoint is installed or removed. The hands part
  runs on `VehicleAmmoLoaderUser`, a marker the system puts on a user while a loader window is open and takes off when
  the last one closes, so no system subscribes to the hands events of every `HandsComponent`. The client redraws an open
  window in `VehicleBoundUiRefreshSystem` when the loader's state arrives.

## Port guns

- A port gun is a gun in a vehicle item slot (`port-gun`, or any id the firing port names) that carries `VehiclePortGun`.
  The firing port is a prop with `VehiclePortGunController` and the slot id. A user strapped into a seat with
  `VehiclePortGunSeat` who uses the port takes the gun (`VehiclePortGunOperator` on them, the operator on the gun) and
  has their eye on the vehicle; using the port again, leaving the seat or the gun leaving its port lets go (a magazine
  leaving the gun does not: the handler is on the gun's own removal message). Before
  the strap goes through the seat raises `VehicleSeatAttemptEvent`, as the driver and weapon seats do.
- One user holds a port gun at a time; a second is told who has it. The operator answers the gun lookup
  (`MannedTurretGetGunEvent`) with the gun, after the weapon seat's answer, so the port gun wins for a user who somehow
  holds both. A user on a port gun cannot pick a hardpoint in the weapons window.
- The gun fires from the vehicle (`GunUseGunOrigin`, `GunIgnoreContainerOwnerCollision`) with the user on the interior
  map. A magazine put to the firing port replaces the gun's: the spent one is ejected to the user's hands if they have a
  free one and to the floor beside the vehicle if they do not. An "Eject magazine" verb takes the magazine out.
- The port guns for the APC are RMC's M56 FPW, which takes a smart-gun magazine and a chambered round. The nearest
  Wolfgate magazine, Goobstation's `MagazineSmart`, belongs to a homing SMG, so the port gun takes a light machine gun
  magazine instead: the PA L6B SAW's `Magazine762x39mmBox` (100 rounds of `Cartridge762x39mmFMJ`) in a
  `MagazineAmmoProvider`, with no chamber. The real guns (`armed.md`) start with `WFVehicleMagazine762x39mmBox`, that magazine
  with casing-less rounds. This is a pick for the owner to confirm; the tests use a four-round cut of
  that magazine. A vehicle gun's cartridge should leave no casing
  (`CartridgeAmmo.deleteOnSpawn`). Firing the L6 magazine's real cartridge from any gun, a plain one on the floor
  included, makes an integration test client assert "Received delta state without having received or constructed an
  implicit full state" when the casing's state arrives; that is not a vehicle fault, and the tests use a cartridge of
  their own that leaves none.

## Weapons window

The reserve counts in `VehicleWeaponsUiEntry` (`MagazineSize`, `StoredMagazines`, `MaxStoredMagazines`,
`HasMagazineData`) come from `VehicleHardpointAmmo`; `StoredMagazines` and `MaxStoredMagazines` are round counts, as in
RMC. They follow a shot through the turret's `GunShotEvent` and the hardpoint refresh event, not a change in the
reserve alone.

## Tests

`VehicleAmmoTest` loads and unloads a box through the loader window from a real player session, checks the refusals,
the hands marker, the gun chambering its reserve, the weapons window state, and a port gun being manned, fired, handed
over and reloaded.
