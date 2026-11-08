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
| F5.2 Shuttle Crash, solo | RoundSetup Crashlanded (`WFCrashScenarioSystem`) | Twelve fixed civilian hulls, any planet, three-minute countdown, `TryDropFromOrbit`; no `wfCrashPool`, no world filter, no deed on the hull, no spawn distance from outposts |
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
2. F1.1, with F0.3 alongside it
3. F1.2
4. F1.3
5. F1.4, F1.5, F3.4 in parallel
6. F2.1, F2.2, F2.3, F2.4 in order, in parallel with F6.1 and F5.4 (crash follow-ups)
7. F1.6 (sale)
8. F4.1, F4.2, F4.3 in order, in parallel with F2.5, F2.6
9. F7.2, then F7.3
10. F8.1, F8.2, F8.3 in order, in parallel with F7.4
11. F4.4 (presets), F6.2 (pads), F7.5, F9.1, F9.2, F9.3
12. F10.x as wanted

## F0: Spikes

No player-facing change. Each spike is a branch with a test or a benchmark and a paragraph of results pasted into
the design doc's open questions. Merge the tests where they are cheap to keep.

| Step | What | Done when | Size |
|---|---|---|---|
| F0.1 Breach spike | **Done** (`OutpostBreachSpikeTest`, `SPIKES.md`). A separate grid can be raided: the outpost needs a ring of pathfinding portals to the ground, and the breach is walk up, attack the wall as a target, walk in. Steering's own wall smashing doesn't work on main and isn't used | | |
| F0.2 Save benchmark | **Done** (`OutpostSaveBenchmarkTest`, `SPIKES.md`). About 0.21 ms an entity to save and 0.16 ms to load, on the main thread; 97 KiB packed at 4,000 entities. Entity cap 2,000 for v1 | | |
| F0.3 Roof and outdoors (open) | RoofComponent on a ground grid instead of ImplicitRoofComponent, walls with IsRoof, a test roof panel, IsRooved per tile, weather overlay and thunder behaviour on and off roof | A test that a roofed tile is rooved and an open one is not, screenshot of the overlay | S |
| F0.4 Client YAML load | **Done.** `ShipPreviewSystem.TryLoadText` loads a grid from a YAML string on the client, and the mapping tools already run it on real clients | | |

## F1: Outpost grid, save, load, sale

