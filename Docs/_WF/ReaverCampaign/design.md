# The Ashfall Reaver campaign

A round-long, automated threat that scales with time and with the sector danger vote, that every player can see on
the sector map and fight. Built on `_WF/SectorControl` (territory, activity, drawing) and `_WF/Encounters` (ships,
crews, zones, markers, rewards). Rounds run about six hours; every number below is tuned to that.

## Why

Today the Reavers are a dice roll: the storyteller starts a raider pack now and then, it is fought or it jumps out,
and nothing carries over. Nobody can look at the map and say "the pirates are over there, and they are getting
bolder". This gives them ground: strongholds that hold space, patrols that fly it, raids that leave it, and a threat
level that rises while nobody stops them and falls when crews do.

## Strongholds

A stronghold is an encounter (`start: Manual`, `lifetime: Persistent`, never pinned) holding station at a cell
centre. Three prototypes, picked by tier and population:

| | lead | guards | crew | when |
|---|---|---|---|---|
| `WFReaverStrongholdLight` | Kalisto, Metis | 1 × Kalisto, Metis | 3 hands, 2 guards | tier ≤ 2 or under 12 players |
| `WFReaverStrongholdMid` | Fenrir, Garm | 2 × Kalisto, Metis, Bastion | 4 hands, 3 guards | tier 3 |
| `WFReaverStrongholdHeavy` | Saintie, Vulture, Ganymede | 2 × Fenrir, Garm | 4 hands, 4 guards | tier ≥ 4 and 15+ players |

The lead holds; the guards fly `Circle` on the lead's key. Only the lead carries zones (warn 1500 m, attack 900 m,
`wf-encounter-pirate-zone` lines, `disengage: Deter` at 1200 m); the guards have none and fight whoever attacks. The
encounter has `leash: 2000` so nobody chases a hauler across the sector. The lead's hold carries four crates of
`WFManifestContraband`: loot for whoever boards and wins. `icon: Flag`; `company: WFAshfallReavers`, `faction:
PirateNF`, profile `WFCrewProfilePirate`, `distress: false`.

Founding claims the cell and its ring with the encounter entity as the source. A stronghold is **broken** when its
lead is out of the fight (`WFEncounterSystem.InFight` false): the campaign calls `Resolve(Destroyed)` itself, since
the Encounters judge waits for every ship. On `WFEncounterResolvedEvent` with `Destroyed` the campaign pays the
bounty, releases the source (freeing every cell it alone supported) and announces. `Ended` (admin or round end) frees
and pays nothing.

**First stronghold.** `firstStrongholdDelay` after `StationsGeneratedEvent`, once the round is in progress, in a
quiet cell 14–24 km from the origin with a clear centre (`WFEncounterSchedulerSystem.IsClearSpace` with 600 m grid
clearance and 6 km station clearance, which also keeps off hidden stations and gravity wells), at least 8 km from any
living player. **Later strongholds.** When held cells ≥ `cellsPerStronghold` × strongholds, `foundInterval` has
passed since the last founding and the count is under `maxStrongholds`: in a held frontier cell at least two cells
from every stronghold, nearer the core preferred, with a clear centre. **Regroup.** With no stronghold standing for
`regroupDelay`, found a fresh light one as the first was, and announce the regrouping, so breaking the only stronghold
buys a real lull instead of ending the arc.

## Spread

Every `spreadInterval` the campaign claims one frontier cell: unowned, adjacent to a held cell, within
`supportRange` rings of a stronghold (which becomes its source), quiet for `quietMinutes`, and not vetoed. The pick is
weighted toward the origin, so the front creeps inward. A frontier cell that holds a station is not claimed at once:
it is **contested** with a 10 min deadline and announced as a blockade warning naming the station; the framework
claims it at the deadline only if the cell stayed quiet. A Reaver-held station is cut off from NPC freight
(`blockades: true` on the faction) and is a raid target.

## Patrols

Each stronghold keeps up to `patrols[tier]` packs out, under a campaign cap of `clamp(players / 5, 1, 6)` packs.
`WFReaverPatrol`: lead Kalisto, Metis, Bastion or Garm with a pilot, gunner, captain and two hands; wing Carrion,
Hazel, Horsefly or Tick with a pilot and gunner on `Escort`. Transient, off budget, no leash, warn 900 m, attack
450 m, `disengage: Deter` 1200 m, `icon: Skull`. Spawned at a clear point 600–1000 m from its stronghold, then given
its route with `WFEncounterSystem.SetOrders`: `GoTo` (range 200) three to five held-cell centres as a random walk
through held space, each checked clear (a point that is not is nudged or skipped), then `GoTo` home. Home with the
route flown, it resolves `Completed`, jumps out and is swept; the stronghold sends another after `patrolInterval`.

