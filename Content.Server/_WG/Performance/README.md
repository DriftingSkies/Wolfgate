# Performance

Small fixes for hot spots found in live server profiles, kept as narrow hooks into upstream systems.

- `WGCollectionExtensions.ClearAndTrim` clears a reused scratch set and frees its storage after a crowded query, so
  atmos and mover lookups don't keep paying for one huge pile of items.
- `SharedGravitySystem.WGRefresh` batches weightlessness refreshes for half a second after a grid's gravity changes and
  walks only that grid's entities, instead of every gravity-affected entity on the server per change.
- `PathfindingSystem` skips mining rock grids (`WGAsteroidGridComponent`). Building their nav chunks cost about 2.6 ms
  per new rock grid, nearly all the cost of a field loading in, and no NPC walks on one.
- `DockingSystem.WGPlacement` speeds up the dock search a ship purchase or FTL dock runs: pairs of docks that would put
  the ship somewhere else are ruled out before the tile check rather than after it, and the tile check skips the
  station's empty chunks. It picks the same dock as before; a six-dock ship at a 77-dock station went from seconds to
  about 60 ms.
- Worldgen churn: a space chunk stays loaded for `wg.worldgen.unload_grace` seconds (60) after nothing needs it
  (`WorldControllerSystem.WGUnloadGrace`), and a GC queue's `minAge` holds entities that long before deleting them
  (`GCQueueSystem.WGMinAge`; `WGAsteroids` waits 60 s). A ship that comes back within the grace keeps the asteroid
  fields around it instead of unloading and respawning them.
- Light sounds (`WGLightSoundSystem`): upstream spawns, networks and deletes one entity for every sound, about
  270 a second on a full live server and more than anything else it makes. Footsteps, gunshots, casings and
  projectile hits go out as one message to the players in range instead, each client plays the sound itself, and
  one nobody is in range of is not sent at all. `wg.audio.light_sounds` off plays them the upstream way.
- Worldgen chunks are freed: upstream keeps one entity in nullspace for every space chunk anyone ever came near
  and never deletes it, 116,000 of them three and a half hours into a live round and the one count that never
  falls. A chunk that has been unloaded for `wg.worldgen.chunk_free_seconds` (600, 0 to keep them all) and owns no
  live debris is deleted. It is made again when someone returns, and lays out its rocks afresh, which an unloaded
  chunk whose debris was collected does anyway.
- `WGAsteroidCullSystem` stops sending every worldgen rock grid to every player. A player is sent a rock grid once one
  of their views comes within `wg.asteroids.send_range` (128 m) and keeps it until the rock is deleted, since the
  client can't detach a grid. Each worldgen chunk with rocks gets a field entity (`WGAsteroidFieldComponent`), sent to
  everyone, listing its rocks' position, rotation and look; on the client `WGAsteroidFieldSystem` reads it so radar
  (`ShuttleNavControl.WGAsteroidFields`) and the far overlay draw rocks whose grids haven't been sent. Rocks no chunk
  owns are sent to everyone as before, `ChunkingSystem` skips rock grids (they hold no decals or gas), and
  `wg.asteroids.cull false` turns it all off live.
- Roofs network as chunk deltas (`SharedRoofSystem.WGDelta`). Maps go to every player, and a cavern map's full roof
  is tens of kilobytes; each tile change on the surface above resent all of it, which emptied clients' state buffers.
  A delta carries only the chunks changed since the client's last ack.
- Radar requests share one walk of the blips every `wg.radar.rebuild_ticks` ticks (4) (`RadarBlipSystem.WGCache`),
  which sorts them into squares so a request skips the squares out of its reach. Between walks a request still
  re-checks each nearby blip and takes its position live, so stale squares can only make a blip late, never wrong.
  Console nav states share one walk of the docks for up to half a
  second, or until something docks or undocks (`ShuttleConsoleSystem.WGDocks`). The server answers one player's blip
  requests at most `wg.radar.max_request_hz` times a second (10); lower it under load.
- Radar consoles ask for blips at `wg.radar.request_hz` (15 a second) instead of twice a second, so a turning missile
  stops lurching between straight-line guesses, and the console finds each blip's grid from its own coordinates instead
  of a spatial query per blip per frame. `ShuttleNavControl`'s radar interval was zero, which divided by zero and drew
  the sweep line to a NaN point.
- `WGPlanetDragSystem` walks the children of planet layers instead of every grid. Suit sensors, APCs, sound spammers and
  pickup magnets look for due work every third tick, locality loaders check twice a second, and a dry auto-drain
  searches for puddles every 5 s.
- A drydock break lifts its finished tiles together every 10 s (`WGDrydockJobComponent.Lifting`) instead of one at a
  time, since each lift resends every fixture of the hull (about 9 KB on a derelict, 50 KB on a big ship) to every player.
- The client Z-level sprite passes (`CEClientZLevelsPreAnimSystem`, `CEClientZLevelsPostAnimSystem`) walk only
  entities off the ground or just landed (`CESharedZLevelsSystem.WGRaised`), instead of every entity with Z physics.
  The full walk over about 52,000 entities took 38% of a live client's game thread. A height joins the list from
  server state, prediction or `SetZPosition`, so raise an entity through those rather than writing `LocalPosition`.
- Nav map deltas (`NavMapComponent.WGDelta`) name the chunks removed since the client's ack instead of listing every
  chunk on the grid, which cost about 30 KB per update on a planet map. The history keeps the newest removals
  (`NavMapSystem.WGRemoved`), and a client behind what it dropped gets a full state.
- A ship sale no longer stalls the tick. `WGHullRetireSystem` (in `_WG/Shipyard`) parks the hull on a paused map, then
  serialises and deletes it 4 ms per tick, and writes the YAML text on a worker. All at once, a 2,750-entity hull took
  about 600 ms to serialise and 150 ms to delete. It logs `Retired <hull>: copy ... delete ... longest ...`.
