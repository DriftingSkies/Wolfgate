# Crew navigation and scenarios

Crew Setup's Rules step and each active crew's Settings tab contain a collapsed **Flight tuning** editor.
Choose a reusable profile or edit its values under Speeds, Handling, Clearances and Docking. The safe default
profile is always available. Save Settings applies a copy to that crew without restarting its current objective
or queued timers. Unfinished or invalid values are rejected by both the window and server.

Circle Grid uses a slow orbit around the destination's bounds center. Attack uses the larger of its requested
task radius and the profile's attack radius. Both also allow for the two hulls and configured clearance.
Escort keeps a target-relative formation slot, enlarged for the hulls; its speed budget includes the leader's
world speed. Orbiting a moving target uses the same extra speed allowance. Physical thruster capabilities and
the engine's own speed limit still apply. Braking retains full authority even when acceleration is reduced.

Hold captures a fixed map position. Inside its drift tolerance it brakes; outside it returns toward the anchor
until it reaches the smaller return tolerance. Taking the helm again or changing settings retains that anchor.
Docking intentionally permits hull contact only in the final docking corridor. Changing the standoff distance
during approach or settling replans the approach; a final creep already underway keeps its chosen dock pair.

## Reusable scenario profile

A scenario can define a `wgCrewNavigation` prototype in its own module and a Fluent name for the profile. Omitted
settings retain the cautious defaults. This example includes every supported setting with its default value:

```yaml
- type: wgCrewNavigation
  id: WGExampleScenarioNavigation
  name: wg-example-scenario-navigation
  settings:
    cruiseSpeed: 10
    followSpeed: 10
    loiterSpeed: 6
    circleSpeed: 4
    attackSpeed: 6
    holdCorrectionSpeed: 2
    nearTargetSpeed: 3
    maximumTurnRate: 0.15
    orbitLookaheadAngle: 10
    thrustMultiplier: 0.45
    angularThrustMultiplier: 0.5
    navigationClearance: 60
    brakingLookahead: 2
    speedTolerance: 0.25
    arrivalSpeed: 0.5
    holdRange: 5
    holdReturnRange: 1
    holdAngularSpeed: 0.02
    escortRange: 8
    escortHeadingRange: 50
    attackRange: 350
    evasionBuffer: 20
    evasionLookahead: 8
    dockApproachSpeed: 3
    dockCreepSpeed: 1.5
    undockSpeed: 3
    dockEvasionBuffer: 3
    dockEvasionLookahead: 4
    dockStandoff: 40
    dockAlignmentRange: 80
    dockStandoffRange: 2
    dockCreepRange: 0.3
    dockSettleSpeed: 0.3
    dockSettleTurnRate: 0.05
    dockApproachTimeout: 120
    dockSettleTimeout: 30
    dockCreepTimeout: 60
```

Distances use meters, speeds meters per second, angular rates radians per second, and durations seconds.
The orbit look-ahead angle uses degrees; smaller angles follow the requested circle more closely.
Thrust multipliers scale available acceleration while piloting. The editor displays accepted bounds; all values
must be finite, and `holdReturnRange` cannot exceed `holdRange`. Arrival and settling speed tolerances must be
positive. Increase docking timeouts when deliberately configuring very slow approaches.

Scenario code copies the profile into the ordinary encounter-spawning API:

```csharp
var navigation = prototypes.Index<WGCrewNavigationProfilePrototype>("WGExampleScenarioNavigation").Settings.Clone();
var mission = new WGCrewMission { Group = "convoy", Navigation = navigation };
setup.TrySpawn(grid, posts, mission, out var members);
objectives.SetQueue(grid, mission.Group, tasks);
```

`WGCrewSetupSystem.TrySpawn` validates the profile and gives each member independent settings. The live settings
request applies the same validation. `WGPilotDutySystem.SetNavigation` is the direct pilot-level update API;
it returns false for invalid limits. Do not mutate the prototype's shared `Settings` instance.

Profiles contain defaults, not references retained by running missions. Editing one crew does not alter its
profile or another crew. Round-local queues and profile copies use the existing crew lifetime; persistent
scenario orchestration is separate from this navigation API.
