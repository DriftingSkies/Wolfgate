# Core vehicle features

What a driver can do that needs no interior: the horn, the spotlight, the engine overcharge and the hardpoint window.
Ported from RMC-14's `Vehicle/Core` and `Client/Vehicle` code (see `port-manifest.md`). Demolition and the crash effect
need the interior and are in `safety.md`.

## Who counts as the driver

All three features act for the operator, the entity in `RMCVehicle.Operator` (the one `RMCVehicleSystem.TrySetOperator`
set). Each client request names a vehicle and the server checks that the sender is its operator; anyone else is ignored.
The client finds the vehicle through the `RMCVehicleOperator` component on the player. RMC also looked a vehicle up from
the interior map the player stood on; the operator is always the same entity, so that fallback is not needed.

## Horn

`VehicleHornSystem`. The use-item key (`UseItemInHand`) sends `VehicleHornRequestEvent`. The bind never consumes the key,
so the hands system still sees it, as in RMC. The server plays `VehicleSound.HornSound` and holds off for `HornCooldown`.

## Spotlight

`VehicleSpotlightSystem`. The flip key (`FlipObject`) sends `VehicleSpotlightToggleRequestEvent`; the server flips
`VehicleSpotlight.Enabled` and mirrors radius, energy, softness and enabled onto the vehicle's `PointLight`.

The server also flips objects on that key, and RMC's bind swallowed the key for everybody. Here the client bind only
consumes the key when the player operates a vehicle that has a spotlight; for anyone else it falls through to the server.

Hardpoint parts reshape the light through `VehicleSpotlightModifier` (radius, energy and softness, each a multiplier and
an addend). The spotlight recalculates on `HardpointSlotsChangedEvent` (install and removal) and on
`VehicleHardpointsRefreshEvent` (damage and repair), skipping parts at zero integrity. The humvee overlight is the one
part that carries the modifier (`Hardpoints/overlight.yml`).

## Overcharge

`VehicleOvercharge` keeps `Duration`, `Cooldown`, `SpeedMultiplier`, `ActiveUntil` and `CooldownUntil`; the grid mover
multiplies the top speed while `ActiveUntil` is in the future. RMC bound it to a marine key. Here it is an action,
`WFActionVehicleOvercharge` (an `InstantAction`), so no key function or options entry is needed.

- `VehicleOverchargeSystem` gives the action to the new operator when `VehicleOperatorSetEvent` names one and takes it
  back from the old one. The action is spawned per grant and deleted on removal. It is not `temporary`: the client logs
  an error when a temporary networked action is deleted.
- The event is raised on the operator (`raiseOnUser`). The handler checks the operator really drives that vehicle and
  that no cooldown is running, sets the two timestamps and plays `OverchargeSound`. It runs on the client too, so the
  burst starts without waiting for the server.
- The action's `UseDelay` is set to the vehicle's `Cooldown` on grant, and a cooldown still running is copied onto it,
  so the button shows the time left even after the driver changes.

## Hardpoint window

The BUI class is `HardpointBoundUserInterface`, named so the vehicle prototypes' `enum.HardpointUiKey.Key` entry finds
it. Using a prying tool on a vehicle opens it on the server (`HardpointSlotSystem`). It is not state driven: the server
keeps `HardpointSlots.Ui` up to date, `VehicleBoundUiRefreshSystem` redraws an open window when that state arrives, and
`HardpointMenu` lists the frame integrity and one row per slot with a clickable row for each installed part, which sends
`HardpointRemoveMessage`. The window shows the vehicle with its turret overlays, spinning.

All text is in `core.ftl`. RMC's `HardpointButton` is not ported: only the supply console used it.

## Visualizers

- `VehicleHardpointVisualizerSystem` shows and hides the hardpoint sprite layers from the layer states the server
  publishes.
- `VehicleWheelVisualizerSystem` animates the wheel layer while the vehicle moves and dims it as the wheels wear out.
- `VehicleFrameDamageVisualizerSystem` fades in the damaged-frame layer as the frame integrity drops.

## Debug overlay

`rmc_vehicle_hardpoint` (admin) toggles `VehicleHardpointDebugOverlay`, which draws the vehicle origin, each turret anchor
and the offset turret and, for each turret gun, its rendered pose, muzzle, fire arc and aim, with a label of the numbers
behind each. The command lives with the other overlay commands in `Overlays/VehicleOverlayCommands.cs`.
