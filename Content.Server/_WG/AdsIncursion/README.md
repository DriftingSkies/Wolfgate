# AdsIncursion

The Automated Defense System as a second sector threat, built on `SectorControl` territory and `Encounters` ships
beside the Ashfall Reaver campaign. The ADS founds nodes (a Nebula gun platform holding a cell centre), claims the
node's cell and its ring, takes one quiet frontier cell at a time and sends sweeps through held space. Every ADS ship
disables what it fights and stops there, and the ADS never claims, contests or challenges a cell with a station in it
(a claim-attempt veto enforces it), so stations stay safe harbours. A threat score (10 a node, 1 a held cell, up to 20
for held space near the origin) sets a tier from Dormant to Overrun; the sector map shows held cells in purple and a
legend line with the tier, nodes, cells and node bounty.
The sector watch banners only capital moments: a rising tier, the first node and a wake from dormancy, the dreadnought
and dormancy. Everything else is a line on the Common channel (a falling tier, later nodes and their fall, deployments,
challenges, kills, retakes); sentinels joining and a challenge turning back say nothing, and an interdicted ship is
warned aboard only.

The first node, and a regrouped one after the last falls, appear far out (16 to 26 km, three cells from anyone else's
space). Later ones fly out as deployments from the standing node nearest the chosen cell, the cell shown contested
meanwhile; one stopped on the way pays half a node's bounty. A node that has stood its interval gets a sentinel, a
Wyrm that flies in from the sector edge and circles it, up to a cap by tier; when the node is hit its sentinels go
after the top attacker and come back three minutes after the last hit. Breaking a node (its lead out of the fight)
frees its cells and pays `nodeBounty[tier]` times one plus half a sentinel alive, to those who hit it or its sentinels,
with a fifth for the ships that stood by. A sweep or sentinel killed pays the sweep bounty, and a sweep frees the cell
it died in. Every spesos bounty is multiplied by the `population` bracket's `bountyScale` and grows by
`shipBountyScale` for each paid attacking ship past the first, up to `maxShipBountyScale`; each helping ship gets the
`nodeVoucher` (node, deployment, dreadnought) or `sweepVoucher` (anything else), unless it still holds one. A crewed ship that stays `interdictDwell` in ADS space has the nearest sweep sent to hold beside it and
query it.

Emergencies (`.Hijack`) pull sweeps off patrol, past the leash and out of held space: an ADS hull seized by boarders
goes on the campaign's `Stolen` list and has the three nearest sweeps, at any range, sent to destroy it, made up again
every 30 s until it is broken or back in ADS hands; an ADS ship fired on, tampered with by a boarder, or with a
crewman downed by a player has two sent after the attacking vessel, or the nearest ship within 1.5 km taken for the
boarders', or one sent to hold beside it when there is none. With no sweep in reach the nearest node launches one. A
hunt ends when its target is out of the fight, docked at a station or back in ADS hands, and one after anything but a
stolen hull after ten minutes.

Every ADS zone interrogates (`zoneTargets: Interrogate`, `WGEncounterInterrogationSystem` in `Encounters`): each crewed
player ship in a warning zone is hailed and asked whether it is a threat. Only a ship that answers YES or fires on the
ADS is warned off and fired on; NO leaves it alone for 15 minutes, no answer for 3. The crews are bare IPC automatons,
immune to vacuum, with their guns in the belt and back slots.

Rocks in held space are protected (`.Mining`). A crewed ship with a mining beam on one is warned once; a ship that stops
is left alone, but its mining time counts for `miningForget`, so short bursts don't reset it. Past `miningGrace` of
mining it is on the declared list of every ADS ship, whatever it answered to a hail, and a sweep is sent after it if one is free or can launch. ADS
ships launched in the next `miningGrudge` count it a threat too. The zones still do the firing, so a miner away from any
ADS ship has only the sweep to worry about.

From `challengeTier` the ADS contests other factions' space: every `challengeInterval` an interdictor flies from the
nearest node to a cell another faction holds and the framework challenge on it is decided by presence at the deadline.
Won, the interdictor stays as a picket holding the cell; lost, it withdraws; killed, it pays the interdictor bounty.
The Reavers answer a challenge on their space with a response pack (`ReaverCampaign`, `.Conflict`).