- One-second jobs take a tick each instead of landing together (`WGTickSpread`): radius heaters and drains. Wound
  overlay refreshes take a share per tick, and fixture damage and the atmos alerts console start on their own offsets.
  Radius heaters also look only for loose things to warm, not anchored or stored ones.
- `IgnitionSourceSystem` walks only lit sources (`WGLit`), and a ship's shield follows it with one transform call.
- More one-second jobs stop landing on one tick: Wolfmed body ticks are spread with `WGTickSpread`, and encounter
  zones, ship status, crew ship status, cargo load and thermal signatures each start on their own part of the second.
- AI ship steering works every other tick and holds its input between (`ShipSteeringSystem`), and `FlammableSystem`
  looks for what's due every third tick.
- NPC ship crews cost less in a fleet fight. A gunner's costlier post checks and target re-checks run four times a second
  (`WGGunnerDutySystem.HoldsPost`), guns are aimed every other tick (`ShipTargetingSystem.WGAimPeriod`; a shot attempt
  lasts 0.2 s, so firing doesn't stop between), and steering runs its area searches every third turn and skips the
  sector maths when nothing is near.
- `WGDeletedRefStateSystem` hand-writes the state of components whose references can outlive what they point at
  (projectiles, deeds, guest access, station grids). A generated state logged an error with a stack trace per deleted
  reference, per send, on the PVS threads. A deleted reference goes out as invalid, and set entries are left out.
- `WGGridDeleteWatchSystem` logs any grid that takes at least 50 ms to delete, by name and entity count.
- The marked upstream edits skip idle-shuttle thruster work, avoid `Enum.GetValues` and nullable boxing on per-tick
  paths, stop `FlammableSystem` from crashing when igniting queues new fire events, and stop networking a thrown
  item's shooter (it outlived the shooter and sent a deleted entity every state).

### Physics and atmos tick cuts (2026-11 profile)

From a profile of live traces: physics cost about 82 ms/s of main-thread time and atmos about 52. Each change has a
cvar or gauge so it can be checked on the test server.

- Idle movers park (`MoverController.WGParking`). A mover that is asleep, with no input, player or relay, loses
  `ActiveInputMoverComponent` and gets `WGParkedMoverComponent`, so MoverController stops visiting about 1,150 of them
  each step to skip them. Its body waking, a move input, NPC steering, or a player attaching puts it back before the
  next mover walk. Every 10 ticks, parked movers are rechecked in case code set their input directly. Relay sources
  never park. `wg.movers.park false` returns them all. `wg_physics_idle_mover_skipped` now counts parked plus skipped.
- A ship's shield is not moved when its ship has moved under 1 cm and turned under 0.0001 rad since the last move.
  Moving the shield reinserts every perimeter fixture, which was nearly all of the follower's cost. The gap never grows
  past that slack.
- Space wind skips its entity search on a high-pressure tile whose wind can't throw anything:
  `sqrt(difference) × 2.25` below `MovedByPressureComponent.ThrowForce`. Nothing below that pushes, and no prototype
  lowers `MoveResist` under it. `wg.atmos.wind_skip_weak false` searches every tile.
- Atmos keeps its grid atmosphere list (`AtmosphereSystem.WGGridCache`) instead of walking every grid each tick. A grid
  atmosphere's startup or removal marks it stale, and it is rebuilt every 5 s regardless.
- `WGAtmosGaugeSystem` reports atmos load every 5 s as `wg_atmos_load{measure, attended}`. The measures are grids,
  tiles, active tiles, high-pressure tiles, excited groups, hotspots, devices, pipe nets and delta-pressure entities. A
  grid counts as attended with a non-ghost player aboard or within 64 m. Wind searches and skips go to
  `wg_atmos_wind_searches_total` and `wg_atmos_wind_skips_total`. The attended split sizes a slower atmos rate for
  grids nobody is near (approved, not built yet).
- AI ship steering was already throttled before this profile: a new input every other tick and an obstacle search every
  third turn. An AI ship therefore reacts up to 2 ticks late to steering and up to 6 ticks late to a new obstacle. This
  round adds no further delay.
- Delta-pressure damage runs serially because `atmos.delta_pressure_parallel_batch_size` is 500: 1,000 entities a pass
  is 2 batches, which ParallelManager runs inline. Whether 125 (parallel) is faster needs a live A/B. It is a cvar, so
  no code change is needed.

### Hiccup instruments (2026-11 WUFF batch)

In 200 hiccup reports from 20-21 Nov, most tick drops were spent in the engine's EntityNet stage, which handles
client messages, and 23 were whole-process freezes with no readings at all. These two systems name the cause next time.

- `WGNetDispatchWatchSystem` times every client message by type and exports `wg_net_dispatch_seconds{type}`. A BUI
  message is named by the message inside it (`BUI ShuttleConsoleFTLRequestMessage`), an input by its key function
  (`Input Use`). A message whose handlers take `wg.net.slow_message_ms` (25) or more is logged with its sender and
  target, and `wg.slow_tick_ms` lines name the tick's slowest message. It hooks the engine's
  `ReceivedSystemMessage`, which fires after each message's two raises, so a message is timed from the end of the one
  before it (`WGDispatchClock`). The first message of a tick is charged what the EntityNet stage took less the rest.
  `wg.net.dispatch_watch false` unhooks it.
- `WGGcWatchSystem` logs any gap between ticks of `wg.gc.stall_warn_seconds` (2) or more with how much of it was GC
  pause, and GC pauses of `wg.gc.pause_warn_ms` (500) or more in a second. It exports `wg_gc_heap_bytes{part}`
  (gen0, gen1, gen2, loh, poh, heap, fragmented, committed) every 5 s and `wg_gc_pause_seconds_total`, so heap growth
  can be told apart from native memory.

### Client messages that did too much (same batch)

- Laser-pointer guns (`WGAimThrottle`) sent their aim every tick for every held pointer, wielded or not, and each one
  dirtied the one component that carries every pointer's line to every player. The client now sends a wielded pointer's
  aim when its target changes, it turns 2° or the holder moves 10 cm, at most 10 times a second, and every 0.25 s
  while it holds still (the server falls back to the holder's facing after 0.5 s). The server takes at most 20 a
  second per pointer, and a line that moved under 5 cm isn't sent again.
- The ghost orbit list is built once per `wg.ghost.orbit_share_seconds` (2) for ordinary ghosts and once for admins,
  and each asker gets it without themselves (`GhostOrbitSystem.SharedTargets`). Each build walks every mob in the
  world, and every open menu asks every 5 s. Tests run it at 0.
- A fire-control console's state is rebuilt at most 4 times a second for fire messages, which the cursor sends 10
  times a second; one held back follows when the interval is up (`FireControlSystem.WGFireUi`). Its refresh message,
  which walks every weapon in the world, runs once a second per console.
- Radar hitscan lines go to a radar when any part of the line passes within range (`RadarBlipSystem.WGLineInReach`).
  The upstream test dropped lines that ended in range and sent every line far from it. Missile arcs go only with
  missiles whose blips the radar is sent.
- A shipyard console shows a hull appraisal up to 15 s old (`ShipyardSystem.WGShownAppraisal`) instead of pricing every
  entity aboard on open and on each ID change; the sale itself still appraises afresh.
- `WGGridFileReader` reads and parses a grid file on the thread pool and loads the parsed data on the main thread, as
  planet sites already did. A staged encounter hull now loads this way, waiting a few ticks for the parse instead of
  doing it in one. Contract wrecks still load in one go; round-setup ships are parsed when their leader picks them (see
  the RoundSetup README).
- Buying a ship takes two steps (`wg.shipyard.async_purchase`, on; tests run it off). A shipyard console's purchase
  starts its hull's parse, says the ship is being prepared and returns; once the parse is done
  (`ShipyardSystem.WGFinishPurchases`), the same purchase runs again from the top, so the card, access, funds and voucher
  are checked on the state then, and the hull loads from the parsed data (`WGLoadHull`). A second order at the console
  meanwhile is refused. The used ship lot does the same (`TraderUsedShipsSystem.WGDeferBuy`), unpacking and parsing a
  listing's saved YAML on a worker (`UsedShipListing.ParseAsync`). Nothing is charged until the hull has loaded.

### Single-tick spikes (same batch)

- Wolfmed infection ran every infected wound, part and septic body on the server on one tick every 5 s, which reached
  the top p99 bucket (17 ms or more) in 16 of the 200 reports. It now files the work by body and runs each body on its
  own tick of the window, corpses and loose limbs every 30 s; see the Wolfmed README. Condition emotes check each body
  on its own tick of their 8 s interval.
- Round setup sends a broadcast's viewer snapshots at most 12 a tick instead of all at once (`WGRoundSetupSystem`).
- Bullet-sized explosions (`WGProcessMicroExplosions`) count against the same per-tick explosion budget as full ones;
  they ran before its stopwatch started, so a tick could spend both budgets.
- Pickup magnets reuse one lookup set, and a magnet over a pile of 64 or more things it can take none of looks again in
  5 s instead of 1 (`MagnetPickupSystem`). One that took something keeps its one-second scan.
- Hull fragments shed in a break-up are judged at most 4 ms a tick (`WGFragmentCleanupSystem`); the rest wait.
- The station job list goes to every player at most once a second (`StationJobsSystem`).
- A ship asked about for the first time is read from one survey per tick (`WGCrewShipStatusSystem.SurveyThisTick`),
  not from four walks of every weapon, thruster and crewman each, so a fleet arriving costs one survey. Watched ships
  were already read from the one-second survey.
- Encounter judging indexes the living crew once (`WGEncounterSystem.IndexCrew`) and reads it for every ship's
  `HasLivingCrew` and `HasLivingEngineer`, which judging asks several times a ship. The index is used only inside
  `Judge` and rebuilt after an encounter resolves; everywhere else those still walk the crew as before.

### Memory that grew all round (same batch)

Process memory climbed about 1.4 GB an hour, 23 GB to 51 GB over one 20 h round, with the entity count flat.

- A round keeps at most `wg.admin_logs.cache_max` (250,000) admin logs in memory. Past it the round's cache is dropped
  and its searches go to the database, which has every log (`AdminLogManager.WGCacheCap`); before, every log of the
  round stayed in memory, three rounds deep.
- Built planet navmesh chunks nobody has been near for `wg.pathfinding.evict_idle_seconds` (300) are freed and left to
  build again on demand; see the Planets README. `wg_pathfinding_planet_chunks` counts what is built.
- The photo store keeps about 256 images in memory, letting the oldest go once held two minutes and saved, to be read
  from the database again when asked for (`PhotoStoreSystem.Hold`). It is a soft cap: images younger than that or
  not yet written stay past it.

### Ship purchases and the freeze clock (2026-11-23 batch)

200 hiccup reports from 21 to 23 Nov (180 tick drops, 20 freezes). The restart at 16:37 UTC on 22 Nov brought in the
batch above; 127 reports come from before it and 73 from after.

- The EntityNet spikes were ship purchases. EntityNet grew by 200 ms/s or more in 63 reports before the restart and in
  none after, while ShipyardSystem, where a purchase now runs, was the worst system in 37 of the 73. Parsing the hull on
  a worker didn't make the spikes smaller, and they grow with the world: a median of 286 ms/s under 700k entities and
  620 ms/s over 800k, the worst at 5 ticks a second. So most of the cost is loading the hull and the rest of the
  purchase, not reading the YAML.
- A purchase now loads its hull on one tick and runs on the next (`ShipyardSystem.WGAsyncPurchase`). At most one hull
  loads a tick, never on a tick a purchase runs, and a purchase turned away after the load deletes its hull. Each logs
  `Bought <hull> at <console>: ... load ... purchase ...` with both times, the hull's entity count and the GC pause in
  each, and exports `wg_shipyard_purchase_seconds{step}`, so the next batch shows which half to cut.
  `TryPurchaseShuttle` no longer appraises the hull just to log the price.
- The freezes keep a 10-minute clock that isn't the game's own. Within a run they come 10, 20, 30 or more minutes apart
  to the second, and where they fall in the 10 minutes carries over the restarts on 21 Nov (between 11:40 and 14:00)
  and 22 Nov (16:37): xx:x8:49 to xx:x9:14 on 21 Nov until 23:30, then xx:x0:53, and xx:x4:17 to xx:x4:22 on 22 and 23 Nov. Look
  first at something on a wall-clock schedule outside the game, such as
  the panel, a scheduled task, a backup or the database. `WGGcWatchSystem`'s stall lines say whether the tick loop
  itself stopped and how much of the stop was GC.
- Memory grew about 810 MB an hour after the restart, against 1,500 to 2,000 before it, while entities still climbed
  from 627k to 864k, so the memory caps above hold.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/Performance/AdminLogManager.WGCacheCap.cs`](AdminLogManager.WGCacheCap.cs)
- [`Content.Server/_WG/Performance/AtmosAlertsComputerSystem.WGBuckets.cs`](AtmosAlertsComputerSystem.WGBuckets.cs)
- [`Content.Server/_WG/Performance/AtmosphereSystem.WGBreachLoop.cs`](AtmosphereSystem.WGBreachLoop.cs)
- [`Content.Server/_WG/Performance/AtmosphereSystem.WGGauges.cs`](AtmosphereSystem.WGGauges.cs)
- [`Content.Server/_WG/Performance/AtmosphereSystem.WGGridCache.cs`](AtmosphereSystem.WGGridCache.cs)
- [`Content.Server/_WG/Performance/AtmosphereSystem.WGMonstermosLimit.cs`](AtmosphereSystem.WGMonstermosLimit.cs)
- [`Content.Server/_WG/Performance/AtmosphereSystem.WGWind.cs`](AtmosphereSystem.WGWind.cs)
- [`Content.Server/_WG/Performance/CEZLevelsSystem.WGGroundSample.cs`](CEZLevelsSystem.WGGroundSample.cs)
- [`Content.Server/_WG/Performance/DockingSystem.WGPlacement.cs`](DockingSystem.WGPlacement.cs)
- [`Content.Server/_WG/Performance/EntityRadiusHeaterSystem.WGSpread.cs`](EntityRadiusHeaterSystem.WGSpread.cs)
- [`Content.Server/_WG/Performance/FireControlSystem.WGFireUi.cs`](FireControlSystem.WGFireUi.cs)
- [`Content.Server/_WG/Performance/GCQueuePrototype.WGMinAge.cs`](GCQueuePrototype.WGMinAge.cs)
- [`Content.Server/_WG/Performance/GCQueueSystem.WGMinAge.cs`](GCQueueSystem.WGMinAge.cs)
- [`Content.Server/_WG/Performance/IgnitionSourceSystem.WGLit.cs`](IgnitionSourceSystem.WGLit.cs)
- [`Content.Server/_WG/Performance/LocalityLoaderSystem.WGDespawn.cs`](LocalityLoaderSystem.WGDespawn.cs)
- [`Content.Server/_WG/Performance/MoverController.WGParking.cs`](MoverController.WGParking.cs)
- [`Content.Server/_WG/Performance/NavMapSystem.WGPlanetLayers.cs`](NavMapSystem.WGPlanetLayers.cs)
- [`Content.Server/_WG/Performance/NavMapSystem.WGRemoved.cs`](NavMapSystem.WGRemoved.cs)
- [`Content.Server/_WG/Performance/RadarBlipSystem.WGCache.cs`](RadarBlipSystem.WGCache.cs)
- [`Content.Server/_WG/Performance/ShipSteererComponent.WGStagger.cs`](ShipSteererComponent.WGStagger.cs)
- [`Content.Server/_WG/Performance/ShipyardSystem.WGAppraisal.cs`](ShipyardSystem.WGAppraisal.cs)
- [`Content.Server/_WG/Performance/ShipyardSystem.WGAsyncPurchase.cs`](ShipyardSystem.WGAsyncPurchase.cs)
- [`Content.Server/_WG/Performance/ShuttleConsoleSystem.WGDocks.cs`](ShuttleConsoleSystem.WGDocks.cs)
- [`Content.Server/_WG/Performance/SimpleFloorPlanPopulatorSystem.WGBudget.cs`](SimpleFloorPlanPopulatorSystem.WGBudget.cs)
- [`Content.Server/_WG/Performance/WGAsteroidCullSystem.cs`](WGAsteroidCullSystem.cs)
- [`Content.Server/_WG/Performance/WGAtmosGaugeSystem.cs`](WGAtmosGaugeSystem.cs)
- [`Content.Server/_WG/Performance/WGDispatchClock.cs`](WGDispatchClock.cs)
- [`Content.Server/_WG/Performance/WGDockIndexSystem.cs`](WGDockIndexSystem.cs)
- [`Content.Server/_WG/Performance/WGGcClock.cs`](WGGcClock.cs)
- [`Content.Server/_WG/Performance/WGGcWatchSystem.cs`](WGGcWatchSystem.cs)
- [`Content.Server/_WG/Performance/WGGridDeleteSystem.cs`](WGGridDeleteSystem.cs)
- [`Content.Server/_WG/Performance/WGGridDeleteWatchSystem.cs`](WGGridDeleteWatchSystem.cs)
- [`Content.Server/_WG/Performance/WGGridFileReader.cs`](WGGridFileReader.cs)
- [`Content.Server/_WG/Performance/WGLightSoundSystem.cs`](WGLightSoundSystem.cs)
- [`Content.Server/_WG/Performance/WGNetDispatchWatchSystem.cs`](WGNetDispatchWatchSystem.cs)
- [`Content.Server/_WG/Performance/WGParkedMoverComponent.cs`](WGParkedMoverComponent.cs)
- [`Content.Server/_WG/Performance/WGRequestCap.cs`](WGRequestCap.cs)
- [`Content.Server/_WG/Performance/WGSerializerWarmupSystem.cs`](WGSerializerWarmupSystem.cs)
- [`Content.Server/_WG/Performance/WorldControllerSystem.WGFreeChunks.cs`](WorldControllerSystem.WGFreeChunks.cs)
- [`Content.Server/_WG/Performance/WorldControllerSystem.WGIdleHelms.cs`](WorldControllerSystem.WGIdleHelms.cs)
- [`Content.Server/_WG/Performance/WorldControllerSystem.WGUnloadGrace.cs`](WorldControllerSystem.WGUnloadGrace.cs)

### Shared

- [`Content.Shared/_WG/Performance/CESharedZLevelsSystem.WGRaised.cs`](../../../Content.Shared/_WG/Performance/CESharedZLevelsSystem.WGRaised.cs)
- [`Content.Shared/_WG/Performance/NavMapComponent.WGDelta.cs`](../../../Content.Shared/_WG/Performance/NavMapComponent.WGDelta.cs)
- [`Content.Shared/_WG/Performance/RoofComponent.WGDelta.cs`](../../../Content.Shared/_WG/Performance/RoofComponent.WGDelta.cs)
- [`Content.Shared/_WG/Performance/SharedGravitySystem.WGRefresh.cs`](../../../Content.Shared/_WG/Performance/SharedGravitySystem.WGRefresh.cs)
- [`Content.Shared/_WG/Performance/SharedGunSystem.WGLightSound.cs`](../../../Content.Shared/_WG/Performance/SharedGunSystem.WGLightSound.cs)
- [`Content.Shared/_WG/Performance/SharedMoverController.WGAroundCollider.cs`](../../../Content.Shared/_WG/Performance/SharedMoverController.WGAroundCollider.cs)
- [`Content.Shared/_WG/Performance/SharedMoverController.WGLightSound.cs`](../../../Content.Shared/_WG/Performance/SharedMoverController.WGLightSound.cs)
- [`Content.Shared/_WG/Performance/SharedRoofSystem.WGDelta.cs`](../../../Content.Shared/_WG/Performance/SharedRoofSystem.WGDelta.cs)
- [`Content.Shared/_WG/Performance/WGAimThrottle.cs`](../../../Content.Shared/_WG/Performance/WGAimThrottle.cs)
- [`Content.Shared/_WG/Performance/WGAsteroidFieldComponent.cs`](../../../Content.Shared/_WG/Performance/WGAsteroidFieldComponent.cs)
- [`Content.Shared/_WG/Performance/WGBlipRemovalBatchEvent.cs`](../../../Content.Shared/_WG/Performance/WGBlipRemovalBatchEvent.cs)
- [`Content.Shared/_WG/Performance/WGCollectionExtensions.cs`](../../../Content.Shared/_WG/Performance/WGCollectionExtensions.cs)
- [`Content.Shared/_WG/Performance/WGDeletedRefStateSystem.cs`](../../../Content.Shared/_WG/Performance/WGDeletedRefStateSystem.cs)
- [`Content.Shared/_WG/Performance/WGLightSound.cs`](../../../Content.Shared/_WG/Performance/WGLightSound.cs)
- [`Content.Shared/_WG/Performance/WGPerformanceCVars.cs`](../../../Content.Shared/_WG/Performance/WGPerformanceCVars.cs)
- [`Content.Shared/_WG/Performance/WGRadarCVars.cs`](../../../Content.Shared/_WG/Performance/WGRadarCVars.cs)
- [`Content.Shared/_WG/Performance/WGTickBuckets.cs`](../../../Content.Shared/_WG/Performance/WGTickBuckets.cs)
- [`Content.Shared/_WG/Performance/WGTickSpread.cs`](../../../Content.Shared/_WG/Performance/WGTickSpread.cs)

### Client

- [`Content.Client/_WG/Performance/LaserPointerSystem.WGAim.cs`](../../../Content.Client/_WG/Performance/LaserPointerSystem.WGAim.cs)
- [`Content.Client/_WG/Performance/RadarBlipsSystem.WGRemovalBatch.cs`](../../../Content.Client/_WG/Performance/RadarBlipsSystem.WGRemovalBatch.cs)
- [`Content.Client/_WG/Performance/ShuttleNavControl.WGAsteroidFields.cs`](../../../Content.Client/_WG/Performance/ShuttleNavControl.WGAsteroidFields.cs)
- [`Content.Client/_WG/Performance/WGAsteroidFieldSystem.cs`](../../../Content.Client/_WG/Performance/WGAsteroidFieldSystem.cs)
- [`Content.Client/_WG/Performance/WGLightSoundSystem.cs`](../../../Content.Client/_WG/Performance/WGLightSoundSystem.cs)
- [`Content.Client/_WG/Performance/WGNetBufferSystem.cs`](../../../Content.Client/_WG/Performance/WGNetBufferSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/Performance/GridDeleteTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/GridDeleteTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/NavMapPlanetLayerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/NavMapPlanetLayerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGAdminLogCacheCapTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGAdminLogCacheCapTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGAngularThrustIdleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGAngularThrustIdleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGAroundColliderTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGAroundColliderTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGAsteroidCullTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGAsteroidCullTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGAtmosWorkCutTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGAtmosWorkCutTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGBottomlessRegenTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGBottomlessRegenTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGBreachLoopTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGBreachLoopTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGDeletedRefStateTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGDeletedRefStateTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGFlammableDebrisTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGFlammableDebrisTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGGravityRefreshTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGGravityRefreshTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGGridDeleteWatchTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGGridDeleteWatchTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGGridFileReaderTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGGridFileReaderTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGGroundSampleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGGroundSampleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGIgnitionLitTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGIgnitionLitTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGLightSoundTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGLightSoundTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGMoverParkingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGMoverParkingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGNavMapDeltaTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGNavMapDeltaTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGNavMapTrimTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGNavMapTrimTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGNetDispatchWatchTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGNetDispatchWatchTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGPreferencesRefreshTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGPreferencesRefreshTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGRadarRebuildTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGRadarRebuildTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGRadarRequestTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGRadarRequestTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGRadiusHeaterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGRadiusHeaterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGRoofDeltaTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGRoofDeltaTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGSharedWalkTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGSharedWalkTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGWorldgenChurnTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGWorldgenChurnTest.cs)
- [`Content.IntegrationTests/Tests/_WG/Performance/WGZLevelOffsetTest.cs`](../../../Content.IntegrationTests/Tests/_WG/Performance/WGZLevelOffsetTest.cs)

### Unit tests

- [`Content.Tests/_WG/Performance/WGAimThrottleTest.cs`](../../../Content.Tests/_WG/Performance/WGAimThrottleTest.cs)
- [`Content.Tests/_WG/Performance/WGDispatchClockTest.cs`](../../../Content.Tests/_WG/Performance/WGDispatchClockTest.cs)
- [`Content.Tests/_WG/Performance/WGGcWatchTest.cs`](../../../Content.Tests/_WG/Performance/WGGcWatchTest.cs)
- [`Content.Tests/_WG/Performance/WGRadarLineTest.cs`](../../../Content.Tests/_WG/Performance/WGRadarLineTest.cs)
- [`Content.Tests/_WG/Performance/WGRequestCapTest.cs`](../../../Content.Tests/_WG/Performance/WGRequestCapTest.cs)
- [`Content.Tests/_WG/Performance/WGTickBucketsTest.cs`](../../../Content.Tests/_WG/Performance/WGTickBucketsTest.cs)
- [`Content.Tests/_WG/Performance/WGTickSpreadTest.cs`](../../../Content.Tests/_WG/Performance/WGTickSpreadTest.cs)

### Localization

- [`Resources/Locale/en-US/_WG/Performance/performance.ftl`](../../../Resources/Locale/en-US/_WG/Performance/performance.ftl)

### Docs

- [`Docs/_WG/Performance/README.md`](../../../Docs/_WG/Performance/README.md)

## Non-modular edits

- [`Content.Client/_CE/ZLevels/Core/CEClientZLevelsSystem.cs`](../../../Content.Client/_CE/ZLevels/Core/CEClientZLevelsSystem.cs)
  - a height from the server joins the sprite passes
  - spawned off the ground
  - the raised list and the lookup that replace the all-entity query
  - walk only raised entities; nothing else has a height to strip
  - the raised list and the lookups that replace the all-entity query
  - walk only raised entities; one that has landed leaves the list
  - removed after the walk, which can't change the set it enumerates
- [`Content.Client/_Goobstation/Weapons/LaserPointer/LaserPointerSystem.cs`](../../../Content.Client/_Goobstation/Weapons/LaserPointer/LaserPointerSystem.cs): a wielded pointer's aim is sent when it changes, at most 10 times a second
- [`Content.Client/_Mono/Radar/RadarBlipsSystem.cs`](../../../Content.Client/_Mono/Radar/RadarBlipsSystem.cs)
  - blips arrived at 2 Hz, so a curving missile lurched between straight-line guesses.
  - lookups and colours for the missile lines, which are rebuilt on every draw frame
  - removals arrive one batch a tick
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
- [`Content.IntegrationTests/PoolManager.Cvars.cs`](../../../Content.IntegrationTests/PoolManager.Cvars.cs): purchase tests check the ship the moment they buy it
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
- [`Content.Server/_Mono/FireControl/FireControlSystem.Console.cs`](../../_Mono/FireControl/FireControlSystem.Console.cs)
  - drop the console's fire-message throttle
  - a console's refresh walks every weapon in the world; once a second
  - the cursor sends this 10 times a second; the state is rebuilt at most 4
- [`Content.Server/_Mono/FireControl/FireControlSystem.cs`](../../_Mono/FireControl/FireControlSystem.cs)
  - only the weapon's own grid can block, so the ray ends where it does
  - no .ToList(), the result is only tested for emptiness
  - was raycastResults.Count == 0
- [`Content.Server/_Mono/FireControl/FireControlSystem.TargetGuided.cs`](../../_Mono/FireControl/FireControlSystem.TargetGuided.cs): send console states the fire-message throttle held back
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
  - new blips join the cached squares before the next sort
  - removals go only to sessions that asked recently
  - the server caps how often it answers one player; the client's throttle is not enforced
  - send removals only to recent radar users, none during round cleanup
  - missile arcs go only with blips this radar is sent
  - radars share one walk of the blips per tick and skip the squares out of reach
  - source positions are taken once, not twice per hitscan
  - source positions are taken once, and only lines passing within range are sent
- [`Content.Server/_Mono/Ships/Systems/ShuttleRestrictionsSystem.cs`](../../_Mono/Ships/Systems/ShuttleRestrictionsSystem.cs): integer division made this 0 or 1, so the 0.5 threshold needed every console powered
- [`Content.Server/_Mono/Temperature/Systems/EntityRadiusHeaterSystem.cs`](../../_Mono/Temperature/Systems/EntityRadiusHeaterSystem.cs)
  - each heater takes its own tick of the second, not all on one
  - what a fire warms is loose; skipping anchored and stored things skips the trees round a planet's fires
  - each heater takes its own tick
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs)
  - the hull's file is parsed off the main thread first; the purchase runs again once it is
  - the console shows an appraisal up to 15 s old; the sale itself appraises afresh
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.cs)
  - only logged, and it priced every entity aboard on the purchase tick
  - logged without the appraisal; the console's admin log records the price paid
  - a purchase that waited for its hull takes the one loaded for it the tick before
- [`Content.Server/_NF/StationEvents/Events/BluespaceErrorRule.cs`](../../_NF/StationEvents/Events/BluespaceErrorRule.cs)
  - frontier#4800 extended, appraise at spawn only for the currency injection
  - frontier#4800, skip the end appraisal when nothing uses the value
- [`Content.Server/_Onyx/Body/Systems/OrganHealthSystem.cs`](../../_Onyx/Body/Systems/OrganHealthSystem.cs)
  - the failed-organ scan runs four times a second, not every tick
  - an organ that just failed is handled next tick, not at the next timed scan
- [`Content.Server/_Onyx/Wounds/WoundBleedingSystem.cs`](../../_Onyx/Wounds/WoundBleedingSystem.cs): clotting deadlines are checked four times a second, not every tick
- [`Content.Server/Administration/Logs/AdminLogManager.Cache.cs`](../../Administration/Logs/AdminLogManager.Cache.cs)
  - forget dropped rounds that left the cache queue
  - a round past the cache cap is searched in the database
  - drop the round's cache past wg.admin_logs.cache_max
  - a round whose cache was dropped at the cap is read from the database
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
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.cs`](../../Atmos/EntitySystems/AtmosphereSystem.cs): an unpaused grid rejoins the cached grid atmosphere list
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.CVars.cs`](../../Atmos/EntitySystems/AtmosphereSystem.CVars.cs): skip wind searches too weak to push
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.DeltaPressure.cs`](../../Atmos/EntitySystems/AtmosphereSystem.DeltaPressure.cs)
  - fixed-size tile buffer for ProcessDeltaPressureEntity
  - skip the shared RNG when the chance is certain (also thread-safe)
  - stack buffer instead of a per-entity array allocation
  - span so callers can use a stack buffer
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs`](../../Atmos/EntitySystems/AtmosphereSystem.GridAtmosphere.cs): rebuild the cached grid atmosphere list
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.HighPressureDelta.cs`](../../Atmos/EntitySystems/AtmosphereSystem.HighPressureDelta.cs)
  - wind below the throw force moves nothing, so skip the search
  - a crowded tile left the set huge and slowed every clear
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Hotspot.cs): a crowded tile left the set huge and slowed every clear
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Monstermos.cs)
  - without space nothing past the equalize limit is ever used, so stop the search there
  - search limit reached
  - a room refilling into a hole vents from the same tile forever; let it leak quietly
  - used range for the cleanup below
  - clear only the range this run used
