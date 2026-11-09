# World development plan

The design of the `Colonies` module. The module's README (`Content.Server/_WF/Colonies/README.md`) says what is built
and where; this doc says why, and the step order it was built in.

## The problem

An outpost today gives its owner a base that comes back next round, a cryopod to wake in and a free POI claim. That is
convenience, not a goal. Nothing a player does at an outpost changes anything outside it, so there is no reason to
keep one running, grow it or defend it once it stands.

This plan gives outposts a place in the loops the game already runs: the sector economy, hauling, mining, the
campaigns, contracts, expeditions, companies and the drydock. A world that players develop becomes a station in the
sector economy. It needs haulers, draws raiders, and at the top lets one company build ships in its orbit.

## Principles

- **Plug into existing loops; don't build beside them.** A colony is a station in the TradeGoods ledger, its threats
  come from the raid director and the campaigns, its jobs go on the Contracts board, its ships go into the company
  Hangar. The overlap check lists every idea cut or changed for that reason.
- **No new goods.** Colonies make and use the ledger's existing goods. The yard builds from Ship Components and Metals.
- **Land and space are different jobs.** The ground extracts, grows, refines and houses people. Orbit builds ships.
  Ships are built in orbit only.
- **A world has one colony, shared.** Every outpost on a world develops the same colony, whoever owns it. Holders
  compete for a share of it, not for separate colonies.
- **Nothing is permanent.** Development decays. A world nobody plays drifts back to open.

## How it ties in

| Loop | What a colony adds to it | What it takes from it |
|---|---|---|
| Hauling (TradeGoods) | A new station on each developed world, short of Supplies and Medical, long on its own goods | Haulers are its fast import channel |
| CargoLoad and planet lift | A reason to land heavy: an outpost pad ignores lift, so a loaded hauler can land there and nowhere else on the world | |
| Mining | Planet ore delivered to the colony raises its Refining and Chemicals output, as materials in a station's cargo market do | |
| Fission Fuel | The sector's only domestic maker: uranium worlds extract it, relieving the strategic import | |
| Husbandry | Wool and hides become Fabric, eggs and milk become Supplies, in the ledger instead of a fridge | Food and water for the pens |
| Outpost industry (F10.2) | The parts press becomes the colony's Fabrication, selling into the ledger instead of a cash box | Metals |
| Combat crews | Reaver strongholds and blockades on colony cells, paid by the campaigns' existing bounties | |
| Expeditions | Camps near a colony come first in the planet expedition offers | |
| Contracts | The colony posts its shortages on the public board | |
| Drydock | | The yard competes with Anvilhold for Ship Components, so a big build raises drydock prices sector-wide |
| Companies | Revenue into the company account, yard designs into the Hangar | Fleet licence rules apply to yard hulls |
| Outpost crews (F4.3) | How other players join a colony: the outposts' crews with their admission settings | |

## The Development Index

A **holder** is a company, or a player with no company. Each holder keeps a score on each **world**. A world is its
prototype (Merak, Thrascias, ...), so the score survives the terrain regenerating every round. Scores live in the
database, like company accounts.

An outpost earns for the company it flies for (its ship access is in Company mode, the existing
`WFShipCompanyLinkEvent` link), or else for its owner. Outposts are founded or dropped again every round, so a holder
only earns in rounds where its outposts land and someone is home.

### Points

Starting numbers, all to be tuned once rounds are played:

| Source | Points | Notes |
|---|---|---|
| Someone home | 2 an hour per outpost | The raid director's `SomeoneHome`; at most 3 outposts per holder per world count. A trickle: being home is what lets producers run, not what scores |
| Production | 1 per crate-equivalent the holder's buildings put on the colony's shelf | The main source, with hauling |
| Imports delivered | 1 per crate a holder's members deliver to the colony | Rewards hauling for your own colony |
| Raids beaten | 25 times the raid's step | The raid director already ranks steps |

Against that:
- **Decay:** 5% a day, applied at round start for the days since the last round.
- **Losses:** a raid that breaks into an outpost costs 25 times its step.
- **Sales:** a sold outpost stops counting. A sale already voids its save lineage.

