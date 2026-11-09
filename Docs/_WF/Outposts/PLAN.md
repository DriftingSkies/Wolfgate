# Outposts implementation plan

The step-by-step order for building the outposts design doc (`OUTPOSTS_DESIGN.md`, not yet in the repo; it belongs
next to this file as `DESIGN.md`). The design doc says what; this says in which order, in what size pieces, and when
the game is in a shippable state in between. Phase numbers (F0 to F10) are the design doc's; every step below is one
pull request inside a phase.

Rules for every step:

- One PR, mergeable on its own, reviewed on its own. Target under 1,500 changed lines; split anything bigger.
- Behind a cvar (`wf.outposts.*`) or invisible to players until the step that turns it on, so main stays playable.
- Ships with its integration test (never Destructive), its loc strings and its README section.
- Marked upstream edits only where the design doc's hooks table names one. New behaviour goes in `_WF` systems.
  Planets, PlanetPois, Caverns, ShipAccess and RoundSetup are `_WF` modules: changes there are ordinary edits, not
  marked ones.
- Nothing in a later step is allowed to be a prerequisite for merging an earlier one. If it is, the order is wrong.

Sizes: S under 500 lines, M 500 to 1,500, L 1,500 to 3,000 (split it if you can).

## Already on main (2026-11-08)

Several steps were built by other modules before outposts started. They are marked **Done** below, and the steps
that build on them now reuse them instead of adding parallel systems.

| Plan step | Built as | Differs from the original plan |
|---|---|---|
| F0.4 Client YAML load | `ShipPreviewSystem.TryLoadText` and `ShipPreviewControl.SetGridText` (Content.Client/_WF/ShipPreview), used by the mapping tools' map window on real clients | None; the answer is yes, through the previewer rather than `MapLoaderSystem` directly |
| F3.1 Owner and allow list | `WFShipAccessComponent` (ShipAccess) | Keyed to ID cards' crew records, not `NetUserId`; modes are Private, Crew and Faction; accounts are registered only for admin-spawned ships (`OwnerUsers`); the allow list is round state and is not saved with the grid |
| F3.2 Per-door rules | `WFDoorAccessRuleComponent`, the door map on the Access tab | Nine rules (Default, OwnerOnly, Players, Code, PlayersOrCode, Public, Sealed, Crew, Company); rules are `DataField`s and save with the grid |
| F3.3 Codes | `WFShipAccessServerSystem.Codes` | None: 5 misses in 10 minutes locks out for 15 |
| F5.1 Play groups | RoundSetup crews (Crew Setup) | Crews with Public, Requests, Code, Invite and company admission, roles with titles and slots, saved setups; scenarios are Ship, Crashlanded, Faction Crew, Down With Ye' Ship! and Formed, not outposts |
| F5.2 Shuttle Crash, solo | RoundSetup Crashlanded (`WFCrashScenarioSystem`) | Twelve civilian hulls from `wfCrashPool`, crash worlds only, three-minute countdown, `TryDropFromOrbit`, deed to the leader at no sale value, sites clear of claims (F5.4) |
| F5.3 Crash multiplay | Crashlanded crews | None |
| F7.1 World bounds | `WFPlanetBoundsSystem`, `BiomeSystem.WFBounds`, `WFPlanetDragSystem` (Planets) | Worlds are circles (`radius`, default 256); the edge is an impassable wall ring rather than a push-back |
| F7.5 prerequisite | Caverns: one cavern map at depth -1 per world, with cavern mouths cut into the ground | The underground layer exists, so F7.5 no longer waits on a branch |
| F10.1 Atmospheric jetpack | `WFAtmosphericJetpackComponent` (Planets/Jetpack) | None found; `MaxGravity` 1.5 g |

Parts that exist and later steps build on:

- Weather and time: `WFPlanetEnvironmentComponent.IsNight`, `WFPlanetWeatherSystem.TryGetReport` and
  `TryGetSchedule` (weather rolled three hours ahead), the weather forecast computer `WFComputerWeatherForecast`.
- Landing: `WFLandingSurveySystem`, `ShuttleSystem.WFSetDown`, the atmosphere-entry refusal outside a world's circle.
- POIs: `wfPlanetPoiTable` placement with footprints, roads, wrecks and camps (PlanetPois); sites on radar
  (PlanetSignals).
- Raiders: tunnel raiders (`WFCavernRaiderSystem`), the Reaver campaign's raids, NPC door prying (NpcPry).
- Saving per player: ProfileSaves' `WFUserWriteQueue` serialises a player's database writes.

## Milestones (stop points)

The plan has four places where you can stop and the feature is coherent:

| Milestone | After step | What players get |
|---|---|---|
| M1 Persistent base | F1.3 | Found an outpost with a console, build on it with stock construction, it comes back next round for parts + 50% |
| M2 Outpost economy | F2.4 | Kit, fabricator, prefab building, own power, trade panel with parachute drops. No ship needed once you are down |
| M3 Live there | F4.2 | Spawn at your outpost with your Outpost Job, friends join through its crew in Crew Setup |
| M4 Something happens | F8.3 | Bounded worlds with POIs to walk to, night raids that breach, early warning, turrets that only shoot raiders |

