# NPC crew

Module name `NpcCrew`. Folders `Content.{Shared,Server}/_WG/NpcCrew/`, `Resources/Prototypes/_WG/NpcCrew/`,
`Resources/Locale/en-US/_WG/NpcCrew/`, tests in `Content.IntegrationTests/Tests/_WG/NpcCrew/`. Namespaces
`Content.*._WG.NpcCrew`, prototype IDs `WGCrew*`, blackboard keys `WGCrew*`, Fluent IDs `wg-crew-*`.

Human-style NPCs that do a job and drop it to fight, then go back to the job. Place First Officer Jeff on any ship and
he walks to the helm, takes it and flies his orders; place Radio Officer Jack and he narrates the ship on Shortband
and Broadband. Crew carry real loadouts: the gun stays holstered until there is a fight, and a dead crewman's kit is
what you loot. Nothing here knows about encounters, freighters or factions beyond what the mob's own
`NpcFactionMember` says. RES (`Docs/_WG/Encounters/design.md`) consumes this module; it does not define it.

## The idea in one paragraph

Every crew NPC runs one HTN root with three branches in priority order: **fight**, **do your job**, **idle**. The HTN
planner already replans every half second and swaps to an earlier branch the moment it becomes valid, shutting the
current task down with `BetterPlan`. So "Jeff pilots until someone shoots him, fights, then goes back to piloting" is
not a state machine we write; it is the branch order plus one rule: every job task tidies up when shut down (Jeff
releases the helm) and every fight starts by drawing and ends by holstering. Jobs are separate compounds selected by a
blackboard key, each with its own system, so a new job is a new compound and a new system, nothing else changes.

## What exists

- **Combat**: upstream `RangedCombatCompound` / `GunCombatCompound` (shoot while `GunAmmoPrecondition` holds, needs the
  gun in the active hand), `MeleeCombatCompound`, `NPCRetaliation` (aggro on whoever damages you, remembered for
  `attackMemoryLength`), `NearbyHostiles` (faction hostility or an aggro entry, within `VisionRadius` of 10 tiles, with
  a line-of-sight consideration). Both combat compounds fail with no target, which is what makes them a clean first
  branch. Reloading and drawing from inventory are `TODO` comments in `Resources/Prototypes/NPCs/Combat/gun.yml`; the
  upstream reason is that searching nested storage is painful. Frontier sidestepped it by building an infinite gun
  into the mob (`MobHumanoidHostileAISimpleRanged`), which is why their mercenaries never holster.
- **Death**: `NPCSystem` puts an NPC to sleep when it goes critical or dies; nothing wakes a corpse.
- **Piloting**: `ShuttleConsoleSystem.AddPilot(console, mob, comp)` attaches a mob with `PilotComponent` to a helm.
  Upstream already enforces most of "physically controls the console": a pilot cannot move, is dropped every tick it
  can't interact with the console (crit, dead, stunned, cuffed), and is cleared when the console is destroyed. Power
  loss does not clear pilots here (`OnConsolePowerChange` only refreshes the UI), so `WGPilotDutySystem` checks it
  itself. `ShipSteeringSystem.Stop` only removes the component; the mob drops out of the ship's input sources the next
  time it answers no input. Steering comes from Mono `ShipSteeringSystem`: `Steer(mob, coords)` puts
  `ShipSteererComponent` on the mob and it answers `GetShuttleInputsEvent` with thrust toward the target, with collision
  and projectile avoidance, `GoToRange` or `Orbit` modes, and `InRangeRotation` for arriving at a given heading. The two
  coexist on one mob: the pilot handler writes no input while its buttons are zero, so the steerer's input stands.
- **Docking**: `DockingSystem.GetDockingConfig(shuttle, targetGrid)` already works out which pair of docks fits and the
  exact shuttle pose that puts them together (the FTL-to-dock path uses it). `CanDock` wants the two docks within 1.2 m
  and 15° and both free; `Dock` is public. There is no auto-dock.
- **Inventory**: sidearms carry a `Clothing` component for the belt and suit-storage slots, longarms for the back and
  suit storage; `InventorySystem.TryEquip`/`TryUnequip` and `HandsSystem.TryPickup` move items; guns take ammo by
  `InteractUsing` (loose rounds into a ballistic provider, a magazine into a magazine gun).
- **HTN plumbing**: `KeyEqualsPrecondition` (Mono), `MoveToOperator`, `SpeakOperator`, `WaitOperator`, `IdleCompound`,
  `NPCSystem.SetBlackboard`, `HTNSystem.Replan`, `NpcFactionSystem.AggroEntity`.