- [`Content.Server/Atmos/EntitySystems/AtmosphereSystem.Processing.cs`](../../Atmos/EntitySystems/AtmosphereSystem.Processing.cs): reuse the grid atmosphere list instead of walking every grid each tick
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
- [`Content.Server/Explosion/EntitySystems/ExplosionSystem.Processing.cs`](../../Explosion/EntitySystems/ExplosionSystem.Processing.cs)
  - the bullet-sized blasts count against the same per-tick budget
  - restarted above, before the bullet-sized blasts
  - a heavy volley of small blasts still leaves full explosions half the budget
  - Debug, was Info; it logged every step of every explosion
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
  - reused by HandleShuttleMovement
  - idle movers leave the update list until woken
  - woken movers rejoin before the walk
  - reset idle-skip counter
  - a sleeping, input-less, player-less mover has nothing to move
  - parks idle movers and sets the skipped gauge
  - two lists were allocated per shuttle per tick
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
- [`Content.Server/Shuttles/Components/ShuttleComponent.cs`](../../Shuttles/Components/ShuttleComponent.cs): what SetAngularThrust last set every angular thruster to; null once one is added
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
  - the arrival hasn't been set yet
  - fixed list instead of Enum.GetValues on every call
  - idle shuttles call this every tick with everything already off
  - idle shuttles call this every tick with every thruster already set
