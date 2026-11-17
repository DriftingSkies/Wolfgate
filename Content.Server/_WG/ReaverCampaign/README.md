# ReaverCampaign

The Ashfall Reaver campaign: a round-long threat built on `SectorControl` territory and `Encounters` ships. The
Reavers found strongholds (persistent encounters holding station at a cell centre), claim the stronghold's cell and
its ring, spread one quiet frontier cell at a time toward the origin, send patrols through held space and, from tier
3, raids at player ships and stations. A threat score (10 a stronghold, 1 a held cell, up to 20 for held space near
the origin) sets a tier from None to Overrun; a rise is announced at once, a fall only after ten minutes under the
threshold. The sector map shows held cells and a legend line with the tier, strongholds, cells and current bounty.
The sector watch banners only capital moments: a rising tier, the first stronghold and a regroup, the citadel setting out,
raised and fallen, and the defeat. Everything else is a line on the Common channel (a falling tier, later strongholds and
their fall, convoys, blockades, kills, retakes, smuggling funding); reinforcements, growth and response packs say nothing, and a
ship a patrol or raid turns on is warned aboard only.
A cell where a campaign ship and a crewed player ship meet is disputed: the map hatches it, and the stronghold's
reinforcement clock, the convoy and reinforcement deadlines there hold until one side leaves or dies; spread skips it.
Campaign crews fire on sight (`WGCrewOnSightComponent` on every campaign hull, set in `SpawnEncounter`): any faction
enemy a crewman sees within 10 m is engaged, aboard or off the ship; the helm, guns and radio only turn on one aboard.
The faction's neutral companies are spared. The sector's random Reaver encounters keep the ordinary rules.
Their fire is reckless and goes through their own hulls, so campaign ships never count one another as attackers
(`.FriendlyFire`); otherwise a few stray rounds would set a hub and its reinforcements on each other.
Campaign crews carry their own NPC faction, `WGAshfallReavers`, hostile to everyone, the Dynasty's people included;
the Fugitive and Godfather jobs add `WGUnderground` beside `NanoTrasen`, and the two are allies.

A stronghold is a hub: a shielded Saintie with docking ports and no guard ships. A hull with no shield generator is
given one, anchored on a free tile in reach of an APC, when the hub spawns. The first stronghold, and a regrouped one
after the last falls, appear far out at once. Every later one flies out as a convoy: the hub spawns beside the
standing stronghold nearest the chosen cell and flies there, the cell shown contested meanwhile and the sector
warned of it. Arrived, it founds the stronghold; its lead out of the fight on the way stops it, founds nothing and pays
half a stronghold's bounty. One convoy is out at a time. The hub is a gun platform (`platform: true`): once founded it
never leaves its point, but turns in place to put its guns on the ship that hit it or the nearest ship in its attack zone.

The Underground (company `WGUnderground`, held by the Fugitive and Godfather jobs and the black market station) is
neutral to the Reavers (`neutralCompanies`, `neutralStations` on the faction): their zones never warn or fire on its
ships (the campaign copies the list to each ship's `NeutralCompanies`, and a shot at one is still answered), patrols and
raids never go after them, they are never marked or counted as presence, and the station's cell is claimed without a
blockade.

The campaign scales with the population (`.Population`): the living, non-ghost players on the sector map, the highest
count of the last ten minutes, or `wg.reavers.population_override` when it is zero or more. Each campaign's
`population` brackets add, from their `minPlayers`, gear tiers (the tier campaign ships pick hull, refit and skill by,
never the threat score), batteries, levels on the tier's cap, patrols per stronghold, intercepts, strongholds on the cap,
a shorter reinforcement interval and a bigger bounty; the highest bracket reached applies, and `wg_reavers status` shows it.

The first stronghold, a regrouped one and the citadel are fortified (`.Batteries`, `WGReaverBatteryComponent`): rung
with the bracket's gun batteries (`battery`, `WGReaverBattery`), gunboats that hold station `batteryRange` out as gun
platforms. While any stands, the hub's shield heals `batteryShieldHeal` times as fast. A destroyed battery pays
`batteryBounty` and the patrol voucher; the hub rebuilds one lost battery each reinforcement interval while not under
fire, and its ring grows with the population but never shrinks. Breaking the hub ends its batteries, and their hits
count for the hub's bounty.

Strongholds level up the longer they stand. A hub at level 0 sends a reinforcement every `reinforcementInterval`
while its level is under the cap for the threat tier (`maxLevelByTier`): a convoy that appears 25 to 28 km out on the
hub's side, flies to the hub and docks. Each dock raises the hub one level (shown in the encounter's name on the map
and in the legend), announced to the sector; a reinforcement whose pilot gives up docking holds beside the hub
and counts all the same, and one that never gets near the hub is ended past its deadline. One lost on the way
founds nothing and pays the patrol bounty. When the hub is hit by player ships its reinforcements undock and attack
the ship that is hitting it, and dock again three minutes after the last hit
with no attacker near. Breaking the hub resolves its reinforcements, which then go through the usual cleanup, and pays `strongholdBounty[tier]` times one plus
half the level.