- **Radio**: `RadioSystem.SendRadioMessage(source, text, channel, radioSource)` from any entity. `Traffic` is
  "Shortband" (1500 m, needs a telecom server in reach or `TelecomExemptComponent` on the radio source); `Common` is
  "Broadband" (whole map through the outpost's telecom server). Faction channels `Nfsd` (TSF) and `Ussp` exist.
- **Ship events**: `DockEvent`/`UndockEvent`, `FTLStartedEvent`, `FTLCompletedEvent`, `ShipWeaponProjectile` on hull
  hits, `MobStateChangedEvent` on crew.
- **Gaps**: no shared awareness between NPCs; pathfinding treats every door with an access reader as pry-only and
  never asks whether the NPC is allowed through; an NPC cannot open a console UI, so it cannot take a helm the way a
  player does; NPC HTNs sleep when no player is within 32 tiles.

## The compartments

Each is a system plus a component plus (where it acts) an HTN compound. Each works alone; the arrows below are the
only couplings, and they are events.

```
 Crew core ──selects──> Pilot duty ──raises WGHelm*/WGOrders* events──> Radio duty
     │                      ▲                                            ▲
     ├──alert events───> Crew alert (optional)      Ship events (dock, FTL, hull hits, crew down)
     └──fight branch──> Weapons (draw, reload, holster)
 Access & doors: a marked pathfinding edit, used by everything that walks.
```

### 1. Crew core

`WGCrewComponent` on the mob: `Duty` (string, mirrored to blackboard `WGCrewDuty`), `Post` (an entity to work at or
return to, usually a map marker), `Group` (string; everyone on a ship with the same group is one crew), `Role` (for
radio lines and events: Captain, Pilot, RadioOperator, Deckhand...), `Engagement`:

- `OnSight`: fights anyone its faction is hostile to or that the crew alert hands it. Deckhands and marines.
- `WhenAttacked`: fights only whoever damages it. The fight branch is gated by `WGCrewMayFightPrecondition`, which
  for these crew is true only while `NPCRetaliation` still remembers an attack, and the crew alert skips them. The
  faction stays whatever the ship's is, so friendly turrets stay friendly; a neutral faction would have made every
  default-hostile faction shoot the pilot. `attackMemoryLength` is 60 s rather than Frontier's 10 so a fight is
  finished, not flickered. Pilots and radio operators.

`WGCrewSystem` writes the blackboard keys on map init and when they change, raises `WGCrewDutyChangedEvent`, and on
`MobStateChangedEvent` raises `WGCrewMemberDownEvent(group, role, dead)` for anyone who cares (the radio operator, the
alert system, RES).

HTN root `WGCrewCompound`:

1. Fight: `WGDrawWeaponOperator`, then `RangedCombatCompound`, then `MeleeCombatCompound` (unchanged upstream).
2. One branch per duty, each gated by `KeyEqualsPrecondition WGCrewDuty == <Duty>`, whose first task is
   `WGHolsterWeaponOperator` (a no-op when nothing is drawn), then that duty's compound.
3. `IdleCompound`, with `IdleRange` small so idle crew drift around their post rather than the ship.

Rules every duty compound follows: its operators' `Shutdown` undoes whatever the operator holds (helm, a UI, an
anchor), it must be restartable from any state (Jeff might be anywhere when combat ends), and it never sets the
`Target` key. The planner does the switching.

**What a deckhand is.** The baseline crewman: duty `Guard`, which is "walk to your post and idle near it". He exists
to be the body count on a ship, to prove the core (branch switching, draw and holster, walking through doors) with the
simplest possible job, and to be the thing boarders fight. Variants are only loadout: sidearm, melee, rifle (the
`Marine`).

**What happens when a crewman dies.** The engine side: his HTN goes to sleep, so he stops planning; if he held the helm
`WGTakeHelmOperator`'s shutdown lets go of it and stops the steering, so the ship drifts; the alert system stops
counting him. The body stays where it fell with its full loadout, holstered or in hand, and that kit is the loot (Mono's
mob cleanup only removes mobs far from any grid and player, so bodies aboard are never tidied away).
`WGCrewMemberDownEvent` goes out with his role: the radio operator turns a captain's or pilot's death into a line,
nobody else's; RES counts crew losses toward "derelict". Critical is the same as dead for everything above except the
event flag; a downed crewman is out of the fight and, without someone treating him, dies within minutes.

### 2. Weapons and loadouts

Crew spawn with a real `Loadout` (`StartingGear`): uniform, armour if any, a holstered sidearm in the belt or suit
storage slot, a longarm on the back for the roles that have one, and two or three magazines or a box of rounds in the
pockets or belt. The convention that keeps this tractable, and that upstream's `TODO` comments were asking for:
weapons and ammo live in equipment slots and pockets, never inside a bag. `WGCrewWeaponComponent` on the mob records
`Drawn` (the entity in hand) and `HolsterSlot` (where it came from).