Everything after M4 (pads, farming, orbital defence, basements) is additive.

## Order

For one or two developers, in this order. Steps on the same line can run in parallel because they do not touch the
same files.

1. F0.1, F0.2 (spikes): **Done**, see `SPIKES.md`. The separate-grid model holds.
2. F1.1 and F0.3 (**Done**)
3. F1.2a and F1.2b (**Done**)
4. F1.3 (**Done**)
5. F1.4, F1.5, F3.4 (**Done**)
6. F2.1, F2.2, F2.3, F2.4 in order, in parallel with F6.1 and F5.4 (crash follow-ups) (**Done**)
7. F1.6 (sale) (**Done**)
8. F4.1, F4.2, F4.3 in order, in parallel with F2.5, F2.6
9. F7.2, then F7.3
10. F8.1, F8.2, F8.3 in order, in parallel with F7.4
11. F4.4 (presets) (**Done**), F6.2 (pads) (**Done**), F7.5 (**Done**), F9.1 (**Done**), F9.2 (**Done**), F9.3 (**Done**)
12. F10.x as wanted

## F0: Spikes

No player-facing change. Each spike is a branch with a test or a benchmark and a paragraph of results pasted into
the design doc's open questions. Merge the tests where they are cheap to keep.

| Step | What | Done when | Size |
|---|---|---|---|
| F0.1 Breach spike | **Done** (`OutpostBreachSpikeTest`, `SPIKES.md`). A separate grid can be raided: the outpost needs a ring of pathfinding portals to the ground, and the breach is walk up, attack the wall as a target, walk in. Steering's own wall smashing doesn't work on main and isn't used | | |
| F0.2 Save benchmark | **Done** (`OutpostSaveBenchmarkTest`, `SPIKES.md`). About 0.21 ms an entity to save and 0.16 ms to load, on the main thread; 97 KiB packed at 4,000 entities. Entity cap 2,000 for v1 | | |
| F0.3 Roof and outdoors | **Done** (`OutpostRoofTest`, `SPIKES.md`). Outposts are terrain grids (`WFPlanetTerrainGridComponent`) with explicit roofs: walls and roof panels roof a tile, bare tiles are outdoors. No overlay screenshot (the test is headless) | | |
| F0.4 Client YAML load | **Done.** `ShipPreviewSystem.TryLoadText` loads a grid from a YAML string on the client, and the mapping tools already run it on real clients | | |

## F1: Outpost grid, save, load, sale