At tier 5 the ADS activates its dreadnought (`dreadnought` on the campaign, `WGAdsDreadnought`, a Wyvern capital hull;
`.Dreadnought`). Once the campaign has stood at Overrun for `dreadnoughtDelay`, it activates beside the strongest node
with an anomaly alert and the gamma alert (`capitalSound`) and hunts the Reavers' strongholds one by one, the citadel
first, holding by a node when none stands. Its hits count as an attack on a Reaver hub, so the hub's reinforcements and
sorties turn on it. It adds 10 to the threat score while out. Destroying it pays `dreadnoughtBounty` and finishes the ADS
for the round: nothing more is founded or launched, and what still stands fights on until broken. `wg_ads dreadnought`
activates one by hand.

Each storyteller preset has its own `wgAdsCampaign` (`Resources/Prototypes/_WG/AdsIncursion/campaigns.yml`). It runs
only while `wg.encounters.enabled` and `wg.sector.enabled` are on, the round is in progress and rule
`WGAdsCampaignRule` runs (in preset `WGContestedSector`; `addgamerule` elsewhere; `wg.ads.enabled` forces it on).

`WGAdsCampaignSystem` is the driver, in partials by concern: `.Nodes` (founding, regroup, breaking), `.Sentinels`,
`.Deployments`, `.Spread`, `.Sweeps`, `.Interdicts`, `.Challenges` (interdictors and pickets), `.Retakes`, `.Threat`,
`.Announce`, `.Rewards`, `.Hold` (pause and the event runner), `.Disputes` (presence) and `.Dreadnought` (the tier 5 capstone and the finished state). Each step is a one-shot public
method for tests and the command: `TryFound`, `TryFoundAt`, `TryLaunchDeployment`, `CompleteDeployment`, `TrySentinel`,
`TrySpreadOnce`, `TryLaunchSweep`, `TryInterdict`, `TryChallenge`, `TryLaunchDreadnought`, `SetTier`, `Recompute`, `ClearCampaign`. State lives on
`WGAdsCampaignComponent` on the sector map entity, with one component per encounter kind on the encounter entities.

Admins use `wg_ads status | found [x y] | deploy | sentinel <node uid> | tier <n> | spread | sweep | interdict <grid uid> |
challenge [q r] | dreadnought | pause | resume | clear`, and see every campaign encounter in the Encounters window.

`Docs/_WG/AdsIncursion/design.md` is the design.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/AdsIncursion/Commands/WGAdsCommand.cs`](Commands/WGAdsCommand.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsCampaignComponent.cs`](Components/WGAdsCampaignComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsCampaignRuleComponent.cs`](Components/WGAdsCampaignRuleComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsDeploymentComponent.cs`](Components/WGAdsDeploymentComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsDreadnoughtComponent.cs`](Components/WGAdsDreadnoughtComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsInterdictorComponent.cs`](Components/WGAdsInterdictorComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsNodeComponent.cs`](Components/WGAdsNodeComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsPicketComponent.cs`](Components/WGAdsPicketComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsSentinelComponent.cs`](Components/WGAdsSentinelComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Components/WGAdsSweepComponent.cs`](Components/WGAdsSweepComponent.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignRuleSystem.cs`](Systems/WGAdsCampaignRuleSystem.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Announce.cs`](Systems/WGAdsCampaignSystem.Announce.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Challenges.cs`](Systems/WGAdsCampaignSystem.Challenges.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.cs`](Systems/WGAdsCampaignSystem.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Deployments.cs`](Systems/WGAdsCampaignSystem.Deployments.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Disputes.cs`](Systems/WGAdsCampaignSystem.Disputes.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Dreadnought.cs`](Systems/WGAdsCampaignSystem.Dreadnought.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Hijack.cs`](Systems/WGAdsCampaignSystem.Hijack.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Hold.cs`](Systems/WGAdsCampaignSystem.Hold.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Interdicts.cs`](Systems/WGAdsCampaignSystem.Interdicts.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Mining.cs`](Systems/WGAdsCampaignSystem.Mining.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Nodes.cs`](Systems/WGAdsCampaignSystem.Nodes.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Retakes.cs`](Systems/WGAdsCampaignSystem.Retakes.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Rewards.cs`](Systems/WGAdsCampaignSystem.Rewards.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Sentinels.cs`](Systems/WGAdsCampaignSystem.Sentinels.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Spread.cs`](Systems/WGAdsCampaignSystem.Spread.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Sweeps.cs`](Systems/WGAdsCampaignSystem.Sweeps.cs)
- [`Content.Server/_WG/AdsIncursion/Systems/WGAdsCampaignSystem.Threat.cs`](Systems/WGAdsCampaignSystem.Threat.cs)

### Shared

- [`Content.Shared/_WG/AdsIncursion/AdsIncursionCVars.cs`](../../../Content.Shared/_WG/AdsIncursion/AdsIncursionCVars.cs)
- [`Content.Shared/_WG/AdsIncursion/WGAdsCampaignPrototype.cs`](../../../Content.Shared/_WG/AdsIncursion/WGAdsCampaignPrototype.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsAnnouncementTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsAnnouncementTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsChallengeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsChallengeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsContentTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsContentTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsDreadnoughtTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsDreadnoughtTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsHijackTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsHijackTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsInterrogationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsInterrogationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsMiningTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsMiningTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsNodeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsNodeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsTestBase.cs`](../../../Content.IntegrationTests/Tests/_WG/AdsIncursion/WGAdsTestBase.cs)

