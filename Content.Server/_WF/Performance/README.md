# Performance

Small fixes for hot spots found in live server profiles, kept as narrow hooks into upstream systems.

- `WFCollectionExtensions.ClearAndTrim` clears a reused scratch set and frees its storage after a crowded query, so
  atmos and mover lookups don't keep paying for one huge pile of items.
- `SharedGravitySystem.WFRefresh` batches weightlessness refreshes for half a second after a grid's gravity changes and
  walks only that grid's entities, instead of every gravity-affected entity on the server per change.
- `PathfindingSystem` skips mining rock grids (`WFAsteroidGridComponent`). Building their nav chunks cost about 2.6 ms
  per new rock grid, nearly all the cost of a field loading in, and no NPC walks on one.
- `DockingSystem.WFPlacement` speeds up the dock search a ship purchase or FTL dock runs: pairs of docks that would put
  the ship somewhere else are ruled out before the tile check rather than after it, and the tile check skips the
  station's empty chunks. It picks the same dock as before; a six-dock ship at a 77-dock station went from seconds to
  about 60 ms.
- Worldgen churn: a space chunk stays loaded for `wf.worldgen.unload_grace` seconds (60) after nothing needs it
  (`WorldControllerSystem.WFUnloadGrace`), and a GC queue's `minAge` holds entities that long before deleting them
  (`GCQueueSystem.WFMinAge`; `WFAsteroids` waits 60 s). A ship that comes back within the grace keeps the asteroid
  fields around it instead of unloading and respawning them.
- `WFAsteroidCullSystem` stops sending every worldgen rock grid to every player. A player is sent a rock grid once one
  of their views comes within `wf.asteroids.send_range` (128 m) and keeps it until the rock is deleted, since the
  client can't detach a grid. Each worldgen chunk with rocks gets a field entity (`WFAsteroidFieldComponent`), sent to
  everyone, listing its rocks' position, rotation and look; on the client `WFAsteroidFieldSystem` reads it so radar
  (`ShuttleNavControl.WFAsteroidFields`) and the far overlay draw rocks whose grids haven't been sent. Rocks no chunk
  owns are sent to everyone as before, `ChunkingSystem` skips rock grids (they hold no decals or gas), and
  `wf.asteroids.cull false` turns it all off live.
- Roofs network as chunk deltas (`SharedRoofSystem.WFDelta`). Maps go to every player, and a cavern map's full roof
  is tens of kilobytes; each tile change on the surface above resent all of it, which emptied clients' state buffers.
  A delta carries only the chunks changed since the client's last ack.
- Radar requests share one walk of the blips per map per tick (`RadarBlipSystem.WFCache`), and console nav states share
  one walk of the docks for up to half a second, or until something docks or undocks (`ShuttleConsoleSystem.WFDocks`).
- Radar consoles ask for blips at `wf.radar.request_hz` (15 a second) instead of twice a second, so a turning missile
  stops lurching between straight-line guesses, and the console finds each blip's grid from its own coordinates instead
  of a spatial query per blip per frame. `ShuttleNavControl`'s radar interval was zero, which divided by zero and drew
  the sweep line to a NaN point.
- `WFPlanetDragSystem` walks the children of planet layers instead of every grid. Suit sensors, APCs, sound spammers and
  pickup magnets look for due work every third tick, locality loaders check twice a second, and a dry auto-drain
  searches for puddles every 5 s.
- A drydock break lifts its finished tiles together every 10 s (`WFDrydockJobComponent.Lifting`) instead of one at a
  time, since each lift resends every fixture of the hull (about 9 KB on a derelict, 50 KB on a big ship) to every player.
- The client Z-level sprite passes (`CEClientZLevelsPreAnimSystem`, `CEClientZLevelsPostAnimSystem`) walk only
  entities off the ground or just landed (`CESharedZLevelsSystem.WFRaised`), instead of every entity with Z physics.
  The full walk over about 52,000 entities took 38% of a live client's game thread. A height joins the list from
  server state, prediction or `SetZPosition`, so raise an entity through those rather than writing `LocalPosition`.
- Nav map deltas (`NavMapComponent.WFDelta`) name the chunks removed since the client's ack instead of listing every
  chunk on the grid, which cost about 30 KB per update on a planet map. The history keeps the newest removals
  (`NavMapSystem.WFRemoved`), and a client behind what it dropped gets a full state.
- A ship sale no longer stalls the tick. `WFHullRetireSystem` (in `_WF/Shipyard`) parks the hull on a paused map, then
  serialises and deletes it 4 ms per tick, and writes the YAML text on a worker. All at once, a 2,750-entity hull took
  about 600 ms to serialise and 150 ms to delete. It logs `Retired <hull>: copy ... delete ... longest ...`.
- One-second jobs take a tick each instead of landing together (`WFTickSpread`): radius heaters and drains. Wound
  overlay refreshes take a share per tick, and fixture damage and the atmos alerts console start on their own offsets.
  Radius heaters also look only for loose things to warm, not anchored or stored ones.
- `IgnitionSourceSystem` walks only lit sources (`WFLit`), and a ship's shield follows it with one transform call.
- More one-second jobs stop landing on one tick: Wolfmed body ticks are spread with `WFTickSpread`, and encounter
  zones, ship status, crew ship status, cargo load and thermal signatures each start on their own part of the second.
- AI ship steering works every other tick and holds its input between (`ShipSteeringSystem`), and `FlammableSystem`
  looks for what's due every third tick.