Module `Outposts`. The spine; everything else hangs off it.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F1.1 Console and grid | **Done** (`OutpostFoundingTest`). WFOutpostComponent, WFOutpostConsoleComponent, the console kit `WFOutpostConsoleKit` (6,000; listed at the Wolfgate trader in F1.3, not before, so nobody buys one while outposts are off), deploy on natural ground founds a 3x3 grid with ForceAnchor + PreventGridAnchorChanges + ShuttleSystem.Disable, ownership by NetUserId (as ShipAccess's `OwnerUsers`), one live outpost per player, name (30 chars), CleanupImmune, destroying the console unanchors, the Overview tab with name and owner, the pathfinding portal ring (each edge tile joined to the ground tile outside it, removed when the grid moves or goes), cvar wf.outposts.enabled | Test: found, name, second console refused, destroy frees the slot and the grid moves; an NPC paths from the ground onto the outpost and back | M | none |
| F1.2a Foundation and ground | **Done** (`OutpostFoundationTest`). Foundation plates (`WFOutpostFoundationPlate`, owner only until F3.4) extend the grid onto open ground anywhere in its claim (since 2026-11-09; first only beside its floor), within a 64-tile span (`wf.outposts.max_span`), and outpost grids never split; the ground under the outpost is carved away (tile removed, natural growth cleared, marked so the biome never regrows it) at founding and under every plate, and comes back when the outpost goes; the portal ring follows the edge; tile cap (`wf.outposts.max_tiles`, 1,600) refuses plates; Overview counters for tiles and objects against their caps (`wf.outposts.max_entities`, 2,000); outposts are exempt from Carcinoma's hull infestation. Carving also keeps weather strikes and wildlife off covered ground: both skip empty tiles | | | |
| F1.2b Claim zones | **Done** (`OutpostClaimTest`). The claim is the outpost's floor on the ground grown by 8 tiles. Inside another's claim nobody founds, lays a plate or builds on raw ground: lattice, structure construction and wrench anchoring each raise `WFGroundBuildAttemptEvent` (marked hooks in `FloorTileSystem`, `ConstructionSystem.Initial`, `AnchorableSystem`), cancelled there. A new outpost's claim has to be clear of the ground the world reserves (POI site bounds, roads, shoulders and footpaths, cavern mouths); outposts never sit on it. Cavern mouths stamped later keep 4 tiles off any grid (their own rule) but may come nearer than the claim | | | |
| F1.3 Save and load | **Done** (`OutpostSaveLoadTest`). Migration WolfgateOutposts and `wolfgate_outpost_save`, the IServerDbManager API with writes queued through `WFUserWriteQueue`; saves belong to the account (3 slots), like the outpost, so no profile id lookup is needed. The save predicate v1 (`IsKept`), appraisal plus plate value, the Saves tab (save, overwrite, delete, load, list with price), staging map load, strip on the copy, clearance v1 (open ground, other claims, reserved ground in the margin), price = appraisal x 1.5 from the bank, the kit, plates and panels listed at John Wolfgate and `wf.outposts.enabled` on (M1), the outline overlay (`wf.outposts.load_delay`, 30 s) with call-off and refund, re-check, swap onto the founding outpost's console tile, ReinitLoadedShip and the outpost re-init (portal ring included), the founding console back as a kit, "saved or loaded this round" (`IsInUse`), gzip SmallestSize. A save loads only onto a freshly founded outpost; loading from the lobby comes with F4.2 | | | |
| F1.4 Save hardening | **Done** (`OutpostHardeningTest`). Autosave (10 min, skip when unchanged, staggered so no two outposts save in the same second), final autosave at round end, size cap refusal (2,000 entities, from F0.2) with the "what is big" list, lineage id on every save, save stamps (WFSaveOriginComponent; deposit box and Mono persistent items refuse them), character editor Outposts tab (metadata list, delete), admin commands (wfoutpost list/restore/refund/delete, admin log types) | Test: autosave skips an unchanged outpost; a stamped item is refused by the deposit box | M | F1.3 |
| F1.5 Preview | **Done** (`OutpostPreviewTest`). Presets preview from their SharedMaps file with F4.4. Server decompresses and sends one save's YAML to its owner on request, rate limited; the client shows it with `ShipPreviewControl.SetGridText` (no new loader); preview in the Save tab and the editor tab; preset grids preview too | Real client log clean of Sandbox violation; preview renders a saved outpost | S | F1.4 |
| F1.6 Sale | **Done** (`OutpostSaleTest`, `OutpostFoundingTest`). The sale value counts what a save keeps that stands anchored, with its contents (they go with the grid), so loose goods are set down on the ground instead. A player buys on the owner's offer with the console's Buy outpost verb (offers last 2 minutes, buyer within 2.5 m). Destroying the console abandons the outpost; the owner re-founds it with a kit on its floor. Sell tab: sell to the game at the sale predicate's value (structures and machines, not goods), shuttle-style checks, "sell at round end" toggle with the wf_outpost_payout table for offline owners, sell to a player standing at the console (price, accept, transfer), any sale voids the lineage, Abandoned state with owner-only re-founding | Test: sale pays the predicate value, voids every save of the lineage, buyer owns the grid | M | F1.4 |

M1 lands at F1.3. Between F1.3 and F2, an outpost is built with stock walls, stock generators and whatever the owner
hauls down by ship. That is fine; it is a persistent base already.

## F2: Kit, fabricator, gizmos, trade

Module `Outposts` (gizmos may get a sub-folder per category).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F2.1 Deploy and repack framework | **Done** (`OutpostDeployTest`). The UTH takes it when it exists (F2.4); the print-cost half of the price rule lands with the fabricator's recipes (F2.2). WFDeployableComponent (use in hand on the tile in front, 2 to 4 s, only on your own foundation, console excepted), repack verb (owner and Builders, 3 s, the machine itself goes into the pack's container so all state survives), the no-board rule and its test (no Machine, MachineBoard or Construction component on an outpost gizmo), StaticPrice = print cost rule and its test, the console and UTH converted to it | Test: deploy, use, repack, redeploy keeps battery charge and contents | M | F1.2 |
| F2.2 Fabricator and construction pack | **Done** (`OutpostFabricatorTest`). The price rule covers Wolfgate recipes; the stock parts, electronics and cables keep upstream's prices (some appraise below their materials). Window and airlock kits set up the stock entities, priced at their materials so printing to sell gains nothing. Outpost Fabricator (LatheComponent with a static T1 recipe pack, slow, no upgrades, prints another fabricator but never a console), prefab wall panel, prefab window, roof panel, manual door kit, prefab airlock kit, shutters kit, foundation plate recipe, powered driver, arc welder, the stock parts/boards/electronics pack, recipe disks for T2/T3 (the gate only; disks come later) | Test: every T1 recipe prints and its product's StaticPrice is at or above its material cost | M | F2.1 |
| F2.3 Power pack | **Done** (`OutpostPowerTest`). Generators and batteries sit on MV, as on a ship, so an APC turns it into LV. The 0.6 scale rule: gizmos modelled on a stock machine supply and store 0.6 of it (RTG, S.U.P.E.R.P.A.C.M.A.N., SMES, substation). The turbine sprite is the directional fan for now. Wind turbine (outdoors, 5 kPa, storm bonus in windy weather), deployable solar strings with the day/night scaling system (reads `IsNight`), small and large stationary batteries, compact RTG, A.W. generator (one uranium fuel rod, shielded), the 0.6 scale rule | Test: turbine makes power outdoors and not under a roof; solar output follows IsNight | M | F2.1 |
| F2.4 Kit, UTH and trade panel | **Done** (`OutpostTradeTest`). Drops wait while no clear open tile is within 5 tiles of the hub; orders are round state, not saved. "Shipyard outfitters" became the outpost outfitter vending machine (`WFVendingMachineOutpostOutfitter`), stocked by John Wolfgate and Marcus Delgado; placing the machine at shipyards is a mapping task. The window kit sells below the stock window it sets up (F2.2), so the deployed-price check is per crate, not per pack. WFUniversalTradeHubComponent (outdoors, 1 kW), Trade tab with the catalogue as cargo-product listings in an outpost group, owner pays with the cargo tax split, 5 in flight, parachute drop onto the UTH under a flare (a descent drawn on the ground layer, not a z-level fall) with the blocked-pad fallback, the Planet Outpost Kit at the shipyard outfitters and WF traders, add-on packs, the price >= appraisal test over kit, packs and listings | Test: an order lands on the UTH within 5 tiles; kit price is at or above its deployed appraisal | M | F2.2, F2.3 |
| F2.5 Atmos and kitchen pack | **Done** (`OutpostLifeSupportTest`). The cap is a rate, a mole a second. The sustenance dispenser makes a nutriment slurry; the rations printer is a lathe on biomass; the wall heater stands on the floor (gizmos deploy onto floor tiles), always on and heating only. CO2 cracker (1 mol/s cap), temperature regulator, wall heater, water and nutrient synthesizers, sustenance dispenser, rations printer, griddle, range, animal feeder (the entity only; pens come in F9) | Test: cracker converts CO2 to O2 and stops at the cap | M | F2.1 |
| F2.6 Comms, logistics and the time/weather console | **Done** (`PlanetDayCycleTest`). Ten flatpacks on the outfitter: telecom relay (Common and Traffic), intercom, planet broadcast console (5 min a console, 1 min a world, 200 characters, builders only, heard by everyone on that world), time/weather console (the forecast console plus a clock line: time to dusk or dawn and the storm phase now), floodlight post, four-cell charger, recycler, organics printer, ore silo and fabrication silo. The countdown to the Active window itself comes with F8.1; until then night is the window | Test: the forecast API returns time-to-night that matches IsNight flipping | M | F2.1 |