### Prototypes

- [`Resources/Prototypes/_WG/AdsIncursion/ai_factions.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/ai_factions.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/campaigns.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/campaigns.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/companies.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/companies.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/encounters.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/encounters.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/factions.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/factions.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/fonts.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/fonts.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/game_rules.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/game_rules.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/gear.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/gear.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/guidebook.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/guidebook.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/manifests.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/manifests.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/names.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/names.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/profiles.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/profiles.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/speech_verbs.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/speech_verbs.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/standing.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/standing.yml)
- [`Resources/Prototypes/_WG/AdsIncursion/vessels.yml`](../../../Resources/Prototypes/_WG/AdsIncursion/vessels.yml)

### Localization

- [`Resources/Locale/en-US/_WG/AdsIncursion/ads.ftl`](../../../Resources/Locale/en-US/_WG/AdsIncursion/ads.ftl)

### Audio

- [`Resources/Audio/_WG/AdsIncursion/aimalf.ogg`](../../../Resources/Audio/_WG/AdsIncursion/aimalf.ogg)
- [`Resources/Audio/_WG/AdsIncursion/attributions.yml`](../../../Resources/Audio/_WG/AdsIncursion/attributions.yml)

### Shared maps

- [`Resources/SharedMaps/_WG/AdsIncursion/nebula.yml`](../../../Resources/SharedMaps/_WG/AdsIncursion/nebula.yml)
- [`Resources/SharedMaps/_WG/AdsIncursion/razorn.yml`](../../../Resources/SharedMaps/_WG/AdsIncursion/razorn.yml)
- [`Resources/SharedMaps/_WG/AdsIncursion/wyrm.yml`](../../../Resources/SharedMaps/_WG/AdsIncursion/wyrm.yml)
- [`Resources/SharedMaps/_WG/AdsIncursion/wyvern.yml`](../../../Resources/SharedMaps/_WG/AdsIncursion/wyvern.yml)
- [`Resources/SharedMaps/_WG/AdsIncursion/zenith.yml`](../../../Resources/SharedMaps/_WG/AdsIncursion/zenith.yml)

### Guidebook

- [`Resources/ServerInfo/_WG/AdsIncursion/Guidebook/Ads.xml`](../../../Resources/ServerInfo/_WG/AdsIncursion/Guidebook/Ads.xml)

### Docs

- [`Docs/_WG/AdsIncursion/design.md`](../../../Docs/_WG/AdsIncursion/design.md)

## Non-modular edits

- [`Resources/Prototypes/_Mono/Guidebook/factions.yml`](../../../Resources/Prototypes/_Mono/Guidebook/factions.yml)

<!-- WOLFGATE-GENERATED END -->
