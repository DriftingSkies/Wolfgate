# Falling through z-levels

Vehicles fall. Driving off an edge where a CE z-level lies below drops the vehicle to that level with damage, and a
parked vehicle whose floor goes from under it falls the same way. Where nothing lies below, the mover's plain refusal
stays. Code: `Content.Shared/_WF/Vehicles/Falling` (`SharedVehicleFallSystem`, `VehicleFallComponent`,
`VehicleFellEvent`), a server system that tells planet ground from a hole, and an empty client system. Tests:
`Content.IntegrationTests/Tests/_WF/Vehicles/VehicleFallTest.cs`.

## How levels are linked

- A z-network (`CEZMapNetworkComponent`, `Content.Shared/_CE/ZLevels/Core/Components`) holds maps by depth. Each member map
  carries `CEZMapComponent` (depth, `MapAbove`, `MapBelow`). `CESharedZLevelsSystem.TryMapDown` / `MapOffset`
  (`Core/EntitySystems/CESharedZLevelsSystem.Maps.cs`) answer "which map is below this one"; a map that is not in a
  network has no level below.
- A level's floor is its tiles. A z-level map is itself a grid on planets (the layer map carries `MapGridComponent`) and a
  station's decks are grids on their maps. `IMapManager.TryFindGridAt` returns a grid with a non-empty tile at a point,
  else the map's own grid.
- Mobs and items fall by CE's z-physics: `CEZPhysicsComponent`, `ProcessZPhysics`
  (`CESharedZLevelsSystem.Update.cs`). Ground under them is `ComputeGroundHeightInternal`
  (`CESharedZLevelsSystem.Movement.cs`): a non-empty tile on the level, or planet terrain that is only unloaded
  (`WfUnloadedGround`, server: `Content.Server/_WF/Planets/CEZLevelsSystem.WFTerrain.cs`); otherwise they sink, and
  `TryMove` puts them at the same x and y on the map below. The landing hurts through `CEZLevelDamageSystem.OnFallDamage`.
- Whole ships fall as grids through transit maps (`CEZLevelsSystem.Gravity.cs`, `.Transit.cs`). That is the only place
  a ship is lifted or dropped; a vehicle on a deck rides along as a child.

## What "a level below exists here" means

| Where | Answer |
|---|---|
| A ship in space | The ship's map is not in a z-network: no level below, the edge refuses. |
| A ship in orbit or in the air layers of a planet | The map is a CE level, but the orbit layer has no grid and the cloud layers have no floor, so there is nowhere to land: refused. |
| A ship in transit between levels | Transit maps are not z-network members; the system leaves them alone: refused. |
| A station deck with a deck below | Yes, if the lower level has floor and room for the vehicle near the spot. |
| Planet ground with a cavern below | Only at a hole (a pinned empty tile) with a landing under it. Bare empty tiles that are only unloaded terrain are ground, not holes: they hold a vehicle up and are not an edge. The cavern is loaded round its mouths only, so a landing needs the pad there to be big enough for the footprint. |
| The bottom level | No map below: refused. |

A vehicle needs `VehicleFall` to do any of this (the vehicle base has it; `enabled: false` opts out). Without it, or
without a level below, everything is as before.

## Approach: the module moves the vehicle (B)

A vehicle does not carry `CEZPhysicsComponent`. The module uses CE's queries (`TryMapDown`, tile lookups) and moves the
vehicle itself with `SetCoordinates`. Why:

- The client rewrites the sprite of every `CEZPhysics` entity each frame: `CEClientZLevelsPreAnimSystem` resets
  `Sprite.Offset` to `SpriteOffsetDefault` (`Content.Client/_CE/ZLevels/Core/CEClientZLevelsSystem.cs:90`),
  `CEClientZLevelsPostAnimSystem` adds the altitude (`:160`), and the airborne draw depth is changed too. The vehicle's
  wheel, turret and hardpoint visuals write sprite state and offsets and would be fought every frame.
- `ProcessZPhysics` sets the body to `InAir` while it falls (`CESharedZLevelsSystem.Update.cs:171-179`), and the mover sets it
  back to `OnGround` every step (`StabiliseBody`). The mover also rewrites the transform from its own state each step.
- Continuous altitude is not wanted: the vehicle drops at once and lands.
- It needs no upstream edit.

## The rule

1. **Edge.** The mover raises `VehicleOpenTileAttemptEvent` for an empty tile it would cover. `SharedVehicleFallSystem`
   handles it on `VehicleFallComponent`, on the client too so the driver's prediction agrees with the server. It allows
   the tile when a level lies below and has a landing near the tile (below), and the tile is not unloaded planet ground.
   Answers are cached for half a second per vehicle and half-tile cell. The event is raised a second time, with
   `HandOver` set, when the vehicle's origin has crossed the edge and the mover would hand the vehicle to the map. That
   answer needs a landing from where the origin is, the search the drop will make, so a vehicle is never left on the map
   with a landing that only a search from the tile ahead found: it stays on its deck, and can back off, until it has room.
   A refused hand-over is asked again at every step that moves the vehicle, so its answer is cached the same way, per
   vehicle and half-tile cell of the origin (`SharedVehicleFallSystem.LandingSearches` counts the searches).
2. **Only the drive crosses an edge.** The mover raises the event only for probes of the drive itself, forward or
   reverse (`_driveProbe` in `GridVehicleMoveSystem.Floor.cs`, set in `TryMoveWithLaneGuidance`). Lane steering, turning
   and nudging treat an empty tile as blocked, so a vehicle blocked ahead cannot slip sideways off the deck.