- NPC ship crews cost less in a fleet fight. A gunner's costlier post checks and target re-checks run four times a second
  (`WFGunnerDutySystem.HoldsPost`), guns are aimed every other tick (`ShipTargetingSystem.WFAimPeriod`; a shot attempt
  lasts 0.2 s, so firing doesn't stop between), and steering runs its area searches every third turn and skips the
  sector maths when nothing is near.
- `WFDeletedRefStateSystem` hand-writes the state of components whose references can outlive what they point at
  (projectiles, deeds, guest access, station grids). A generated state logged an error with a stack trace per deleted
  reference, per send, on the PVS threads. A deleted reference goes out as invalid, and set entries are left out.
- `WFGridDeleteWatchSystem` logs any grid that takes at least 50 ms to delete, by name and entity count.
- The marked upstream edits skip idle-shuttle thruster work, avoid `Enum.GetValues` and nullable boxing on per-tick
  paths, stop `FlammableSystem` from crashing when igniting queues new fire events, and stop networking a thrown
  item's shooter (it outlived the shooter and sent a deleted entity every state).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Performance/AtmosAlertsComputerSystem.WFBuckets.cs`](AtmosAlertsComputerSystem.WFBuckets.cs)
- [`Content.Server/_WF/Performance/AtmosphereSystem.WFBreachLoop.cs`](AtmosphereSystem.WFBreachLoop.cs)
- [`Content.Server/_WF/Performance/AtmosphereSystem.WFMonstermosLimit.cs`](AtmosphereSystem.WFMonstermosLimit.cs)
- [`Content.Server/_WF/Performance/CEZLevelsSystem.WFGroundSample.cs`](CEZLevelsSystem.WFGroundSample.cs)
- [`Content.Server/_WF/Performance/DockingSystem.WFPlacement.cs`](DockingSystem.WFPlacement.cs)
- [`Content.Server/_WF/Performance/EntityRadiusHeaterSystem.WFSpread.cs`](EntityRadiusHeaterSystem.WFSpread.cs)
- [`Content.Server/_WF/Performance/GCQueuePrototype.WFMinAge.cs`](GCQueuePrototype.WFMinAge.cs)
- [`Content.Server/_WF/Performance/GCQueueSystem.WFMinAge.cs`](GCQueueSystem.WFMinAge.cs)
- [`Content.Server/_WF/Performance/IgnitionSourceSystem.WFLit.cs`](IgnitionSourceSystem.WFLit.cs)
- [`Content.Server/_WF/Performance/LocalityLoaderSystem.WFDespawn.cs`](LocalityLoaderSystem.WFDespawn.cs)
- [`Content.Server/_WF/Performance/NavMapSystem.WFPlanetLayers.cs`](NavMapSystem.WFPlanetLayers.cs)
- [`Content.Server/_WF/Performance/NavMapSystem.WFRemoved.cs`](NavMapSystem.WFRemoved.cs)
- [`Content.Server/_WF/Performance/RadarBlipSystem.WFCache.cs`](RadarBlipSystem.WFCache.cs)
- [`Content.Server/_WF/Performance/ShipSteererComponent.WFStagger.cs`](ShipSteererComponent.WFStagger.cs)
- [`Content.Server/_WF/Performance/ShuttleConsoleSystem.WFDocks.cs`](ShuttleConsoleSystem.WFDocks.cs)
- [`Content.Server/_WF/Performance/SimpleFloorPlanPopulatorSystem.WFBudget.cs`](SimpleFloorPlanPopulatorSystem.WFBudget.cs)
- [`Content.Server/_WF/Performance/WFAsteroidCullSystem.cs`](WFAsteroidCullSystem.cs)
- [`Content.Server/_WF/Performance/WFDockIndexSystem.cs`](WFDockIndexSystem.cs)
- [`Content.Server/_WF/Performance/WFGridDeleteSystem.cs`](WFGridDeleteSystem.cs)
- [`Content.Server/_WF/Performance/WFGridDeleteWatchSystem.cs`](WFGridDeleteWatchSystem.cs)
- [`Content.Server/_WF/Performance/WFSerializerWarmupSystem.cs`](WFSerializerWarmupSystem.cs)
- [`Content.Server/_WF/Performance/WorldControllerSystem.WFIdleHelms.cs`](WorldControllerSystem.WFIdleHelms.cs)
- [`Content.Server/_WF/Performance/WorldControllerSystem.WFUnloadGrace.cs`](WorldControllerSystem.WFUnloadGrace.cs)

### Shared

- [`Content.Shared/_WF/Performance/CESharedZLevelsSystem.WFRaised.cs`](../../../Content.Shared/_WF/Performance/CESharedZLevelsSystem.WFRaised.cs)
- [`Content.Shared/_WF/Performance/NavMapComponent.WFDelta.cs`](../../../Content.Shared/_WF/Performance/NavMapComponent.WFDelta.cs)
- [`Content.Shared/_WF/Performance/RoofComponent.WFDelta.cs`](../../../Content.Shared/_WF/Performance/RoofComponent.WFDelta.cs)
- [`Content.Shared/_WF/Performance/SharedGravitySystem.WFRefresh.cs`](../../../Content.Shared/_WF/Performance/SharedGravitySystem.WFRefresh.cs)
- [`Content.Shared/_WF/Performance/SharedMoverController.WFAroundCollider.cs`](../../../Content.Shared/_WF/Performance/SharedMoverController.WFAroundCollider.cs)
- [`Content.Shared/_WF/Performance/SharedRoofSystem.WFDelta.cs`](../../../Content.Shared/_WF/Performance/SharedRoofSystem.WFDelta.cs)
- [`Content.Shared/_WF/Performance/WFAsteroidFieldComponent.cs`](../../../Content.Shared/_WF/Performance/WFAsteroidFieldComponent.cs)
- [`Content.Shared/_WF/Performance/WFCollectionExtensions.cs`](../../../Content.Shared/_WF/Performance/WFCollectionExtensions.cs)
- [`Content.Shared/_WF/Performance/WFDeletedRefStateSystem.cs`](../../../Content.Shared/_WF/Performance/WFDeletedRefStateSystem.cs)
- [`Content.Shared/_WF/Performance/WFPerformanceCVars.cs`](../../../Content.Shared/_WF/Performance/WFPerformanceCVars.cs)
- [`Content.Shared/_WF/Performance/WFRadarCVars.cs`](../../../Content.Shared/_WF/Performance/WFRadarCVars.cs)
- [`Content.Shared/_WF/Performance/WFTickBuckets.cs`](../../../Content.Shared/_WF/Performance/WFTickBuckets.cs)
- [`Content.Shared/_WF/Performance/WFTickSpread.cs`](../../../Content.Shared/_WF/Performance/WFTickSpread.cs)

### Client

- [`Content.Client/_WF/Performance/ShuttleNavControl.WFAsteroidFields.cs`](../../../Content.Client/_WF/Performance/ShuttleNavControl.WFAsteroidFields.cs)
- [`Content.Client/_WF/Performance/WFAsteroidFieldSystem.cs`](../../../Content.Client/_WF/Performance/WFAsteroidFieldSystem.cs)
- [`Content.Client/_WF/Performance/WFNetBufferSystem.cs`](../../../Content.Client/_WF/Performance/WFNetBufferSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Performance/GridDeleteTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/GridDeleteTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/NavMapPlanetLayerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/NavMapPlanetLayerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFAroundColliderTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFAroundColliderTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFAsteroidCullTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFAsteroidCullTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFBottomlessRegenTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFBottomlessRegenTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFBreachLoopTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFBreachLoopTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFDeletedRefStateTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFDeletedRefStateTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFFlammableDebrisTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFFlammableDebrisTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFGravityRefreshTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFGravityRefreshTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFGridDeleteWatchTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFGridDeleteWatchTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFGroundSampleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFGroundSampleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFIgnitionLitTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFIgnitionLitTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFNavMapDeltaTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFNavMapDeltaTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFNavMapTrimTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFNavMapTrimTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFPreferencesRefreshTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFPreferencesRefreshTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFRadiusHeaterTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFRadiusHeaterTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFRoofDeltaTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFRoofDeltaTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFSharedWalkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFSharedWalkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFWorldgenChurnTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFWorldgenChurnTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Performance/WFZLevelOffsetTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Performance/WFZLevelOffsetTest.cs)

### Unit tests

- [`Content.Tests/_WF/Performance/WFTickBucketsTest.cs`](../../../Content.Tests/_WF/Performance/WFTickBucketsTest.cs)
- [`Content.Tests/_WF/Performance/WFTickSpreadTest.cs`](../../../Content.Tests/_WF/Performance/WFTickSpreadTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/Performance/performance.ftl`](../../../Resources/Locale/en-US/_WF/Performance/performance.ftl)

### Docs

- [`Docs/_WF/Performance/README.md`](../../../Docs/_WF/Performance/README.md)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/CEClientZLevelsSystem.cs`](../../../Content.Client/_CE/ZLevels/Core/CEClientZLevelsSystem.cs)
  - a height from the server joins the sprite passes
  - spawned off the ground
  - the raised list and the lookup that replace the all-entity query
  - walk only raised entities; nothing else has a height to strip
  - the raised list and the lookups that replace the all-entity query
  - walk only raised entities; one that has landed leaves the list
  - removed after the walk, which can't change the set it enumerates
- [`Content.Client/_Mono/Radar/RadarBlipsSystem.cs`](../../../Content.Client/_Mono/Radar/RadarBlipsSystem.cs)
  - blips arrived at 2 Hz, so a curving missile lurched between straight-line guesses.
  - lookups and colours for the missile lines, which are rebuilt on every draw frame
  - the sent coord already names its grid, so only an off-grid blip needs the query
  - one dictionary lookup per missile, not a scan of every blip per missile.
- [`Content.Client/Atmos/Consoles/AtmosMonitoringConsoleNavMapControl.cs`](../../../Content.Client/Atmos/Consoles/AtmosMonitoringConsoleNavMapControl.cs): wizden#38974, subnets carry a Color instead of a hex string
- [`Content.Client/Pinpointer/NavMapSystem.cs`](../../../Content.Client/Pinpointer/NavMapSystem.cs): removals are named directly
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - the sweep divided by the zero interval, so the sweep hand keeps a period of its own.
  - the sweep hand's own timer, since the request accumulator is zeroed each frame
  - the sweep hand turns on its own timer, so it keeps moving while requests are throttled
  - this divided by a zero interval and drew the sweep to a NaN point every frame
  - distant rocks from their field lists
- [`Content.Client/Weapons/Ranged/Systems/GunSystem.cs`](../../../Content.Client/Weapons/Ranged/Systems/GunSystem.cs): wizden#36641, was EntityUid
- [`Content.IntegrationTests/Tests/Gravity/WeightlessStatusTests.cs`](../../../Content.IntegrationTests/Tests/Gravity/WeightlessStatusTests.cs)
  - weightlessness now refreshes half a second after gravity changes
  - batched weightlessness refresh
- [`Content.Server/_CE/ZLevels/Core/CEZGridConnectorSystem.cs`](../../_CE/ZLevels/Core/CEZGridConnectorSystem.cs): links only depend on a tile existing, so ignore swaps between solid tiles and runs with no connector
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Gravity.cs)
  - scratch collections instead of three allocations per call
  - scratch state for CollectRigidSet
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.GroundFriction.cs): reuse a parked grid's sample instead of re-walking its hull every tick
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.Transit.cs)
  - grids are force-sent and nearby viewers get transit z-eyes; a global override recursed every hull child each tick
  - the matching add was removed