### World tiers

The world's tier comes from the **sum** of every holder's score, so all its outposts build it together.

| Tier | World total | Unlocks |
|---|---|---|
| Settled | 100 | The Development tab shows the world and its holders |
| Colony | 500 | The world becomes a station in the ledger (below) |
| Charter | 2,000 | A company can become governor |
| Dominion | 6,000 | The governor can build the orbital yard |

A tier is lost when the total falls 10% below its threshold, so a world doesn't flicker between tiers.

### Shares

A holder's **share** is its score over the world total. Shares decide who governs, not who earns. Each holder earns
from its own buildings (below).

## More than one company on a world

Every outpost on a world adds to the same colony. That makes the world a commons with a contest on top.

- **Shared:**
  - the world's tier;
  - its population;
  - its shelf, which is one ledger station;
  - its imports.

  A hauler who fills the colony's Supplies helps every holder there. A blockade hurts all of them.
- **Kept apart:**
  - Each holder's own buildings produce for that holder. What they put on the shelf is credited, and its sale paid,
    to that holder's company account or bank.
  - Each holder sets, for its own output, which goods it sells to the ledger and which it keeps for its own use or a
    yard build.
- **Contested:**
  - *Ground.* Claims already stop outposts overlapping. Deposits and the world's three outpost lots are first come,
    first served.
  - *The charter* (below), decided by score, so the way to take a world from a rival is to out-develop them.
- **No fighting over the charter.** The game has no company-against-company war today, and server rules on base
  raiding apply as they do now. Taking the charter is economic.
- **Map:**
  - A Colony world's cell shows a neutral colonised pattern, and its hover lists the holders and their shares.
  - A Charter world's cell takes the governor's colour.
- **Solo players** develop the world and earn from their buildings, but never govern.

## Mechanics

### 1. Baseline: an outpost that survives

A colony is pointless if its first crew starves. Before anything else:

- Every preset has water and a food source. Today Workshop and Bastion have neither.
- Every preset has a starting crate of steel, glass and plastic, so its fabricator does something on minute one.
- The animal feeder feeds the animals in its pen from its storage. Today it only marks the pen.
- Decide whether raids default on for outposts. `wf.outposts.raids` is off, so the outposts plan's M4 never reaches
  players, and without raids the campaign ties below have nothing on the ground.

### 2. The colony station

At Colony tier the world becomes a station in the TradeGoods ledger: a runtime `WFTradeStationComponent` for the world,
rebuilt each round from the outposts that land. The station is not tied to a POI or game map id, unlike every station
today.

- **Exchange.** Players reach it at a **colony exchange**, a deployable vendor and chute pair. Any holder may deploy
  one, and every exchange on the world reads the same shelf.
- **Population.** The colony has a population, as every station in the ledger does. It grows from:
  - the world's tier;
  - housing (beds and cryopods);
  - Supplies and Medical on the shelf.

  It uses those goods at the ledger's rates. A dry shelf slows growth and then shrinks the population, and a smaller
  population makes less.
- **Industries come from buildings.** They come from what the outposts actually run, using the ledger's existing
  industries and rates:

  | Buildings | Industry | Notes |
  |---|---|---|
  | Hydroponics, gardens | Food | Fertilizer in, Supplies out |
  | Pens with livestock | Agriculture | Supplies in, Fertilizer and Fabric out; wool and hides add Fabric |
  | Ore silo with ore delivered | Refining | Scrap in, Metals out; planet ore is the materials boost |
  | Parts press | Fabrication | Metals in, Components out; replaces the cash box from F10.2 |
  | Extractor on a uranium deposit | Extraction (new) | Makes Fission Fuel, the one good with no maker today |
  | Extractor on a volatile deposit | Chemicals | Plasma, salt and coal are its existing materials |

  Each industry follows the ledger's rule that output scales with the input's level, so a colony nobody supplies makes
  a quarter of what a supplied one does.
