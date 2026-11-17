# AsteroidMining

Belt asteroids as single rocks, cracked with a ship-mounted mining laser (Star Citizen-style rock cracking with EVE
Online-style beams). Worldgen swaps every ore tile asteroid, in the Wilds, the 9 km ring, Cluster A and Very Far, for
a cluster of single rocks of the same kind holding the same amount of ore. Scrap asteroids (now scrap only, no ore
veins) and wrecks stay as tiles. Half of the gas-bearing plain and rock asteroids (Enormous and up) become barren tile
asteroids instead: plain rock with no ore, but the same gas deposits, anomaly rock and rooms, for gas mining.

- **Worldgen:** `replacement.yml` maps each tile asteroid kind (rock, ice, basalt, sand, chromite, andesite) to a
  cluster marker. `WGAsteroidSystem` swaps it through `WGDebrisPlacingEvent` and gives the cluster the tile
  asteroid's rock count as its mass budget. A tick later the marker scatters rocks around its centre by its size
  weights (small the most common, huge the rarest) until the budget runs out, hands them to its world chunk and
  removes itself. Compositions start from each kind's mineral table and plain-rock share, tilted towards iron and coal
  (which steel and plasteel need) over malachite, quartz and salt; chromite is still where lithium comes from, at
  the same share. A kind's `barren` prefix and `barrenChance` swap the sizes that prefix defines (`barren.yml`)
  for a barren tile asteroid instead of a cluster.