- [`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs`](../../_CE/ZLevels/Core/CEZLevelsSystem.WallCollision.cs): a sleeping hull has not moved since its last check
- [`Content.Server/_Crescent/ShipShields/ShipShieldsSystem.cs`](../../_Crescent/ShipShields/ShipShieldsSystem.cs)
  - unused, and the lookup is costly
  - nearby sessions only, not a global override
- [`Content.Server/_EinsteinEngines/Silicon/Charge/Systems/SiliconChargeSystem.cs`](../../_EinsteinEngines/Silicon/Charge/Systems/SiliconChargeSystem.cs)
  - drain pass timing
  - a battery swap is handled on the next tick instead of waiting for the 1 s pass
  - make the next drain pass due immediately
  - every silicon drains on a 1 s accumulator; NPCs keep their cvar delay
  - drainTime replaces frameTime
  - the event fan-out and speed refresh only matter when the state changes
- [`Content.Server/_EinsteinEngines/Silicon/Death/Systems/SiliconChargeDeathSystem.cs`](../../_EinsteinEngines/Silicon/Death/Systems/SiliconChargeDeathSystem.cs): already dead, nothing to redo
- [`Content.Server/_Mono/Claws/ClawsSystem.cs`](../../_Mono/Claws/ClawsSystem.cs): GrowTimer is not networked and the stage did not change
- [`Content.Server/_Mono/Detection/ThermalSignatureSystem.cs`](../../_Mono/Detection/ThermalSignatureSystem.cs)
  - off the tick other one-second systems run on
  - each pass refreshes one quarter of the signatures, by uid
  - only this pass's slice is refreshed, over the time since it was last
  - the other slices keep their stored heat and only add to the totals