M2 lands at F2.4.

## F3: Ship Access

Module `ShipAccess`. F3.1 to F3.3 are **Done** (see the table at the top). One step is left.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F3.4 Outposts adopt it | **Done** (`OutpostAccessTest`). Listed builders also lay plates and roof panels and build in the claim; per-door chosen cards don't survive a save yet. WFShipAccessComponent on the outpost grid, console owner-only, the Access tab on the outpost console (reuse `ShipAccessScreen`), the owner registered by account in `OwnerUsers` so it survives saves and new bodies, crew members added through Crew mode, company members' outposts start in Faction mode. The allow list is round state today (crew record keys), so the save stores each entry by account and character and re-keys it to that character's crew record when they spawn; door rules already save with the grid | Test: an allow-listed crew member opens doors but not the console, and still does after a save and a reload in a new round | M | F1.3 |

## F4: Outpost spawn

Module `Outposts` (cryopod, Outpost Job, the outpost scenario's server side) plus RoundSetup (the scenario card and
lobby). The original plan's `SpawnOptions` module, lobby panel and play groups are not built: RoundSetup's Crew Setup
already has the lobby, readiness, admission, roles, titles, saved setups, mid-round joining and the refund queue.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F4.1 Cryopod and Outpost Job | **Done** (`OutpostSettlerTest`). The pod is Frontier's `MachineCryoSleepPod` with its own pack; settlers join a gridless `WFOutpostStation` made on the first join, hidden from job pickers like the scenario stations; the join chooser asks the server for the offer with an Outposts network event, so RoundSetup's state is unchanged. Custom job titles can now inherit (`parent:`), and the settler's takes the Contractor's rules. WFOutpostCryopodComponent on a Frontier CryoSleepComponent pod (unpowered spawn, anchored, max 6 per outpost), the WFOutpostSettler job with setPreference: false, JobWFOutpostSettler role loadout, custom title entry, loadout editing in the editor's Outposts tab, and the smallest way to use them: an Outpost choice in the mid-round Join Round chooser for a player who owns a live outpost with a free pod | Test: a late joiner who owns a live outpost appears in its pod as the settler job with their loadout | M | F1.3, F3.4 |
| F4.2 Outpost scenario | **Done** (`OutpostScenarioTest`). `WFRoundScenario.Outpost` in RoundSetup: a Your outpost page where the host picks one of their saves (the Saves panel in choose mode, with its preview), contributions, consent and the refund queue as for ship crews (`WFShipScenarioSystem.Shares`), the price and every share checked before anything loads, drops queued in `WFOutpostSystem.Drops` (read at once, one landing a second, each only once its world has finished preloading, on a random site of open unclaimed ground clear of reserved ground), members wait in the lobby and wake in a free pod of the landed outpost, on the settler station, fallback to Normal (settlers become Contractors) with a refund and a message, `wf.outposts.max_live` (40). Instead of reserving footprints in the PlanetPois placer, a drop lands after the POIs are placed and searches for a clear site; the placer reservation is left to F7.2. The 300 ms load target was not measured | Test: two outpost crews readied pre-round both land in their pods; a third with no money falls back to Normal | L, split F4.2a (scenario and pick) and F4.2b (round-start loads) | F4.1 |
| F4.3 Joinable positions | **Done** (`OutpostCrewTest`). An outpost's crew is a started RoundSetup crew linked to it: the crew whose outpost landed, or one formed when its owner wakes there through the join chooser. `WFOutpostCrewComponent` on the grid keeps the crew's name, description, admission (Public, Requests, Code, Invite, company) and openings through saves; invitations, codes and members are round state. When the outpost lands or its owner wakes there, the openings come back and the name, description and admission fill in where the host left the crew's own unset. In-round edits go through Who can join as for any crew, and members who join mid-round wake in a free pod. A sale drops the crew and unlinks it. The crew's outpost on its card replaces the vessel line | Test: a player joins a live outpost's crew mid-round and spawns in its pod; one outside the crew cannot | S | F4.2 |
| F4.4 Presets | **Done** (`OutpostPresetTest`). Homestead, workshop, farmstead and bastion at fixed prices (12,500 to 36,000); a preset lands as no slot under its own name, onto a fresh outpost from the console or by drop for an Outpost crew; the MapInit path is the existing staging loader (grids load onto an initialised staging map). wfOutpostPreset prototype, the four preset grids in Resources/SharedMaps/_WF/Outposts, MapInit path in the loader, presets in the Save tab and on Your ship, the universal preset test | Test: every preset has a console, a pod, a UTH, fits the cap and is priced at or above appraisal x 1.5 | M | F4.2, F2.4 |