Module `Outposts`. The spine; everything else hangs off it.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F1.1 Console and grid | WFOutpostComponent, WFOutpostConsoleComponent, the console entity (bought at WF traders for 6,000, no kit yet), deploy on natural ground founds a 3x3 grid with ForceAnchor + PreventGridAnchorChanges + ShuttleSystem.Disable, ownership by NetUserId (as ShipAccess's `OwnerUsers`), one live outpost per player, name (30 chars), CleanupImmune, destroying the console unanchors, the Overview tab with name and owner, the pathfinding portal ring (each edge tile joined to the ground tile outside it, removed when the grid moves or goes), cvar wf.outposts.enabled | Test: found, name, second console refused, destroy frees the slot and the grid moves; an NPC paths from the ground onto the outpost and back | M | none |
| F1.2 Foundation and ground | Foundation plates (own item, own AfterInteract, extend the grid, carve and reserve the ground tile, move the portal ring to the new edge), ground carving on found, RoofComponent and the roof panel from F0.3, claim zone (bounding box + 8) that blocks other founders and raw-ground building by non-crew and keeps clear of PlanetPois footprints, roads and cavern mouths, size cap counters on the Overview tab, weather strikes and fauna spawns skip grid-covered tiles (edits in the Planets modules), Carcinoma exemption | Test: plates extend the grid and not the ground map, claim zone refuses a second founder, thunder never lands under a grid | M | F1.1, F0.3 |
| F1.3 Save and load | Migration WolfgateOutposts and wf_outpost_save, IServerDbManager API (writes queued through `WFUserWriteQueue` so they never overlap a character save), profile id lookup, the save predicate v1 (players, mobs, cash, deeds, ghost roles), appraisal with the predicate plus plate value, Save tab (save to 3 slots, delete, list with price), staging map load, strip on the copy, clearance v1 (other grids' tiles, claim zones, stale state removed), price = appraisal x 1.5 via session TryBankWithdraw, 30 s overlay with cancel and refund, re-check, clear ground, swap, ReinitLoadedShip + outpost re-init (the portal ring included), "in use this round", saves stored with gzip SmallestSize | Test: round trip of a small outpost with a locker of items, battery charge and a door; strip list gone; price charged once; blocked load refunds | L, split into F1.3a (DB + save + Save tab) and F1.3b (load pipeline) if it passes 1,500 lines | F1.2, F0.2 |
| F1.4 Save hardening | Autosave (10 min, skip when unchanged, staggered so no two outposts save in the same second), final autosave at round end, size cap refusal (2,000 entities, from F0.2) with the "what is big" list, lineage id on every save, save stamps (WFSaveOriginComponent; deposit box and Mono persistent items refuse them), character editor Outposts tab (metadata list, delete), admin commands (wfoutpost list/restore/refund/delete, admin log types) | Test: autosave skips an unchanged outpost; a stamped item is refused by the deposit box | M | F1.3 |
| F1.5 Preview | Server decompresses and sends one save's YAML to its owner on request, rate limited; the client shows it with `ShipPreviewControl.SetGridText` (no new loader); preview in the Save tab and the editor tab; preset grids preview too | Real client log clean of Sandbox violation; preview renders a saved outpost | S | F1.4 |
| F1.6 Sale | Sell tab: sell to the game at the sale predicate's value (structures and machines, not goods), shuttle-style checks, "sell at round end" toggle with the wf_outpost_payout table for offline owners, sell to a player standing at the console (price, accept, transfer), any sale voids the lineage, Abandoned state with owner-only re-founding | Test: sale pays the predicate value, voids every save of the lineage, buyer owns the grid | M | F1.4 |

M1 lands at F1.3. Between F1.3 and F2, an outpost is built with stock walls, stock generators and whatever the owner
hauls down by ship. That is fine; it is a persistent base already.

## F2: Kit, fabricator, gizmos, trade

Module `Outposts` (gizmos may get a sub-folder per category).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F2.1 Deploy and repack framework | WFDeployableComponent (use in hand on the tile in front, 2 to 4 s, only on your own foundation, console excepted), repack verb (owner and Builders, 3 s, the machine itself goes into the pack's container so all state survives), the no-board rule and its test (no Machine, MachineBoard or Construction component on an outpost gizmo), StaticPrice = print cost rule and its test, the console and UTH converted to it | Test: deploy, use, repack, redeploy keeps battery charge and contents | M | F1.2 |
| F2.2 Fabricator and construction pack | Outpost Fabricator (LatheComponent with a static T1 recipe pack, slow, no upgrades, prints another fabricator but never a console), prefab wall panel, prefab window, roof panel, manual door kit, prefab airlock kit, shutters kit, foundation plate recipe, powered driver, arc welder, the stock parts/boards/electronics pack, recipe disks for T2/T3 (the gate only; disks come later) | Test: every T1 recipe prints and its product's StaticPrice is at or above its material cost | M | F2.1 |
| F2.3 Power pack | Wind turbine (outdoors, 5 kPa, storm bonus in windy weather), deployable solar strings with the day/night scaling system (reads `IsNight`), small and large stationary batteries, compact RTG, A.W. fuel generator, the 0.6 scale rule | Test: turbine makes power outdoors and not under a roof; solar output follows IsNight | M | F2.1 |
| F2.4 Kit, UTH and trade panel | WFUniversalTradeHubComponent (outdoors, 1 kW), Trade tab with the catalogue as cargo-product listings in an outpost group, owner pays with the cargo tax split, 5 in flight, parachute drop from the top air layer onto the UTH with the blocked-pad fallback, the Planet Outpost Kit at the shipyard outfitters and WF traders, add-on packs, the price >= appraisal test over kit, packs and listings | Test: an order lands on the UTH within 5 tiles; kit price is at or above its deployed appraisal | M | F2.2, F2.3 |
| F2.5 Atmos and kitchen pack | CO2 cracker (1 mol/s cap), temperature regulator, wall heater, water and nutrient synthesizers, sustenance dispenser, rations printer, griddle, range, animal feeder (the entity only; pens come in F9) | Test: cracker converts CO2 to O2 and stops at the cap | M | F2.1 |
| F2.6 Comms, logistics and the time/weather console | Telecom relay flatpack, intercom, planet broadcast console with its rate limits, time/weather console on top of the existing forecast (`TryGetReport`, `TryGetSchedule`, the `WFComputerWeatherForecast` UI) plus what is new: time to night and to the Active window, storm phase; floodlight post, multi-cell charger, materials recycler, organics printer, ore silo flatpack, fabrication silo flatpack | Test: the forecast API returns time-to-night that matches IsNight flipping | M | F2.1 |

M2 lands at F2.4.

## F3: Ship Access

Module `ShipAccess`. F3.1 to F3.3 are **Done** (see the table at the top). One step is left.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F3.4 Outposts adopt it | WFShipAccessComponent on the outpost grid, console owner-only, the Access tab on the outpost console (reuse `ShipAccessScreen`), the owner registered by account in `OwnerUsers` so it survives saves and new bodies, crew members added through Crew mode, company members' outposts start in Faction mode. The allow list is round state today (crew record keys), so the save stores each entry by account and character and re-keys it to that character's crew record when they spawn; door rules already save with the grid | Test: an allow-listed crew member opens doors but not the console, and still does after a save and a reload in a new round | M | F1.3 |

## F4: Outpost spawn

Module `Outposts` (cryopod, Outpost Job, the outpost scenario's server side) plus RoundSetup (the scenario card and
lobby). The original plan's `SpawnOptions` module, lobby panel and play groups are not built: RoundSetup's Crew Setup
already has the lobby, readiness, admission, roles, titles, saved setups, mid-round joining and the refund queue.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F4.1 Cryopod and Outpost Job | WFOutpostCryopodComponent on a Frontier CryoSleepComponent pod (unpowered spawn, anchored, max 6 per outpost), the WFOutpostSettler job with setPreference: false, JobWFOutpostSettler role loadout, custom title entry, loadout editing in the editor's Outposts tab, and the smallest way to use them: an Outpost choice in the mid-round Join Round chooser for a player who owns a live outpost with a free pod | Test: a late joiner who owns a live outpost appears in its pod as the settler job with their loadout | M | F1.3, F3.4 |
| F4.2 Outpost scenario | `WFRoundScenario.Outpost` in RoundSetup: the leader's saved outpost takes the place of the vessel on Your ship (with its preview), contributions and the refund queue as for ship crews, the price check before any grid load, footprint reservation in the PlanetPois placer before sites are placed, queued round-start loads (one grid a second; a capped outpost is a 300 ms load), members wait in the lobby until the grid lands, fallback to Normal with a refund and a message, the live-outpost cap cvar | Test: two outpost crews readied pre-round both land in their pods; a third with no money falls back to Normal | L, split F4.2a (scenario and pick) and F4.2b (round-start loads) | F4.1 |
| F4.3 Joinable positions | The outpost's crew roles and admission (Public, Requests, Code, Invite, company) saved with the outpost and restored as its crew when it loads, in-round edits through Who can join, mid-round joiners spawn in a free pod | Test: a player joins a live outpost's crew mid-round and spawns in its pod; one outside the crew cannot | S | F4.2 |
| F4.4 Presets | wfOutpostPreset prototype, the four preset grids in Resources/SharedMaps/_WF/Outposts, MapInit path in the loader, presets in the Save tab and on Your ship, the universal preset test | Test: every preset has a console, a pod, a UTH, fits the cap and is priced at or above appraisal x 1.5 | M | F4.2, F2.4 |

M3 lands at F4.2.

## F5: Play groups and Shuttle Crash

Module RoundSetup. F5.1 to F5.3 are **Done** as Crew Setup and the Crashlanded scenario. What the crash start still
lacks against the design doc is one step:

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F5.4 Crash follow-ups | World filter (sanctioned only, over 3 g excluded, Carcinoma opt-in), hulls from a `wfCrashPool` prototype instead of the fixed list, the hull's deed to the leader after the crew spawns, a sale baseline on the hull, spawn distance from claim zones and other crash sites | Test: a crash never picks an excluded world and never lands inside a claim zone | S | F1.2 |

## F6: Landing guard and pads

Module `Outposts`, hooks in Planets.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F6.1 Landing guard | Enter Atmosphere refused over a claim zone with the owner named (next to the existing refusal outside a world's circle), transit exit refused over one, a lift-lost hull shifted to the nearest clear ground before touchdown (in `WFLandingSurveySystem`) | Test: a hull dropped over an outpost lands next to it, not on it | S | F1.2 |
| F6.2 Landing pads | WFLandingPadComponent marker, pad area (5x5 to 21x21) on the outpost grid, orbit capture within 32 m for pilots with access, fit check, scripted descent and launch (lift ignored), WFPadDockedComponent (static while parked, touchdown skips the Smimsh crush against the outpost), pad access from the access panel, 5 kW while guiding, pad markers on orbit radar | Test: a lift-less hull descends to the pad, sits without crushing it, launches back to orbit | L, split F6.2a (capture and descent) and F6.2b (dock, launch, access, radar) | F6.1, F3.4 |

## F7: Bounded worlds and POIs

Module PlanetPois. F7.1 is **Done** (`WFPlanetBoundsSystem`).

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F7.2 POI types and beacons | wfPoiType (claim behaviour per site) on the existing `wfPlanetPoiTable` entries, outpost footprints reserved in the existing placer, orbit beacons on top of PlanetSignals starting as "Unknown signal" and revealed at 50 m, the first three types from existing grids: Mine (surface part), Abandoned outpost, Small derelict | Test: a world rolls its table's POI count, none inside a reserved footprint, each with a beacon | M | F4.2 |
| F7.3 Claiming | Claim on a POI console (no hostiles, repaired or hacked as the type says), free, claim baseline stored, half-of-world cap, claimed POIs save and sell like any outpost | Test: claim an abandoned outpost, save it, sale pays only what was added | S | F7.2, F1.6 |
| F7.4 The rest of the taxonomy | Automated outpost (turrets, hackable core), Tradepost (WF Traders, public pad, public UTH, safe zone), Derelict capital ship, Farm, Workshop (recipe disks drop here), Ruins with clue items, Heavenly shrine, Alien artifact site, faint signals, the survey tool | Each type has a grid, a test that it loads, and a beacon behaviour | L across several PRs, one or two types each | F7.3 |
| F7.5 Caverns interplay | Cavern chunks under a claim zone generate as unmineable foundation rock, no cavern mouth inside a claim zone, clearance and placement ignore depths below 0, Mine POI claims cover the surface grid only, nothing below depth 0 is saved, the raid director gets a "tunneller" arrival that surfaces at the claim edge (reusing the tunnel raiders) | Test: a cavern chunk under a claim zone is solid rock; an outpost above an existing tunnel loads and its save is unchanged | M | F7.2 |

## F8: Outpost attacks

Module `OutpostRaids`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F8.1 Active state and warning | Per-world Active window from IsNight with prototype overrides, storm stacking, the Overview and time/weather console countdowns, WFEarlyWarningComponent and the mast (120 s, 240 s long-range), PA code Red via ShipPa, the 15 s popup without one | Test: the Active window opens at 18:00 local and the EWS fires 120 s before a scripted raid | S | F1.2, F2.6 |
| F8.2 Director and wildlife raids | WFRaidDirectorSystem (attention, threat points, gates, caps, grace, one raid per window), wfRaidTier and wfRaidTable, ladder steps 0 and 1 (wildlife), walk-in spawner (40 to 60 tiles out, 24 from players, inside bounds, away from POIs and crash sites), SleepPlayerCheckRangeOverride on raid mobs, the raid chunk loader in Planets, director despawn at dawn and by leash, turrets and mines hostile to raider factions and hostile wildlife, turret cap | Test: with someone home and the window open, a step-1 pack spawns, walks to the claim edge and despawns at dawn | M | F8.1 |
| F8.3 Humanoid raiders | The breach HTN operator from F0.1 (walk the ground to outside the nearest edge tile, melee the blocking wall or door as a target until the tile is clear, walk in; restart steering when its smash leaves no path), steps 3 and 4 (scavengers, raider party with a breacher), raider factions, mounted-cosmetic arrival, WFRaidLootComponent (CargoSellBlacklist, not saved, not paid for), breacher targets turrets first, console never targeted | Test: a raider party breaks a prefab wall, enters, and its loot is not in the next save | M | F8.2 |
| F8.4 Burrows, shuttles, siege | WFRaidBurrowComponent with TimedSpawner (step 2), the raider shuttle (WFRaidShuttleComponent, drop from orbit outside the claim zone, leaves at dawn) (step 5), siege (step 6) | Test: a burrow keeps spawning until destroyed; a raid shuttle lands outside the claim zone | M | F8.3, F6.1 |

M4 lands at F8.3.

## F9: Farming

Module `Husbandry`.

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| F9.1 Pens, feeders, livestock records | WFAnimalFeederComponent (from the F2.5 entity), pen detection (enclosed area with a feeder), 6 per feeder, WFLivestockComponent, passive faction, livestock records in the save (prototype, name, age, cooldowns) and respawn at feeders on load | Test: two chickens in a pen are in the record, come back on load, and the appraisal counts them | M | F1.4 |
| F9.2 Capture, taming, herding | Lasso (WF rope leash, 3 s struggle, big animals take two), taming by feeding 3 times in 5 minutes, protected flag so wildlife never retires them, herding whistle (Follow / Stay / Go to pen on HTN follow) | Test: lasso, tame, whistle to pen | M | F9.1 |
| F9.3 Native livestock | Per-world livestock variants in each fauna table with Udder, EggLayer, Wooly and hide, predators take unpenned animals in Active windows | Test: a world's fauna table contains at least one tameable species | S | F9.2, F8.2 |

## F10: Late

Each is its own PR or small series; order by demand. F10.1 (atmospheric jetpack) is **Done**.

| Step | Ships | Needs |
|---|---|---|
| F10.2 Parts press, power exporter, mining bots | The producers with their home gate and caps, bot HTN and dock | F2.2, F8.2 |
| F10.3 Orbital defence | Sensor uplink, flak battery, orbital defence battery, descent jammer, all against WFRaidShuttleComponent only until Q1 is answered | F8.4 |
| F10.4 Basements | An outpost as a set of grids, one per depth, bound by CE grid connectors like a multi-deck ship; the save format gains a depth per grid; a dug-down hatch in the outpost floor becomes a CE ladder onto the owner's depth -1 grid; caps apply to the set | F7.5, F1.4 |
| F10.5 Deposit terminal, broadcast console extras, T2/T3 disk drops in Workshops | | F2.6, F7.4 |

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