- `WGDrawWeaponOperator` (first task of the fight branch): pick the best weapon across equipment slots (longarm,
  then sidearm, then anything with `MeleeWeapon`), `TryPickupAnyHand` straight out of the slot (unequip first if the
  containers refuse), then `TrySelect` that hand so the combat compounds shoot with it, and record the slot. Finishes
  even when nothing is found, which lets the melee compound fight bare-handed. Trap: `TrySelectEmptyHand` asks
  `IsHolding` about a null entity and is always false here; never build on it.
- `WGReloadOperator`: when `GunAmmoPrecondition` fails and the gun has an ammo provider, find a compatible magazine or
  round stack in pockets or belt, swap to a free hand, `InteractUsing` it on the gun (that is exactly what a player
  does), and go back to shooting. No ammo left: holster the empty gun and draw the next weapon, down to melee. This
  is the one genuinely new piece of combat behaviour and the one most likely to need polish.
- `WGHolsterWeaponOperator` (first task of every duty branch, and the fight branch's shutdown): `TryEquip` the drawn
  weapon back into `HolsterSlot`; if the slot is gone, keep holding it. Idempotent.

Trade-off accepted: Frontier's built-in infinite gun never runs dry and needs none of this. Real loadouts mean a crew
that fights too long ends up in melee, and that a boarding party is paid in rifles and magazines. Brackets and
rosters should be written knowing that the loadout is the loot table.

As built: `WGReloadOperator` follows drawing in the fight branch. It reloads detachable magazines through the
ordinary slot and interaction APIs, cycles the bolt, and selects the gun again. Compatible spare magazines are
taken from equipment slots; an empty gun without usable ammunition is holstered in favor of a loaded backup,
melee weapon or bare hands. Loose rounds and bag management remain outside the current crew loadouts.

### 3. Pilot duty (First Officer Jeff)

`WGPilotDutyComponent`: `Console` (optional assigned helm, else the nearest powered `ShuttleConsoleComponent` on the
mob's grid), `Orders` and their parameters:

| Order | Behaviour | Steering |
|---|---|---|
| `Hold` | Take the helm, stop the ship, keep station. | `GoToRange` to own position, `Range` 5 |
| `GoTo` | Fly a list of waypoints, then `Hold` at the last; raise `WGPilotOrdersCompletedEvent`. | `GoToRange`, `InRangeMaxSpeed` = cruise speed |
| `Loiter` | Circle a point or a grid at a radius. | `Orbit` |
| `Follow` | Keep range on another grid (an escort). | `GoToRange` with coordinates relative to the target grid; `EntityCoordinates` follow for free |
| `Dock` | Fly to the target grid and dock by hand (below). | three phases, then `DockingSystem.Dock` |
| `Undock` | Undock and back off to a standoff point. | `DockingSystem.Undock`, then `GoToRange` |

`WGCrewPilotCompound`: pick the helm (`WGPickHelmOperator`, writes `WGCrewHelm` and `WGCrewHelmCoords`, and fails at
planning when there is none), `MoveToOperator` to it, then `WGTakeHelmOperator`: `EnsureComp<PilotComponent>`,
`AddPilot`, `ShipSteeringSystem.Steer` to the current target, and stay in the operator while the pilot is attached. No
helm: `IdleCompound`. If the helm attachment is lost (crit, power, destroyed console) the operator fails and the
planner restarts the compound: Jeff gets up, finds a helm, takes it again when he can.

Letting go: `WGTakeHelmOperator`'s shutdown calls `Stop` and `RemovePilot` for every reason but one. A plain `Failed`
is also how `NPCSystem` puts an NPC to sleep when no player is within 32 tiles (`npc.pause_when_no_players_in_range`,
on by default), and releasing then would park every ship nobody is near, so on `Failed` the helm is kept while
`CanHoldHelm` holds: attached to an anchored, powered console on his grid, not crit or dead, not player-controlled.
`WGPilotDutySystem.Update` releases the helm the moment any of that stops holding, and also when the HTN is running a
plan without `WGTakeHelmOperator` in it (an NPC that wakes straight into a fight has no old plan to shut down). A duty
change isn't a better branch, so the planner would keep the helm plan forever; the operator itself lets go when
`WGCrewDuty` is no longer `Pilot`.

Waypoint advancing, orbit centre updates and docking phases live in `WGPilotDutySystem.Update`, not in the HTN
operator, so a ship far from any player keeps flying while Jeff's HTN sleeps. The operator only owns the helm.
`Hold`, `GoTo(waypoints)`, `Loiter(center, radius)` and `Follow(grid, range)` are the public API; each raises
`WGPilotOrdersChangedEvent` and re-steers if the pilot is at the helm. `WGHelmTakenEvent` and `WGHelmReleasedEvent`
fire on attach and release, `WGPilotOrdersCompletedEvent` once when a `GoTo` reaches its last waypoint. `Hold` keeps
station where the ship is when it is given, and counts as arrived below 0.5 m/s rather than at cruise speed, so it
actually stops the ship. A `Follow` whose grid is deleted becomes `Hold`. `Dock(grid)` and `Undock()` follow the same
pattern. Admins give orders with `wg_crew orders <mob> hold | goto <x> <y> [...] | loiter <x> <y> <radius> |
follow <grid|here> | dock <grid|here> | undock`, in map coordinates.

What "can't pilot" means, and who enforces it:

- dead or down: the operator's shutdown releases the helm (upstream also drops a pilot the tick he can't interact);
- fighting: Jeff is `WhenAttacked`, so only being shot gets him off the helm; then the fight branch wins the replan,
  `WGTakeHelmOperator` shuts down and the helm is released. Boarders who leave him alone get a ship that keeps flying,
  which is the correct trade: shoot the pilot and you stop the ship, at the price of a pilot shooting back;
- walking: impossible while attached, movement is blocked;
- no console, no power, console gone: `WGPilotDutySystem` (power) or upstream (destroyed console) drops him, the
  operator fails, Jeff waits at the wreck of his helm under `IdleCompound` and retries every replan.

**Docking by hand.** The `Dock` order runs three phases in `WGPilotDutySystem`, reusing the FTL-dock maths:

1. **Plan**: `GetDockingConfig(ownGrid, targetGrid)` gives a dock pair and the final pose (position and angle) of our
   grid with the docks mated. Reject a config whose approach lane (a box from the final pose out 40 m along the target
   dock's outward normal, our grid's width wide) intersects another grid; try the next pair; none left, abort.
2. **Approach**: steer to a standoff pose 40 m out along that normal with `InRangeRotation` = the final angle, normal
   avoidance on, `InRangeMaxSpeed` 3. Arrive, settle until angular and linear velocity are near zero.
3. **Creep**: steer to the final pose with `AvoidCollisions` off (the target grid is 40 m away and avoidance would
   fight the approach), `InRangeMaxSpeed` 1.5, `Range` 0.3, heading held. Every tick check `CanDock(ownDock,
   targetDock)`; the moment it holds, `Dock`. `CanDock` wants 1.2 m and 15°, which is loose compared to the steerer's
   heading tolerance (about 2°), so position is the only thing that has to be good.

Abort rules: a collision in phase 3 (`FinishOnCollide`), or 60 s in phase 3 without `CanDock`, backs off to the standoff
and retries; three failures mean `Hold` at the standoff and a `WGPilotDockFailedEvent` (Jack says so on Shortband).
Both docks must stay free during the creep; a player docking there first is an abort. The FTL hop (`TryFTLDock`) is kept
only behind `wg.crew.dock_ftl_fallback`, off by default, for ships that must arrive docked no matter what. Expect this
phase to need tuning per hull: thruster placement decides how cleanly a grid can creep sideways, and a hull that can't
hold a heading at 1.5 m/s will not dock by hand at all.

As built (`WGPilotDutySystem.Docking.cs`), with these departures:

- The plan runs the per-pair `GetDockingConfig` over every free pair, nearest standoff first, because the two-grid one
  returns only its single best pair. Upstream's config check also rejects a final pose that overlaps our grid where it
  is now, so a ship ordered to dock from right beside the port fails the plan.
- Approach steers to the standoff in map coordinates, refreshed every update, so the target's hull is avoided on the
  way; only the creep steers on the target grid. Settle lowers the arrival speed and turn rate (0.3 m/s, 0.05 rad/s) so
  the steerer brakes rather than waits. The steerer applies `InRangeRotation` only once in range and otherwise faces
  where it is going, so the creep holds the heading with `AlwaysFaceTarget` and a `TargetRotation` offset recomputed
  every update.
- `DockMaxAttempts` counts hand-flown attempts and is checked before planning and on reaching the standoff, so 0 goes
  straight to the fallback. A plan that finds no pair counts as a failed attempt and is retried after 5 s; a chosen dock
  that is taken or gone sends the next attempt back to planning.
- In the creep, hitting another grid, or the target's hull more than 2.5 m from the port, aborts; touching the hull at
  the port is the docks mating.
- The fallback is `GetDockingConfig` plus `FTLDock`, not `TryFTLDock`: when no pair fits, `TryFTLDock` hops the ship
  next to the target undocked, which would hide the failure.
- `WGPilotDockedEvent` and `WGPilotDockFailedEvent` are raised after the switch to `Hold`, like
  `WGPilotOrdersCompletedEvent`, so a handler can give new orders.
- `Undock` waits for the pilot to be at the helm, releases every docked port, flies `DockStandoff` out along the reverse
  of their mean outward normal (away from what it left) at `DockApproachSpeed`, then holds and raises
  `WGPilotOrdersCompletedEvent`. Nothing docked completes at once.

### 4. Radio duty (Radio Officer Jack)

`WGRadioOperatorComponent` on the mob: a `Channels` map from message class to radio channel (`Local → Traffic`,
`Alert → Common`, optional `Faction → Nfsd`), `Callsign` (defaults to the grid's name, "Freighter XX-121"), a
`Cooldown` per message class, `Post` to stand at (`Hold` branch is just `MoveToOperator` to the post then idle).

`WGRadioOperatorSystem` is event-driven, not HTN-driven, so it works while Jack's HTN sleeps and needs no operator of
its own. It listens on Jack's grid and speaks through `RadioSystem.SendRadioMessage(jack, text, channel, jack)` with
Jack's name as sender, so killing or downing Jack ends the broadcasts literally (the system checks he is alive and not
critical before every line). Lines are for things that happen, never for the passage of time; there are no periodic
sitreps:

| Trigger | Class | Example |
|---|---|---|
| `DockEvent` with another grid | Local | "Freighter XX-121 docking at Caelestinus Central, port 3." |
| `UndockEvent` | Local | "Freighter XX-121 clear of Caelestinus Central, departing." |
| `FTLStartedEvent` / `FTLCompletedEvent` | Local | "Freighter XX-121 jumping." / "Freighter XX-121 arriving, bearing 040." |
| `WGPilotOrdersCompletedEvent` / `WGPilotDockFailedEvent` | Local | "Freighter XX-121 on station." / "Docking aborted, holding off the port." |
| First hostile hull hit (`ShipWeaponProjectile` from another grid) or crew alert raised | Alert | "Mayday, mayday, Freighter XX-121 under attack at 4120, -1870, hostile vessel Ratbag!" |
| A non-crew hostile mob on the grid | Alert | "We are being boarded!" |
| `WGCrewMemberDownEvent` for the captain | Alert | "Captain is down!" |
| `WGCrewMemberDownEvent` for the pilot | Alert | "Helm is down, we're adrift!" |
| No hostile on or firing at the grid for 2 min after an alert | Alert | "Freighter XX-121, hostiles gone, resuming course." |

One mayday per attack episode (an episode runs until the all-clear), one line per crew role going down. All text is
Fluent with the callsign, station, dock, bearing and hostile name as arguments and several variants per line, so
repeats aren't eaten by the radio system's duplicate filter. Jack carries `TelecomExemptComponent` (a ship radio set)
so Shortband works away from the outpost; Broadband already does. He also says a short local line (`SpeakOperator`
style, via `ChatSystem`) so people on the bridge hear him talk.

Jack has no special HTN beyond standing at his post; he is `WhenAttacked` so he stays on the radio while the fight is
elsewhere. If his ship has no Jeff, he still reports docks, jumps and attacks.

As built (`WGRadioOperatorSystem`; the role adds the component and `TelecomExempt` through `wgCrewRole.components`),
with these departures:

- Channels are three fields, `LocalChannel`, `AlertChannel` and an optional `FactionChannel` that alerts also go to.
  `Sent` keeps the last 20 transmissions with their line, for VV and tests.
- The cooldown is per line, not per class: a class-wide one would eat "Captain is down!" right after the mayday, and
  the docking line right after "arriving".
- "Jumping" is said while the drive spools (`FTLComponent` in `Starting`, polled once a second): `FTLStartedEvent`
  comes once the ship is in FTL space, where Shortband reaches nobody.
- Hostile acts are a crewman hurt by someone outside the crew, caught on `BeforeDamageChangedEvent` because Wolfmed
  routes a body's damage through its parts and the body's `DamageChangedEvent` then has no origin, and a hostile mob
  aboard (`GetNearbyHostiles` over the grid, once a second). A partial `SpaceArtillerySystem` now reports damaging
  ship-weapon hits on anchored hull entities through its existing subscription and one marked call. External
  vessel threats and shared alerts also reach the radio; repeated activity extends the current episode.
- Crew with an empty group count as one crew per grid, so marker-placed crew of different ships don't share a radio.

### 5. Crew alert (optional, shared awareness)

`WGCrewAlertSystem`: once a second, for each `Group` on a grid, look at members' blackboards for a `Target`. When one
appears, raise `WGCrewAlertEvent(group, hostiles)` and for every `OnSight` member: `AggroEntity(member, hostile)`,
raise `AggroVisionRadius` to cover the ship, `Replan`. The alert decays `AlertDecay` (60 s) after the last member lost
its target, restoring vision and raising `WGCrewAlertClearedEvent`. `WhenAttacked` members are left alone. Without
this system the crew still fights, one NPC at a time, as it sees things; with it the deckhands converge on a boarder,
which is what a ship's crew should do.

As built (`WGCrewAlertSystem`): alerts are scoped to `(grid, group)`, including empty groups. Only living,
non-player, enabled `OnSight` crew with `ShareAlerts` enabled report and receive targets. Reports must be living
mobs on the same grid. The system polls once a second and emits `WGCrewAlertEvent` for newly reported targets,
then one `WGCrewAlertClearedEvent` after 60 seconds without a valid target (or when the group has no eligible crew).
Both events carry the grid and group for captain behavior. External ship threats share the alert lifetime but are
kept separate from infantry targets. `ReportShipThreat` is the encounter-facing entry point.

Both normal and aggro vision extend to at least the ship's bounding-box diagonal: the upstream target search uses
normal vision until a target is acquired. Existing combat selection, line-of-sight and pathfinding still apply;
sharing a target does not let crew see through walls. The system never assigns the combat `Target` key.
Original vision overrides are restored on clearing, leaving the group/grid, changing engagement, disabling
sharing, death or player takeover. Only hostility added by the alert is removed, preserving existing hostility and
unexpired personal retaliation. The radio officer's independent attack-episode reporting is unchanged.

### 6. Access and doors

Crew need two things to walk a real ship: access, and a pathfinder that respects it. Access is `AccessComponent` tags
on the mob (Frontier mobs already have the component); for purchased ships locked by `ShipAccess`, `WGCrewSystem`
registers the NPC in the ship's allow list the way a crew member would be (an integration point, not a bypass).

The pathfinder flags any door with an `AccessReaderComponent` as `Access` and offers NPCs only prying through it.
This needs a small marked edit outside `_WG` (`// WOLFGATE(NpcCrew)`), three hunks:

- `PathfindingSystem.GetFlags`: a blackboard `NavAccess` sets `PathFlags.Access`, which exists but nothing sets today.
- `PathfindingSystem.GetTileCost`: an `Access` door costs the same as a plain door for a request carrying that flag.
- `NPCSteeringSystem.TryHandleFlags`: with the flag, if `AccessReaderSystem.IsAllowed(npc, door)` use
  `InteractionActivate` (the door opens normally) and only fall back to prying when not allowed and `NavPry` is set.

As built: `WGCrewSystem.Apply` enables `NavAccess` and ensures an `AccessComponent` on each crewman; it does not
grant blanket access tags. Crew steering compares both bodies' hard collision masks: the current engine helper
reads the first body's fixtures twice and incorrectly treats doors as free space for humans. A marked hook stops
blended movement input while crew settle at blocked nodes. RobustToolbox is unchanged.
The pathfinding hooks allow reader doors in an access-aware path. A partial
`NPCSteeringSystem` in this module checks the reader and normal door-opening rules before interacting, then leaves
unsuccessful attempts to the existing obstacle handling (`NavPry` is still required for prying). Permission is
checked at the door; the path planner does not yet price individual doors using the requester's credentials.

`WGCrewAccessSystem.RegisterSpawnShip` runs once through `SpawnCrewman`: on a grid already managed by ShipAccess,
it equips a passenger ID card if needed, creates a crew record on the grid when the card has none, and enrolls that
card through `WGShipAccessServerSystem.TryAddCard`. The card is ordinary lootable equipment, and the allow-list
entry is revocable. No credential is reissued by duty changes or movement onto another ship. Crew spawned on
unmanaged grids use whatever normal access tags/cards the mapper or admin gives them. Per-door owner-only,
sealed and custom lists continue to enforce their normal rules.

### 7. Spawning and tools

- `WGCrewSpawnPointComponent` map marker: `Role` (crew prototype), `Post` (itself by default), `Group`. `WGCrewSystem`
  spawns the mob on map init and fills in `WGCrewComponent`. Mappers place crew by placing markers.
- Naming: `RandomMetadata` with the species name datasets and a role title from Fluent, so the sender reads
  "First Officer Jeff Marlow".
- `wg_crew` admin command: `spawn <role> [post]`, `orders <npc> hold|goto x y|loiter x y r|follow <grid>|dock <grid>|
  undock`, `duty <npc> <duty>`, `list [grid]`. An admin verb "Orders..." on a crew NPC opens the same choices.
- Everything on `WGCrewComponent`, `WGPilotDutyComponent`, `WGCrewWeaponComponent` and `WGRadioOperatorComponent` is
  editable in VV.

### 8. Crew setup tool (admin)

The test and setup surface: a **Crew Setup** window in the Wolfgate admin tab, next to Spawn Vessel and ERT Builder and
built the same way (a `DefaultWindow` on the client, one request event to an admin-checked server system, a result
event back). It is the last compartment to build, because it only drives the others, but its backend is not GUI code:
the planner that decides where crew go is `WGCrewPlanner`, a server class the setup tool, the `wg_crew` command and
RES all call.

**Pick a ship**: a grid that exists (dropdown of named grids, with "the grid I'm on" first) or a vessel prototype to
spawn first (the Spawn Vessel list). **Plan**: the server runs `WGCrewPlanner.Plan(grid)` and returns a roster, one
row per post. Posts come from the grid itself, in this order: `WGCrewSpawnPoint` markers if the map has any (mapper
intent wins), else helms (`ShuttleConsoleComponent` → Pilot), gunnery consoles (→ Gunner, once that duty exists), a
radio post (a telecom or intercom entity, else the tile beside the helm → RadioOperator), the inside tile of each
external dock (→ Deckhand, guarding the airlock), and the centres of the largest rooms (→ Deckhands, found by a
flood
fill over the grid's tiles bounded by walls and doors). The plan is a list of `(post coordinates, role, loadout)`.
The admin edits it in a table: role and loadout dropdowns per row, add and remove rows, a deckhand count spinner that
fills remaining room posts, a captain checkbox. **Mission**: crew group name, callsign (defaults to the ship name),
company for the ship and crew (so IFF, access and radio channels line up), engagement default per role, radio channels
for Local and Alert, and the pilot's orders: Hold, GoTo (coordinates, or "where I'm looking" from the admin's position),
Loiter (point and radius), Follow (grid), Dock (grid). **Buttons**: Preview (the server spawns temporary post markers
visible to admins for 30 s and the window lists them with a teleport button each), Spawn, Clear crew (despawn the
group), Orders (re-issue the mission to a crew that already exists). The spawn runs through `WGCrewSystem.SpawnCrew`
exactly as a map marker would, so a ship set up by hand and a ship set up by the tool are indistinguishable.

`wg_crew plan <grid>` prints the same plan as text, which is how the planner is tested before any window exists.

As built: `WGCrewSetupWindow` is available in the Wolfgate admin tab and on a crew NPC's admin verb. The server
checks Spawn permission on every request; teleport additionally requires Admin. It validates the complete roster
before spawning, supports custom starting gear before map initialization, and clears only the chosen grid/group.
Preview is a private client overlay lasting 30 seconds. Destinations are map coordinates; posts are local grid
coordinates. The planner retains its existing spaced-open-tile heuristic instead of a room flood-fill.
`WGCrewSetupSystem.TrySpawn` is shared with future encounter callers. RES has no runtime in this checkout.

## NPCs to start with

In build order; each one proves a compartment.

1. **Deckhand** (`WGCrewDeckhand`, duty `Guard`, `OnSight`). Stands at a post, idles nearby, draws and fights what it
   sees, holsters and goes back. Proves the core compound, the shutdown discipline and draw/holster with the simplest
   job. Variants by loadout: sidearm, melee, rifle (`WGCrewMarine`, with armour).
2. **First Officer** (`WGCrewPilot`, duty `Pilot`, `WhenAttacked`). Jeff. Proves helm attach and release, orders,
   waypoints, the pilot-and-steerer coexistence, "down means drifting", and then docking by hand.
3. **Radio Officer** (`WGCrewRadioOperator`, duty `Hold` at the radio post, `WhenAttacked`). Jack. Proves event-driven
   speech and channel routing; also the first thing players will notice.
4. **Captain** (`WGCrewCaptain`). A radio officer whose system also issues orders: on alert, `Hold` if his
   `HeaveTo` flag is set, otherwise leave the pilot flying; on alert cleared, restore the previous orders. One
   component (`WGCaptainComponent`) with those two reactions; everything else is Jack's code. Gives a ship one entity
   whose death matters, and the "Captain is down!" line something to mean.
5. **Gunner** (`WGCrewGunner`, duty `Gunnery`). Takes a gunnery console the way Jeff takes the helm and fires
   the ship's `FireControllable` guns at the crew's hostiles through the same `FireControlSystem.AttemptFire` path
   Mono's ship AI uses; no gunner at the console, no ship guns. Implemented with explicit vessel threats from hull
   impacts or encounter reports; friendly factions are excluded. The console must stay powered, unoccupied by a
   player, and within interaction range. Only one gunner drives the grid each tick.

As built: the captain saves the remaining route on alert and restores it on all-clear. New orders issued during
the episode take precedence; captain or pilot death, player takeover, or leaving the grid/group cancels restoration.
The captain role carries the same radio component as the radio officer.

Not now: engineers (repair is a deep rabbit hole), medics (upstream medibot logic exists but patient selection is a
project), anyone who needs to manage a bag.

## Risks

- **Reloading.** The one new combat behaviour. If `WGReloadOperator` misbehaves, the fallback is already in the
  design (holster, draw the next weapon, melee), so a broken reload degrades to a short fight, not a frozen NPC.
- **Docking by hand.** Depends on each hull creeping straight at 1.5 m/s. Build it against two or three freighter hulls
  before promising it for any ship; the FTL fallback stays behind a cvar for the ones that can't.
- **Sleep.** NPC HTNs pause with no player within 32 tiles. Steering, docking phases and radio are system-driven and
  keep working; the HTN only owns attach, release, drawing and walking. If attach itself has to happen unobserved (a
  ship spawned far away), give the pilot prototype `SleepPlayerCheckRangeOverride`, as Mono's AI cores do.
- **Combat compounds on a moving ship.** Steering is grid-relative and should be fine, but `MoveToOperator` under
  acceleration hasn't been exercised; the first live test is Jeff's own ship under way with a boarder.
- **Pry-happy crew.** Until the access edit lands, a crew on a stock ship pries its own doors. Ship this with the edit
  or with readerless interior doors; don't ship without either.
- **Duplicate radio lines.** The radio system drops an identical message already in flight. Variants and arguments
  in every template, and the cooldown per message class.

## Tests

`Content.IntegrationTests/Tests/_WG/NpcCrew/`: a test grid with a helm, a post, a dock and a door; a second grid with a
matching dock.

- Core: a deckhand with a hostile spawned in view plans the fight branch with its sidearm in hand; with the hostile
  removed, within two replans it is back on the duty branch with the sidearm back in its slot.
- Weapons: an empty gun with a magazine in a pocket is reloaded; with no ammo anywhere the NPC ends up in melee.
- Pilot: Jeff spawned with `GoTo` acquires `PilotComponent` and `ShipSteererComponent` within a few seconds; the grid
  gains velocity toward the waypoint; killing Jeff removes both and the grid stops being driven; a hostile shooting
  Jeff releases the helm, a hostile standing next to him does not.
- Docking: a `Dock` order against the second grid ends with the two docks docked within 90 s on the test hull.
- Radio: a `DockEvent` on Jack's grid produces one message on `Traffic` with the callsign and station name; a hull hit
  from another grid produces one mayday on `Common` and no second one; the captain's death produces one line; a dead
  Jack produces none.
- Doors (with the access edit): a crew NPC with access paths through a reader door without prying it; one without
  access pries.
- Planner: on a test grid with one helm, two docks and two rooms, `Plan` returns a Pilot at the helm, a RadioOperator
  beside it, a Deckhand inside each dock and the rest in the rooms; a grid with `WGCrewSpawnPoint` markers returns
  exactly the markers.

No `Destructive` pool settings.

## Decisions taken

- Pilots and radio operators fight only when attacked; deckhands and marines fight on sight and take crew alerts.
- Real loadouts, holstered outside a fight, drawn for it, reloaded from pockets; the loadout is the loot.
- Docking is done by hand, in three phases on top of the FTL-dock maths; the FTL hop is a cvar fallback, off.
- The radio reports events only (docking, jumps, mayday, boarded, captain down, helm down, all clear). No sitreps.

## Open questions

1. **How much ammo?** Assumed two or three magazines per crewman. More means longer fights and richer loot; less means
   boarders face melee sooner.
2. **Does being hit by a ship gun count as "attacked" for Jeff?** Assumed no: hull hits are the deckhands' and the
   radio's business; Jeff keeps flying until someone shoots *him*. Yes would stop every freighter at the first volley.
3. **Should the radio operator also stay quiet about non-faction ships docking?** Assumed he reports every dock on
   Shortband; it is local range and that is the flavour.
4. **Should NPC crew count as crew for `ShipAccess`?** Assumed yes via the allow list. The alternative is a blanket
   "NPCs ignore ship locks", which players will dislike the first time a hostile boarder uses it.

## Objective queue implementation

Crew Setup now has a live crew list and editable per-grid/group objective queues. Supported tasks are Hold, GoTo,
Dock, Undock, Loiter, Escort, Attack and Retreat; repeated GoTo tasks provide patrol routes. Timed tasks use seconds
(zero is indefinite), navigation tasks finish on arrival, and failures pause visibly. See the module README for
controls and the handoff for verified behavior. Queues are round-local. Crew remain active without nearby players;
captains orbit external threats and pilots enable projectile avoidance outside docking.