- **Buildings work real inputs.** A building sets how much a holder can make; real items decide how much it does make.
  An animal counts only alive and fed, so a pen needs its feeder stocked. An ore silo makes Metals out of the raw ore in
  it, at the ledger's worth of materials a crate (`materialCrateValue`), and stops when it is empty; sheets in the silo
  are never touched. D8 below carries the rule to the rest.
- **Imports, slow channel.** What the colony is owed comes down as parachute drops onto a UTH, using the F2.4 drop
  system, at the rate a freighter would deliver.
- **Imports, fast channel.** Player haulers delivering at an exchange. Their ships land on an outpost pad, which
  ignores lift, so a loaded hauler gets down where it otherwise couldn't.
- **Blockade.** A blockaded colony gets no drops, as a blockaded station gets no freighters.
- **Deposits.** Each world gets deposits of what it extracts: a resource node placed with the world's sites, shown by
  the existing survey scanner and worked by an extractor inside a claim. Extractors are producers with the existing
  home gate and cap. First pass at which world does what (open question 1):

  | World | Leans to |
  |---|---|
  | Asclepiu | Food, Agriculture |
  | Merak | Refining |
  | Fervidus | Chemicals |
  | Thrascias, Aerumna | Fission Fuel |
  | Carcinoma | Pharmaceuticals, from its biothreat |

### 3. Territory

A new SectorControl driver, `Colonies`, draws developed worlds on the sector map: neutral at Colony tier, the
governor's colour at Charter tier, as above.

- **Holding the cell.** Flying a cell is what keeps it today. For a colony cell, someone home at any of its outposts
  counts as that activity.
- **Retakes.** The driver vetoes retakes through `WFSectorRetakeAttemptEvent`, so a ship parked in orbit can't wipe a
  colony off the map.
- **ADS.** A Colony cell counts as holding a station, so the ADS leaves it alone, as its claim veto already does for
  station cells.

### 4. The campaigns notice

Development has a cost, and combat crews get work out of it.

- **Raids:** the raid director's attention is multiplied by the world's tier (1x, 1.5x, 2x, 2.5x), so a bigger colony
  gets more and harder raids.
- **Reavers:** a Colony or higher cell is a Reaver target. The campaign may set a stronghold beside it, and a Reaver
  hold on the cell blockades it through the existing `blockades` flag. The UTH drops stop, and the shelf drains toward
  0.25, felt within 30 minutes as at any station. Breaking the stronghold pays the campaign's existing bounty.
- **Expeditions:** camps within a few hundred metres of a colony's outposts come first in the planet expedition offers
  for that world.
- **DEFCON:** a blockaded colony adds to the score through `WFDefconGatherEvent`.

### 5. Colony contracts

At Colony tier the colony is a contractor on the public Contracts board. It posts supply contracts for what its shelf
is short of, with the board's existing supply-goods objective and pay. It needs a contractor made at runtime, since
contractors today name a game map. No new contract kinds.

### 6. The charter

At Charter tier one company governs the world.

- **Who:** the company with the largest share, at least 35%, and 3 or more members (the fleet licence rule).
- **Succession:** the charter passes when another eligible company has 25% more points than the governor at three
  daily checks in a row. It lapses if the governor's share falls under 20% or the world drops below Charter tier.
- **Powers** are control, never a share of anyone's income:
  - *Permits.* A holder with no points on the world needs the governor's permit to found there. Holders already on
    the world never need one, so a governor can't lock out its rivals.
  - *Lots.* The governor's outposts land on the world's outpost lots before anyone else's.
  - *Defence.* The governor sets whether the outposts' orbital batteries engage campaign ships in orbit (below).
  - *The yard*, at Dominion tier.

### 7. Orbital defence

At Charter tier, the flak and orbital defence batteries from F10.3 can fire on campaign ships in the world's orbit
layer, not only on descending raid shuttles. They still need a powered sensor uplink, and they never fire on a player
ship. This answers the outposts plan's Q1.

### 8. The yard (orbit only)

At Dominion tier the governor can build a **yard platform**: a static grid on the world's orbit layer. It belongs to the
world's charter, not to an outpost save, so it passes to the next governor with the charter.