M3 lands at F4.2.

## F5: Play groups and Shuttle Crash

Module RoundSetup. F5.1 to F5.3 are **Done** as Crew Setup and the Crashlanded scenario. What the crash start
lacked against the design doc was one step, now done too:

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F5.4 Crash follow-ups | **Done** (`CrashSitesTest`). The Carcinoma opt-in is an Allow Carcinoma box on the Crashlanded card, carried on the lobby; Carcinoma is unsanctioned, so the opt-in stands in for the sanction. Gravity is read live off the ground layer. Clearances are 32 m from claim zones and 64 m from other crews' crash hulls, over 24 tries; a hull with no clear spot waits at the last one and the F6.1 shift keeps it off claims. The sale baseline is `WFSaleBaselineComponent` in Shipyard, on the grid-appraisal hook, so it also lowers a chop-shop price. Fixed on the way: a cardless leader was given the abstract `IDCardStandard` (now `PassengerIDCard`), and the Outposts saves panel threw when the lobby closed after client shutdown. World filter (sanctioned only, over 3 g excluded, Carcinoma opt-in), hulls from a `wfCrashPool` prototype instead of the fixed list, the hull's deed to the leader after the crew spawns, a sale baseline on the hull, spawn distance from claim zones and other crash sites | Test: a crash never picks an excluded world and never lands inside a claim zone | S | F1.2 |

## F6: Landing guard and pads