- [`Content.Server/_Mono/Drone/EntitySystems/DeleteNearbyDronesSystem.cs`](../../_Mono/Drone/EntitySystems/DeleteNearbyDronesSystem.cs)
  - throttle for Update
  - drones read once per pass
  - throttled to 2 Hz, drones read once per pass, and only drones on the grid's own map count
- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs)
  - only the weapon's own grid can block, so the ray ends where it does
  - no .ToList(), the result is only tested for emptiness
  - was raycastResults.Count == 0
- [`Content.Server/_Mono/Fuel/MaterialFuelSystem.cs`](../../_Mono/Fuel/MaterialFuelSystem.cs): was return, which skipped every later burning fuel entity
- [`Content.Server/_Mono/NPC/HTN/ShipSteeringSystem.cs`](../../_Mono/NPC/HTN/ShipSteeringSystem.cs)
  - how often steering works out a new input
  - how many steering turns one area search for obstacles lasts
  - steering works every other tick and holds its input between, saving its scans
  - holds this pilot's last obstacle search
  - this input holds for the whole turn
  - held until the next turn
  - the area searches run every few turns; what they found is looked at again every turn
  - fills the kept set, was a new one per search
  - end of the area searches
  - with nothing to avoid the wish is clear, so skip working out every sector's thrust
- [`Content.Server/_Mono/NPC/HTN/ShipTargetingSystem.cs`](../../_Mono/NPC/HTN/ShipTargetingSystem.cs)
  - how often a ship's guns are aimed; a shot attempt lasts 0.2 s, so they keep firing between
  - each turn covers the whole period
  - each ship aims on its own tick of the period
- [`Content.Server/_Mono/Projectiles/TargetSeeking/TargetSeekingSystem.cs`](../../_Mono/Projectiles/TargetSeeking/TargetSeekingSystem.cs)
  - mines (zero scan arc) never acquire; hoist the per-seeker values out of the loop
  - other maps and out-of-range consoles are rejected before the angle math
  - hoisted above
  - computed and range-tested above
  - see above
- [`Content.Server/_Mono/Radar/RadarBlipSystem.cs`](../../_Mono/Radar/RadarBlipSystem.cs)
  - removals go only to sessions that asked recently
  - send removals only to recent radar users, none during round cleanup
  - every radar on a map shares one walk of the blips per tick
- [`Content.Server/_Mono/Ships/Systems/ShuttleRestrictionsSystem.cs`](../../_Mono/Ships/Systems/ShuttleRestrictionsSystem.cs): integer division made this 0 or 1, so the 0.5 threshold needed every console powered
- [`Content.Server/_Mono/Temperature/Systems/EntityRadiusHeaterSystem.cs`](../../_Mono/Temperature/Systems/EntityRadiusHeaterSystem.cs)
  - each heater takes its own tick of the second, not all on one
  - what a fire warms is loose; skipping anchored and stored things skips the trees round a planet's fires
  - each heater takes its own tick
- [`Content.Server/_NF/StationEvents/Events/BluespaceErrorRule.cs`](../../_NF/StationEvents/Events/BluespaceErrorRule.cs)
  - frontier#4800 extended, appraise at spawn only for the currency injection
  - frontier#4800, skip the end appraisal when nothing uses the value
