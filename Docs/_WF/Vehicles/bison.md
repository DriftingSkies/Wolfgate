# The Bison remote cargo carrier

Colonial Marines Universe's Bison (AGPL-3.0, `Content.CMU/.../CargoVehicle`; see `port-manifest.md`): a small remote
vehicle that carries one crate, driven from a handheld controller. It is the one CMU vehicle whose C# is copied, because
it does not touch CMU's diverged hardpoint code. Ids are CMU's: `CMUCargoCarrier`, `CMUCargoCarrierController`,
`CMUCargoCarrierWreck` and four `CMUCargoCarrierDebris*` fragments, with the component names `CMUCargoVehicle`,
`CMUCargoVehicleController`, `CMUCargoVehicleRemotePilot` and `CMUCargoVehicleControlSession`. Names and popups are in
`bison.ftl`, and CMU's key names are kept.

## What it is

The carrier is an `RMCVehicle` with the grid mover, and it is its own operator: `CMUCargoVehicleSystem` sets the
carrier as its operator at map init, and `RMCVehicleSystem` puts `RMCVehicleOperator` on the carrier itself. It has no
interior, no hardpoints and no integrity; its `Damageable` uses `VehicleFrameCLFVan` on `StructuralInorganic`. It gets
the spawn defaults every Wolfgate vehicle has: `save: false`, `CleanupImmune`, `FTLSmashImmune` and a `StaticPrice`. The
price is 5000, a placeholder for the owner to set. Admins spawn the carrier and its controller separately; CMU's
deployment crate (`CMUCrateCargoCarrier`) is not ported, since it is built on RMC's strapped case and the requisitions
catalogs. Vehicles are admin-spawned.

## Driving it

1. Use the controller on the carrier, holding it, to pair them. Neither may be armed or in use.
2. Use the controller in hand to start remote control. The pilot's mind visits the carrier (`MindSystem.Visit`), so the
   player sees, steers and honks as the carrier; the body stays where it was, without the SSD icon. The carrier is given
   three actions: return to body, toggle the cargo bay and arm the self-destruct. The actions are `InstantAction`s the
   system adds and deletes itself.
3. Using the controller in hand again, or the return action, ends it.

The pilot's mind goes back to their body, and the actions are taken away and deleted, whenever any of these happens:

- the return action or the controller used in hand again;
- the carrier is destroyed or deleted, or the component removed;
- the controller is deleted, dropped, stowed or taken out of their hand;
- the pilot's body leaves `Alive` (critical or dead), or is deleted; the visit is ended before the mind system would
  hand the mind to the carrier, so a deleted body leaves a ghost and not a carrier with a player in it;
- the carrier or the pilot's body is on another map than when control began (a ship jumped);
- the player disconnects, or their mind is moved by something else;
- the carrier arms itself at 300 damage.

All but the map change act on the event that causes them. The system also checks every live session four times a
second, which is how a map change and a controller that left the hand without an event are found. Control that begins
with the carrier and the pilot on different maps is fine; only a change afterwards ends it. A fall to the level below
(see Falling) is not a jump: the carrier's `VehicleFellEvent` moves the map control is kept to, so the pilot, whose body
stays where it was, keeps the carrier on the new level.

The pilot's player is attached to the carrier, not to the body, so the client takes the carrier's input context: it has
`Input` with the `human` context, which has the use-item key the horn is bound to. Anything said to the pilot goes to the
carrier, since the body has no player to hear it.

A drifting or turning deck changes nothing: the carrier is driven by the grid mover, which works in the grid's frame,
so it keeps its deck position and drives as far on a moving ship as on one at rest. It is stopped at a deck edge like
every other vehicle, unless a z-level lies below.

## Falling

The carrier has `VehicleFall` like every vehicle, so driving it off an edge with a level below drops it there
(`falling.md`). It is a Weak vehicle with no frame integrity, so the landing hurts its `Damageable`: 10% of its
`bodyHealth` of 300, which is 30 Blunt, ignoring its damage modifier set. The damage goes through the same path as any
other, so a landing that takes it to 300 arms the self-destruct as automatic arming does: control ends, the pilot is back in
their body, the carrier stops, and five seconds later it blows up and leaves its wreck. Its cargo falls with it.

## Cargo

The bay is opened and closed with the verbs on the carrier or the action. A crate the user is pulling is loaded with a
verb (a 2 second do-after, within 2.5 tiles); the crate comes out beside the user when unloaded. Cargo needs the bay
open, the self-destruct not armed and the carrier standing still. CMU asked for a power-loader grabbable entity storage;
here a crate is an entity storage whose prototype is, or inherits from, one of the carrier's `crateParents` (`CrateGeneric`
by default, which is every crate in the game's crate family), so lockers, boxes and trade crates are refused. Pulling puts a
virtual item in the user's hand, which does not count as holding something.

## Self-destruct

Armed by hand with the action, or by 300 damage, the self-destruct counts down five seconds with a beep a second and
cannot be stopped. Armed by damage, the carrier loses its pilot and stops; armed by hand, the pilot keeps steering until it
goes. At the end `CMUCargoVehicleSystem` handles the trigger before the explosion: it sends the pilot back, opens the loaded
crate and throws its contents clear, deletes the crate, and leaves a wreck, 4 to 7 pieces of debris and a diamond of tile
fire (`FireRange` 1, stage 2). The explosion is the carrier's own `ExplodeOnTrigger` and `Explosive`.

The explosion type is `FireBomb`, with CMU's intensity numbers (160, slope 6, at most 16 on a tile). It burns and hurts,
breaks no floor and does no structural damage, so a carrier blowing up on a ship deck leaves the hull's floor alone, and a plain
wall beside it stands (the self-destruct test checks both). Anything next to it still takes the blast's blunt, piercing and heat
damage, which a window might not survive. CMU's oil splatters, its `RMC` explosion type and its scorch and shockwave effects are RMC
content Wolfgate lacks and are left out. The wreck takes no damage from explosions, as in CMU, and goes at 100 damage from anything else.

## Sounds

The carrier's hit sound is its own: `CMUCargoVehicle.collisionSound`, played on `VehicleCollisionEvent` when the carrier is
going at least `collisionSoundMinSpeed` (4) when it is stopped, with a cooldown. The module's crash sound belongs to
`VehicleEnter`, which the carrier lacks, and the mover's own hit sound has no minimum speed; the carrier's `VehicleSound` only
sets the thud of a mob it runs over. Engine, horn and thud sounds are Wolfgate's or the module's; the beep and ramp sounds are
RMC's, copied with their cmss13 attribution.

## Left out of CMU's version

The CMU-only mover fields (`maxRotationSpeedDegrees`, the rotation acceleration fields and `canSmashWalls`), `transferDamage`
and `requiresHands` on the vehicle, `VehicleOperatorDamage`, `CMUZPhysics`, `CMUVehicleZTraversal` and `CMUZLevelViewer`, RMC's
corrosion, scorch and explosion-effect components, `RequireProjectileTarget` and `MeleeReceivedMultiplier`, and the sleeping
status effect CMU gave an SSD pilot (Wolfgate has no such effect). CMU's `maximumDamage` field, which nothing reads there either, is dropped.
