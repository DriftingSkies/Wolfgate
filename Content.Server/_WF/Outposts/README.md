# Outposts

Player-built bases on planets: found one with an outpost console, build on it, save it to your account and bring it
back in a later round, spawn there, and defend it from night raids. The build order is `Docs/_WF/Outposts/PLAN.md`,
the spike results `Docs/_WF/Outposts/SPIKES.md`; the design doc belongs next to them as `DESIGN.md`.

Founding (`WFOutpostSystem`, off until `wf.outposts.enabled` is set): a player uses an outpost console kit
(`WFOutpostConsoleKit`) on a planet's ground, and a 3 x 3 grid is laid over it with the console (`WFOutpostConsole`) in
the middle. Every tile of the square has to be natural ground (not built over) with no other grid over it; the biome's
own growth on it is cleared, anything else anchored there refuses. The grid is held down like a station (ForceAnchor,
PreventGridAnchorChanges, its body static) and kept from cleanup; each account has one live outpost
(`WFOutpostComponent.Owner`). Destroying the console abandons the outpost (`Abandon`): it stays held down and keeps its
claim, but isn't saved or sold, and the owner may found another. Only the owner can found it again, with a console kit
used on its floor, which puts a new console there (`TryRefound`).

The ground under an outpost is carved away (`BiomeSystem.WFOutposts`): the tile is removed and marked modified so the
biome never grows it back, and loose things on it move onto the outpost. Its floor is then the only floor there, so the
ground's navmesh, weather strikes and wildlife never reach under its walls. The carved tiles come back as they were when
the outpost is deleted or sold. Its edge is joined to the ground by a ring of pathfinding portals
(`WFOutpostSystem.Portals`), so NPCs walk on and off it. Foundation plates (`WFOutpostFoundationPlate`, stacks of 30) extend the
outpost a tile at a time onto open ground anywhere in its claim, so its floor may lie in separate patches, up to
`wf.outposts.max_tiles` (1,600) and no wider than `wf.outposts.max_span` (64) tiles either way. Outpost grids never
split (`CanSplit` off), so a patch cut off by a breach stays part of the outpost.

An outpost is a terrain grid (`WFPlanetTerrainGridComponent`, from Planets), not a vessel: open to the sky until roofed,
so weather and lightning reach its bare tiles. Walls roof the tile they stand on, and roof panels (`WFOutpostRoofPanel`)
roof a floor tile (`WFOutpostSystem.Roofs`); both stack to 30, crates hold stacks of 10, and their art is
`Textures/_WF/Outposts/building.rsi` (`Tools/_WF/Outposts/build_plates.py`). Floor tiles go on an outpost like on any
grid: `FloorTileSystem` doesn't count the planet layer as a grid placed close by. Carcinoma's tendrils leave terrain grids alone.

The console's Overview tab shows a top-down map of the outpost (`WFOutpostMapControl`, a `NavMapControl` fed by the
grid's nav map, refreshed when it is held down) with its claim outlined and the console and trade hub marked, next to
its name, owner, trade hub status, and floor tiles and objects as bars against their caps (objects:
`wf.outposts.max_entities`, 2,000, what a save can hold). The owner can rename it (30 characters). Only the owner
opens the console. Plates and roof panels are laid by the owner and by cards listed as builders on the Access tab.

Access (`WFOutpostSystem.Access`): an outpost takes ship access (`WFShipAccessComponent`, module ShipAccess) like a
bought ship, registered to the owner's account (`OwnerUsers`), so the owner keeps it in any body and whoever is
listed opens its doors but not its console. The console's Access tab is the shuttle console's `ShipAccessScreen`, its
messages handled through `WFShipAccessServerSystem.SubscribeAccessTab`. A founder whose card names a faction company
founds the outpost under it, in Faction mode. The mode, lock and door rules save with the grid; the allow list holds
this round's record keys, so a save keeps each entry by account and character (`WFOutpostComponent.Access`, holders
found through their minds) and a loaded outpost lists that character's card again when they are in a body
(`PendingAccess`). Record keys are dropped from the serialised grid (`DropRecordKeys`): door readers are rewritten
on load and cards left on the outpost come back blank.

Claim zones (`WFOutpostSystem.Claims`): the ground within 8 tiles of an outpost's floor is its owner's. Nobody else may
found an outpost or lay a plate there, or build on the raw ground: laying lattice, starting a structure or anchoring
something there raises `WFGroundBuildAttemptEvent`, which the claim cancels with a popup naming the outpost. A new
outpost is only founded where its whole claim is clear of the ground the world keeps for itself (`IsReserved`: POI site
bounds, roads, their shoulders and footpaths, cavern mouths).

Saving (`WFOutpostSystem.Saves`): the owner saves the outpost into one of three slots per account (outposts belong to
accounts, not characters) on the console's Saves tab. The grid is serialised in one tick, so a save is the outpost as
it stood that tick, then written, gzip'd (SmallestSize) and stored on a worker in `wolfgate_outpost_save`
(`ServerDbManager.Outposts`), queued behind the account's character writes. A save notes what its parts were worth
(`Appraise`): what stands on it that a save keeps (`IsKept`: not players, mobs, cash, deeds or ghost roles) and the
plates its floor grew by past the founding square. Saves over `wf.outposts.max_entities` are refused, naming the three
prototypes it holds most of.

