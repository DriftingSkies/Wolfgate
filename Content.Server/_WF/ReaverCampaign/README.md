# ReaverCampaign

The Ashfall Reaver campaign: a round-long threat built on `SectorControl` territory and `Encounters` ships. The
Reavers found strongholds (persistent encounters holding station at a cell centre), claim the stronghold's cell and
its ring, spread one quiet frontier cell at a time toward the origin, send patrols through held space and, from tier
3, raids at player ships and stations. A threat score (10 a stronghold, 1 a held cell, up to 20 for held space near
the origin) sets a tier from None to Overrun; a rise is announced at once, a fall only after ten minutes under the
threshold. The sector map shows held cells and a legend line with the tier, strongholds, cells and current bounty.
A cell where a campaign ship and a crewed player ship meet is disputed: the map hatches it, and the stronghold's
reinforcement clock, the convoy and reinforcement deadlines there hold until one side leaves or dies; spread skips it.

A stronghold is a hub: a shielded Saintie with docking ports and no guard ships. A hull with no shield generator is
given one, anchored on a free tile in reach of an APC, when the hub spawns. The first stronghold, and a regrouped one
after the last falls, appear far out at once. Every later one flies out as a convoy: the hub spawns beside the
standing stronghold nearest the chosen cell and flies there, the cell shown contested meanwhile and the sector
warned of it. Arrived, it founds the stronghold; its lead out of the fight on the way stops it, founds nothing and pays
half a stronghold's bounty. One convoy is out at a time.

Strongholds level up the longer they stand. A hub at level 0 sends a reinforcement every `reinforcementInterval`
while its level is under the cap for the threat tier (`maxLevelByTier`): a convoy that appears 25 to 28 km out on the
hub's side, flies to the hub and docks. Each dock raises the hub one level (shown in the encounter's name on the map
and in the legend), announced to the sector; a reinforcement whose pilot gives up docking holds beside the hub
and counts all the same, and one that never gets near the hub is ended past its deadline. One lost on the way
founds nothing and pays the patrol bounty. When the hub is hit by player ships its reinforcements undock and attack
the ship that is hitting it, and dock again three minutes after the last hit
with no attacker near. Breaking the hub ends its reinforcements and pays `strongholdBounty[tier]` times one plus
half the level.

Players fight it by breaking strongholds (the lead ship out of the fight breaks it: its cells are freed and its bounty
paid to everyone who hit it, with a fifth for the ships that stood by the assault), killing patrols (the cell the lead
died in is freed and a smaller bounty paid; each navy's members also get its own faction credits, from the campaign's
`credits` list), and flying lanes: only quiet cells are taken, and a station's cell is
contested for ten minutes, announced as a blockade, before it falls. Each storyteller preset has its own
`wfReaverCampaign` (`Resources/Prototypes/_WF/ReaverCampaign/campaigns.yml`) with the pace, caps, tier ladder,
bounties and encounter prototypes; a preset with none runs no campaign. It runs only while `wf.encounters.enabled`,
`wf.sector.enabled` and `wf.reavers.enabled` are on and the round is in progress, and its own Reaver encounters on
the storyteller's side are weighted up by tier through `WFEncounterWeightEvent`.

Reaver space has a cost. A crewed ship that stays a minute in held space (`interceptDwell`) has the nearest patrol
within three cells, or a fresh one from the nearest stronghold, turned on it to close and attack, and is warned with
the patrol's bearing and range; the patrol gives up when the ship is disabled or gone, or has been out of held space
or out of reach for a minute, then flies the rest of its route, and the ship is left alone for `interceptCooldown`.
How many intercepts run at once is set by tier (`intercepts`). A ship that hits a stronghold or helps kill a patrol is
marked for half an hour: raids weigh it three times as heavy and hunt it anywhere on the map.

`WFReaverCampaignSystem` is the driver, in partials by concern: `.Strongholds` (founding, regroup, breaking),
`.Hub` (shield placement, levels), `.Reinforcements` (convoys, docking, break-apart), `.Convoys` (later foundings flown out from a standing stronghold),
`.Spread`, `.Patrols` (routes and recall), `.Intercepts` (intercepts and marks), `.Raids`, `.Threat` (score, tier,
legend), `.Announce` (sector watch announcements, PA, chat and radio warnings) and `.Rewards` (bounties and the support
pool). Each step is a one-shot public method for tests and the command: `TryFound`, `TryFoundAt`, `TryLaunchConvoy`,
`CheckConvoys`, `CompleteFounding`, `TryReinforce`, `CheckReinforcements`, `CompleteDock`, `CheckStrongholds`, `ScatterReinforcements`, `RegroupReinforcements`, `TrySpreadOnce`, `TryLaunchPatrol`, `TryLaunchRaid`, `TryIntercept`, `CheckIntercepts`, `SetTier`, `Recompute`, `ClearCampaign`. State
lives on `WFReaverCampaignComponent` on the sector map entity, with `WFReaverStrongholdComponent`,
`WFReaverConvoyComponent`, `WFReaverReinforcementComponent`, `WFReaverPatrolComponent` and `WFReaverRaidComponent` on the encounter entities and `WFReaverMarkComponent` on marked
player ships.