- **Metallic rocks:** each cluster rock may roll `WGAsteroidMetallic` instead of its cluster's kind, at 1% by the stations
  plus 0.35% per kilometre from the sector centre (about 4% on the 9 km ring, 8% Very Far):
  mostly iron, silver and gold, sometimes diamond, with little plain rock, and each vein drops half again as much
  ore (its composition's `oreYield`). Drawn from the `metallic` look's RSIs
  (blue steel with bare plates). It resists heat (45-60%), so it needs an M40 at high power or two M12s. Fragments and
  boulders from it stay metallic.
- **Radar:** asteroid grids carry no label on any scanner. `WGAdminRadarUnlabelled` keeps the admin scanner's true
  IFF from adding one.
- **Rocks:** each asteroid is one rock entity (`WGAsteroidRock*`) anchored at the centre of a small grid of invisible,
  indestructible `WGAsteroidCore` tiles (`WGAsteroid{Small,Medium,Large,Huge}`). The grid gives the rock ship
  collisions, a radar hull, FTL clearance and tractor beam support. Each rock rolls a mass (in ore veins, plain rock
  included), one to three ores from its `wgAsteroidComposition`, a resistance, an instability and a cracking window.
  Sprite variants 0-2 are round; 3-5 are elongated, lobed or bitten, and their `shapes` (from the sprite generator)
  give the tiles' outline, a convex hull fixture and a smaller mass.
- **Cracking:** the `WGWeaponTurretMiningBeam` (M40 Prospector, heavy) and `WGWeaponTurretMiningLaserSmall` (M12
  Prospector, light: 40 m, about half the heat and a quarter of the crack speed, too weak to overcharge a rock alone) are energy ship guns in the M25/Apollo family, aimed
  and fired from a gunnery console. Their hitscan pulses (10 a second) heat the asteroid or boulder they hit instead of
  damaging it, and their forks spin while they fire. They are power-hungry: an M40 draws 5 kW from the grid while
  firing (1 kW idle) and an M12 1.9 kW (0.4 kW idle), so a laser has to fit the ship's power budget. The gunnery console grows a mining laser panel
  (`FireControlWindow.AsteroidMining`, under the shield control) once its ship has a mining laser: it sets the power
  of every mining laser on the ship in 1/10% steps (50% by default) and scans the rock they are on, or were last on
  while it cools (ores, plain rock, resistance, heat and window, crack progress, yield). Heat settles at
  `power × strength × (1 − resistance)` and unstable rocks drift around it. In the green window the crack bar fills at
  the beams' summed crack rate (`1.5 s + 0.035 s` per vein, divided by 2 for an M40 and 0.5 for an M12). The yellow band past it fills the bar half as
  fast again but each second there costs ore, down to half the rock for a crack made all in yellow. The meter only
  shows the heat now, not where a power setting will settle it, so finding the right power takes some feel; the impact
  flare brightens in the window and its sparks turn red in the yellow band, so the trigger can also be feathered by eye. Several lasers on one rock add their power and
  crack rate.
- **Cockpit:** a pilot in the cockpit works the lasers through the helm's gunnery link. The MFD's MIN page
  (`WGCockpitMiningPage`), whose selector shows only while the linked ship has mining lasers, carries the gunnery
  panel's scan as instruments: a power lever (drag or mouse wheel, plus the 1/10% steps), a heat dial with the window,
  hot and overcharge bands, crack progress round its rim and the yield, lamps for window, hot, overcharge and overload,
  and the rock's make-up as a bar. `WGMiningBeamSystem.Cockpit` sends the scan to the helm and takes the power from it,
  for pilots `WGCockpitGunnerySystem` has linked, whether or not they hold the guns. Pointing at a rock on the ship cam
  or the NAV plot locks brackets onto it and opens a callout (`WGAsteroidScanner`): range, size, veins, ores and plain
  rock, resistance, window, heat drift and how hard an overcharge would blow, with a heat ring while it is warm. Mass,
  ores, resistance, instability and window are networked for this; a rock out of view range only gives its signal.
- **Mass:** asteroid grids are `DensityScale` (8) times as dense as a hull of the same footprint, so collision
  warnings, autopilot avoidance and impacts treat them as solid rock.
- **Outcomes:** a clean crack keeps the mass its yield allows. Large and huge rocks split into two smaller asteroids
  (two fifths or half of the mass), which come off as hot as their parent; of the rest, 15% comes out as boulders and the remainder as floating ore piles
  scattered evenly over where the rock was. Each vein drops `oreYield` (1.35) times a vein rock's ore, since cracking
  rocks one by one is slower than chewing through tile asteroids was. Holding the rock past the red line (heat 85%) for 2 s blows it up: 35% of the mass survives, the blast grows
  with plasma, and the lasers can't fire for 10 s. Weapons and explosions shatter a rock for a tenth of its mass once
  it has taken its `shatterDamage` (about 530 per vein), so an Apollo or M25 can break a rock in a pinch, but slowly and for
  far less ore than a mining laser. Past 60% crack progress the rock shakes; when it breaks, `WGAsteroidCrackEffect` draws a
  flash, a shockwave, small tumbling shards and a dust cloud, and fragments fly apart before the grids' damping
  settles them. Anything else on a cracked asteroid's grid, players included, is left floating where it was.
- **Hand mining:** `WGAsteroidBoulder` is a pullable chunk of 2-4 veins. Pull it aboard and break it with a pickaxe,
  drill or kinetic accelerator (it is `Gatherable`), or crack it in space with the mining laser like a small rock, for
  all of its ore. It is opaque to the laser's ray and blocks bullets.
- **Collection:** ore floats where the rock was; ore boxes and tractor beams pick it up as before. The ore magnet
  (`MachineOreMagnet`) runs on `WGOreMagnetSystem` instead of the generic magnet pickup: switched on and powered, it
  locks onto loose ore within 30 m that it can see (walls block it, windows don't), up to six at once. Each locked ore
  gets `WGOreMagnetPulledComponent`, which `WGOreMagnetOverlay` draws as a faint cyan arc from the magnet with sparks
  running inward and an aura round the ore that tightens; after 0.4 s plus 0.03 s per metre it flies into storage.
  Its sprite has `off`, `on` and an unshaded `glow` state, switched by `WGOreMagnetVisuals.Active`.
- **Sound:** each laser loops a hum while it fires (`mining_laser_heavy.ogg`, `mining_laser_light.ogg`, synthesised by
  `Tools/_WG/AsteroidMining/gen_laser_hum.py`): a deep rumbling drone for the M40, a higher, lighter one for the M12.
  It plays on each client, and its pitch climbs with the rock's heat so the window can be found by ear.
- **Getting one:** cargo sells both as flatpacks under Shuttle (M12 2,500, M40 7,500). Their boards are in the cargo
  board pack and cost real materials; the M12 unlocks with Salvage Equipment, next to the M25, and mounts on a light
  energy hardpoint; the M40 unlocks with Mass Excavation and needs a heavy one.

`WGAsteroidSystem` swaps worldgen debris, fills clusters, builds the grids, rolls compositions and runs queued cracks;
`DebrisFeaturePlacerSystem.Asteroids` gives cluster rocks and fragments to their world chunk so they unload with it.
`WGMiningBeamSystem` turns pulses into heat and feeds the gunnery consoles' mining panel and the cockpit's MIN page;
`WGAsteroidHeat` holds the bands and `WGAsteroidScan` how a rock reads on a scan. On the client, `WGAsteroidVisualsSystem` picks sprite variants and looks, tints the veins by the leading ore
and lights the cracks with heat, `WGMiningBeamOverlay` draws the beams, `WGAsteroidCrackOverlay` draws rocks breaking
and `WGMiningBeamVisualizerSystem` spins the fork.

A rock only reaches clients within view range, and an asteroid's tiles are invisible, so a zoomed-out camera would see
rocks pop in and out. Each asteroid grid carries a copy of its rock's look (`WGAsteroidGridVisuals`, kept in step by
`WGAsteroidSystem`), and `WGAsteroidFarOverlay` draws the rock from it whenever the rock itself hasn't been sent.
Worldgen rock grids themselves only reach players who have come within 128 m of them (Performance module,
`WGAsteroidCullSystem`); for the rest, the overlay and radar draw the rock from its chunk's field list.

Balance knobs: which kinds swap and the size budgets in `replacement.yml`, rock masses, shatter damage, fragment and
boulder shares in `asteroids.yml`, ore weights and rock stats in `compositions.yml`, the bands in `WGAsteroidHeat`,
and each laser's strength, crack rate, range, fire rate and battery in `mining_beam.yml`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/AsteroidMining/DebrisFeaturePlacerSystem.Asteroids.cs`](DebrisFeaturePlacerSystem.Asteroids.cs)
- [`Content.Server/_WG/AsteroidMining/WGAsteroidClusterComponent.cs`](WGAsteroidClusterComponent.cs)
- [`Content.Server/_WG/AsteroidMining/WGAsteroidGridComponent.cs`](WGAsteroidGridComponent.cs)
- [`Content.Server/_WG/AsteroidMining/WGAsteroidSystem.cs`](WGAsteroidSystem.cs)
- [`Content.Server/_WG/AsteroidMining/WGMiningBeamSystem.Cockpit.cs`](WGMiningBeamSystem.Cockpit.cs)
- [`Content.Server/_WG/AsteroidMining/WGMiningBeamSystem.cs`](WGMiningBeamSystem.cs)
- [`Content.Server/_WG/AsteroidMining/WGOreMagnetComponent.cs`](WGOreMagnetComponent.cs)
- [`Content.Server/_WG/AsteroidMining/WGOreMagnetSystem.cs`](WGOreMagnetSystem.cs)

### Shared

- [`Content.Shared/_WG/AsteroidMining/WGAsteroidComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidComponent.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidCompositionPrototype.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidCompositionPrototype.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidCrackEffectComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidCrackEffectComponent.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidGridVisualsComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidGridVisualsComponent.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidHeat.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidHeat.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidReplacementPrototype.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidReplacementPrototype.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidScan.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidScan.cs)
- [`Content.Shared/_WG/AsteroidMining/WGAsteroidShape.cs`](../../../Content.Shared/_WG/AsteroidMining/WGAsteroidShape.cs)
- [`Content.Shared/_WG/AsteroidMining/WGMiningBeamEmitterComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGMiningBeamEmitterComponent.cs)
- [`Content.Shared/_WG/AsteroidMining/WGMiningBeamGunneryMessages.cs`](../../../Content.Shared/_WG/AsteroidMining/WGMiningBeamGunneryMessages.cs)
- [`Content.Shared/_WG/AsteroidMining/WGMiningPulseComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGMiningPulseComponent.cs)
- [`Content.Shared/_WG/AsteroidMining/WGOreMagnetPulledComponent.cs`](../../../Content.Shared/_WG/AsteroidMining/WGOreMagnetPulledComponent.cs)

### Client

- [`Content.Client/_WG/AsteroidMining/BaseShuttleControl.GridBatch.cs`](../../../Content.Client/_WG/AsteroidMining/BaseShuttleControl.GridBatch.cs)
- [`Content.Client/_WG/AsteroidMining/FireControlWindow.AsteroidMining.cs`](../../../Content.Client/_WG/AsteroidMining/FireControlWindow.AsteroidMining.cs)
- [`Content.Client/_WG/AsteroidMining/ShuttleConsoleWindow.AsteroidMining.cs`](../../../Content.Client/_WG/AsteroidMining/ShuttleConsoleWindow.AsteroidMining.cs)
- [`Content.Client/_WG/AsteroidMining/WGAsteroidCrackOverlay.cs`](../../../Content.Client/_WG/AsteroidMining/WGAsteroidCrackOverlay.cs)
- [`Content.Client/_WG/AsteroidMining/WGAsteroidFarOverlay.cs`](../../../Content.Client/_WG/AsteroidMining/WGAsteroidFarOverlay.cs)
- [`Content.Client/_WG/AsteroidMining/WGAsteroidScanner.cs`](../../../Content.Client/_WG/AsteroidMining/WGAsteroidScanner.cs)
- [`Content.Client/_WG/AsteroidMining/WGAsteroidScanReader.cs`](../../../Content.Client/_WG/AsteroidMining/WGAsteroidScanReader.cs)
- [`Content.Client/_WG/AsteroidMining/WGAsteroidVisualsSystem.cs`](../../../Content.Client/_WG/AsteroidMining/WGAsteroidVisualsSystem.cs)
- [`Content.Client/_WG/AsteroidMining/WGCockpitMiningPage.cs`](../../../Content.Client/_WG/AsteroidMining/WGCockpitMiningPage.cs)
- [`Content.Client/_WG/AsteroidMining/WGCockpitView.AsteroidMining.cs`](../../../Content.Client/_WG/AsteroidMining/WGCockpitView.AsteroidMining.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningAnnunciator.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningAnnunciator.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningBeamOverlay.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningBeamOverlay.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningBeamVisualizerSystem.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningBeamVisualizerSystem.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningChargeMeter.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningChargeMeter.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningFigures.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningFigures.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningGauges.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningGauges.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningHeatDial.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningHeatDial.cs)
- [`Content.Client/_WG/AsteroidMining/WGMiningPowerLever.cs`](../../../Content.Client/_WG/AsteroidMining/WGMiningPowerLever.cs)
- [`Content.Client/_WG/AsteroidMining/WGOreMagnetOverlay.cs`](../../../Content.Client/_WG/AsteroidMining/WGOreMagnetOverlay.cs)
- [`Content.Client/_WG/AsteroidMining/WGOreMagnetVisualsSystem.cs`](../../../Content.Client/_WG/AsteroidMining/WGOreMagnetVisualsSystem.cs)
- [`Content.Client/_WG/AsteroidMining/WGOverlayDraw.cs`](../../../Content.Client/_WG/AsteroidMining/WGOverlayDraw.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/AsteroidMining/AsteroidMiningTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AsteroidMining/AsteroidMiningTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AsteroidMining/CockpitMiningTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AsteroidMining/CockpitMiningTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AsteroidMining/OreMagnetTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AsteroidMining/OreMagnetTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/AsteroidMining/asteroids.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/asteroids.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/barren.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/barren.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/compositions.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/compositions.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/gc.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/gc.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/mining_beam.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/mining_beam.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/replacement.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/replacement.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/shaders.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/shaders.yml)
- [`Resources/Prototypes/_WG/AsteroidMining/tiles.yml`](../../../Resources/Prototypes/_WG/AsteroidMining/tiles.yml)

### Localization

- [`Resources/Locale/en-US/_WG/AsteroidMining/asteroid-mining.ftl`](../../../Resources/Locale/en-US/_WG/AsteroidMining/asteroid-mining.ftl)

### Textures

- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_huge.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_huge.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_huge_metallic.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_huge_metallic.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_large.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_large.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_large_metallic.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_large_metallic.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_medium.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_medium.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_medium_metallic.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_medium_metallic.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_small.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_small.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_small_metallic.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/asteroid_small_metallic.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/boulder.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/boulder.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Asteroids/boulder_metallic.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Asteroids/boulder_metallic.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Structures/mining_beam.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Structures/mining_beam.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Structures/mining_laser_small.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Structures/mining_laser_small.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Structures/ore_magnet.rsi/`](../../../Resources/Textures/_WG/AsteroidMining/Structures/ore_magnet.rsi/)
- [`Resources/Textures/_WG/AsteroidMining/Tiles/asteroid_core.png`](../../../Resources/Textures/_WG/AsteroidMining/Tiles/asteroid_core.png)

### Audio

- [`Resources/Audio/_WG/AsteroidMining/attributions.yml`](../../../Resources/Audio/_WG/AsteroidMining/attributions.yml)
- [`Resources/Audio/_WG/AsteroidMining/mining_laser_heavy.ogg`](../../../Resources/Audio/_WG/AsteroidMining/mining_laser_heavy.ogg)
- [`Resources/Audio/_WG/AsteroidMining/mining_laser_light.ogg`](../../../Resources/Audio/_WG/AsteroidMining/mining_laser_light.ogg)

## Non-modular edits

- [`Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs`](../../../Content.Client/_Mono/FireControl/UI/FireControlConsoleBoundUserInterface.cs): the mining laser panel's scan
- [`Content.Client/Shuttles/UI/BaseShuttleControl.xaml.cs`](../../../Content.Client/Shuttles/UI/BaseShuttleControl.xaml.cs)
  - queue lets the radar's grid loops batch every grid into one draw call
  - queued grids are transformed and drawn with the rest of their loop
  - small grids aren't worth a thread-pool round trip
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs)
  - batched with the other grids
  - queued outlines go under this label, as before
  - redrew every blip so far once per grid; drawn once after the loop
  - drawn once per grid inside the loop; drawn once after it instead
  - per-grid draws moved out of the loop, so its grids batch into one draw call
  - docks draw over their grid's queued outline