**Patrol killed.** When a patrol resolves `Destroyed`, the cell its lead died in is freed unless a stronghold stands
in it, the patrol bounty is paid, and the sector watch says so without a chime ("Reaver patrol destroyed at K-7, the
cell is clear, bounty paid"). **Stronghold under attack.** Once a stronghold has taken hits from player ships, the
nearest patrol within one cell is recalled to it (`SetOrders`: `GoTo` the stronghold at 600 m); a recalled patrol drops any intercept it was on.

## Raids

One campaign-wide raid clock from tier 3, interval `raidInterval[tier]` divided by nothing (not per stronghold),
concurrency capped at `clamp(players / 8, 1, 4)`, skipped when nothing qualifies. Targets come from activity data: a
crewed player ship in or beside held space (a marked ship anywhere on the map, below), not docked, not raided in the
last 45 min, weighted by crew count (three times that for a marked ship); every
third raid targets instead the nearest non-protected station to held space (a core station from tier 4), where the
navies' persistent patrols meet it. Three prototypes: `WFReaverRaidPack` (raider + wing, orders `GoTo` the target at
600 m then `Attack` it), `WFReaverRaidBoarders` (`hunt: true`, as the storyteller's boarders) and
`WFReaverRaidStation` (raider + wing, `GoTo` the station standoff, `wander: 4` there). Launched from the stronghold
nearest the target; the target ship gets a warning with bearing and distance over its PA and in chat to those aboard
(`WFSectorNoticeSystem.Tell`), and the common channel hears it too. The
storyteller's own raiders keep spawning everywhere: the campaign answers `WFEncounterWeightEvent` and scales the
weight of `WFEncounterPirateRaiders` and `WFEncounterPirateBoarders` by `threatScale[tier]`, capped at 1.5.

## Intercepts and marks

Staying in Reaver space gets a ship hunted. The campaign tick times every crewed player ship (the activity system's
`CrewedShips`) in a cell the faction holds; leaving held space resets its clock. Once a ship has been inside for
`interceptDwell` and is off its cooldown, a patrol is turned on it, within a campaign-wide cap of simultaneous
intercepts by tier (`intercepts`): the nearest patrol that is out, not already intercepting and not recalled, within
three cells of the ship; failing that, a fresh patrol from the nearest stronghold if the patrol caps allow. Its orders
(`SetOrders`): `GoTo` where the ship is (range 600 m), `Attack` the ship, then the route points it had still to call
at and home, so a patrol whose target is disabled flies on instead of going idle. The ship gets "Reaver patrol turning
to intercept Hauler, bearing 045, 3.2 km." over its PA and in chat to those aboard, and the common channel hears it,
with bearing and range from the ship to the patrol. A ship no patrol could be sent after is tried again in 30 s.

An intercept ends when its target is disabled (`WFCrewShipStatusSystem.IsDisabled`, the test the `Attack` task ends
on), deleted or off the sector map, has been out of held space for 60 s, or has been more than three cells from the
patrol for 60 s. The patrol gets the rest of its route and home again, and the target goes on `interceptCooldown`. A
patrol recalled to its stronghold, or one that resolves, drops its intercept the same way. The state is
`InsideSince` and `InterceptCooldowns` on `WFReaverCampaignComponent`, and `Intercepting`, `InterceptSince`, `Resume`
and the two give-up clocks on `WFReaverPatrolComponent`.

**Marks.** Every player ship that lands a hit on a stronghold, or hit a patrol that is destroyed (the encounter's
`ShipHits`), gets `WFReaverMarkComponent` for 30 min, refreshed by each new hit or kill. Raids weigh a marked ship
three times as heavy and take it as a target anywhere on the sector map, not only in or beside held space; its warning
reads "Reaver raiders are hunting Hauler, bearing 045, 3.2 km." Marks run out in the tick.

## Threat

Score = strongholds × 10 + held cells + 20 × (1 − distance of the nearest held cell to the origin / 27 000). Tier
thresholds are the campaign's (`tierThresholds`, five numbers). Tier names: None, Scattered, Organised, Entrenched,
Dominant, Overrun. A rise is announced at once; a fall only after the score has stayed under the threshold for 10
min, and never within 10 min of the last tier announcement. The announcement comes from the sector watch
(`wf-reaver-watch`), with `/Audio/Misc/redalert.ogg` and a red colour rising, `/Audio/Misc/notice1.ogg` and a teal
colour falling, and names the tier, the strongholds and cells held and the current stronghold bounty. Founding,
breaking and regrouping are announced the same way with the cell callsign and the bearing and range from the nearest
station. The legend line sent to the framework reads "Ashfall Reavers: Entrenched, 3 strongholds, 21 cells, bounty
180,000".

## Rewards

`WFEncounterRewardSystem.PayBounty(encounter, reward, deed, supporters)` pays everyone who hit the encounter's ships
(`MinimumHits` across all sides, alive, not of the losers' company) a share of the spesos, split first per ship then
per head, under the hourly cap, with each navy's own faction credits (the campaign's `credits` list: one credit entity,
companies and stronghold and patrol amounts per navy; the first grant rides on the main reward and the rest as
`extraCredits`, rewards of no spesos); and pays a
20 % support pool to the supporters: crewed player ships that spent 90 s within 2500 m of the stronghold while it was
under attack, meaning within two minutes of the last hit on it, without hitting it (medics, tenders, scouts). The
campaign tracks supporters on `WFReaverStrongholdComponent`. `PayBounty` returns how many players were paid; the
announcement says the bounty was paid only when it is above zero. Stronghold bounty: `strongholdBounty[tier]` (Standard 60k, 90k, 120k, 160k, 200k,
250k); patrol bounty `patrolBounty` (20k). Paid on `Destroyed` only.

## The vote

The lobby's sector danger vote picks the campaign: each `wfReaverCampaign` names the `wfEncounterPreset` it serves.
The campaign runs only when `wf.encounters.enabled`, `wf.sector.enabled` and `wf.reavers.enabled` are all on; a
preset with no campaign runs none (logged once).

| | Quiet | Standard | Dangerous |
|---|---|---|---|
| firstStrongholdDelay | 60 min | 25 min | 20 min |
| regroupDelay | 45 min | 30 min | 20 min |
| spreadInterval | 20 min | 12 min | 7 min |
| quietMinutes | 15 | 10 | 6 |
| foundInterval | 90 min | 60 min | 40 min |
| cellsPerStronghold | 8 | 7 | 6 |
| maxStrongholds | 2 | 4 | 5 |
| supportRange | 2 | 2 | 3 |
| patrols by tier (0–5) | 0,1,1,1,1,2 | 0,1,1,2,2,2 | 0,1,2,2,3,3 |
| patrolInterval | 15 min | 10 min | 7 min |
| interceptDwell | 60 s | 60 s | 60 s |
| interceptCooldown | 10 min | 10 min | 10 min |
| intercepts by tier (0–5) | 0,1,1,1,1,2 | 0,1,1,2,2,2 | 0,1,2,2,3,3 |
| raidInterval by tier (3–5) | 60, 50, 40 min | 40, 32, 26 min | 30, 24, 18 min |
| threatScale by tier | 1,1,1,1.1,1.2,1.3 | 1,1,1.1,1.2,1.35,1.5 | 1,1.1,1.2,1.3,1.4,1.5 |
| tierThresholds | 10,22,40,60,80 | 12,30,55,80,105 | 15,35,60,90,120 |

Standard, unopposed: Scattered at the first founding (25 min), Organised about 2.5 h, Entrenched about 3.5 h,
Dominant near the fifth hour, the front in the inner ring by the end. Any crew can set that back at any time.

## Everyone can take part

- Guns: hunt patrols (frees a cell at once, paid), break strongholds (collapses a region, paid, loot aboard). Either
  marks the ship for raids for half an hour.
- No guns: cross held space quickly (a minute inside brings a patrol); fly a lane to keep it quiet (a blockade counts moving ships only, so docking or loitering does not stop it);
  stand by an assault for the support pool; haul and salvage.
- Navies: the Halcyon's cell is never taken, but a front at the doorstep is theirs, and each navy's faction credits
  (TSF and PDV alike) flow through the bounty.
- Ghosts: strongholds, patrols and raids on the Encounters tab of the orbit menu as any encounter.
- Admins: `wf_reavers status | found [x y] | tier <n> | spread | patrol | raid | intercept <grid uid> | pause |
  resume | clear` (status lists intercepts and marks), and every campaign encounter in the Encounters admin window.
- Round end: a summary line (peak cells held, strongholds founded and broken, patrols destroyed) through
  `RoundEndTextAppendEvent`.

## Module layout (`ReaverCampaign`)

- `Content.Shared/_WF/ReaverCampaign`: `WFReaverCampaignPrototype.cs` (`wfReaverCampaign`: `preset`, every number
  above, the prototype ids, bounties, sounds and colours), `ReaverCampaignCVars.cs` (`wf.reavers.enabled`).
- `Content.Server/_WF/ReaverCampaign`: `README.md`, `Components/WFReaverStrongholdComponent.cs` (cell, tier at
  founding, hits seen, supporters), `Components/WFReaverPatrolComponent.cs` (stronghold, route),
  `Components/WFReaverRaidComponent.cs` (target), `Components/WFReaverMarkComponent.cs` (until when a ship is
  marked), `Components/WFReaverCampaignComponent.cs` (campaign state on the sector map entity: tier, score, clocks,
  intercept clocks, counters for the round summary), `Systems/WFReaverCampaignSystem.cs` with partials
  `.Strongholds.cs`, `.Spread.cs`, `.Patrols.cs`, `.Intercepts.cs`, `.Raids.cs`, `.Threat.cs`, `.Announce.cs`,
  `.Rewards.cs`, `Commands/WFReaverCommand.cs`.
- `Resources/Prototypes/_WF/ReaverCampaign/campaigns.yml`, `encounters.yml`; `Resources/Locale/en-US/_WF/ReaverCampaign/reavers.ftl`;
  `Resources/ServerInfo/_WF/ReaverCampaign/Reavers.xml` guidebook page if the guidebook takes module pages without
  an upstream edit (follow an existing `_WF` guidebook page; otherwise leave it out and say so).
- Tests: `Content.IntegrationTests/Tests/_WF/ReaverCampaign/` (founding claims ring, lead down breaks and pays,
  patrol kill frees a cell, spread respects quiet and veto, contest claims at deadline, tier rises and falls with
  hysteresis, regroup, every votable preset has exactly one campaign, intercept past the dwell and not outside held
  space, route restored and cooldown held, marks from a patrol kill and a stronghold hit steering a raid, the Reaver
  border notice). Every step is a one-shot method the test can call (`TryFound`, `TrySpreadOnce`, `TryLaunchPatrol`,
  `TryLaunchRaid`, `TryIntercept`, `CheckIntercepts`, `SetTier`), with time from `IGameTiming`.

Edits inside `_WF/Encounters` (same fork, no markers; the README overview gains a paragraph): `TrySpawn(...,
bool offBudget = false)`; public `SetOrders(encounter, key, queue)` that sets `HasOrders`/`Flown`; public
`PayBounty` in the reward system with the helper selection pulled out of `OnResolved`; `WFEncounterWeightEvent`
(by ref) raised in the scheduler's `Pick` at the weight line; public `IsClearSpace(map, point, gridClearance,
stationClearance)` and `Stations(map)` on the scheduler.

## Decisions

- Strongholds are encounters, not stations: crews, zones, markers, the admin window, the ghost menu and cleanup
  come for free, and no map file is needed.
- Lead down is broken. The judge waits for the guards; the campaign does not.
- Killing a patrol frees its cell at once. Slow decay would hide the feedback.
- One raid clock for the campaign, capped by population. Per-stronghold clocks reached 36 raids an hour.
- Light garrisons first. A pirate frigate at minute 20 on a ten-player round is not a fight, it is a wall.
- Only tiers, strongholds and patrol kills are announced. Cell churn shows on the map.
- Staying in held space, not crossing it, is what brings a patrol, and intercepts use the patrols already out first,
  so the downside of Reaver space is a fight, not a toll.

## Later

- A medevac tender beside a stronghold under assault (`WFEncounterMedicTrader` via `TryStartAt`).
- A broken stronghold's lead as a claimable hulk (the derelict machinery).
- Relief freight to a liberated station; a traffic boost at low tiers.
- A Wolfgate admin-tab window for the campaign.
- Storyteller raider placement restricted to held and adjacent cells.