- **Machinery:** the yard is built on the Drydock module's berths, cranes and jobs, with a new kind of job: build a hull
  instead of repair one.
- **What it builds:** yard designs, a catalogue of hulls that no shipyard sells and no faction voucher gives (heavy
  frigates and capitals). It never builds a shipyard or voucher hull, so it undercuts neither.
- **What it takes:** each stage needs Ship Components and Metals crates delivered at the yard's chute, plus time.
  - Those are existing goods. A build pulls on the same Ship Components shelf as Anvilhold's repairs, so a big
    build makes drydock work dearer, a cause players can see.
  - The colony's own Fabrication helps by making Components for the sector, but the crates still have to be flown up
    to the yard.
- **Persistence:** progress is saved in the database, so a capital can take several rounds.
- **Done:**
  - The hull spawns at the berth with a deed to the company that ordered it.
  - That company's first completion of a design grants a Hangar fleet licence for it. The licence is what carries
    over between rounds, as any company licence does, and it is the only way to get a licence for a yard design.
- **Berths:** the governor may open berths to other companies holding 10% or more of the world. Their jobs stay
  theirs if the charter changes hands.
- **Never on the ground.** The outpost fabricator, the pads and the exchanges never assemble a hull.

### 9. Heritage

An outpost that lives many rounds earns perks. A round counts when someone is home for 30 minutes. The count saves with
the outpost, and a sale resets it, as a sale already voids the lineage.

| Rounds | Perk |
|---|---|
| 5 | Reload markup 1.5 down to 1.4 |
| 15 | Two more cryopods (8) and producer cap +25% |
| 30 | Reload markup 1.3, the outpost's name on the planet map |

## Overlap check

| Idea | Existing feature | Decision |
|---|---|---|
| Landing tax on a governed world | Company owner's cut | **Cut.** Charter powers are control, never income from others. |
| Colony pays player jobs | StationRoles salaries, company payroll | **Cut.** Players join through outpost crews (F4.3); population is a ledger number. |
| Company research, survey levels | Upstream R&D, Xenoarch | **Cut.** Deposits are found each round with the survey scanner; nothing carries over but the score. |
| New goods or yard components | The ledger's goods | **Cut.** Colonies use existing industries; the yard eats Ship Components and Metals. |
| Selling from the trade hub | Pallets, planet cargo depots, chutes | **Replaced** by the colony exchange, a ledger station's ordinary vendor and chute. |
| Colony port in orbit | Pads, UTH drops | **Cut.** The port is on the ground: pads for haulers, UTH drops for the slow channel. |
| Governor posts contracts | Contracts: stations post on the board | **Reused.** The colony is a contractor at Colony tier, governor or not. |
| Charter won by capturing a console | Nothing: no company-against-company war exists | **Cut.** Succession is by score. |
| Planetary shipyard | Shipyard, Hangar, faction vouchers, Drydock | **In orbit, on Drydock berths,** for yard designs only, granting a Hangar licence. |
| Ships built on the ground | Pads | **Cut.** |
| Territory | SectorControl | **Reused** as a driver. |
| Colony under attack | Raid director, Reaver campaign, ADS, expeditions | **Reused:** attention multiplier, campaign targeting, `blockades`, the ADS station veto, expedition offers. |
| Orbital guns | F10.3 batteries | **Reused** with campaign ships as targets (Q1). |
| A Lattice relay for colonies | Lattice relays | **Not needed.** Every world sits 6 to 14.5 km out, inside the anchored band. |

## Persistence

New tables, in a migration named `WolfgateDevelopment`:

- `wf_world_development`: holder (company id or user id), world prototype, points, last update.
- `wf_world_charter`: world prototype, governing company, granted at, succession streak.
- `wf_world_yard`: world prototype, the platform's grid save, open berths.
- `wf_yard_job`: company, world, design, stage, stage progress, crates delivered.

Population, the colony's shelf and its industries are round state, rebuilt at round start from the world's tier and the
outposts that land. Heritage is a field on the outpost save.

## Risks