Module `Outposts`, hooks in Planets.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F6.1 Landing guard | **Done** (`OutpostLandingTest`). The claim covers the owner too; pads (F6.2) are how an owner lands on their own outpost. "Transit exit refused" is the set-down hold, next to the obstacle hold in `CEZLevelsSystem.WFLandingRefusal.cs`; the lift-lost shift lives in the z-levels flight partials (`WFLandingClaims`), not `WFLandingSurveySystem`, since claims are server-only. Crash spawns and orbit decay still drop over claims; the shift moves them off before touchdown. Enter Atmosphere refused over a claim zone with the owner named (next to the existing refusal outside a world's circle), transit exit refused over one, a lift-lost hull shifted to the nearest clear ground before touchdown (in `WFLandingSurveySystem`) | Test: a hull dropped over an outpost lands next to it, not on it | S | F1.2 |
| F6.2 Landing pads | **Done** (`OutpostPadTest`). The descent and launch are planet hops with an exact pose (`WFHopExact`) that spare the pad's grid from the touchdown crush, raised from Enter Atmosphere and Lift Off through `WFOrbitDescentAttemptEvent` and `WFLiftoffDivertEvent`. A parked hull is static and can't be repacked under. Pad access is the outpost's Access-tab allow list plus a per-pad Public toggle (a verb for builders), not a new column on the access panel. An unpowered pad refuses to launch rather than freeing the hull over the outpost. The Tradepost got a 13x13 apron south of its road with an unpowered public pad, and a public hub on its forecourt that stands in only when an outpost's own hubs aren't Ready. WFLandingPadComponent marker, pad area (5x5 to 21x21) on the outpost grid, orbit capture within 32 m for pilots with access, fit check, scripted descent and launch (lift ignored), WFPadDockedComponent (static while parked, touchdown skips the Smimsh crush against the outpost), pad access from the access panel, 5 kW while guiding, pad markers on orbit radar, the Tradepost's public pad (any pilot) and public UTH (an owner's orders may drop there) from F7.4 | Test: a lift-less hull descends to the pad, sits without crushing it, launches back to orbit | L, split F6.2a (capture and descent) and F6.2b (dock, launch, access, radar) | F6.1, F3.4 |

## F7: Bounded worlds and POIs

Module PlanetPois. F7.1 is **Done** (`WFPlanetBoundsSystem`).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F7.2 POI types and beacons | **Done** (`PlanetPoiTypeTest`). `wfPoiType` (name, claim: None, Free, Repair or Hack, surface only) on table entries and on a table's wrecks, kept on the placed site. Mine (Merak's mineshaft, Aerumna's mine; surface only), Abandoned outpost (Merak's salvage outpost, the bandit hideout) and Small derelict (every world's wrecks). The beacons are the existing site signals on the orbit layer, "Unknown signal" until someone on the ground comes within 50 m. Each world keeps three outpost lots (`outpostLots`, `outpostLotSize` 48): squares placed after the sites and before the roads, laid with natural ground and pinned, so roads, wrecks, camps and growth keep off them; they are not reserved ground, and drops land on a free lot before searching at random | Test: a world rolls its table's POI count, none inside a reserved footprint, each with a beacon | M | F4.2 |
| F7.3 Claiming | **Done** (`OutpostPoiClaimTest`). Every claimable site gets a site terminal (`WFPoiClaimTerminal`) on a free floor tile nearest its middle once its world has preloaded: broken for a Repair site (weld it), locked for a Hack site (a multitool, 10 s). Using it claims the site for free when nothing alive in it would fight the claimant, the claimant has no live outpost, and fewer than half of the world's claimable sites are held. A wreck becomes the outpost in place; a site built into the ground is lifted onto its own grid (its non-natural floor, roofs and everything anchored on it) and the ground under it carved. The terminal makes way for an outpost console, the outpost is named after the site, and its value then is stored as `ClaimBaseline` (saved with it) and taken off every sale. A claimed site stops being reserved ground | Test: claim an abandoned outpost, save it, sale pays only what was added | S | F7.2, F1.6 |
| F7.4 The rest of the taxonomy | **Done** (`PlanetPoiTaxonomyTest`, `PlanetPoiTypeTest`, `PlanetPoiContentTest`). Types carry a `beacon` (Unknown, Known, Faint), a `revealRange` and `spawns`. Automated outpost: new grid `automated_outpost.yml` (fenced yard, four `WFAds` turrets marked `WFPoiCoreGuard`, `WFPoiAutomatedCore` in a roofed bunker), claim Free; a multitool on the core (15 s) takes the turrets in 24 m off their faction. Tradepost: the gas station with its fuel trader, beacon Known, a `PacifiedZone20` from its spawns; its public pad and public UTH came with F6.2. Derelict capital: a table's `capitals` (Large vessels, Repair) on Merak, Fervidus, Thrascias and Aerumna. Farm: the greenhouse (Repair). Workshop: the mechanic's garage, not claimable since its mechanic trades there, spawns a homestead recipe disk (`WFOutpostRecipeDiskWorkshop`); the T2/T3 disks stay F10.5. Ruins: the ancient temple with two `WFPoiClueNotes`, each revealing an unfound site on the world. Heavenly shrine: the fountain of life. Alien artifact site: new grid `alien_artifact_site.yml` (sandbagged pit round an artifact, no trail). Both are Faint: off radars and the planet map until someone is within 12 m or sweeps with the `WFPoiSurveyScanner` (200 m, at the outfitter) | Each type has a grid, a test that it loads, and a beacon behaviour | L across several PRs, one or two types each | F7.3 |
| F7.5 Caverns interplay | **Done** (`OutpostCavernTest`). The biome loader lays `WFOutpostFoundationRock` (indestructible) on every cavern tile under a claim as the chunk loads, so it unloads and comes back like any rock; cavern already loaded or pinned under a new claim keeps what it has until it unloads, so nobody is entombed. `WFCavernOpenAttemptEvent` keeps the ground over a claim shut: a mouth waits, a hole takes its natural tile back, cavern stairs and shovel shafts are refused. Placement, POI claims and saves were already ground-only (an outpost is its own grid, a POI claim lifts only ground tiles); the test holds the save's contents the same once the cavern under it loads. Tunnellers are step 3 packs (`tunnel: true`, `WFMobRaidTunneller`) on every table, coming up 2 tiles outside the claim; with no cavern they walk in. Cavern chunks under a claim zone generate as unmineable foundation rock, no cavern mouth inside a claim zone, clearance and placement ignore depths below 0, Mine POI claims cover the surface grid only, nothing below depth 0 is saved, the raid director gets a "tunneller" arrival that surfaces at the claim edge (reusing the tunnel raiders) | Test: a cavern chunk under a claim zone is solid rock; an outpost above an existing tunnel loads and its save is unchanged | M | F7.2 |

## F8: Outpost attacks

Module `OutpostRaids`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F8.1 Active state and warning | **Done** (`RaidWindowTest`). `wfRaidWindow` gives a world's hours (dusk to dawn by default) and whether storms open it; the forecast and outpost consoles count down to it. The mast and long-range mast warn 120 s and 240 s ahead with ship code Red; without one, a popup 15 s ahead. A raid is scheduled through `WFRaidSystem.ScheduleRaid` and raises `WFRaidArrivedEvent` on landing.) | Test: the Active window opens at 18:00 local and the EWS fires 120 s before a scripted raid | S | F1.2, F2.6 |
| F8.2 Director and wildlife raids | **Done** (`RaidDirectorTest`), behind `wf.outposts.raids` (off). Attention is a point a minute while someone is home, scaled by floor tiles, and saved with the outpost. Gates: window, grace (15 min), one raid a window, 4 raids and 40 mobs sector-wide. Tables for the default world, Merak, Thrascias, Fervidus and Carcinoma, steps 0 and 1. The terrain loader is `WFTerrainLoaderComponent` in Planets. Turrets and perimeter mines are on the `WFOutpostDefence` faction.) | Test: with someone home and the window open, a step-1 pack spawns, walks to the claim edge and despawns at dawn | M | F8.1 |
| F8.3 Humanoid raiders | **Done** (`RaidBreachTest`; loot in `OutpostSaveLoadTest`). `WFRaidBreachOperator` runs the F0.1 walk-up breach in `WFRaidBreachSystem`: walk up, melee the blocker, step onto the broken tile, break inner walls on the way, and restart steering whenever a target changes, since a plain re-register keeps a lost path. Step 3 is scavengers, step 4 a party with a breacher arriving on a cosmetic quad (`WFRaidPack.arrival`). One faction, `WFOutpostRaiders`, hostile to NanoTrasen, PetsNT and `WFOutpostDefence` only, so raiders leave wildlife alone. Raid loot is kept out the way mobs and cash are: an outpost save writes it and the load strips it, because an unsaved entity leaves an invalid entry in its container. A beaten raid now moves the outpost to the table's next step, so the missing step 2 doesn't stall the ladder | Test: a raider party breaks a prefab wall, enters, and its loot is not in the next save | M | F8.2 |
| F8.4 Burrows, shuttles, siege | **Done** (`RaidArrivalTest`). The step 2 burrow is a `FaunaBurrow` marked `WFRaidBurrowComponent` rather than a TimedSpawner, because a TimedSpawner can't hand its spawns to the raid; the director adopts the burrow's `Spawned` list each pass, and a raid with a burrow standing isn't over. The step 5 shuttle (`tide.yml`, `WFRaidShuttleComponent`) is set down on the ground beside the walk-in spot, outward from the claim, refused if it would touch another grid or leave the world (the raid then walks in), and held like an outpost; it isn't flown down from orbit, which waits on F10.3's descent jammer. Step 6 is the same party in three waves 150 s apart. The raid MoveTo keeps its target key on arrival, since clearing it raced a plan made before it was cleared. Walk-in spots and the claim-edge point now need open ground round them, judged on unloaded ground by the biome, which ended the raid tests' NoPath flake | Test: a burrow keeps spawning until destroyed; a raid shuttle lands outside the claim zone | M | F8.3, F6.1 |

M4 lands at F8.3.

## F9: Farming

Module `Husbandry`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F9.1 Pens, feeders, livestock records | **Done** (`LivestockSaveTest`). A pen is the floor flooded from a feeder up to walls, fences, closed doors or other feeders, within 100 tiles and without running off the grid; feeders in a pen add up. Animals that lay, give milk or grow wool standing in an outpost's pens become livestock every 5 s (Passive faction). The outpost raises `WFOutpostSavingEvent`/`SavedEvent`/`SettledEvent`/`AppraiseEvent`/`FingerprintEvent`; Husbandry writes `WFLivestockRecordsComponent` onto the grid only while it is serialised (prototype, name if renamed, age, egg/milk/wool time left, feeder tile, price) and spawns the records in the feeder's pen once the save lands. Cooldowns are set through partials on EggLayer, Udder and Wooly systems. WFAnimalFeederComponent (from the F2.5 entity), pen detection (enclosed area with a feeder), 6 per feeder, WFLivestockComponent, passive faction, livestock records in the save (prototype, name, age, cooldowns) and respawn at feeders on load | Test: two chickens in a pen are in the record, come back on load, and the appraisal counts them | M | F1.4 |
| F9.2 Capture, taming, herding | **Done** (`HerdingTest`). The lasso is a distance joint drawn with JointVisuals, not a Tether rope: the rope core pulls on the grid under a kinematic body, and mobs are kinematic. A loop is a 3 s do-after; 50 kg and up takes two. Taming is any food used by hand on a `WFTameableComponent` animal (2 s each), 3 inside 5 minutes. Livestock gets wildlife `Protected`, loses its `FaunaLeash`, and runs `WFLivestockCompound`, which follows the blackboard's FollowTarget when the whistle set one and idles otherwise; the whistle's three commands are verbs, and a blow repeats the last. Lasso and whistle are at the outpost outfitter. Lasso (WF rope leash, 3 s struggle, big animals take two), taming by feeding 3 times in 5 minutes, protected flag so wildlife never retires them, herding whistle (Follow / Stay / Go to pen on HTN follow) | Test: lasso, tame, whistle to pen | M | F9.1 |
| F9.3 Native livestock | **Done** (`NativeLivestockTest`). Seven natives built on the upstream goat, cow and chicken (`WFNativeLivestock`: BreathingImmunity, WFTameable; own Temperature, offspring and a hide), added to Asclepiu, Verda Meadow, Merak Flats and Oasis, Thrascias and its Tundra, Fervidus and its Ashfields, Aerumna Forest and Carcinoma (which the Marsh nests). Wild ones keep their parent's faction. Every 5 s, livestock not in an outpost pen while its world's Active window is open goes into `WFLivestockPrey` (no relations, so default-hostile factions hunt it; outpost defence and raiders don't); penned or by day it is Passive. Per-world livestock variants in each fauna table with Udder, EggLayer, Wooly and hide, predators take unpenned animals in Active windows | Test: a world's fauna table contains at least one tameable species | S | F9.2, F8.2 |

## F10: Late

Each is its own PR or small series; order by demand. F10.1 (atmospheric jetpack) is **Done**.

| Step | Ships | Needs |
|---|---|---|
| F10.2 Parts press, power exporter, mining bots | **Done** (`OutpostIndustryTest`), module `OutpostIndustry`. The home gate is the raid director's `SomeoneHome`: a producer runs anchored on an outpost, powered, with someone home, and while the outpost's producers together are under `wf.outposts.producer_cap` (4000 spesos of value an hour, round state). The parts press makes a 150-speso box of pressed parts from 5 steel and 2 plastic sheets a minute; the power exporter draws 30 kW for 0.5 spesos a second of saved credit that a builder cashes out; the mining bot dock keeps two bots (`WFMiningBotOperator`) that drill ore-bearing rock within 24 m by day and bring the ore into the dock's storage, resting in the Active window. All three are at the outpost outfitter. The producers with their home gate and caps, bot HTN and dock | F2.2, F8.2 |
| F10.3 Orbital defence | **Done** (`OrbitalDefenceTest`), in `OutpostRaids`. A step 5 shuttle raid on an outpost with a powered sensor uplink or descent jammer now descends from orbit: three salvos `wf.outposts.descent_salvo_interval` (10 s) apart, two more under a jammer, during which the raid counts as on. While an uplink tracks it, each powered flak battery (15) and orbital defence battery (40) fires at every salvo on its 100 hull; shot down, it never lands and its first wave is lost, which beats a one-wave raid. Without either gizmo it lands at once as in F8.4. The batteries have no target but a descending raid shuttle, pending Q1. Sensor uplink, flak battery, orbital defence battery, descent jammer, all against WFRaidShuttleComponent only until Q1 is answered | F8.4 |
| F10.4 Basements | **Done** (`OutpostBasementTest`). A basement hatch kit (outfitter, 1500) on an outpost floor tile with floor all round digs a 3x3 room on the outpost's depth -1 grid on the cavern, lined up with the outpost, opens the tile over a CE ladder and binds the grids with a CE connector pillar; foundation plates against the basement's rock dig it further within the claim. Caps, appraisal and autosave fingerprint cover both grids. Saves keep the basement as its own grid under `wfDepths` with its depth; a load puts it back under the outpost, or floors the hatches over without a cavern. The caverns' hole queue leaves alone ground and cavern tiles under another grid's floor, which also stops it refilling the ground carved from under every outpost. An outpost as a set of grids, one per depth, bound by CE grid connectors like a multi-deck ship; the save format gains a depth per grid; a dug-down hatch in the outpost floor becomes a CE ladder onto the owner's depth -1 grid; caps apply to the set | F7.5, F1.4 |
| F10.5 Deposit terminal, broadcast console extras, T2/T3 disk drops in Workshops | **Done** (`OutpostLogisticsTest`, `OutpostFabricatorTest.WorkshopDisksTeachTheirBoards`). The deposit terminal is a bank ATM gizmo (5% fee) at the outfitter that never pays out: `WFDepositOnlyComponent` cancels withdrawal messages and its window hides them. The broadcast console can add its position, and while its outpost is raided it offers a call for help: red, to everyone in the sector, past the broadcast cooldowns, once in 10 minutes. Workshops leave the homestead disk 60%, a T2 industrial disk 30% and a T3 advanced disk 10% | F2.6, F7.4 |

## Rules of thumb while executing

- When a step grows past L, stop and split it, even if the split ships a stub UI. A stub Save tab that only saves is
  better than one giant PR.
- Do not start F4 before F1.4 is in. Spawn at Outpost is where dupes and refunds go wrong; hardening first.
- Reuse before adding. Access is ShipAccess, lobbies and crews are RoundSetup, bounds and landing are Planets, site
  placement is PlanetPois, previews are ShipPreview. A step that needs one of them to behave differently changes it
  there.
- The parallel track is F3.4, F5.4 and F6.1: small steps that touch ShipAccess, RoundSetup and Planets, not the
  outpost spine.
- Every step re-runs the price tests. Kit, packs, listings, recipes and presets all sit under the same "price at or
  above appraisal" test; a new gizmo that breaks it does not merge.
- Outposts only exist at depth 0 until F10.4; F7.5 is a generator change in Caverns, not an outpost change.