- [`Content.Server/Sound/EmitSoundSystem.cs`](../../Sound/EmitSoundSystem.cs): finding what's due walks every spammer; NextSound keeps the rate
- [`Content.Server/Spreader/SpreaderSystem.cs`](../../Spreader/SpreaderSystem.cs)
  - see Update
  - SpreaderGrid is created lazily by the first spreader on the grid
  - create the grid's queues on first use
  - empty queues for a grid that just got its first spreader; no scan, so spreaders still waiting for MapInit are not queued twice
  - queue spreaders deferred from the previous pass
  - adding a SpreaderGrid mid-query is unsafe, so defer spreaders moving to a grid without one
- [`Content.Server/Station/Systems/StationJobsSystem.cs`](../../Station/Systems/StationJobsSystem.cs)
  - seconds until the job list may go out again
  - the job list goes to every player at most once a second
- [`Content.Server/Weapons/Ranged/Systems/GunSystem.AutoFire.cs`](../../Weapons/Ranged/Systems/GunSystem.AutoFire.cs)
  - guns mid-burst, filled from OnBurstActivated
  - only visit bursting guns and auto-shoot guns, not every GunComponent
- [`Content.Server/Weapons/Ranged/Systems/GunSystem.cs`](../../Weapons/Ranged/Systems/GunSystem.cs): hits make no sound entity
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
  - unloaded chunks are deleted after a while
  - drop a deleted chunk's unload grace timer
  - no longer a candidate for freeing
  - freed once it has been unloaded long enough
  - grids that have a player or pilot
  - parked, empty helms don't load worldgen
  - wait out the unload grace, so passing ships don't unload and respawn debris
  - delete chunks unloaded long ago
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
- [`Content.Shared/_Goobstation/Weapons/SmartGun/SharedLaserPointerSystem.cs`](../../../Content.Shared/_Goobstation/Weapons/SmartGun/SharedLaserPointerSystem.cs)
  - one pointer's aim is taken at most 20 times a second, however often a client sends it
  - a line that hasn't moved isn't sent to every client again