| Risk | Counterweight |
|---|---|
| One company holds a world forever | Daily decay; succession by score; the campaigns press harder on bigger colonies |
| A charter held by a company nobody plays | Decay drops its share; the charter lapses or passes |
| Rivals frozen out by permits | Permits only bind holders with no points there |
| Free riders on a shared colony | Revenue follows each holder's own buildings; only the tier and shelf are shared |
| Fission Fuel made at home breaks the strategic import | Extraction rates set so colonies relieve the import, not replace it; tuned with the economy's simulation |
| Low population servers | Tier thresholds scale with the `population` brackets the campaigns already use |
| Free ships | Yard builds are slow, multi-round and draw on a shared shelf; designs are sold nowhere, so no arbitrage |

## Implementation plan

Same rules as the outposts plan: one PR a step, under 1,500 lines, behind `wf.colonies.enabled` until the step that
turns it on, with its test, Fluent strings and README section.

### Status

D0 to D7 are built in one change, with `wf.colonies.enabled` on. The module README says what is where. Tests
(`Tests/_WF/Colonies/ColonyTest` and `ColonyCellsTest`, `Tests/_WF/Husbandry/FeederTest`):

| Step | Covered by |
|---|---|
| D0.1 | `ColonyCellsTest`: a company's runtime faction and the neutral colonies faction claim cells and reach the client's map with their names and colours |
| D0.2, D3.1, D3.2, D3.4, D3.5, D8.2 | `BuildingsWorkTheColony`: a Colony world's body becomes a station its outposts trade on; a penned goat runs Agriculture and makes Fabric, and nothing while it starves; an extractor over a uranium seam runs Extraction only while someone is home; an ore silo's Refining draws down the ore in it, gives back what the ore didn't cover and stops empty |
| D0.3, D6.1 | `YardPlatformSavesAndComesBack`: a platform saves by world and is placed again where it stood, cranes linked |
| D1.1 | `OutpostPresetTest`: every preset has a water and a food source |
| D1.2 | `FeederTest`: a starving penned chicken eats one item off the feeder |
| D1.3 | Raids stay on by default (recorded in the outposts PLAN.md, F8.2) |
| D2.1, D2.2 | `ScoresAddUpAndDecay`: two holders' points settle one world; a month of decay unsettles it |
| D2.1, D3.1, D3.2, D3.3, D4.3 | `ColonyTradesPaysAndImports`: hours home earn points; a goat's output is owed to its holder only and paid into their bank; the exchange's chute and vendor work the one shelf, a crate hauled in scores, and the Market app lists the colony; a Supplies shortage makes the colony its contractor's home; what it is owed drops on a ready hub, and a blockade holds it back and raises DEFCON |
| D4.1, D4.2 | `ColonyHoldsItsCellUntilAHoldBlockadesIt`: a Colony world holds its cell, the ADS leaves it alone, raids come more often and a parked ship can't retake it; a blockading hold next door takes it while nobody is home, which blockades the colony |
| D5.1, D5.2, D6.1 | `GovernorEngagesBatteriesAndDeploysTheYard`: the batteries fire at a raider hull in orbit only once the governor engages them, never at a player ship; a newcomer needs a permit or an open world, and the lots wait for the governor's outposts; the kit deploys only in orbit, for the governor, at Dominion, on clear space and once |
| D5.1, D6.2, D6.3 | `CharterPassesAndYardBuildGrantsLicence`: a 25% lead for three checks takes the charter; a build's stages take crates, survive a reload and finish into a hull and a licence; a company has no berth until the governor opens one, and a shipyard hull is refused |
| D7.1 | `HeritageCountsRoundsAndGrantsItsPerks`: a round counts once after enough time home; the three thresholds lower the markup, add cryopods and producer cap, and name the outpost on the planet map; a sale resets it |

Not yet covered by a test: goods a holder keeps off the shelf and their drops (D3.2), expedition offers near a colony
(D4.3), a platform changing hands with the charter (D6.1), and the Development tab and yard console windows.

The Reaver campaign seeks out colony cells (D4.2) since D9: its spread weighs a cell beside a colony four times.

