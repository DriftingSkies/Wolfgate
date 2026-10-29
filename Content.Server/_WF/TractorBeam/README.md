# TractorBeam

Lets a ship hold, reel in or lock another ship or loose debris with a powered dish and console instead of docking.
Crews put a `WFTractorBeamEmitter` (or the small dish) on the arrestor and a `WFComputerTractorBeam` on the same grid,
then pick a radar contact to hold, reel to a set range or lock in place. A captured ship's helm shows who is holding
it. The Roswell vessel ships with a dish.

Entry points: `TractorBeamSystem` and its `.Audio`, `.AutoCatch`, `.Collection` and `.Visuals` partials (physics, power and debris
collection), `TractorBeamPhysics` (force and power maths), `TractorBeamStationKeepingSystem` and
`MoverController.TractorPrediction` (the arrestor's braking), `TractorBeamConsoleComponent` with its client window,
and `TractorCaptureBanner`. The notes below are the player-facing behaviour and tuning.

## Tractor beams

Place `WFTractorBeamEmitter` on an arrestor's high-voltage cable and
`WFComputerTractorBeam` on the same grid with APC power. The console discovers
anchored dishes automatically. Select a dish and radar contact. **Hold target**
captures the current distance. Once the beam is active, enter a desired separation
in metres (center to center) and press **Set range** to reel inward at the configured
rate, then hold at that range. The display shows the current and ordered separation
and the hull-safe minimum. Range orders cannot acquire new targets, push outward,
overlap hulls, or renew a failing lock's power allowance. Once the ships have stopped,
**Lock in place** captures their separation and relative angle and actively holds
the target against translation and rotation. Resistance consumes beam power even
when the target remains still. Hold/Set range leave this mode; **Release** stops the whole field.
Each dish holds one target; several dishes or ships can hold the same target.
Reopening the console restores the selected dish's captured target. Disabled pin
and range controls explain the required action in hover tooltips. Pinning requires
both ships below 0.2 m/s and 0.05 rad/s; its power check tolerates small allocation
fluctuations, while restraint still uses only the power actually supplied.
The console board is `WFTractorBeamComputerCircuitboard`.

**Auto-catch** (a checkbox per dish) makes an idle dish catch the nearest movable grid, ship or debris, whose
center enters the inner zone of its cone, inside the warning band. It is held like **Hold target**, then locked in
place as soon as both ships are slow enough to pin. Visibility, shield, docking and power rules are the same as for a
manual capture, and the restart cooldown after a release still applies. The console that switched it on controls the
catch.
Unanchor, rotate, and reanchor the dish to change its installed facing.

The dish faces along its local +Y axis (north in its default placement) and operates
inside a 90-degree forward sector, out to 240 m (96 m for the small dish). Turn the arrestor or mount its dish
to bring a contact into that sector before capturing it. Captures release when the
target's center leaves either the angular boundary or the maximum range, including
when the arrestor turns away. The control display shows the selected dish's sector
and warns during the outer 15% of its angle or range, before the lock is lost.

Hold resists moving closer, farther away, sideways, and rotating, using damped
springs about the distance, source-relative bearing, and relative angle captured at lock. A range
order shortens that held distance while retaining the other restraints.
Turning the arrestor carries the target around its center of mass on the held arm,
including the target's tangential velocity and relative hull orientation. Sideways
resistance applies a lever-arm torque back to the arrestor, so either ship can turn
the coupled pair; inertia, powered thrusters, beam strength, and electricity still
limit authority. Turning too fast can overload the beam or lose cone coverage.
Equal and opposite impulses and reaction torques act on the two ships. Inward, outward,
sideways, and rotational resistance all share the same force and power limit.
Distance adds electrical strain even without resistance: field strain is
`0.6 × (distance / maxRange)²`, measured from the dish to the target's center.
It uses 10% strain / 298 kW at 100 m and 60% / 1.24 MW at 240 m for a stationary
target. Mechanical strain fills the remaining headroom up to the 2 MW limit.
The field-maintenance cost is paid before mechanical restraint: insufficient
power weakens the beam, with no force available below that cost. A powered dish
may retain an energized capture while waiting for that supply; the existing
base-power loss and range-release rules still apply. Distance alone causes no
physical force or hull creaks. Reeling closer reduces the distance load.
Lock in place instead uses a firm powered position and rotation
restraint, with equal reaction on the arrestor. Insufficient beam power or force
allows the target to slip; insufficient arrestor thrust lets the arrestor be dragged.

While a powered beam is active, the source automatically brakes through its real
directional thrusters and gyroscope, even with nobody at the helm. Pilot movement
overrides automatic linear braking; pilot rotation overrides automatic angular
braking. Available thrust and power still limit its authority. A light or poorly
powered source can therefore be dragged by its target; cooperating ships contribute
their own forces. There is no mass-threshold freeze or free space anchor.

While any mode is active, the cone also collects free-floating objects
and other movable grids. Its width at the target matches the target hull's projected
width across the beam, including its rotation, rather than widening with distance.
Its side boundaries match the drawing and end at the selected target. The visible
effect fades out ahead of a rounded envelope around the target hull, keeping it
clear of the ship; this visual clearance does not prevent nearby debris collection.
Objects are selected by their center, grids by intersection with their
world bounds; both must also have their center inside the dish's operating sector.
Cargo aboard ships, held/contained items, anchored machinery, static
grids, and the source's own docked group are excluded. A dish shares one force and
power budget across its primary target and everything it collects, with reaction
forces on its source ship. Secondary objects and grids continuously accelerate
toward the dish, with no collection speed cap or arrival brake. The dish's offset
also applies rotational recoil to the arrestor. Debris can strike and damage
equipment through normal collision and impact behavior; this is an intentional risk
of collecting the wrong objects. The selected vessel uses the separate range control.
Collected trade crates land in the collecting ship's hold and count toward its cargo weight (CargoLoad). A beam-held
grid's cargo is not added to the holding ship's thrust penalty: towing a loaded barge is a tug role for now.

Default per-dish tuning (prototype fields):

- `maxRange`: 240 m, measured from the dish to the target's center of mass.
- `rangeStrainAtMaxRange`: 0.6, stationary electrical strain at the range limit.
- `coneHalfAngle`: 0.7853982 radians (45 degrees either side of the dish's facing).
- `maxForce`: 12,000 force units (60 standard thrusters).
- `frequency`: 0.7 Hz; `dampingRatio`: 1.
- `idlePower`: 5 kW; `holdingPower`: 100 kW; `maxPower`: 2 MW.
- `reelSpeed`: 2 m/s; `collectionAcceleration`: 2 m/s², sustained while in the field.
- `looseCollectionMultiplier`: 50. Free-floating objects and mobs request 100 m/s²,
  while secondary grids retain 2 m/s². All still share the dish's force/power budget.
- `collectionStandOff`: 1 m of clearance when reeling the primary vessel; its minimum range also accounts for both hull sizes.

Strain is the square root of demanded force divided by maximum force, making even
moderate resistance significant. Power rises with that strain: 25% of the force
limit means 50% strain and a 1.05 MW demand. After paying the holding overhead,
half the requested additional power supplies only a quarter of the requested force.
The supply may take one
second to establish a new lock, during which an underpowered beam applies no
force. After that, supply below holding power drops the lock. Strain measures the
current resistance rather than hull damage. Two continuous seconds at 100% displayed
strain break the capture, clear its effect, and return the dish to idle power, even
with a full power supply. Dropping below 100% resets this overload timer; the duration
is configurable with `overloadDuration`. Any release (manual, overload, power loss,
or invalid target) starts a 12-second dish cooldown (`restartCooldown`). The console
shows its countdown and capture is rejected server-side until it expires. Repeated
release commands on an idle dish do not extend the cooldown. Rotational restraint in every mode
also shares the dish's force and power budget with translation and collected debris.

Server validation rejects foreign emitters, self-targets, hidden/undetected
contacts, static grids, targets outside the operating sector and the source's own docked group.
Locks release on power loss, console loss, unanchoring, range or firing-arc loss, grid splitting,
target deletion, source-grid reassignment or FTL. Tractor beams do not inhibit FTL.
Anchored or static grids, station-anchor-disabled shuttles, and ships docked to such
grids cannot be selected or collected by the beam. Existing locks release if either
ship becomes fixed. Loose anchored objects remain excluded from debris collection.
Active shields prevent acquiring a new primary lock. Raising shields after capture
does not release the tether or prevent its existing pin/range controls; reacquiring
after release requires lowering the shields again.

The dish uses the supplied 128×128 artwork at 1.5× scale (a six-tile canvas), centered
on the emitter with its open face pointing forward (+Y), and a matching enlarged
collision footprint with four times its original fixture mass, and draws a
faint animated fan with a soft rounded hull cutout. Its color progresses from cyan
through amber to red as strain rises, with irregular red flickering above 85% strain.
The captured ship's helm shows the names of the ships holding it, across console
views, and clears the warning when the last active beam releases.

The user-supplied engage, loop, and disengage sounds play across both participating hulls.
The small dish (`WFTractorBeamEmitterSmall`) uses the same controls, operating cone,
restraint and collection physics. Its centered three-tile sprite has a 2.8 × 1 tile
collision footprint at density 300. It reaches 96 m with 7,200 N maximum force,
10 kW idle draw, 30 kW holding draw, and 150 kW maximum draw. It connects to MV cable
under its center tile; the full-size dish still uses HV. Distance still adds
strain; at maximum range an unresisted capture draws 102 kW. Its visual fan is half
the normal target width and its engage, loop and disengage clips are 6 dB quieter.
The loop starts alongside engagement at half playback gain, without waiting for the intro.
The three supplied hull creaks play intermittently while the beam meets resistance,
including attempts to translate or rotate a held ship; an idle hold does not creak.
Screen distortion,
obstacle occlusion and research unlocks are not implemented. Ship class is controlled by map/loadout
placement; the component is not hard-coded to a vessel class.

Validation:

```text
dotnet test Content.Tests/Content.Tests.csproj --filter FullyQualifiedName~TractorBeam
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj --filter FullyQualifiedName~TractorBeam
```

Integration tests use real grids and machine prototypes. They inject the power
network's received-power result to isolate the tractor system from unrelated
station machinery. Manual multiplayer testing should cover generator load changes,
piloted thrust/braking, crowded battles and the appearance of long beams.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/TractorBeam/MoverController.TractorPrediction.cs`](MoverController.TractorPrediction.cs)
- [`Content.Server/_WF/TractorBeam/Systems/ShuttleConsoleSystem.TractorCapture.cs`](Systems/ShuttleConsoleSystem.TractorCapture.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamConsoleComponent.cs`](TractorBeamConsoleComponent.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamStationKeepingSystem.cs`](TractorBeamStationKeepingSystem.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamSystem.Audio.cs`](TractorBeamSystem.Audio.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamSystem.AutoCatch.cs`](TractorBeamSystem.AutoCatch.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamSystem.Collection.cs`](TractorBeamSystem.Collection.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamSystem.cs`](TractorBeamSystem.cs)
- [`Content.Server/_WF/TractorBeam/TractorBeamSystem.Visuals.cs`](TractorBeamSystem.Visuals.cs)

### Shared

- [`Content.Shared/_WF/TractorBeam/TractorBeamConsoleBoundUserInterfaceState.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamConsoleBoundUserInterfaceState.cs)
- [`Content.Shared/_WF/TractorBeam/TractorBeamEmitterComponent.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamEmitterComponent.cs)
- [`Content.Shared/_WF/TractorBeam/TractorBeamGeometry.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamGeometry.cs)
- [`Content.Shared/_WF/TractorBeam/TractorBeamOperatingCone.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamOperatingCone.cs)
- [`Content.Shared/_WF/TractorBeam/TractorBeamPhysics.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamPhysics.cs)
- [`Content.Shared/_WF/TractorBeam/TractorBeamVisualComponent.cs`](../../../Content.Shared/_WF/TractorBeam/TractorBeamVisualComponent.cs)

### Client

- [`Content.Client/_WF/TractorBeam/TractorBeamConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/TractorBeam/TractorBeamConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/TractorBeam/TractorBeamConsoleWindow.cs`](../../../Content.Client/_WF/TractorBeam/TractorBeamConsoleWindow.cs)
- [`Content.Client/_WF/TractorBeam/TractorBeamOverlay.cs`](../../../Content.Client/_WF/TractorBeam/TractorBeamOverlay.cs)
- [`Content.Client/_WF/TractorBeam/TractorBeamRadarControl.cs`](../../../Content.Client/_WF/TractorBeam/TractorBeamRadarControl.cs)
- [`Content.Client/_WF/TractorBeam/TractorBeamVisualizerSystem.cs`](../../../Content.Client/_WF/TractorBeam/TractorBeamVisualizerSystem.cs)
- [`Content.Client/_WF/TractorBeam/UI/ShuttleConsoleWindow.TractorBeam.cs`](../../../Content.Client/_WF/TractorBeam/UI/ShuttleConsoleWindow.TractorBeam.cs)
- [`Content.Client/_WF/TractorBeam/UI/TractorCaptureBanner.cs`](../../../Content.Client/_WF/TractorBeam/UI/TractorCaptureBanner.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamArcTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamArcTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamAudioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamAudioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamAutoCatchTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamAutoCatchTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamCollectionAccelerationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamCollectionAccelerationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamCollectionTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamCollectionTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamConsoleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamConsoleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamConsoleWindowTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamConsoleWindowTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamDistanceLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamDistanceLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamHoldRestraintTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamHoldRestraintTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamOverloadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamOverloadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamRangeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamRangeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamSmallDishTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamSmallDishTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamStationKeepingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamStationKeepingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorBeamTest.cs)
- [`Content.IntegrationTests/Tests/_WF/TractorBeam/TractorCaptureWarningTest.cs`](../../../Content.IntegrationTests/Tests/_WF/TractorBeam/TractorCaptureWarningTest.cs)

### Unit tests

- [`Content.Tests/_WF/TractorBeam/TractorBeamDistancePowerTest.cs`](../../../Content.Tests/_WF/TractorBeam/TractorBeamDistancePowerTest.cs)
- [`Content.Tests/_WF/TractorBeam/TractorBeamGeometryTest.cs`](../../../Content.Tests/_WF/TractorBeam/TractorBeamGeometryTest.cs)
- [`Content.Tests/_WF/TractorBeam/TractorBeamOperatingConeTest.cs`](../../../Content.Tests/_WF/TractorBeam/TractorBeamOperatingConeTest.cs)
- [`Content.Tests/_WF/TractorBeam/TractorBeamPhysicsTest.cs`](../../../Content.Tests/_WF/TractorBeam/TractorBeamPhysicsTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/TractorBeam/Entities/Structures/Machines/tractor_beam.yml`](../../../Resources/Prototypes/_WF/TractorBeam/Entities/Structures/Machines/tractor_beam.yml)
- [`Resources/Prototypes/_WF/TractorBeam/Shipyard/roswell.yml`](../../../Resources/Prototypes/_WF/TractorBeam/Shipyard/roswell.yml)

### Localization

- [`Resources/Locale/en-US/_WF/TractorBeam/tractor-beam.ftl`](../../../Resources/Locale/en-US/_WF/TractorBeam/tractor-beam.ftl)
- [`Resources/Locale/en-US/_WF/TractorBeam/tractor-capture.ftl`](../../../Resources/Locale/en-US/_WF/TractorBeam/tractor-capture.ftl)

### Textures

- [`Resources/Textures/_WF/TractorBeam/Structures/Machines/tractor_dish.rsi/`](../../../Resources/Textures/_WF/TractorBeam/Structures/Machines/tractor_dish.rsi/)

### Audio

- [`Resources/Audio/_WF/TractorBeam/creak1.ogg`](../../../Resources/Audio/_WF/TractorBeam/creak1.ogg)
- [`Resources/Audio/_WF/TractorBeam/creak2.ogg`](../../../Resources/Audio/_WF/TractorBeam/creak2.ogg)
- [`Resources/Audio/_WF/TractorBeam/creak3.ogg`](../../../Resources/Audio/_WF/TractorBeam/creak3.ogg)
- [`Resources/Audio/_WF/TractorBeam/meta.yml`](../../../Resources/Audio/_WF/TractorBeam/meta.yml)
- [`Resources/Audio/_WF/TractorBeam/tractorbeam_disengage.ogg`](../../../Resources/Audio/_WF/TractorBeam/tractorbeam_disengage.ogg)
- [`Resources/Audio/_WF/TractorBeam/tractorbeam_engage.ogg`](../../../Resources/Audio/_WF/TractorBeam/tractorbeam_engage.ogg)
- [`Resources/Audio/_WF/TractorBeam/tractorbeam_loop.ogg`](../../../Resources/Audio/_WF/TractorBeam/tractorbeam_loop.ogg)

### Shared maps

- [`Resources/SharedMaps/_WF/TractorBeam/Shuttles/roswell.yml`](../../../Resources/SharedMaps/_WF/TractorBeam/Shuttles/roswell.yml)

## Non-modular edits

- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml): xmlns:tractor for the capture banner
- [`Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleConsoleWindow.xaml.cs)
- [`Content.Server/Physics/Controllers/MoverController.cs`](../../Physics/Controllers/MoverController.cs): Track linear/angular braking and show the correct counterthrust directions.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs)
- [`Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs`](../../../Content.Shared/Shuttles/BUIStates/ShuttleBoundUserInterfaceState.cs): expose active tractor sources to the captured ship's helm

<!-- WOLFGATE-GENERATED END -->