- [`Content.Shared/_Mono/CloakHeat/Systems/CloakHeatSystem.cs`](../../../Content.Shared/_Mono/CloakHeat/Systems/CloakHeatSystem.cs)
  - only dirty when the networked state changes
  - skip the dirty when nothing networked changed
- [`Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs`](../../../Content.Shared/_Mono/Fixtures/FixtureDamageDealerSystem.cs): off the tick other one-second systems start on
- [`Content.Shared/_Mono/ForceParent/ForceParentSystem.cs`](../../../Content.Shared/_Mono/ForceParent/ForceParentSystem.cs): the second call was outside the if and ran on every move
- [`Content.Shared/_Mono/Shipyard/ShipGuestAccessComponent.cs`](../../../Content.Shared/_Mono/Shipyard/ShipGuestAccessComponent.cs)
  - was AutoGenerateComponentState; WGDeletedRefStateSystem sends the sets, whose entries can be deleted first
  - networked by WGDeletedRefStateSystem
- [`Content.Shared/_Mono/VendingMachine/VendingMachinePurchaseComponent.cs`](../../../Content.Shared/_Mono/VendingMachine/VendingMachinePurchaseComponent.cs): not networked, the client never reads it and the grid can be deleted first
- [`Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs`](../../../Content.Shared/_NF/Shipyard/Components/ShuttleDeedComponent.cs): WGDeletedRefStateSystem sends the state, since the ship can be deleted first
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
- [`Content.Shared/Light/Components/RoofComponent.cs`](../../../Content.Shared/Light/Components/RoofComponent.cs): networked by SharedRoofSystem.WGDelta, which sends only changed chunks
- [`Content.Shared/Light/EntitySystems/SharedRoofSystem.cs`](../../../Content.Shared/Light/EntitySystems/SharedRoofSystem.cs): delta state sends only changed chunks
- [`Content.Shared/Movement/Systems/SharedMoverController.cs`](../../../Content.Shared/Movement/Systems/SharedMoverController.cs)
  - reuse a still mob's result for a few ticks
  - footsteps make no sound entity
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
  - was AutoGenerateComponentState; WGDeletedRefStateSystem sends the state, since Shooter and Weapon can be deleted first
  - networked by WGDeletedRefStateSystem