Every save of an outpost carries its lineage (`WFOutpostComponent.Lineage`, a GUID set at founding and kept through
saves and loads), so a sale can later void all of them. What a save holds is stamped (`WFSaveOriginComponent`): safety
deposit boxes refuse it and Mono's persistent round-end items skip it, so nothing is kept twice. An outpost autosaves
into the slot it was last saved into or loaded from every `wf.outposts.autosave_interval` seconds (600), only when it
changed (`Fingerprint`), at most one outpost a second, and once more at round end.

The character editor's Outposts tab (`WFOutpostSavesPanel`, `WFOutpostSystem.Editor`) lists the account's saves and
deletes them. Admins have `wfoutpost list|restore|refund|delete <player> [slot]` (`WFOutpostCommand`): restore makes a
save loadable again this round, refund pays back what a load of it cost. Everything is admin-logged as `WFOutpost`.

Previews (`WFOutpostSystem.Preview`): the Preview button on a save, at the console (owner only) or in the editor tab,
asks the server for that save's grid; it reads the row, un-gzips it on a worker and sends the YAML to the owner alone,
once a second at most. The client's `WFOutpostPreviewBox` shows it in the ship previewer (`ShipPreviewControl`).

Loading (`WFOutpostSystem.Loading`): a save loads onto a freshly founded outpost (no plates, nothing anchored but the
console) for its appraisal x 1.5, paid from the owner's bank. It is loaded onto a paused staging map, stripped of what
a save never keeps, priced on the copy, and placed so its console lands on the founding console's tile. Every tile it
will cover has to be open ground (or the founding outpost's), outside other claims, with no reserved ground within the
claim margin. The outline is drawn on the ground (`WFOutpostPendingLoadComponent`, client
`WFOutpostPendingLoadOverlay`) for `wf.outposts.load_delay` seconds (30); then the ground is checked again and the save
swapped in: the founding outpost goes (loose things on it stay), the save is held down, carved in, ringed with portals
and re-initialised like a used ship (`UsedShipReinitSystem`), and the founding console packs back into its kit. A load
called off, blocked at landing or whose outpost is lost is refunded. A save saved or loaded in a round can't be loaded
again until the next, so an outpost can't be loaded twice or copied while it still stands.

Gizmos (`WFOutpostSystem.Deploy`): an outpost machine comes in a pack (`WFDeployableComponent`). Used in hand, the
pack deploys it on the outpost floor tile in front of the user, facing them, if the user builds on that outpost and
nothing stands there. The owner or a builder packs a deployed one up with the Pack up verb (`WFRepackableComponent`):
the machine itself goes into the pack's container, so its charge, contents and settings come back with it. A pack
holding its machine is worth the machine. Gizmos carry no Machine, MachineBoard or Construction component, so none
can be taken apart for a board, and each is worth what its pack is (`OutpostDeployTest.GizmoRules`). The console kit
deploys by founding, and the console of an outpost still as it was founded packs back into its kit, giving the
outpost up. Walls and windows (`face: false`) stand square with the outpost instead of facing their user.

Fabricator (`fabricator.yml`, `construction.yml`, `recipes.yml`): the outpost fabricator is a gizmo lathe, twice as
slow as an autolathe and with no parts to upgrade; like a stock lathe it links ore and fabrication silos and shows
sheets going in (its `inserting` state is drawn in `build_colony.py`), and plays Nova's start, loop and end sounds
while its queue runs (`WFLatheRunningSoundSystem`). It prints foundation plates, roof panels, prefab walls, doors and
shutters (gizmos that pack up again; a wall welded in place for 5 seconds, `WFPrefabWallFixed` through `WFWeldSwapComponent`, no
longer does until its welds are cut, by its owner or builders), window and airlock kits (colony-pattern ones that come apart as stock ones),
colony floor tiles (`floors.yml`), another fabricator, the powered driver, the arc welder (it makes its own fuel) and
the stock parts, electronics and cables. The gizmo and prefab art in `Textures/_WF/Outposts/Colony` is NovaSector's
colony fabricator set (CC BY-SA 3.0), adapted to our 3/4 view: the prefab wall and window are the stock solid wall
and window recoloured to Nova's palette, and the doors' lit lids are painted as our dark wall tops. Doors face four
ways: north and south show the front, east and west a side view turned to stand in a north-south wall, so a door set
in a side wall (deployed from beside it, or a preset door at q=1) lies along it. The airlock's lights, wiring panel and
emag sparks show on its own status screen and ride with its leaf, in place of the stock airlock's overlays; the manual
door slides with Nova's manual door sound (`Audio/_WF/Outposts`), not the stone door's grind. `build_colony.py`
and `build_walls.py` in `Tools/_WF/Outposts` rebuild it from the DMIs. The outpost console (`Textures/_WF/Outposts/
console.rsi`) is drawn for Wolfgate in that set's palette at the stock computer's size: four facings, an unshaded screen with the
outpost's map in claim amber, and a shrink-wrapped `packed` state for its kit.
It never prints a console. Its `BlueprintReceiver` takes only outpost recipe disks (tag `WFOutpostRecipeDisk`). A POI
workshop leaves one disk (`WFOutpostRecipeDiskWorkshopSpawner`): the homestead disk 60% of the time, the T2 industrial
disk (autolathe, ore processor, biogenerator, gas recycler) 30%, the T3 advanced disk (protolathe, circuit imprinter,
ChemMaster, chem dispenser) 10%. Each Wolfgate recipe's product is worth at least its
materials (`OutpostFabricatorTest`); the stock recipes keep upstream's prices.

Power (`power.yml`, `WFOutpostPowerSystem`): every generator and battery sits on the MV cable on its tile, as on a ship,
so an outpost needs an APC on a wall to turn that into LV for its machines; the kit brings the APC electronics, steel and
MV cable to build one. A power exporter's 30 kW is more than one APC gives, so it wants two on its LV run. Wind turbines (`WFWindTurbineComponent`) make their calm power
under open sky in air of 5 kPa or more, up to twice that as the weather's wind rises to storm strength, and nothing
under a roof. Solar strings (`WFSolarStringComponent`) follow the planet clock: nothing at night (`IsNight`), rising
from dawn to a noon peak. Both are set once a second. The gizmos modelled on stock machines run at 0.6 of them
(`OutpostPowerTest.ScaleRule`): the compact RTG of the RTG, the A.W. generator of the S.U.P.E.R.P.A.C.M.A.N., the large
battery of the SMES and the small one of the substation. The A.W. (Atomic Whisperer) burns one uranium fuel rod
(`WFFuelRodGeneratorSystem`): used on it, the rod goes in, keeps what fuel it has left on itself, lasts three hours at
optimal power and comes out with the eject button; it is shielded and gives off no radiation. Nothing stacks on a tile: a deploy is refused where anything
low, mid or high blocks it.

Trade (`trade.yml`, `WFOutpostSystem.Trade.cs`): the console's Trade tab lists the cargo products in the `WFOutpost`
group. The owner pays from their bank with the cargo console's tax split (`taxAccounts` on the console), up to five
orders at once. After `wf.outposts.trade_lead_time` seconds a flare (`WFTradeDropFlare`) lights on the outpost's
universal trade hub (`WFUniversalTradeHubComponent`, a 1 kW gizmo that takes orders only with power and open sky; its colony-style pad, `trade_hub.rsi`, is drawn by
`build_trade_hub.py`, its corner beacons lit while powered) and
the order comes down onto it by parachute: `WFTradeDropSystem` spawns a `WFTradeDropDescent` there, which
`WFTradeDropVisualsSystem` draws as the product sinking under a canopy over a growing shadow, and after 8 seconds swaps
it for the product with a thud and a puff of dust. If the hub's tile is roofed or blocked, it lands on the nearest
clear open tile within 5 tiles; while there is none the order waits. Orders aren't saved: they are lost with the round.

Landing guard (`WFLandingClaimEvent`, answered in `WFOutpostSystem.Claims.cs`): hulls keep off every outpost's claim,
its owner's too. Enter Atmosphere is refused over one with the outpost named, a gentle set-down holds off over one,
and a hull falling without lift into the gap over the ground is moved sideways to the nearest unclaimed spot before
it lands (`CEZLevelsSystem.WFLandingClaims.cs`, `OutpostLandingTest`).

The Planet Outpost Kit (`WFCratePlanetOutpostKit`) holds a console kit, a trade hub, a fabricator, a wind turbine, a
solar string, a small battery, cable, and ten plates and ten roof panels. The add-on crates (shell, foundation,
power) and every pack are sold by the outpost outfitter (`WFVendingMachineOutpostOutfitter`), which John Wolfgate and
Marcus Delgado also stock. Each costs at least what it appraises at with its packs deployed, and each Trade listing at
least what its delivery appraises at (`OutpostTradeTest`). John Wolfgate also sells the console kit, plates and roof
panels on their own (`WFWolfgateVendInventory`).

Sale (`WFOutpostSystem.Sale.cs`): the console's Sell tab sells the outpost to the game for its sale value
(`SaleValue`): what a save keeps that stands anchored on it, with whatever is inside those, and its plates. Loose
goods and people are set down on the ground, which comes back, and the grid goes. Or the owner offers it to a player
standing within 2.5 m of the console for a price; the offer stands two minutes, and the player buys it with the
console's Buy outpost verb, paying the seller from their bank. The buyer gets it as a new outpost: a new lineage, its
allow list cleared and ship access registered to them. A sale is refused while a load is coming down, a save is being
written, trade orders are in flight or another player is on the outpost. Any sale voids every save of the outpost's
lineage. "Sell when the round ends" sells it at PostRound, before the last autosaves; an owner who isn't in a body
then is owed the sum in `wolfgate_outpost_payout` (migration `WolfgateOutpostPayouts`) and paid when they next spawn.

Settlers (`WFOutpostSystem.Settlers.cs`): outpost cryopods (`WFOutpostCryopod`, a Frontier cryo pod set up from
`WFOutpostCryopodPack`, six to an outpost) let the owner join mid-round from the lobby. Join Round asks the server
(`WFOutpostJoinQueryEvent`) and adds a "Wake up at" choice when the player owns a live outpost with a free pod; picking
it spawns them in that pod as `WFOutpostSettler`, through `WFRoundSetupSystem.TrySpawnAt` on a gridless station
(`WFOutpostStation`) made for the round and kept out of every job picker. The settler's loadout and custom title are
set per character on the editor's Outposts tab.

Life support (`WFOutpostLifeSupportSystem`, `lifesupport.yml`): the atmos and kitchen pack, sold by the outfitter.
The CO2 cracker (`WFCo2CrackerComponent`) turns the air's carbon dioxide into oxygen, a mole a second at most, while
powered. Synthesizers (`WFReagentSynthesizerComponent`) fill a drainable tank with one reagent while powered: water,
plant nutrient, and a nutriment slurry at the sustenance dispenser. The rest are stock machines on boardless frames:
temperature regulator and wall heater (space heater parts; the wall heater is always on and heats only), griddle
(electric grill), range, a rations printer (a lathe pressing biomass into ration bricks) and the animal feeder, which is
only a trough until farming adds pens.

Drops (`WFOutpostSystem.Drops.cs`): an Outpost crew in Crew Setup brings its host's save down with no founding outpost.
`TryQueueDrop` checks the save (`CanDrop`: outposts on, under `wf.outposts.max_live`, none of the owner's standing or
queued, the save there and not used this round) and queues it; the save is read at once and lands, one a second, once
its world has finished preloading, on a free outpost lot of the world's (PlanetPois `outpostLots`) or else a random
site of open unclaimed ground clear of reserved ground (32 tries within
0.6 of a bounded world's radius, or 150 tiles of an unbounded one's middle). It is placed like a load (`Settle`) and the
caller hears back through its callback, with the new outpost or why it couldn't land.

Crew (`WFOutpostCrewComponent`): an outpost's Crew Setup crew is saved with it, its name, description, admission and
openings (not its members, invitations or code). RoundSetup writes it before every save and fingerprint
(`WFRoundSetupSystem.SyncOutpostCrew`) and takes it back when the outpost lands for an Outpost crew or its owner wakes
there through the join chooser, which puts them in its crew (`JoinOutpostCrew`). A sale removes it.

Logistics (`logistics.yml`, `WFOutpostLogisticsSystem`): packs for a telecom relay (Common and Traffic), an intercom,
a floodlight post, a four-cell charger (`WFMultiChargerComponent`, 50 charge a second into each cell while powered),
a recycler, an organics printer, an ore silo and a fabrication silo. The time/weather console is the forecast console
with a clock line (`WFPlanetWeatherSystem.TryGetDayCycle`: time to dusk at 18:00 or dawn at 06:00, and the storm
phase now). The planet broadcast console (`WFPlanetBroadcastConsoleComponent`) lets a builder send one line, gold, to
everyone on the outpost's world; five minutes a console and a minute a world between broadcasts, logged. A tick box
adds the console's position. While the outpost is raided (`WFRaidDirectorSystem.IsRaided`) the window shows a call for
help: a red announcement with the outpost, world and position to everyone in the sector, ignoring the broadcast
cooldowns, once in 10 minutes a console. The deposit terminal is a bank ATM's cash slot and deposit (5% fee) on a
gizmo frame; `WFDepositOnlyComponent` cancels any withdrawal message and its window hides the withdrawal half.

Basements (`WFOutpostSystem.Basements.cs`, `basements.yml`): an outpost is a set of grids, one per depth. A basement
hatch kit (outfitter) used on a floor tile with floor all round it digs a 3x3 room on the outpost's depth -1 grid, a
grid on the cavern lined up tile for tile with the outpost (`WFOutpostBasementComponent`), founding it if need be. The
floor tile becomes a hole (`WFOutpostComponent.Hatches`, saved) over a CE ladder, and a CE connector pillar in the room's
corner binds the two grids into one z-network. Foundation plates laid against the basement's rock dig it a tile
further. Basement tiles must lie over the claim's foundation; the floor and entity caps, appraisal, fingerprint and
stamping count both grids. A save keeps the basement as its own grid under `wfDepths` with its depth; staging loads it
beside the outpost and settling puts it on the cavern below, or floors the hatches over if the world has no cavern.
Deleting the outpost brings whatever is loose in the basement up to the ground and puts the cavern back.

Claiming sites (`WFOutpostSystem.PoiClaims.cs`, `WFPoiClaimTerminalSystem`, `poi_claims.yml`): a site whose
`wfPoiType` can be claimed gets a site terminal when its world preloads, broken for Repair sites and locked for Hack
sites (multitool). Using it with nothing hostile left in the site makes the site the user's outpost, if they have none
and under half the world's claimable sites are held: a wreck in place, a site in the ground lifted onto its own grid.
Its value at claiming (`ClaimBaseline`, saved) is taken off its sale value, so a sale pays only what was added.

Presets (`WFOutpostPresetPrototype`, `presets.yml`, grids in `Resources/SharedMaps/_WF/Outposts`): ready-made outposts,
the homestead, workshop, farmstead and bastion, each with a console, pods, a trade hub under open sky and its own power.
They use the colony prefab walls (welded in place), windows, doors and floors players make at the fabricator, lay plates
only where something stands, run their generators on MV to an APC, keep a CO2 cracker and a temperature regulator in
the living area, start those with an A.W. on a crate of two uranium fuel rods (`WFCrateOutpostFuelRods`), and are laid
out as rooms added on over time, so they read as player-built.
The console's Saves tab lists them after the slots (`TryStartPresetLoad`, in place of a fresh outpost like a load) and an
Outpost crew's page lists them after the host's saves (`TryQueuePresetDrop`). Both stage the grid like a save, for the
preset's fixed price, and land it as no slot under the preset's name; it is saved only when its owner chooses. Clients
preview a preset from its file. `OutpostPresetTest` holds each price at or above its appraisal with the load markup.

Landing pads (`WFLandingPadSystem`, `pads.yml`): a beacon from the outfitter marks a square (5 to 21 tiles, widened or
narrowed by builders) on the outpost floor. A pilot with access (the owner or the Access tab's allow list, or anyone on a
public pad) who asks to descend within 32 m of a powered, free pad is hopped straight onto it, squared to its grid,
whatever the hull's lift (`WFOrbitDescentAttemptEvent`, `WFHopExact`, which spares the pad's grid from the touchdown
crush). The parked hull is static (`WFPadDockedComponent`) until Lift Off hops it back to orbit (`WFLiftoffDivertEvent`).
The beacon draws 5 kW while guiding. Every world's pads are listed on its orbit radar map. The Tradepost has an
unpowered public pad (`WFPoiPublicLandingPad`) on an apron, and a public trade hub (`WFPoiPublicTradeHub`) that takes the
drops of any outpost on its world whose own hubs can't.