- [`Content.Server/Atmos/EntitySystems/AutomaticAtmosSystem.cs`](../../Atmos/EntitySystems/AutomaticAtmosSystem.cs): asteroid rocks are airless; atmos per rock grid is wasted work
- [`Content.Server/Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs`](../../Worldgen/Systems/Debris/DebrisFeaturePlacerSystem.cs)
  - drifting rocks left a stale key per move
  - a placing handler may swap the debris
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/ore_magnet.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/ore_magnet.yml)
  - pulls only ore it can see, with an arc and aura, and a new sprite
  - the field's cyan, to match the new sprite and pull arc
  - on and off states while switched on and powered
- [`Resources/Prototypes/_Mono/Shipyard/Civilian/caracara.yml`](../../../Resources/Prototypes/_Mono/Shipyard/Civilian/caracara.yml): fitted with the M40 mining laser in place of the Apollo
- [`Resources/Prototypes/_Mono/Shipyard/Civilian/noble.yml`](../../../Resources/Prototypes/_Mono/Shipyard/Civilian/noble.yml): M12 mining lasers in place of the mining pulsers
- [`Resources/Prototypes/_Mono/Shipyard/Civilian/peregrine.yml`](../../../Resources/Prototypes/_Mono/Shipyard/Civilian/peregrine.yml): M12 mining lasers in place of the mining autopulsers
- [`Resources/Prototypes/_Mono/Shipyard/MMC/akasono.yml`](../../../Resources/Prototypes/_Mono/Shipyard/MMC/akasono.yml): an M12 mining laser in place of the pulser
- [`Resources/Prototypes/_Mono/Shipyard/MMC/kami.yml`](../../../Resources/Prototypes/_Mono/Shipyard/MMC/kami.yml): M12 mining lasers in place of the mining pulsers
- [`Resources/Prototypes/_NF/Entities/World/Debris/asteroids.yml`](../../../Resources/Prototypes/_NF/Entities/World/Debris/asteroids.yml): scrap asteroids hold only scrap, ore comes from the rock clusters
- [`Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml`](../../../Resources/Prototypes/Recipes/Lathes/Packs/cargo.yml): heavy and light mining laser boards
- [`Resources/Prototypes/Research/industrial.yml`](../../../Resources/Prototypes/Research/industrial.yml)
  - light mining laser board
  - mining laser board
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/arribane.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/arribane.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/caracara.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/caracara.yml): Its Apollo mining laser is swapped for the M40 Prospector, the heavy asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/noble.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/noble.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/peregrine.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/peregrine.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/Civilian/rosetta.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/Civilian/rosetta.yml): Its Apollo mining laser is swapped for the M40 Prospector, the heavy asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/CivilianExp/pelican.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/CivilianExp/pelican.yml): Its M25 mining pulsar is swapped for the M12 Prospector, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/MMC/akasono.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/MMC/akasono.yml): Its M25 mining pulsar is swapped for the M12 Prospector, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/MMC/kami.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/MMC/kami.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.
- [`Resources/SharedMaps/_Mono/Shuttles/TSF/shiv.yml`](../../../Resources/SharedMaps/_Mono/Shuttles/TSF/shiv.yml): Its M25 mining pulsars are swapped for M12 Prospectors, the light asteroid mining laser.

<!-- WOLFGATE-GENERATED END -->