- [`Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs`](../../_Onyx/Body/Systems/OrganHealthSystem.cs)
  - the failed-organ scan runs four times a second, not every tick
  - an organ that just failed is handled next tick, not at the next timed scan
- [`Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs`](../../_Onyx/Wounds/WoundBleedingSystem.cs): clotting deadlines are checked four times a second, not every tick
- [`Content.Server/Anomaly/AnomalySystem.Generator.cs`](../../Anomaly/AnomalySystem.Generator.cs): wizden#41390, was IsTileAirBlocked
- [`Content.Server/Atmos/Consoles/AtmosAlertsComputerSystem.cs`](../../Atmos/Consoles/AtmosAlertsComputerSystem.cs)
  - off the tick other one-second systems start on
  - consoles with a closed UI share one sweep of the alarms
  - closed consoles only need the alert level for their screen
- [`Content.Server/Atmos/Consoles/AtmosMonitoringConsoleSystem.cs`](../../Atmos/Consoles/AtmosMonitoringConsoleSystem.cs)
  - wizden#38974
  - wizden#38974, drop pipe data of removed pipe nets
  - wizden#38974, was pipeColor.Color.ToHex()
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.API.cs`](../../Atmos/EntitySystems/AtmosphereSystem.API.cs)
  - wizden#41390, cached airtight check
  - wizden#38974, tell the atmos monitor a pipe net is gone
  - wizden#38974, pipe net removal event
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.DeltaPressure.cs`](../../Atmos/EntitySystems/AtmosphereSystem.DeltaPressure.cs)
  - fixed-size tile buffer for ProcessDeltaPressureEntity
  - skip the shared RNG when the chance is certain (also thread-safe)
  - stack buffer instead of a per-entity array allocation
  - span so callers can use a stack buffer
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.HighPressureDelta.cs`](../../Atmos/EntitySystems/AtmosphereSystem.HighPressureDelta.cs): a crowded tile left the set huge and slowed every clear
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs): a crowded tile left the set huge and slowed every clear
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs)
  - without space nothing past the equalize limit is ever used, so stop the search there
  - search limit reached
  - a room refilling into a hole vents from the same tile forever; let it leak quietly
  - used range for the cleanup below
  - clear only the range this run used
- [`Content.Server/Atmos/EntitySystems/AutomaticAtmosSystem.cs`](../../Atmos/EntitySystems/AutomaticAtmosSystem.cs): unclaimed wrecks never need an atmosphere
- [`Content.Server/Atmos/EntitySystems/FlammableSystem.cs`](../../Atmos/EntitySystems/FlammableSystem.cs)
  - double buffer, so fire events queued while igniting cannot break the loop
  - see Update
  - swap buffers so events raised while igniting wait for the next tick
  - clear the processed buffer
  - burning can destroy things and spawn flammable debris, which broke the live query
- [`Content.Server/Atmos/EntitySystems/GasTileOverlaySystem.cs`](../../Atmos/EntitySystems/GasTileOverlaySystem.cs): don't send empty chunk lists
- [`Content.Server/Atmos/EntitySystems/HeatExchangerSystem.cs`](../../Atmos/EntitySystems/HeatExchangerSystem.cs): wizden#41390, was IsTileAirBlocked
- [`Content.Server/Chunking/ChunkingSystem.cs`](../../Chunking/ChunkingSystem.cs): mining rocks never hold decals or gas, and are only sent to players near them
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`](../../Explosion/EntitySystems/ExplosionSystem.Processing.cs): Debug, was Info; it logged every step of every explosion
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.Visuals.cs`](../../Explosion/EntitySystems/ExplosionSystem.Visuals.cs): sessions on the explosion's map instead of every client
- [`Content.Server/Explosion/EntitySystems/ProjectileGrenadeSystem.cs`](../../Explosion/EntitySystems/ProjectileGrenadeSystem.cs): wizden#36641, the grenade is deleted so it can't be the gun; was uid, null
- [`Content.Server/Fluids/EntitySystems/DrainSystem.cs`](../../Fluids/EntitySystems/DrainSystem.cs)
  - each drain takes its own tick
  - each drain takes its own tick of the second, not all on one
  - a dry drain searches less often
- [`Content.Server/GameTicking/Rules/GameRuleSystem.Utility.cs`](../../GameTicking/Rules/GameRuleSystem.Utility.cs): wizden#41390, was IsTileAirBlocked
- [`Content.Server/IgnitionSource/IgnitionSourceSystem.cs`](../../IgnitionSource/IgnitionSourceSystem.cs): walk a copy of the lit list; a fire this starts can light another source
- [`Content.Server/Medical/SuitSensors/SuitSensorSystem.cs`](../../Medical/SuitSensors/SuitSensorSystem.cs)
  - finding what's due walks every sensor; NextUpdate keeps the rate
  - the status is built below, once a server is known to be there to take it.
  - check the server before building the status and its packet.
  - the status and packet, moved here from above.
- [`Content.Server/Movement/Systems/LagCompensationSystem.cs`](../../Movement/Systems/LagCompensationSystem.cs)
  - only entities holding recent positions are culled each tick
  - walk only the entities that hold positions instead of every lag-compensated mob
  - now holds a position to cull
- [`Content.Server/Movement/Systems/MobCollisionSystem.cs`](../../Movement/Systems/MobCollisionSystem.cs): a sleeping body is not moving or being moved, so it has nothing to push
- [`Content.Server/NPC/HTN/HTNSystem.cs`](../../NPC/HTN/HTNSystem.cs): was 0.02; keeps one planning slice under the 0.004 queue budget
- [`Content.Server/NPC/HTN/PrimitiveTasks/Operators/MoveToOperator.cs`](../../NPC/HTN/PrimitiveTasks/Operators/MoveToOperator.cs): a replan toward the coordinates steering already walks to keeps that path instead of running A* again
- [`Content.Server/NPC/Pathfinding/PathfindingSystem.Grid.cs`](../../NPC/Pathfinding/PathfindingSystem.Grid.cs)
  - aggregate pre-reject in OnMoveEvent
  - the body's aggregate layer, mask and hard bound every fixture's, so skip the fixture walk
  - no NPC walks a mining rock, and building its nav chunks cost ~2.6 ms per new rock grid
  - wizden#44340, tile entities carry their fixtures
  - wizden#44340, resolve transforms and hard fixtures once per tile
  - wizden#44340, iterate the cached hard fixtures
- [`Content.Server/NPC/Systems/NPCCombatSystem.Ranged.cs`](../../NPC/Systems/NPCCombatSystem.Ranged.cs): was return, which skipped every NPC after one whose gun was cooling down
- [`Content.Server/NPC/Systems/NPCSystem.cs`](../../NPC/Systems/NPCSystem.cs)
  - replaced by a snapshot pass spread over ~60 ticks
  - see above
  - process the next slice of the sleep/wake snapshot
- [`Content.Server/NPC/Systems/NPCUtilitySystem.cs`](../../NPC/Systems/NPCUtilitySystem.cs)
  - reused by NearbyHostilesQuery
  - scratch-list overload avoids LINQ and per-call allocation
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs)
  - gauge of sleeping movers skipped
  - idle-mover skip
  - movers skipped this tick for being asleep with no input
  - reset idle-skip counter
  - a sleeping, input-less, player-less mover has nothing to move
  - idle movers skipped
  - Enum.GetValues allocated every tick per shuttle
- [`Content.Server/Pinpointer/NavMapSystem.cs`](../../Pinpointer/NavMapSystem.cs)
  - a planet layer grid's nav map would cover the whole world and nothing shows it
  - back in use, so no longer a removal
  - deltas name removed chunks; the rebuild below takes back any still in use
  - the history of removed chunks stays bounded
  - a chunk that was never sent changes nothing for clients
  - deltas name removed chunks, and the history of them stays bounded
- [`Content.Server/Power/EntitySystems/ApcSystem.cs`](../../Power/EntitySystems/ApcSystem.cs): every APC is walked for a UI refresh or a deferred state update that can wait a tick or two
- [`Content.Server/Power/EntitySystems/BatteryInterfaceSystem.cs`](../../Power/EntitySystems/BatteryInterfaceSystem.cs)
  - UI state is pushed at 4 Hz, and only for interfaces with a viewer
  - ActiveUserInterfaceComponent is the engine's open-UI marker
  - skip the ActiveUserInterfaceComponent slot
- [`Content.Server/Power/EntitySystems/BatterySystem.cs`](../../Power/EntitySystems/BatterySystem.cs): was -= _updateAccumulator, which zeroed it
- [`Content.Server/Power/Generator/PortableGeneratorSystem.cs`](../../Power/Generator/PortableGeneratorSystem.cs)
  - UI state is pushed at 4 Hz, and only for generators with a viewer
  - ActiveUserInterfaceComponent is the engine's open-UI marker
  - skip the ActiveUserInterfaceComponent slot
- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenCornerClutter.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenCornerClutter.cs): yield between tiles so a big dungeon never finishes in one tick
- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenCorridorDecalSkirting.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenCorridorDecalSkirting.cs): yield between tiles so a big dungeon never finishes in one tick
- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenDungeonEntrance.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenDungeonEntrance.cs): yield between entrances so a big dungeon never finishes in one tick
- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenEntranceFlank.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenEntranceFlank.cs): yield between spawns so a big dungeon never finishes in one tick
- [`Content.Server/Procedural/DungeonJob/DungeonJob.PostGenRoomEntrance.cs`](../../Procedural/DungeonJob/DungeonJob.PostGenRoomEntrance.cs): yield between rooms so a big dungeon never finishes in one tick
- [`Content.Server/Shuttles/Systems/DockingSystem.Shuttle.cs`](../../Shuttles/Systems/DockingSystem.Shuttle.cs)
  - rule out pairs that land elsewhere before the tile walk, not after
  - chunk-first walk, same result; this ran per dock pair for every fixture
  - read the dock index instead of walking every entity on the grid
- [`Content.Server/Shuttles/Systems/GridDeletionContainerSystem.cs`](../../Shuttles/Systems/GridDeletionContainerSystem.cs): logs grids that take long to delete
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
  - consoles should see a new dock at once
  - consoles should see an undock at once
  - every open console asks for this, so share one walk of the docks
- [`Content.Server/Shuttles/Systems/ThrusterSystem.cs`](../../Shuttles/Systems/ThrusterSystem.cs)
  - fixed list instead of Enum.GetValues on every call
  - idle shuttles call this every tick with everything already off
- [`Content.Server/Sound/EmitSoundSystem.cs`](../../Sound/EmitSoundSystem.cs): finding what's due walks every spammer; NextSound keeps the rate
- [`Content.Server/Spreader/SpreaderSystem.cs`](../../Spreader/SpreaderSystem.cs)
  - see Update
  - SpreaderGrid is created lazily by the first spreader on the grid
  - create the grid's queues on first use
  - empty queues for a grid that just got its first spreader; no scan, so spreaders still waiting for MapInit are not queued twice
  - queue spreaders deferred from the previous pass
  - adding a SpreaderGrid mid-query is unsafe, so defer spreaders moving to a grid without one
- [`Content.Server/Weapons/Ranged/Systems/GunSystem.AutoFire.cs`](../../Weapons/Ranged/Systems/GunSystem.AutoFire.cs)
  - guns mid-burst, filled from OnBurstActivated
  - only visit bursting guns and auto-shoot guns, not every GunComponent
- [`Content.Server/Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs`](../../Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs): still in its chunk, so the index needs no churn
- [`Content.Server/Worldgen/Systems/Debris/SimpleFloorPlanPopulatorSystem.cs`](../../Worldgen/Systems/Debris/SimpleFloorPlanPopulatorSystem.cs): spawns are queued and made a few ms per tick instead of in this handler
- [`Content.Server/Worldgen/Systems/GC/GCQueueSystem.cs`](../../Worldgen/Systems/GC/GCQueueSystem.cs)
  - queue minimum age
  - leave entities queued until the queue's minimum age has passed
  - keep an age-protected entry's first time, and overflow such queues within the time limit
- [`Content.Server/Worldgen/Systems/LocalityLoaderSystem.cs`](../../Worldgen/Systems/LocalityLoaderSystem.cs)
  - each loader checks twice a second on its own tick; a player can't outrun that
  - see above
  - restrictions come from a per-tick index, and the player scans run only if the grid holds one
- [`Content.Server/Worldgen/Systems/WorldControllerSystem.cs`](../../Worldgen/Systems/WorldControllerSystem.cs)
  - chunk unload grace
  - idle helms load nothing
  - drop a deleted chunk's unload grace timer
  - grids that have a player or pilot
  - parked, empty helms don't load worldgen
  - wait out the unload grace, so passing ships don't unload and respawn debris
  - loaded-chunk counter
- [`Content.Shared/_CE/ZLevels/Core/Components/CEZPhysicsComponent.cs`](../../../Content.Shared/_CE/ZLevels/Core/Components/CEZPhysicsComponent.cs): raises AfterAutoHandleState so the client notes a height from the server
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Activation.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Activation.cs)
  - a deleted entity leaves the sprite passes
  - a deleted body leaves the slope set
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Movement.cs)
  - reused by OnTileChanged
  - List.Contains per MoveEvent went quadratic in battles
  - below this a z set is a no-op
  - bodies whose ground varies within their tile
  - set by ComputeGroundHeightInternal on a stair curve
  - one typed query over the union of the changed tiles instead of an untyped query per tile
  - stair height and stickiness change within a tile
  - remember who stands on a stair curve
  - set lookup instead of List.Contains
  - reset the stair flag for this lookup
  - this ground varies within the tile
  - skip the dirty and wake when a convoy or parachute re-sets the same height
  - raised entities join the sprite passes
  - skip the dirty and wake when the velocity is unchanged
  - recompute ground height only when the tile changed
  - release a battle's oversized set afterwards
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Update.cs): predicted height joins the sprite passes
- [`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs`](../../../Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.WallCollision.cs)
  - reused by GetWallContacts
  - pooled copy, WallCollides removes from the set while we walk it
- [`Content.Shared/_Goobstation/Factory/RoboticArmSystem.cs`](../../../Content.Shared/_Goobstation/Factory/RoboticArmSystem.cs): the check was inverted, so the arms never ran
- [`Content.Shared/_Mono/CloakHeat/Systems/CloakHeatSystem.cs`](../../../Content.Shared/_Mono/CloakHeat/Systems/CloakHeatSystem.cs)
  - only dirty when the networked state changes
  - skip the dirty when nothing networked changed
- [`Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs`](../../../Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs): off the tick other one-second systems start on
- [`Content.Shared/_Mono/ForceParent/ForceParentSystem.cs`](../../../Content.Shared/_Mono/ForceParent/ForceParentSystem.cs): the second call was outside the if and ran on every move
- [`Content.Shared/_Mono/Shipyard/ShipGuestAccessComponent.cs`](../../../Content.Shared/_Mono/Shipyard/ShipGuestAccessComponent.cs)
  - was AutoGenerateComponentState; WFDeletedRefStateSystem sends the sets, whose entries can be deleted first
  - networked by WFDeletedRefStateSystem
- [`Content.Shared/_Mono/VendingMachine/VendingMachinePurchaseComponent.cs`](../../../Content.Shared/_Mono/VendingMachine/VendingMachinePurchaseComponent.cs): not networked, the client never reads it and the grid can be deleted first
- [`Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs`](../../../Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs): WFDeletedRefStateSystem sends the state, since the ship can be deleted first
- [`Content.Shared/_Onyx/Targeting/TargetingSnapshotComponent.cs`](../../../Content.Shared/_Onyx/Targeting/TargetingSnapshotComponent.cs): not networked; a thrown item outlives its thrower and sent a deleted entity every state
- [`Content.Shared/_White/Standing/SharedLayingDownSystem.cs`](../../../Content.Shared/_White/Standing/SharedLayingDownSystem.cs): only viewers of the body need it
- [`Content.Shared/Atmos/Consoles/Components/AtmosMonitoringConsoleComponent.cs`](../../../Content.Shared/Atmos/Consoles/Components/AtmosMonitoringConsoleComponent.cs)
  - wizden#38974, subnets carry a Color
  - wizden#38974, was string HexCode
- [`Content.Shared/CCVar/CCVars.Atmos.cs`](../../../Content.Shared/CCVar/CCVars.Atmos.cs): fewer thread-pool jobs per pass
- [`Content.Shared/Damage/Systems/DamageableSystem.cs`](../../../Content.Shared/Damage/Systems/DamageableSystem.cs)
  - size the dictionary once instead of growing it from empty on every spawn
  - size once
- [`Content.Shared/Fluids/Components/DrainComponent.cs`](../../../Content.Shared/Fluids/Components/DrainComponent.cs): when a dry auto-drain next searches for puddles
- [`Content.Shared/Gravity/SharedGravitySystem.cs`](../../../Content.Shared/Gravity/SharedGravitySystem.cs)
  - batched weightlessness refresh
  - batch per grid and walk only its entities, not every gravity-affected entity
  - upstream loop kept for merges
- [`Content.Shared/IgnitionSource/SharedIgnitionSourceSystem.cs`](../../../Content.Shared/IgnitionSource/SharedIgnitionSourceSystem.cs)
  - the server walks only lit sources
  - overridden on the server, which keeps the list of lit sources
- [`Content.Shared/Light/Components/RoofComponent.cs`](../../../Content.Shared/Light/Components/RoofComponent.cs): networked by SharedRoofSystem.WFDelta, which sends only changed chunks
- [`Content.Shared/Light/EntitySystems/SharedRoofSystem.cs`](../../../Content.Shared/Light/EntitySystems/SharedRoofSystem.cs): delta state sends only changed chunks
- [`Content.Shared/Movement/Systems/SharedMoverController.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.cs)
  - reuse a still mob's result for a few ticks
  - only static bodies count below, so skip the rest of the lookup and keep the set small