3. **Drop.** The footprint (the hard fixtures' box) is sampled every 0.4 tiles; a sample is supported when some grid has a
   tile under it, or the ground there is only unloaded. When at least `UnsupportedFraction` (0.5) of the samples are
   unsupported the vehicle drops. A vehicle the mover has let over an edge is checked every tick; every vehicle with the
   component is checked four times a second, which finds a parked vehicle whose floor was removed. The mover hands a
   vehicle to the map when its origin crosses an edge, and a vehicle on no grid is not driven; a hull with its origin near
   one end can get there with little of it over the edge, so a vehicle on no grid drops at any gap. A vehicle found fully
   supported is not sampled again until a tile changes near it (`TileChangedEvent`), it moves, or two seconds have
   passed, so parked vehicles cost nothing between changes. Changes are remembered per square of eight tiles of the map,
   and a vehicle counts the squares its hull and a tile around it touch, so terrain that streams in elsewhere on a planet
   leaves it alone. One that rests on ground that is only unloaded is sampled at every sweep, since pinning a hole there
   changes no tile.
4. **Landing.** The spot is the matching x and y on the level below, then free spots within two tiles in half-tile steps,
   nearest first. A spot needs floor under every sample and nothing hard in the footprint. Hard means the mover's rules:
   something it would be blocked by (walls, other vehicles, hard fixtures), except mobs and things it smashes, which are
   hit instead (`TryGetSmashable`; a vehicle too light to smash a thing cannot land on it, nor can one land on a powered
   door that driving at would not smash). With no spot the vehicle
   does not fall, is refused as before, and tries again after two seconds. The vehicle lands square to the grid it ends
   on, with its facing kept in the world.
5. **Effects.** The operator, interior link, lock, hardpoints and occupants stay, because only the vehicle's transform
   changes. Speed goes to zero. The frame loses a share of its maximum integrity and each wheel a flat amount through
   `HardpointSystem.DamageHardpoint` and `VehicleWheelSystem.DamageWheels`:

   | Weight class | Frame | Per wheel | Mob under the landing (Blunt) |
   |---|---|---|---|
   | Weak | 10% | 50 | 25 |
   | Light | 15% | 75 | 35 |
   | Medium | 20% | 100 | 50 |
   | Heavy | 25% | 125 | 70 |

   Heavier vehicles come down harder; the numbers are the author's picks and `VehicleFallComponent` can override all three.

   A vehicle with no `HardpointIntegrity` has no frame to hurt. If it has a `Damageable` the landing hurts that instead: the
   same share (10/15/20/25%) of `VehicleFallComponent.BodyHealth` as Blunt damage, ignoring resistances, through
   `DamageableSystem`, so a destruction threshold or any listener on the damage (the Bison arming itself) answers it as it
   would any other damage. A vehicle that the damage destroys skips the rest of the landing. The Bison sets `bodyHealth: 300`,
   the damage at which it arms its self-destruct.
   Everyone inside gets the strongest interior crash effect (`VehicleInteriorSystem.DoInteriorCrashEffect`): camera kick,
   and for those not strapped in a knockdown and a throw. A mob under the landing is hurt, knocked down for 3 s and held
   clear of the hull as a run-over (`VehicleRunoverComponent`). Smashable things are hit by the mover's own smash
   (`GridVehicleMoverSystem.SmashTarget`) with the settings `TryGetSmashable` gives, so a table lands as it would be
   driven into: sized to its thresholds, destroyed once, leaving its sheet.
6. **Others** can listen to `VehicleFellEvent` on the vehicle. The Bison does: a remote pilot keeps the carrier through a fall
   (see `bison.md`).

## The driver's and gunner's view

The engine side works: the driver's eye targets the vehicle (`SharedEyeSystem.SetTarget` adds the vehicle as a view
subscriber), so PVS and the biome loader follow the vehicle to its new map, and the main viewport draws the vehicle's
level. What does not work is everything CE draws around that level, because CE reads the viewer's body, which is on the
interior map and not in any z-network:

- Server, `CEZLevelsSystem.View.cs`: `UpdateViewer` (`:147`) takes the map and position from `Transform(ent)` for the
  extra eyes on the levels above and below; `UpdateView` (`:49`) keeps them at the viewer's position. A driver gets none.
- Client, `ScalingViewport.CEZLevels.cs`: `RenderZLevels` takes `playerMap` from `_player.LocalEntity` (`:115-127`), walks
  `TryMapOffset(playerMap, -1)` (`:153`) and reads `GetLocalAltitude` / `GetAbsoluteAltitude` of the body (`:148`, `:227`).
  For a driver nothing above or below is drawn, before and after a fall.

The fix is in CE's files, not in the module: use the eye target's transform (`EyeComponent.Target ?? body`) for the map
and position in those places, and refresh the viewer when the target changes map. It was not done here.

## Not verified

- Nothing was seen in a real client: the sprite, the landing sound and dust, and the camera were not looked at.
- Prediction is tested only as far as the open-tile answer agreeing on both sides; a driven vehicle crossing an edge with a
  predicting client was not run.
- A multi-level drop is one level at a time: a landing needs floor, so the vehicle never lands in the air.
- A vehicle the mover has already handed to the map (above) whose landing has since gone, because the level below changed
  in the second between the mover's check and the drop, stays on the map and is not driven until an admin moves it.
- Planet caverns: landing on a mouth's pad was not exercised, only the unloaded-ground rule with a stand-in biome.