- [`Content.Shared/Station/Components/StationDataComponent.cs`](../../../Content.Shared/Station/Components/StationDataComponent.cs)
  - was AutoGenerateComponentState; WGDeletedRefStateSystem sends Grids, whose entries can be deleted first
  - networked by WGDeletedRefStateSystem
- [`Content.Shared/StationRecords/SharedStationRecordsSystem.cs`](../../../Content.Shared/StationRecords/SharedStationRecordsSystem.cs): a deleted station goes out as invalid, without logging an error on a PVS thread
- [`Content.Shared/Storage/EntitySystems/MagnetPickupSystem.cs`](../../../Content.Shared/Storage/EntitySystems/MagnetPickupSystem.cs)
  - a magnet over a pile it can take nothing from looked at the whole pile every second
  - finding what's due walks every magnet; NextScan keeps the rate
  - one set reused for every magnet, and a big pile it can take none of waits 5 s
  - a magnet that took something from a big pile keeps its one-second scan
- [`Content.Shared/Storage/EntitySystems/MaterialStorageMagnetPickupSystem.cs`](../../../Content.Shared/Storage/EntitySystems/MaterialStorageMagnetPickupSystem.cs): finding what's due walks every magnet; NextScan keeps the rate
- [`Content.Shared/Stunnable/SharedStunSystem.cs`](../../../Content.Shared/Stunnable/SharedStunSystem.cs): only viewers of the body need it
- [`Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs`](../../../Content.Shared/Weapons/Melee/SharedMeleeWeaponSystem.cs): a target can be deleted before the swing arrives
- [`Content.Shared/Weapons/Ranged/Components/TargetedProjectileComponent.cs`](../../../Content.Shared/Weapons/Ranged/Components/TargetedProjectileComponent.cs)
  - was AutoGenerateComponentState; WGDeletedRefStateSystem sends Target, which can be deleted first
  - networked by WGDeletedRefStateSystem