At tier 5 the Reavers raise a citadel (`citadel` on the campaign, `WGReaverCitadel`, a Vulture; `.Citadel`). Once the
campaign has stood at Overrun for `citadelDelay`, a citadel convoy sets out from the highest-level stronghold to a held
edge cell, announced in a chimed banner with the gamma alert (`capitalSound`) on top; it stands outside `maxStrongholds` and the one-convoy rule. Raised, it claims
`citadelRings` rings, grows `citadelExtraLevels` past the tier's level cap and scores ten more than a stronghold. Breaking
it pays `citadelBountyScale` times a stronghold's bounty and finishes the Reavers for the round: nothing more is founded
or launched, and what still stands fights on until broken. `wg_reavers citadel` sends one out by hand.

Players fight it by breaking strongholds (the lead ship out of the fight breaks it: its cells are freed and its bounty
paid to everyone who hit it, with a fifth for the ships that stood by the assault), killing patrols (the cell the lead
died in is freed and a smaller bounty paid; each navy's members also get its own faction credits, from the campaign's
`credits` list, and each player ship that helped break a stronghold or convoy, or destroy a patrol or reinforcement,
gets the campaign's `strongholdVoucher` or `patrolVoucher`, a `WGDrydockRepairVoucher` bound to that ship, unless it
still holds one; every spesos bounty grows by `shipBountyScale` for each paid attacking ship past the first, up to
`maxShipBountyScale`, so a bigger group doesn't thin every share), and flying lanes: only quiet cells are taken, and a station's cell is
contested for ten minutes, announced as a blockade, before it falls. Each storyteller preset has its own
`wgReaverCampaign` (`Resources/Prototypes/_WG/ReaverCampaign/campaigns.yml`) with the pace, caps, tier ladder,
bounties and encounter prototypes; a preset with none runs no campaign. It runs only while `wg.encounters.enabled`,
`wg.sector.enabled` are on, the round is in progress and the Contested Sector game mode runs (rule `WGReaverCampaignRule`, in
preset `WGContestedSector`; `wg.reavers.enabled` forces it on without the rule), and its own Reaver encounters on
the storyteller's side are weighted up by tier through `WGEncounterWeightEvent`.

A lead out of the fight (crew dead, disabled, hull shot to pieces, or no one at the helm for two minutes) breaks its
patrol, raid, convoy, reinforcement or stronghold. A patrol that chases out of held space and the ring round it is
called back. A hub hit by a player ship recalls the patrols within two cells and, with no reinforcement docked, sends a
sortie pack after the attacker every five minutes. A cell sector control gives back to a ship that held it is announced;
a standing hub's own cell is never given back.

Reaver space has a cost. A crewed ship that stays a minute in held space (`interceptDwell`) has the nearest patrol
within three cells, or a fresh one from the nearest stronghold, turned on it to close and attack, and is warned with
the patrol's bearing and range; the patrol gives up when the ship is disabled or gone, or has been out of held space
or out of reach for twenty seconds, then flies the rest of its route, and the ship is left alone for `interceptCooldown`.
How many intercepts run at once is set by tier (`intercepts`). A ship that hits a stronghold or helps kill a patrol is
marked for half an hour: raids weigh it three times as heavy and hunt it anywhere on the map.

`WGReaverCampaignSystem` is the driver, in partials by concern: `.Strongholds` (founding, regroup, breaking),
`.Hub` (shield placement, levels), `.Reinforcements` (convoys, docking, break-apart), `.Convoys` (later foundings flown out from a standing stronghold),
`.Spread`, `.Patrols` (routes, recall and the leash), `.Intercepts` (intercepts and marks), `.Response` (a hub's sortie packs), `.Retakes`, `.Raids`, `.Threat` (score, tier,
legend), `.Announce` (sector watch announcements, PA, chat and radio warnings) and `.Rewards` (bounties and the support
pool). Each step is a one-shot public method for tests and the command: `TryFound`, `TryFoundAt`, `TryLaunchConvoy`,
`CheckConvoys`, `CompleteFounding`, `TryReinforce`, `CheckReinforcements`, `CompleteDock`, `CheckStrongholds`, `ScatterReinforcements`, `RegroupReinforcements`, `TrySpreadOnce`, `TryLaunchPatrol`, `TryLaunchRaid`, `TryIntercept`, `CheckIntercepts`, `SetTier`, `Recompute`, `ClearCampaign`. State
lives on `WGReaverCampaignComponent` on the sector map entity, with `WGReaverStrongholdComponent`,
`WGReaverConvoyComponent`, `WGReaverReinforcementComponent`, `WGReaverPatrolComponent` and `WGReaverRaidComponent` on the encounter entities and `WGReaverMarkComponent` on marked
player ships.

Admins use `wg_reavers status | found [x y] | convoy | reinforce <stronghold uid> | tier <n> | spread | patrol | raid | intercept <grid uid> |
pause | resume | clear` (`found` places a level 0 hub at once, `convoy` sends one out as the campaign does, `reinforce` sends a
stronghold its next reinforcement; status lists levels, reinforcements, convoys, intercepts and marks; a pause ends with the round), and see every campaign encounter in the Encounters
window. A pause holds every campaign clock (founding, patrols, reinforcements, spread, raids, intercept dwell and cooldowns,
marks, tier falls, blockades and the timers of campaign encounters), so nothing falls due at once on resuming. The same
hold applies while the campaign or the Encounters switch is off and while the event runner (the Encounters scheduler) is
paused, which also holds every running encounter's expiry; `wg_reavers status` says which. A retaken cell pays the
tier's `retakeBounty` to the crews of the ships that held it, through the encounter bounty path and its hourly cap, and
the "retaken" announcement says so. The round-end summary lists peak cells held, strongholds founded and broken, patrols destroyed, convoys
stopped, raids launched and cells retaken, and how often the Reavers were broken.
Freed Reaver cells are announced as clearing (on the radio, one line per batch) and breaking the last stronghold with no
convoy out announces the ending; see `WGSectorCellClearingEvent` in the SectorControl module.

When another faction challenges a Reaver cell (the ADS, `AdsIncursion`), the nearest stronghold scrambles a response
pack (`.Conflict`, `WGReaverResponseComponent`, `campaign.ResponsePack`) to the cell to attack the challenger's lead and
fly home once the challenge is decided; its ships count as Reaver presence there. A cell lost to a challenge is announced
and counted for the round summary. `wg_reavers respond <q> <r>` sends one by hand.

`Docs/_WG/ReaverCampaign/design.md` is the design.

Smuggling funds the Reavers (`.Funding`). Each smuggling crate (the market's `contrabandGoods`) sold at an open chute,
and each crate of NPC freight (a manifest seal) fenced at a `sealedOnlyChutes` chute (the Underground fence), banks
toward a war chest, and so does every `fundingGoodsValue` (5,000) of `fundingGoodsTag` goods (narcotics) sold on a
cargo pallet (`WGPalletSaleEvent`); every `fundingCrates` (4) make a payout, bringing every stronghold's next
reinforcement and the next spread forward by `fundingShare` (a quarter) of their intervals and sending a patrol from the
stronghold with fewest out, up to `fundingPatrols` past the usual patrol cap and without moving that stronghold's own
patrol clock. Each payout is announced on the common channel. The chest banks up to `fundingBank` (3) payouts, so a bulk
run keeps paying, and spends one whenever a stronghold stands, the campaign isn't held and `fundingCooldown` (5 minutes)
has passed since the last; `wg_reavers status` shows it.

Ship raids favour laden ships (up to three times the weight for a hold worth 100,000, through
`WGCargoValueQueryEvent`), and `RecentlyRaided` tells the market which stations a raid was just sent at.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ReaverCampaign/Commands/WGReaverCommand.cs`](Commands/WGReaverCommand.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverBatteryComponent.cs`](Components/WGReaverBatteryComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverCampaignComponent.cs`](Components/WGReaverCampaignComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverCampaignRuleComponent.cs`](Components/WGReaverCampaignRuleComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverConvoyComponent.cs`](Components/WGReaverConvoyComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverMarkComponent.cs`](Components/WGReaverMarkComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverPatrolComponent.cs`](Components/WGReaverPatrolComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverRaidComponent.cs`](Components/WGReaverRaidComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverReinforcementComponent.cs`](Components/WGReaverReinforcementComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverResponseComponent.cs`](Components/WGReaverResponseComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Components/WGReaverStrongholdComponent.cs`](Components/WGReaverStrongholdComponent.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignRuleSystem.cs`](Systems/WGReaverCampaignRuleSystem.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Announce.cs`](Systems/WGReaverCampaignSystem.Announce.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Batteries.cs`](Systems/WGReaverCampaignSystem.Batteries.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Boarding.cs`](Systems/WGReaverCampaignSystem.Boarding.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Citadel.cs`](Systems/WGReaverCampaignSystem.Citadel.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Conflict.cs`](Systems/WGReaverCampaignSystem.Conflict.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Convoys.cs`](Systems/WGReaverCampaignSystem.Convoys.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.cs`](Systems/WGReaverCampaignSystem.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Disputes.cs`](Systems/WGReaverCampaignSystem.Disputes.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.FriendlyFire.cs`](Systems/WGReaverCampaignSystem.FriendlyFire.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Funding.cs`](Systems/WGReaverCampaignSystem.Funding.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Hold.cs`](Systems/WGReaverCampaignSystem.Hold.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Hub.cs`](Systems/WGReaverCampaignSystem.Hub.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Intercepts.cs`](Systems/WGReaverCampaignSystem.Intercepts.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Neutral.cs`](Systems/WGReaverCampaignSystem.Neutral.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Patrols.cs`](Systems/WGReaverCampaignSystem.Patrols.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Population.cs`](Systems/WGReaverCampaignSystem.Population.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Raids.cs`](Systems/WGReaverCampaignSystem.Raids.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Ranks.cs`](Systems/WGReaverCampaignSystem.Ranks.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Reinforcements.cs`](Systems/WGReaverCampaignSystem.Reinforcements.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Response.cs`](Systems/WGReaverCampaignSystem.Response.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Retakes.cs`](Systems/WGReaverCampaignSystem.Retakes.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Rewards.cs`](Systems/WGReaverCampaignSystem.Rewards.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Spread.cs`](Systems/WGReaverCampaignSystem.Spread.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Strongholds.cs`](Systems/WGReaverCampaignSystem.Strongholds.cs)
- [`Content.Server/_WG/ReaverCampaign/Systems/WGReaverCampaignSystem.Threat.cs`](Systems/WGReaverCampaignSystem.Threat.cs)

### Shared

- [`Content.Shared/_WG/ReaverCampaign/ReaverCampaignCVars.cs`](../../../Content.Shared/_WG/ReaverCampaign/ReaverCampaignCVars.cs)
- [`Content.Shared/_WG/ReaverCampaign/WGReaverCampaignPrototype.cs`](../../../Content.Shared/_WG/ReaverCampaign/WGReaverCampaignPrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverAnnouncementTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverAnnouncementTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverCitadelTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverCitadelTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverContentTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverContentTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverConvoyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverConvoyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverDisputeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverDisputeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverEndingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverEndingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFightTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFightTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFoundingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFoundingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFundingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverFundingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverGameModeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverGameModeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverGunPlatformTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverGunPlatformTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverInterceptTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverInterceptTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverNeutralTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverNeutralTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverNoticeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverNoticeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverOwnSideTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverOwnSideTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPatrolTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPatrolTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPauseTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPauseTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPopulationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverPopulationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverRaidTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverRaidTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverReinforcementTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverReinforcementTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSanctuaryTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSanctuaryTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSeizedTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSeizedTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSimulationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverSimulationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverStarvedTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverStarvedTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverStrongholdTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverStrongholdTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverTestBase.cs`](../../../Content.IntegrationTests/Tests/_WG/ReaverCampaign/WGReaverTestBase.cs)

### Prototypes

- [`Resources/Prototypes/_WG/ReaverCampaign/ai_factions.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/ai_factions.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/campaigns.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/campaigns.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/companies.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/companies.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/encounters.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/encounters.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/factions.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/factions.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/game_presets.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/game_presets.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/game_rules.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/game_rules.yml)
- [`Resources/Prototypes/_WG/ReaverCampaign/shields.yml`](../../../Resources/Prototypes/_WG/ReaverCampaign/shields.yml)

### Localization

- [`Resources/Locale/en-US/_WG/ReaverCampaign/reavers.ftl`](../../../Resources/Locale/en-US/_WG/ReaverCampaign/reavers.ftl)

### Docs

- [`Docs/_WG/ReaverCampaign/design.md`](../../../Docs/_WG/ReaverCampaign/design.md)

## Non-modular edits

- [`Resources/Prototypes/_Mono/PointsOfInterest/nodes.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/nodes.yml): the Contested Sector game mode spawns the same sector as Standard
- [`Resources/Prototypes/_Mono/PointsOfInterest/pdvhelios.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/pdvhelios.yml): the Contested Sector game mode spawns the same sector as Standard
- [`Resources/Prototypes/_Mono/PointsOfInterest/tsfmchalcyon.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/tsfmchalcyon.yml): the Contested Sector game mode spawns the same sector as Standard
- [`Resources/Prototypes/_Mono/PointsOfInterest/tsfmcindustry.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/tsfmcindustry.yml): the Contested Sector game mode spawns the same sector as Standard
- [`Resources/Prototypes/_Mono/PointsOfInterest/underground.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/underground.yml): the station flies the neutral WGUnderground company, so the Reavers leave it and its cell alone
- [`Resources/Prototypes/_Mono/PointsOfInterest/ussp.yml`](../../../Resources/Prototypes/_Mono/PointsOfInterest/ussp.yml): the Contested Sector game mode spawns the same sector as Standard
- [`Resources/Prototypes/_Mono/Roles/Jobs/Underground/fugitive.yml`](../../../Resources/Prototypes/_Mono/Roles/Jobs/Underground/fugitive.yml)
  - Underground crew fly the neutral WGUnderground company, so the Reavers leave them alone
  - Underground people are allied with the Reaver campaign's crews
- [`Resources/Prototypes/_Mono/Roles/Jobs/Underground/godfather.yml`](../../../Resources/Prototypes/_Mono/Roles/Jobs/Underground/godfather.yml)
  - Underground crew fly the neutral WGUnderground company, so the Reavers leave them alone
  - Underground people are allied with the Reaver campaign's crews
- [`Resources/Prototypes/_NF/PointsOfInterest/base.yml`](../../../Resources/Prototypes/_NF/PointsOfInterest/base.yml): the Contested Sector game mode spawns the same sector as Standard

<!-- WOLFGATE-GENERATED END -->
