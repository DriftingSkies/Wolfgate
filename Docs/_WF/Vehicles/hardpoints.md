# Hardpoints

How a vehicle is built from parts, how the parts wear, and what the framework needs from them. Ported from RMC-14's
`Vehicle/Hardpoint` folder with the power loader, skills and RMC repair stripped (see `design.md`).

## Model

- A vehicle (the frame) carries `HardpointSlots`: a vehicle family and a list of slots. Each slot has an id, a hardpoint
  type, an optional slot type and compatibility id, a visual layer, a `required` flag and insert and remove delays.
  `HardpointSystem` makes one item slot per entry.
- A part carries `HardpointItem` (type, family, slot type, compatibility id, a damage multiplier and a repair rate) and
  `HardpointIntegrity`. It fits a slot when family, slot type, compatibility id (null equals null) and type all match
  and the slot's whitelist passes.
- The family, type, slot type and compatibility id are not prototype kinds. Each is a hidden entity prototype carrying
  a marker component, and the fields hold `EntProtoId`s (`Resources/Prototypes/_WF/Vehicles/Hardpoints/hardpoint_types.yml`).
  `HardpointPrototypeTest` checks that every reference resolves.
- Slots nest one level: a turret part has its own `HardpointSlots` for a cannon and a launcher. Nested slots are
  addressed as `parent::child` (`VehicleTurretSlotIds`, `VehicleSlotPath`); `VehicleTopologySystem` walks them and finds
  the vehicle that holds a part.
- Wheels and treads are hardpoints of type wheel that also carry `VehicleWheelItem`. The vehicle lists its wheel slots in
  `VehicleWheelSlots`; `VehicleWheelSystem` keeps the wheel appearance and damages the wheels when the mover raises
  `VehicleCollisionEvent` with kind `Impact` (speed times `collisionDamagePerSpeed`, at least `minCollisionDamage`).

## Install and remove

Using a part on the vehicle starts a do-after of the slot's insert delay and then inserts it into the first empty valid
slot, nested ones included. Direct item-slot inserts and ejects are cancelled unless the system is completing one.

Removal needs a tool with the vehicle's `removeToolQuality` (prying) in the active hand. Using such a tool on the vehicle
opens the hardpoint window (`HardpointBoundUserInterface`, see `core-features.md`); the window sends `HardpointRemoveMessage`, the
server runs the do-after, ejects the part to a hand or the floor, and deletes a part whose integrity is zero. The
window's error line is a localised string from `hardpoints.ftl`.

An insert with no user (a `startingItem`, an admin) skips the install check. If the part does not fit the slot, `OnInserted`
queues it for `HardpointSlotSystem` to take back out on the next update (the container cannot give it up while it is still
being inserted), lifting the slot's eject lock for that one call; the roster test then reports the slot as not holding its
starting item. A pending install is dropped when its
installer is deleted mid do-after, since that do-after never reports back.

## Damage

- A part whose prototype sets no `integrity` (or sets zero) starts at `maxIntegrity`. That is done at map init, not when
  the component starts, so an entity on an uninitialised map is still equal to its prototype.
- `DamageModifyEvent` on the vehicle: sources carrying `VehicleDamageMultiplier` scale the damage and deal their brute
  damage as slash. Every top-level hardpoint and its intact children then loses integrity equal to the damage after
  the part's own modifier sets (`HardpointDamageModifier`, `VehicleArmorHardpoint`) or the vehicle's armour buff, times
  the part's `damageMultiplier`. The frame takes `frameDamageFractionWhileIntact` (0.1) of the hit while any top-level
  part stands and all of it otherwise, and the vehicle's own damage is scaled the same way.
- Explosions skip `DamageModifyEvent`, so the same routing runs from `BeforeExplodeEvent` (integrity only; the damage
  the explosion deals to the vehicle itself is left alone, as in RMC). The armour coefficient is answered live from
  `GetExplosionResistanceEvent`: the product of `explosionCoefficient` over the installed armour. No `ExplosionResistance`
  component is written and no upstream code is touched.
- Armour adds its modifier sets to the vehicle's `DamageProtectionBuff` on install and removes them on removal.
- After integrity changes the system raises `VehicleHardpointsRefreshEvent` on the vehicle, so spotlight, gunner view and
  weapon window code can refresh without the hardpoint system knowing them.
- A required slot that is empty, or holds a part at zero integrity, makes `VehicleCanRunEvent` false. Wheels do the same
  for their own slots.
- Support parts add up onto the vehicle: speed, acceleration, gunner view and fire rate. A sum with no part left is
  removed at once (`RemComp`), because the refresh that follows reads it. The fire rate reaches mounted
  guns through `GunRefreshModifiersEvent`, subscribed on `HardpointItem` so the `(Gun, GunRefreshModifiersEvent)` pair
  stays free. RMC's accuracy multiplier is gone with its accuracy system.

## Repair

Repair goes through `SharedToolSystem.UseTool`, which checks and spends the welder's fuel. A welder (quality `Welding`)
mends a chunk of 5% of the maximum per do-after and the next chunk starts on its own; the frame stops at 75% (the weld
cap) and needs a wrench (`Anchoring`) for the rest; a part other than the frame at zero cannot be repaired. Time per chunk
is the fraction repaired over the slot type's `repairRate` (frame: one second per 5%), at least one second, and fuel is
`fuelPerSecond` times that.

## The parts

The unarmed parts of the van, humvee, APC and tank (wheels including the SPP van's, treads, armour, support modules and the
humvee and APC attachments) and the humvee overlight, which carries the spotlight modifier (`Hardpoints/overlight.yml`), are
in the files of `Hardpoints/` named by what they are; the guns, launchers and port guns are in `Hardpoints/*_weapons.yml` and
`port_guns.yml` (`armed.md`). Turret rotation, the weapon seats and the ammo system are in `weapons.md` and `ammo.md`.