Admins use `wf_reavers status | found [x y] | convoy | reinforce <stronghold uid> | tier <n> | spread | patrol | raid | intercept <grid uid> |
pause | resume | clear` (`found` places a level 0 hub at once, `convoy` sends one out as the campaign does, `reinforce` sends a
stronghold its next reinforcement; status lists levels, reinforcements, convoys, intercepts and marks; a pause ends with the round), and see every campaign encounter in the Encounters
window. The round-end summary lists peak cells held, strongholds founded and broken, patrols destroyed, convoys
stopped and raids launched.

`Docs/_WF/ReaverCampaign/design.md` is the design.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/ReaverCampaign/Commands/WFReaverCommand.cs`](Commands/WFReaverCommand.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverCampaignComponent.cs`](Components/WFReaverCampaignComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverConvoyComponent.cs`](Components/WFReaverConvoyComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverMarkComponent.cs`](Components/WFReaverMarkComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverPatrolComponent.cs`](Components/WFReaverPatrolComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverRaidComponent.cs`](Components/WFReaverRaidComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverReinforcementComponent.cs`](Components/WFReaverReinforcementComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Components/WFReaverStrongholdComponent.cs`](Components/WFReaverStrongholdComponent.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Announce.cs`](Systems/WFReaverCampaignSystem.Announce.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Convoys.cs`](Systems/WFReaverCampaignSystem.Convoys.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.cs`](Systems/WFReaverCampaignSystem.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Disputes.cs`](Systems/WFReaverCampaignSystem.Disputes.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Hub.cs`](Systems/WFReaverCampaignSystem.Hub.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Intercepts.cs`](Systems/WFReaverCampaignSystem.Intercepts.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Patrols.cs`](Systems/WFReaverCampaignSystem.Patrols.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Raids.cs`](Systems/WFReaverCampaignSystem.Raids.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Reinforcements.cs`](Systems/WFReaverCampaignSystem.Reinforcements.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Rewards.cs`](Systems/WFReaverCampaignSystem.Rewards.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Spread.cs`](Systems/WFReaverCampaignSystem.Spread.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Strongholds.cs`](Systems/WFReaverCampaignSystem.Strongholds.cs)
- [`Content.Server/_WF/ReaverCampaign/Systems/WFReaverCampaignSystem.Threat.cs`](Systems/WFReaverCampaignSystem.Threat.cs)

### Shared

- [`Content.Shared/_WF/ReaverCampaign/ReaverCampaignCVars.cs`](../../../Content.Shared/_WF/ReaverCampaign/ReaverCampaignCVars.cs)
- [`Content.Shared/_WF/ReaverCampaign/WFReaverCampaignPrototype.cs`](../../../Content.Shared/_WF/ReaverCampaign/WFReaverCampaignPrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverContentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverConvoyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverConvoyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverDisputeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverDisputeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverFoundingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverFoundingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverInterceptTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverInterceptTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverNoticeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverNoticeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverPatrolTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverPatrolTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverRaidTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverRaidTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverReinforcementTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverReinforcementTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverStrongholdTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverStrongholdTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverTestBase.cs`](../../../Content.IntegrationTests/Tests/_WF/ReaverCampaign/WFReaverTestBase.cs)

### Prototypes

- [`Resources/Prototypes/_WF/ReaverCampaign/campaigns.yml`](../../../Resources/Prototypes/_WF/ReaverCampaign/campaigns.yml)
- [`Resources/Prototypes/_WF/ReaverCampaign/encounters.yml`](../../../Resources/Prototypes/_WF/ReaverCampaign/encounters.yml)
- [`Resources/Prototypes/_WF/ReaverCampaign/factions.yml`](../../../Resources/Prototypes/_WF/ReaverCampaign/factions.yml)
- [`Resources/Prototypes/_WF/ReaverCampaign/shields.yml`](../../../Resources/Prototypes/_WF/ReaverCampaign/shields.yml)

### Localization

- [`Resources/Locale/en-US/_WF/ReaverCampaign/reavers.ftl`](../../../Resources/Locale/en-US/_WF/ReaverCampaign/reavers.ftl)

### Docs

- [`Docs/_WF/ReaverCampaign/design.md`](../../../Docs/_WF/ReaverCampaign/design.md)

## Non-modular edits

None.

<!-- WOLFGATE-GENERATED END -->