Other modules keep state in a save through events on the outpost (`WFOutpostEvents.cs`): `WFOutpostSavingEvent` and
`WFOutpostSavedEvent` round the serialisation, `WFOutpostSettledEvent` once a loaded save has landed, and
`WFOutpostAppraiseEvent` and `WFOutpostFingerprintEvent` for its value and change check. Husbandry keeps penned livestock
this way.

Foundation rock (`WFOutpostSystem.FoundationRock.cs`): the cavern under an outpost's claim loads as foundation rock
(`WFOutpostFoundationRock`, laid by the biome loader through `BiomeSystem.Foundation.cs` from the cavern's
`WFOutpostFoundationComponent`) in place of its own rock and tunnels, and the ground over it won't open: no mouth is cut
there, a hole fills again, and stairs and shafts are refused (`WFCavernOpenAttemptEvent`). Cavern already loaded or
changed under a new claim keeps what it has until it unloads. An outpost is only ever its own grid on the ground, so
its placement, claims and saves never reach below it.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Outposts/BiomeSystem.Foundation.cs`](BiomeSystem.Foundation.cs)
- [`Content.Server/_WF/Outposts/BiomeSystem.WFOutposts.cs`](BiomeSystem.WFOutposts.cs)
- [`Content.Server/_WF/Outposts/Commands/WFOutpostCommand.cs`](Commands/WFOutpostCommand.cs)
- [`Content.Server/_WF/Outposts/Components/WFCo2CrackerComponent.cs`](Components/WFCo2CrackerComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFDeployableComponent.cs`](Components/WFDeployableComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFDepositOnlyComponent.cs`](Components/WFDepositOnlyComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFFuelRodGeneratorComponent.cs`](Components/WFFuelRodGeneratorComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFLatheRunningSoundComponent.cs`](Components/WFLatheRunningSoundComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFMultiChargerComponent.cs`](Components/WFMultiChargerComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostBasementComponent.cs`](Components/WFOutpostBasementComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostComponent.cs`](Components/WFOutpostComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostConsoleComponent.cs`](Components/WFOutpostConsoleComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostCrewComponent.cs`](Components/WFOutpostCrewComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostCryopodComponent.cs`](Components/WFOutpostCryopodComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostFoundationComponent.cs`](Components/WFOutpostFoundationComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostFoundationPlateComponent.cs`](Components/WFOutpostFoundationPlateComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostHatchKitComponent.cs`](Components/WFOutpostHatchKitComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostKitComponent.cs`](Components/WFOutpostKitComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostRoofPanelComponent.cs`](Components/WFOutpostRoofPanelComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostStationComponent.cs`](Components/WFOutpostStationComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFOutpostTurretComponent.cs`](Components/WFOutpostTurretComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFPadDockedComponent.cs`](Components/WFPadDockedComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFPadGuidedComponent.cs`](Components/WFPadGuidedComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFPlanetBroadcastConsoleComponent.cs`](Components/WFPlanetBroadcastConsoleComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFPoiClaimTerminalComponent.cs`](Components/WFPoiClaimTerminalComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFReagentSynthesizerComponent.cs`](Components/WFReagentSynthesizerComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFRepackableComponent.cs`](Components/WFRepackableComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFSolarStringComponent.cs`](Components/WFSolarStringComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFUniversalTradeHubComponent.cs`](Components/WFUniversalTradeHubComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFWeldSwapComponent.cs`](Components/WFWeldSwapComponent.cs)
- [`Content.Server/_WF/Outposts/Components/WFWindTurbineComponent.cs`](Components/WFWindTurbineComponent.cs)
- [`Content.Server/_WF/Outposts/NavMapSystem.Outposts.cs`](NavMapSystem.Outposts.cs)
- [`Content.Server/_WF/Outposts/ServerDbManager.Outposts.cs`](ServerDbManager.Outposts.cs)
- [`Content.Server/_WF/Outposts/WFFuelRodGeneratorSystem.cs`](WFFuelRodGeneratorSystem.cs)
- [`Content.Server/_WF/Outposts/WFLandingPadSystem.cs`](WFLandingPadSystem.cs)
- [`Content.Server/_WF/Outposts/WFLatheRunningSoundSystem.cs`](WFLatheRunningSoundSystem.cs)
- [`Content.Server/_WF/Outposts/WFOutpostEvents.cs`](WFOutpostEvents.cs)
- [`Content.Server/_WF/Outposts/WFOutpostLifeSupportSystem.cs`](WFOutpostLifeSupportSystem.cs)
- [`Content.Server/_WF/Outposts/WFOutpostLogisticsSystem.cs`](WFOutpostLogisticsSystem.cs)
- [`Content.Server/_WF/Outposts/WFOutpostPowerSystem.cs`](WFOutpostPowerSystem.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Access.cs`](WFOutpostSystem.Access.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Basements.cs`](WFOutpostSystem.Basements.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Claims.cs`](WFOutpostSystem.Claims.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.cs`](WFOutpostSystem.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Deploy.cs`](WFOutpostSystem.Deploy.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Drops.cs`](WFOutpostSystem.Drops.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Editor.cs`](WFOutpostSystem.Editor.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Foundation.cs`](WFOutpostSystem.Foundation.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.FoundationRock.cs`](WFOutpostSystem.FoundationRock.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Loading.cs`](WFOutpostSystem.Loading.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.PoiClaims.cs`](WFOutpostSystem.PoiClaims.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Portals.cs`](WFOutpostSystem.Portals.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Preview.cs`](WFOutpostSystem.Preview.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Roofs.cs`](WFOutpostSystem.Roofs.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Sale.cs`](WFOutpostSystem.Sale.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Saves.cs`](WFOutpostSystem.Saves.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Settlers.cs`](WFOutpostSystem.Settlers.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Trade.cs`](WFOutpostSystem.Trade.cs)
- [`Content.Server/_WF/Outposts/WFOutpostSystem.Welding.cs`](WFOutpostSystem.Welding.cs)
- [`Content.Server/_WF/Outposts/WFPoiClaimTerminalSystem.cs`](WFPoiClaimTerminalSystem.cs)
- [`Content.Server/_WF/Outposts/WFTradeDropSystem.cs`](WFTradeDropSystem.cs)

### Shared

- [`Content.Shared/_WF/Outposts/OutpostCVars.cs`](../../../Content.Shared/_WF/Outposts/OutpostCVars.cs)
- [`Content.Shared/_WF/Outposts/WFDeployEvents.cs`](../../../Content.Shared/_WF/Outposts/WFDeployEvents.cs)
- [`Content.Shared/_WF/Outposts/WFGroundBuildAttemptEvent.cs`](../../../Content.Shared/_WF/Outposts/WFGroundBuildAttemptEvent.cs)
- [`Content.Shared/_WF/Outposts/WFLandingPadComponent.cs`](../../../Content.Shared/_WF/Outposts/WFLandingPadComponent.cs)
- [`Content.Shared/_WF/Outposts/WFOutpostConsoleUi.cs`](../../../Content.Shared/_WF/Outposts/WFOutpostConsoleUi.cs)
- [`Content.Shared/_WF/Outposts/WFOutpostJoinMessages.cs`](../../../Content.Shared/_WF/Outposts/WFOutpostJoinMessages.cs)
- [`Content.Shared/_WF/Outposts/WFOutpostPendingLoadComponent.cs`](../../../Content.Shared/_WF/Outposts/WFOutpostPendingLoadComponent.cs)
- [`Content.Shared/_WF/Outposts/WFOutpostPresetPrototype.cs`](../../../Content.Shared/_WF/Outposts/WFOutpostPresetPrototype.cs)
- [`Content.Shared/_WF/Outposts/WFOutpostSavesEvents.cs`](../../../Content.Shared/_WF/Outposts/WFOutpostSavesEvents.cs)
- [`Content.Shared/_WF/Outposts/WFPlanetBroadcast.cs`](../../../Content.Shared/_WF/Outposts/WFPlanetBroadcast.cs)
- [`Content.Shared/_WF/Outposts/WFPoiClaimEvents.cs`](../../../Content.Shared/_WF/Outposts/WFPoiClaimEvents.cs)
- [`Content.Shared/_WF/Outposts/WFSaveOriginComponent.cs`](../../../Content.Shared/_WF/Outposts/WFSaveOriginComponent.cs)
- [`Content.Shared/_WF/Outposts/WFTradeDropComponent.cs`](../../../Content.Shared/_WF/Outposts/WFTradeDropComponent.cs)

### Client

- [`Content.Client/_WF/Outposts/HumanoidProfileEditor.Outposts.cs`](../../../Content.Client/_WF/Outposts/HumanoidProfileEditor.Outposts.cs)
- [`Content.Client/_WF/Outposts/WFDepositTerminalBoundUserInterface.cs`](../../../Content.Client/_WF/Outposts/WFDepositTerminalBoundUserInterface.cs)
- [`Content.Client/_WF/Outposts/WFOutpostConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/Outposts/WFOutpostConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/Outposts/WFOutpostConsoleWindow.xaml`](../../../Content.Client/_WF/Outposts/WFOutpostConsoleWindow.xaml)
- [`Content.Client/_WF/Outposts/WFOutpostConsoleWindow.xaml.cs`](../../../Content.Client/_WF/Outposts/WFOutpostConsoleWindow.xaml.cs)
- [`Content.Client/_WF/Outposts/WFOutpostJoinSystem.cs`](../../../Content.Client/_WF/Outposts/WFOutpostJoinSystem.cs)
- [`Content.Client/_WF/Outposts/WFOutpostMapControl.cs`](../../../Content.Client/_WF/Outposts/WFOutpostMapControl.cs)
- [`Content.Client/_WF/Outposts/WFOutpostPendingLoadOverlay.cs`](../../../Content.Client/_WF/Outposts/WFOutpostPendingLoadOverlay.cs)
- [`Content.Client/_WF/Outposts/WFOutpostPendingLoadOverlaySystem.cs`](../../../Content.Client/_WF/Outposts/WFOutpostPendingLoadOverlaySystem.cs)
- [`Content.Client/_WF/Outposts/WFOutpostPreviewBox.cs`](../../../Content.Client/_WF/Outposts/WFOutpostPreviewBox.cs)
- [`Content.Client/_WF/Outposts/WFOutpostSavesPanel.cs`](../../../Content.Client/_WF/Outposts/WFOutpostSavesPanel.cs)
- [`Content.Client/_WF/Outposts/WFOutpostSavesSystem.cs`](../../../Content.Client/_WF/Outposts/WFOutpostSavesSystem.cs)
- [`Content.Client/_WF/Outposts/WFPlanetBroadcastBoundUserInterface.cs`](../../../Content.Client/_WF/Outposts/WFPlanetBroadcastBoundUserInterface.cs)
- [`Content.Client/_WF/Outposts/WFPlanetBroadcastWindow.cs`](../../../Content.Client/_WF/Outposts/WFPlanetBroadcastWindow.cs)
- [`Content.Client/_WF/Outposts/WFTradeDropVisualsSystem.cs`](../../../Content.Client/_WF/Outposts/WFTradeDropVisualsSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostBasementTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostBasementTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostBreachSpikeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostBreachSpikeTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostCavernTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostCavernTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostClaimTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostClaimTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostCrewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostCrewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostDeployTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostDeployTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostFabricatorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostFabricatorTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostFoundationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostFoundationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostFoundingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostFoundingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostHardeningTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostHardeningTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostLandingTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostLandingTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostLifeSupportTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostLifeSupportTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostLogisticsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostLogisticsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostPadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostPadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostPoiClaimTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostPoiClaimTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostPowerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostPowerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostPresetTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostPresetTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostPreviewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostPreviewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostRoofTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostRoofTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaveBenchmarkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaveBenchmarkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaveLoadTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostSaveLoadTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostScenarioTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostScenarioTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostSettlerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostSettlerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Outposts/OutpostTradeTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Outposts/OutpostTradeTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Outposts/basements.yml`](../../../Resources/Prototypes/_WF/Outposts/basements.yml)
- [`Resources/Prototypes/_WF/Outposts/console.yml`](../../../Resources/Prototypes/_WF/Outposts/console.yml)
- [`Resources/Prototypes/_WF/Outposts/construction.yml`](../../../Resources/Prototypes/_WF/Outposts/construction.yml)
- [`Resources/Prototypes/_WF/Outposts/fabricator.yml`](../../../Resources/Prototypes/_WF/Outposts/fabricator.yml)
- [`Resources/Prototypes/_WF/Outposts/floors.yml`](../../../Resources/Prototypes/_WF/Outposts/floors.yml)
- [`Resources/Prototypes/_WF/Outposts/foundation.yml`](../../../Resources/Prototypes/_WF/Outposts/foundation.yml)
- [`Resources/Prototypes/_WF/Outposts/lifesupport.yml`](../../../Resources/Prototypes/_WF/Outposts/lifesupport.yml)
- [`Resources/Prototypes/_WF/Outposts/logistics.yml`](../../../Resources/Prototypes/_WF/Outposts/logistics.yml)
- [`Resources/Prototypes/_WF/Outposts/pads.yml`](../../../Resources/Prototypes/_WF/Outposts/pads.yml)
- [`Resources/Prototypes/_WF/Outposts/poi_claims.yml`](../../../Resources/Prototypes/_WF/Outposts/poi_claims.yml)
- [`Resources/Prototypes/_WF/Outposts/power.yml`](../../../Resources/Prototypes/_WF/Outposts/power.yml)
- [`Resources/Prototypes/_WF/Outposts/presets.yml`](../../../Resources/Prototypes/_WF/Outposts/presets.yml)
- [`Resources/Prototypes/_WF/Outposts/recipes.yml`](../../../Resources/Prototypes/_WF/Outposts/recipes.yml)
- [`Resources/Prototypes/_WF/Outposts/roofs.yml`](../../../Resources/Prototypes/_WF/Outposts/roofs.yml)
- [`Resources/Prototypes/_WF/Outposts/settlers.yml`](../../../Resources/Prototypes/_WF/Outposts/settlers.yml)
- [`Resources/Prototypes/_WF/Outposts/tools.yml`](../../../Resources/Prototypes/_WF/Outposts/tools.yml)
- [`Resources/Prototypes/_WF/Outposts/trade.yml`](../../../Resources/Prototypes/_WF/Outposts/trade.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Outposts/outposts.ftl`](../../../Resources/Locale/en-US/_WF/Outposts/outposts.ftl)
- [`Resources/Locale/en-US/_WF/Outposts/pads.ftl`](../../../Resources/Locale/en-US/_WF/Outposts/pads.ftl)

### Textures

- [`Resources/Textures/_WF/Outposts/building.rsi/`](../../../Resources/Textures/_WF/Outposts/building.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/airlock.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/airlock.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/arc_welder.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/arc_welder.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/battery_large.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/battery_large.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/battery_small.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/battery_small.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/co2_cracker.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/co2_cracker.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/door.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/door.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/fabricator.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/fabricator.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/floor_tiles.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/floor_tiles.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/floors.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/floors.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/fuel_generator.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/fuel_generator.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/griddle.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/griddle.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/heater.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/heater.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/ore_silo.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/ore_silo.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/plastic_panels.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/plastic_panels.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/prefab_wall.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/prefab_wall.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/prefab_window.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/prefab_window.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/range.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/range.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/rations_printer.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/rations_printer.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/recycler.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/recycler.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/rtg.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/rtg.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/shutters.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/shutters.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/solar_string.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/solar_string.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/synthesizers.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/synthesizers.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/trade_hub.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/trade_hub.rsi/)
- [`Resources/Textures/_WF/Outposts/Colony/wind_turbine.rsi/`](../../../Resources/Textures/_WF/Outposts/Colony/wind_turbine.rsi/)
- [`Resources/Textures/_WF/Outposts/console.rsi/`](../../../Resources/Textures/_WF/Outposts/console.rsi/)
- [`Resources/Textures/_WF/Outposts/Effects/drop_shadow.rsi/`](../../../Resources/Textures/_WF/Outposts/Effects/drop_shadow.rsi/)

### Audio

- [`Resources/Audio/_WF/Outposts/attributions.yml`](../../../Resources/Audio/_WF/Outposts/attributions.yml)
- [`Resources/Audio/_WF/Outposts/aw_reactor.ogg`](../../../Resources/Audio/_WF/Outposts/aw_reactor.ogg)
- [`Resources/Audio/_WF/Outposts/fabricator_end.ogg`](../../../Resources/Audio/_WF/Outposts/fabricator_end.ogg)
- [`Resources/Audio/_WF/Outposts/fabricator_loop.ogg`](../../../Resources/Audio/_WF/Outposts/fabricator_loop.ogg)
- [`Resources/Audio/_WF/Outposts/fabricator_start.ogg`](../../../Resources/Audio/_WF/Outposts/fabricator_start.ogg)
- [`Resources/Audio/_WF/Outposts/manual_door_close.ogg`](../../../Resources/Audio/_WF/Outposts/manual_door_close.ogg)
- [`Resources/Audio/_WF/Outposts/manual_door_open.ogg`](../../../Resources/Audio/_WF/Outposts/manual_door_open.ogg)

### Shared maps

- [`Resources/SharedMaps/_WF/Outposts/bastion.yml`](../../../Resources/SharedMaps/_WF/Outposts/bastion.yml)
- [`Resources/SharedMaps/_WF/Outposts/farmstead.yml`](../../../Resources/SharedMaps/_WF/Outposts/farmstead.yml)
- [`Resources/SharedMaps/_WF/Outposts/homestead.yml`](../../../Resources/SharedMaps/_WF/Outposts/homestead.yml)
- [`Resources/SharedMaps/_WF/Outposts/workshop.yml`](../../../Resources/SharedMaps/_WF/Outposts/workshop.yml)

### Tools

- [`Tools/_WF/Outposts/build_colony.py`](../../../Tools/_WF/Outposts/build_colony.py)
- [`Tools/_WF/Outposts/build_plates.py`](../../../Tools/_WF/Outposts/build_plates.py)
- [`Tools/_WF/Outposts/build_trade_hub.py`](../../../Tools/_WF/Outposts/build_trade_hub.py)
- [`Tools/_WF/Outposts/build_walls.py`](../../../Tools/_WF/Outposts/build_walls.py)

### Docs

- [`Docs/_WF/Outposts/PLAN.md`](../../../Docs/_WF/Outposts/PLAN.md)
- [`Docs/_WF/Outposts/SPIKES.md`](../../../Docs/_WF/Outposts/SPIKES.md)

## Non-modular edits

- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs): the account's outpost saves, as a tab of their own
- [`Content.Client/Pinpointer/UI/NavMapControl.cs`](../../../Content.Client/Pinpointer/UI/NavMapControl.cs): agree with Draw, so a map framed off the origin doesn't flicker.
- [`Content.Client/UserInterface/Controls/MapGridControl.xaml.cs`](../../../Content.Client/UserInterface/Controls/MapGridControl.xaml.cs): snap to the target, not the origin.
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - saved outposts
  - round-end sales owed to offline owners
  - one saved outpost per account and slot; saves found by lineage
  - an outpost saved by its owner, brought back in a later round
- [`Content.Server/_Mono/Persistence/PersistentProfileSystem.Items.cs`](../../_Mono/Persistence/PersistentProfileSystem.Items.cs): something saved with an outpost comes back with the outpost, not with its carrier
- [`Content.Server/Construction/ConstructionSystem.Initial.cs`](../../Construction/ConstructionSystem.Initial.cs): raw ground inside another player's outpost claim is theirs to build on.
- [`Content.Server/Parallax/BiomeSystem.ChunkLoader.cs`](../../Parallax/BiomeSystem.ChunkLoader.cs): the cavern under an outpost's claim is foundation rock
- [`Content.Shared.Database/LogType.cs`](../../../Content.Shared.Database/LogType.cs): founding, saving, loading and admin handling of player outposts
- [`Content.Shared/Construction/EntitySystems/AnchorableSystem.cs`](../../../Content.Shared/Construction/EntitySystems/AnchorableSystem.cs): raw ground inside another player's outpost claim is theirs to build on.
- [`Content.Shared/Tiles/FloorTileSystem.cs`](../../../Content.Shared/Tiles/FloorTileSystem.cs): raw ground inside another player's outpost claim is theirs to build on.

<!-- WOLFGATE-GENERATED END -->