D6.4 uses two existing admin-only classic hulls, the Pacem and the Infiltrator, as the first designs; no new hull was
mapped. Placeholder art: the extractor is a tinted ore silo, the exchange chute and the yard chute tinted cargo chutes.

### D0: Spikes

| Step | What | Done when | Size |
|---|---|---|---|
| D0.1 Company factions | Can SectorControl draw a cell for a runtime company, and a neutral shared pattern? | A test that claims and draws both | S |
| D0.2 Runtime station | A ledger station made at runtime for a world, not keyed by POI or game map id | A test that a runtime station trades and appears in the Market app | S |
| D0.3 World-owned grid | A static grid on a world's orbit layer, saved by world rather than by outpost | A round trip of a platform | S |

### D1: Baseline

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D1.1 Presets that survive | Water and food in every preset, starter material crate, preset prices re-checked | Universal preset test checks for a water and a food source | S | none |
| D1.2 Feeders feed | The feeder feeds penned animals from its storage | Test: a penned chicken stays fed from a stocked feeder | S | none |
| D1.3 Raids decision | `wf.outposts.raids` default settled | Decision recorded in PLAN.md | S | none |

### D2: The index

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D2.1 Score and storage | `wf_world_development`, point sources, decay, holder from the company link | Test: an outpost with someone home earns; decay applies across rounds | M | D1 |
| D2.2 Tiers and shares | World tiers from the total, shares, the Development tab on the outpost console | Test: two companies' outposts on one world raise one tier | M | D2.1 |

### D3: The colony station

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D3.1 Station and exchange | Runtime world station, population, the colony exchange | Test: a Colony world's exchange sells and buys from one shelf | L, split station and exchange | D0.2, D2.2 |
| D3.2 Industries | Industries from buildings, output credited to each holder | Test: a holder's farm raises Food and pays that holder only | M | D3.1 |
| D3.3 Imports | UTH drops as the slow channel, blockade stop, haul points | Test: an owed crate drops on a UTH; a blockade stops it | M | D3.1 |
| D3.4 Deposits | Deposits per world, extractors, Extraction for Fission Fuel | Test: an extractor on a uranium deposit adds Fission Fuel | M | D3.2 |
| D3.5 Farm and press outputs | Wool and hides to Fabric, eggs and milk to Supplies, the parts press as Fabrication | Test: penned sheep raise the colony's Fabric | S | D3.2 |

**Milestone N1, colonies matter:** after D3.3, outposts feed the sector economy, earn for their holders and need
haulers.

### D4: Territory and threats

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D4.1 Colonies driver | Neutral and governor cells, ground activity, retake veto | Test: a Colony cell holds while someone is home and a parked ship can't retake it | M | D0.1, D2.2 |
| D4.2 Campaign attention | Raid attention multiplier, Reaver targeting, blockade, DEFCON source | Test: a Reaver hold on a Colony cell blockades it | M | D4.1, D3.3 |
| D4.3 Contracts and expeditions | Runtime colony contractor, expedition offers near colonies | Test: a Supplies shortage posts a contract | S | D3.1 |

### D5: The charter

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D5.1 Governor | `wf_world_charter`, succession and lapse, permits, lot priority | Test: a company overtaking by 25% for three checks takes the charter | M | D2.2 |
| D5.2 Orbital defence | Batteries engage campaign ships in orbit at Charter tier, governor's toggle | Test: a battery fires at a Reaver hull and never at a player ship | M | D5.1 |

### D6: The yard

| Step | Ships | Done when | Size | Needs |
|---|---|---|---|---|
| D6.1 Platform | `wf_world_yard`, the platform kit, deploy in orbit, passes with the charter | Test: a platform saves, loads and changes hands with the charter | L, split deploy and save | D0.3, D5.1 |
| D6.2 Yard jobs | Drydock build jobs, stages fed by Ship Components and Metals, `wf_yard_job` across rounds | Test: a stage completes from delivered crates and survives a round restart | L, split jobs and persistence | D6.1 |
| D6.3 Designs and licences | The yard design catalogue, deed on completion, Hangar licence grant, open berths | Test: the first completion grants a licence; the yard refuses a shipyard hull | M | D6.2 |
| D6.4 First designs | Two or three yard hulls (mapping) | Shipyard tests pass for them | M each | D6.3 |