- [`Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.Ballistic.cs`](../../../Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.Ballistic.cs)
  - drop stale uids
  - a cartridge that left the container by other means must not stay in Entities
- [`Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs`](../../../Content.Shared/Weapons/Ranged/Systems/SharedGunSystem.cs)
  - lets the server track bursting guns
  - wizden#36641, gunUid was not nullable
  - wizden#36641, a projectile may have no shooter
  - server hook so auto-fire only walks bursting guns
  - casings make no sound entity
- [`Resources/Prototypes/_CE/Entities/Structures/water.yml`](../../../Resources/Prototypes/_CE/Entities/Structures/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/_Mono/Entities/Objects/Specific/Planet/flora.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Specific/Planet/flora.yml): decorative flora has no physics body, so passing mobs build no contacts with it
- [`Resources/Prototypes/_NF/Entities/Tiles/water.yml`](../../../Resources/Prototypes/_NF/Entities/Tiles/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/Entities/Tiles/water.yml`](../../../Resources/Prototypes/Entities/Tiles/water.yml): bottomless pools never need refilling, and each tile cost a check every tick
- [`Resources/Prototypes/GC/world.yml`](../../../Resources/Prototypes/GC/world.yml): debris overflow deleted in one tick; larger depth and a minimum age spread it

<!-- WOLFGATE-GENERATED END -->