- [`Content.Shared/Movement/Systems/SharedMoverController.Input.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.Input.cs): Equals(object) boxed the nullable every call
- [`Content.Shared/NPC/Systems/NpcFactionSystem.cs`](../../../Content.Shared/NPC/Systems/NpcFactionSystem.cs): allocation-free hostile search for the NPC utility query
- [`Content.Shared/Pinpointer/SharedNavMapSystem.cs`](../../../Content.Shared/Pinpointer/SharedNavMapSystem.cs)
  - a client behind the trimmed removal history gets a full state
  - name only the removals since the client's ack, not every chunk on the grid
  - was allChunks, every chunk on the grid
  - replaces AllChunks
  - removals are named directly
  - removals are named directly; copies now fill the new data and new chunks count
- [`Content.Shared/Projectiles/ProjectileComponent.cs`](../../../Content.Shared/Projectiles/ProjectileComponent.cs)
  - was AutoGenerateComponentState; WFDeletedRefStateSystem sends the state, since Shooter and Weapon can be deleted first
  - networked by WFDeletedRefStateSystem
- [`Content.Shared/Station/Components/StationDataComponent.cs`](../../../Content.Shared/Station/Components/StationDataComponent.cs)
  - was AutoGenerateComponentState; WFDeletedRefStateSystem sends Grids, whose entries can be deleted first
  - networked by WFDeletedRefStateSystem
- [`Content.Shared/StationRecords/SharedStationRecordsSystem.cs`](../../../Content.Shared/StationRecords/SharedStationRecordsSystem.cs): a deleted station goes out as invalid, without logging an error on a PVS thread
- [`Content.Shared/Storage/EntitySystems/MagnetPickupSystem.cs`](../../../Content.Shared/Storage/EntitySystems/MagnetPickupSystem.cs): finding what's due walks every magnet; NextScan keeps the rate
- [`Content.Shared/Storage/EntitySystems/MaterialStorageMagnetPickupSystem.cs`](../../../Content.Shared/Storage/EntitySystems/MaterialStorageMagnetPickupSystem.cs): finding what's due walks every magnet; NextScan keeps the rate
- [`Content.Shared/Stunnable/SharedStunSystem.cs`](../../../Content.Shared/Stunnable/SharedStunSystem.cs): only viewers of the body need it
- [`Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs`](../../../Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs): a target can be deleted before the swing arrives
- [`Content.Shared/Weapons/Ranged/Components/TargetedProjectileComponent.cs`](../../../Content.Shared/Weapons/Ranged/Components/TargetedProjectileComponent.cs)
  - was AutoGenerateComponentState; WFDeletedRefStateSystem sends Target, which can be deleted first
  - networked by WFDeletedRefStateSystem
- [`Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.Ballistic.cs`](../../../Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.Ballistic.cs)
  - drop stale uids
  - a cartridge that left the container by other means must not stay in Entities
- [`Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs`](../../../Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs)
  - lets the server track bursting guns
  - wizden#36641, gunUid was not nullable
  - wizden#36641, a projectile may have no shooter
  - server hook so auto-fire only walks bursting guns
- [`Resources/Prototypes/_CE/Entities/Structures/water.yml`](../../../Resources/Prototypes/_CE/Entities/Structures/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/_Mono/Entities/Objects/Specific/Planet/flora.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Specific/Planet/flora.yml): decorative flora has no physics body, so passing mobs build no contacts with it
- [`Resources/Prototypes/_NF/Entities/Tiles/water.yml`](../../../Resources/Prototypes/_NF/Entities/Tiles/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/Entities/Tiles/water.yml`](../../../Resources/Prototypes/Entities/Tiles/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/GC/world.yml`](../../../Resources/Prototypes/GC/world.yml): debris overflow deleted in one tick; larger depth and a minimum age spread it

<!-- WOLFGATE-GENERATED END -->