**Milestone N2, dominion:** after D6.3.

### D7: Heritage

| Step | Ships | Needs |
|---|---|---|
| D7.1 Heritage | Round count on the save, the three perk thresholds | D2.1 |

### D8: Worked buildings

D0 to D7 as first built scored presence above development: an occupied outpost earned 10 points an hour, a fully built
one about 14 more, and a building counted because it existed. D8 makes output follow what players put in.

| Step | Ships | Done when | Size | State |
|---|---|---|---|---|
| D8.1 Home points | Home points down from 10 to 2 an hour | The cvar's default | S | Built |
| D8.2 Fed animals, worked ore | Livestock count only alive and fed; ore silos draw raw ore for the Metals they make | Test: a starving goat works nothing; a silo's ore runs down and an empty one stops | S | Built |
| D8.3 Produce at the exchange | Wool, milk, eggs and crops sold at the exchange chute become fractions of Fabric and Supplies crates for the seller's holder; the passive wool, dairy and egg rates go | Test: sheared wool sold at the chute raises Fabric and scores; an unsheared sheep raises nothing | M | Built |
| D8.4 Staffing | The per-kind caps (`most`) give way to staffing: each running building needs some of the colony's people, so growth follows housing and a supplied shelf | Test: more beds and Supplies let more buildings run | M | Open |
| D8.5 Yard contracts | A build posts supply contracts for its Ship Components and Metals on the public board, as a short colony does | Test: an unfed stage posts a contract; a delivery feeds it | S | Open |

Watch for: the exchange must not pay twice for goods that also sell at cargo (D8.3); Fission Fuel output once extractors
are the only passive producers left (open question 2); and the tier thresholds, which were set for 10 home points an
hour and need a pass through the economy's simulation.

### D9: The colony in the world

D0 to D8 left the colony a ledger: a tier on a tab, drops from nowhere, raiders from nowhere and a charter nobody
granted. D9 gives each of those a place or a sender in the round. Built in one change:

| Step | Ships | Covered by |
|---|---|---|
| D9.1 The seat | A site by tier at world build: landing field, depot with the exchange, governor's hall; a depot stands for the colony with no outposts; colonists live there | `ColonySeatMapsTest`, `ColonySeatTest` |
| D9.2 The outfitter | Saves read as manifests filed with the outfitter; an outpost with heritage comes down on its old lot | Strings; no test for the lot |
| D9.3 Raiders are someone | Packs need their source standing: a raiding camp on the world, a Reaver stronghold in reach, or no campaign; the campaign spreads towards colonies | `RaidBreachTest` |
| D9.4 Freight and comms | Trade orders need the world's uplink; a colony's imports come by a supply run that can be shot down | `OutpostTradeTest` for the uplink; no test for the run |
| D9.5 The Bench grants | Charter changes are writs of the Bench; Article V of the Common Writ; the charter office in the hall; a hot war stops successions and raises a chartered colony's draw | `ColonySeatTest` for the office standing; no test for the war |
| D9.6 The hull on the stocks | A build stands by the platform as a frame that grows with the work and makes way for the vessel | `CharterPassesAndYardBuildGrantsLicence` |
| D9.7 The planet matters | `baseTier` and `rates` by world, climate on farms, produce sold at the exchange (D8.3), biomass on Carcinoma | `ScoresAddUpAndDecay`, `BuildingsWorkTheColony` |

Left open: a world that tiers up mid-round gets its seat at the next round's world build; raid points still outweigh
the rest of the index; the seat maps are generated and want a mapper's pass.

## Open questions

1. Which deposits does each world get? The table under the colony station is a first pass.
2. How much Fission Fuel may colonies make before the strategic import stops mattering?
3. Which hulls are the first yard designs, and do TSF- or PDV-aligned companies get faction-pattern designs?
4. Should tier thresholds scale with server population, and by which brackets?
5. Does a colony exchange need its own sprite, or is it a vendor and chute side by side?
