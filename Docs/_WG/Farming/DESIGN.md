# Farming, cooking and the homestead economy: design

Status, 2026-11-18: this is its own pull request (branch `Cooking-Farming`, cut from upstream `main`), separate from
Outposts, so the two big reworks can be tested first. Farming is a **rework of botany**: its rules (light, water,
climate, quality, fertilizer, sprinklers) apply to every plant holder, on stations as on worlds. Cooking **replaces the
microwave**: station microwaves and ranges are hearths. Fishing and foraging ride along. Outposts will hook into these
modules once both are in (outpost producer cap, blueprint shelves, the outpost range as a hearth).

Source note: the Stardew Valley wiki refuses automated fetches, so the Stardew rules are from general knowledge.

## The aim

An outpost should earn while its owner tends it, the way a Stardew farm does: plant, water, harvest, process, sell, put
the money back in. Farming, animals, brewing, manufacturing and mining each make goods the others need, so players trade
with each other and with NPC stations instead of each owner making everything alone.

Three rules hold every income source to the same shape:

1. **It needs tending.** Nothing pays forever with nobody home. A neglected farm stops; it doesn't rot into a loss
   that punishes a weekend away (Stardew's crops die out of season; ours just stall).
2. **Raw goods are cheap, processed goods are worth more, and processing needs another chain's goods.** A keg needs
   crops and glass; glass needs sand from mining; a greenhouse needs the parts press. That is what makes it circular.
3. **Cap it with what already exists.** `wf.outposts.producer_cap` (4,000 spesos an hour of output) and the TradeGoods
   shelf prices already stop a flood. New sources count against the same cap and sell into the same shelves.

## What already exists (checked before designing)

| Exists | Where | Use it for |
|---|---|---|
| Upstream botany: plant holders, seeds with genetics, water, nutrients, weeds, pests, mutation, seed extractor | `Content.Server/Botany` | The plant itself. Don't replace it; add a growth driver and quality on top. |
| Penned livestock, feeders, eggs/milk/wool timers, lasso, taming, whistle, saved records | `_WG/Husbandry` | Animals. Add mood and product quality; the pens are done. |
| Parts press, power exporter, mining bot dock, hourly producer cap | `_WG/OutpostIndustry` | Manufacturing and mining. Extend, don't duplicate. |
| Colony industries, climate-scaled growth, world deposits, development index | `_WG/Colonies` | The "world matters" layer; farms count toward a world's tier. |
| Trade crates, shelf-driven prices, freight, market app | `_WG/TradeGoods` | Where goods are sold. New commodities become trade goods. |
| Planet light cycle | `LightCycleComponent` | The clock crops grow on. |
| Safes take seeds and produce | `Hideouts/safes.yml` | Stock carried between rounds. |

## Farming

### Growth on the planet's clock (RimWorld timing, Stardew rules)

Upstream botany ages a plant on a fixed timer. Farming replaces that, on outpost grids only, with growth that follows
the world's day:

- A plant gains growth only while it is **lit**: in daylight from the planet's `LightCycle`, or under a grow lamp.
  Night growth is zero, like RimWorld's rest hours. Indoors with no lamp, nothing grows.
- It also needs to be **watered today**. A tray or tilled soil holds a "watered" flag that the day rolling over
  clears, as in Stardew. Watered and lit: full growth. Lit but dry: no growth that day, no damage.
- A crop's time is set in **days**, not minutes: a planet day is the unit. Short crops take 2 to 4 days, long ones 8
  to 13, as in Stardew. The owner tunes how long a planet day is through the light cycle, not here.
- Regrowing crops (berries, beans, peppers) go back a few days after each harvest instead of being replanted.
- **Seasons** become **climate**: each world already has a temperature that `Colonies` reads. A crop has a band it
  grows in. Outside it, it stalls instead of dying. Greenhouses (an enclosed, heated room with glass walls) ignore the
  outdoor band. A frozen world can still farm, but only indoors, which is the point of a greenhouse.

### Quality

Every harvest rolls a quality: normal, silver, gold, or iridium (the top tier, which only fertilizer reaches). Price
multipliers 1, 1.25, 1.5 and 2, as in Stardew. Quality rides on the produce through processing: a gold fruit makes gold
wine.

What raises it:

- **Soil**: fertilizer (below).
- **Care**: a plant that was watered every lit day of its life gets a bonus.
- **Skill**: a light per-character farming level could come later; leave it out of the first pass.

### Soil and fertilizer

| Item | Effect | Made from |
|---|---|---|
| Basic fertilizer | quality up one step on average | compost (any produce + time in a composter) |
| Quality fertilizer | quality up two steps; iridium possible | compost + bone meal (butchery) |
| Speed-gro | 10% faster | compost + chemistry |
| Deluxe speed-gro | 25% faster | speed-gro + plasma (mining) |
| Retaining soil | stays watered on alternate days | compost + clay or sand (mining) |

Composting closes the loop: spoiled produce, animal waste and kitchen scraps become fertilizer.

### Watering and sprinklers

Watering by hand with a can or hose is the early game. Sprinklers water at day rollover:

| Sprinkler | Covers | Made from |
|---|---|---|
| Basic | 4 tiles (plus shape) | steel, a pipe |
| Quality | 8 (3x3 ring) | steel, gold, parts press parts |
| Advanced | 24 (5x5 ring) | plasteel, gold, a circuit board |

Sprinklers draw water from the outpost's water tank, which needs refilling from a well, rain catcher or water trade
good. That gives water a price on dry worlds.

### Crops

Start with a dozen that cover the uses, mapped to upstream seeds where they exist (wheat, tomato, potato, apple, grape,
coffee, tea, tobacco, cotton, sugarcane, soybean, chili). Each gets: days to grow, regrow days or none, climate band,
base value, and what it feeds (kitchen, keg, jar, mill, loom, animals). Later: giant crops (a 3x3 field of one ripe
crop can merge), fruit trees that take many days but then give fruit every few days for good.

### What's automated and what isn't

Stardew's balance holds here: sprinklers take the watering, but harvest and replant stay manual. A harvester bot can
come late as an expensive OutpostIndustry gizmo (like the mining bot), with its own cap.

## Animals (extends Husbandry)

The pens, feeders and product timers exist. Add:

- **Mood (0 to 100)**. Up from being fed, petted once a day, a pen that is lit and warm, and a heater in winter climates.
  Down from hunger, crowding, cold and being left out at night. A mood over 70 can produce a large or better-quality
  product. Under 30, no product.
- **Feed** goes into feeders as hay (from wheat, via a mill or hay bale) or grain. An empty feeder means hungry
  animals. That links fields and pens.
- **Product quality** follows mood and age, the same four tiers.
- **Housing tiers** stay as the Hideout tab's buildings: coop (chickens, ducks), barn (cows, goats, sheep, pigs). A
  barn with an auto-feeder silo fills feeders from stored hay.

## Artisan goods (processing machines)

These are where most of the money is, as in Stardew. Each machine is bought from the outfitter or built at a lathe,
runs on outpost power, and counts toward the producer cap.

| Machine | In | Out | Time | Value |
|---|---|---|---|---|
| Keg | fruit / grain / hops / coffee beans / honey | wine / beer / pale ale / coffee / mead | 1 to 7 days | 2 to 3 times the input |
| Preserves jar | fruit / vegetable | jam / pickles | 2 to 3 days | 2 times plus a flat amount |
| Cheese press | milk | cheese | 1 day | about 1.5 to 2 times |
| Mayonnaise machine | egg | mayonnaise | hours | about 1.5 times |
| Loom | wool / cotton | cloth | 1 day | about 3 times; cloth also feeds tailoring |
| Oil press | sunflower / soybean / truffle | cooking oil | hours | small; used in cooking |
| Mill | wheat / rice / sugarcane | flour / rice flour / sugar | instant | feeds kitchens and bakeries |
| Cask (in a cellar room) | finished wine / cheese | aged up one quality step | many days | quality multiplier |
| Bee hive | none (needs flowers within 5 tiles) | honey; the flower sets its kind and price | 4 days | flower-dependent |

Brewing needs **glass bottles** (sand from mining, a furnace) and **casks** (wood or steel). That is the main hook from
farming into mining and manufacturing.

## Other passive income

Each one is sized so it is worth running, makes one or two goods other chains need, and needs something from another
chain. Grouped by how far they are from what exists.

### Builds on what exists

- **Mining (extends the mining bot dock).** Bots bring ore. Add an **ore washer** (water in, ore out a grade better)
  and a **geode cracker**: some rock gives geodes that open into gems, rare minerals and the odd artefact. Gems are a
  luxury trade good and go into jewellery and high-tier sprinklers. Mining gives sand (glass), clay (pots, retaining
  soil), coal (heating greenhouses and pens) and metals (machines).
- **Manufacturing (extends the parts press).** A chain of presses, each turning one good into a better one: sheets
  become parts, parts become machine frames, frames plus circuits become sprinklers, kegs and grow lamps. Here
  farmers buy their machines from manufacturers, who buy food from farmers.
- **Power.** The exporter exists. Add **biogas digesters** (animal waste and spoiled produce in, power and fertilizer
  out) and **solar fields** on sunny worlds. A digester ties husbandry to power; solar ties climate to power.

### New and fits the setting

- **Data mining (the crypto idea, in setting).** A **compute rack** draws heavy power and makes "encrypted ledgers",
  a trade good banks and corporate stations buy. It runs hot, so it needs cooling (a freezer loop, cold-world bonus)
  and eats power that would otherwise sell through the exporter. That tension is the design: does a cold world's
  power go to the grid, the greenhouse heaters or the racks? Racks degrade and need parts press parts to repair.
  Keep its rate low and capped: it is the hands-off option, so it pays the least per hour of attention.
- **Aquaculture.** Fish tanks or ponds: feed (grain or bugs) in, fish and roe out. Roe goes into a preserves jar
  (caviar). Pond water fertilizes crops.
- **Mushroom cellar.** A dark, damp room: logs (from a tree farm) plus spores. Grows at night and without light,
  so it uses the outpost's dark rooms and evens out a farm's day-only output.
- **Xenobotany.** Plants from a world's native flora (PlanetNatives, Xenobiology), grown for drugs and medicine sold
  to medical stations and needed for chemistry. Climate-locked to their home world, which makes trading between
  worlds worth it.
- **Tailoring and leather.** Loom cloth plus tanned hides (butchery) into clothing and armour padding, sold to
  outfitters.
- **Kitchen and canteen.** The canteen building cooks meals from flour, oil, produce, cheese and meat. Meals buff
  workers, and NPC stations buy them as the highest-value food good.

### Later, needs more design

- **Contracts board** at the outpost: NPC stations post orders ("40 bottles of beer in 3 days") that pay above the
  shelf. This feeds the Contracts module, and players can post their own orders for other players to fill.
- **Player shops**: a counter on the outpost where visitors buy goods the owner priced. Ties into "outposts as
  destinations" from `Docs/_WG/Hideouts/DIRECTION.md`.

## How it all connects

```
            water ─────────────┐
 mining ──► sand ─► glass ─────┼──► kegs, jars ◄── crops ◄── fertilizer ◄── compost ◄── waste
   │        clay ─► soil ──────┘        │            │                         ▲
   │        coal ─► heat ─► greenhouse  ▼            ▼                         │
   │        metal ─► parts press ─► machines     wheat ─► hay ─► animals ──────┤
   │                    ▲             │                           │            │
   └─► gems ─► luxury   │             ▼                           ▼            │
                     circuits     sprinklers               milk/egg/wool ─► cheese, mayo, cloth
 power ◄── solar, digester ◄──────────────────────────────────────┘ (waste)
   │
   └─► compute racks ─► encrypted ledgers (sold to banks)
```

Each income source needs at least one input from another source and makes at least one good another source needs.
A player who specialises earns more than one who does everything, because the bottleneck goods (glass, hay, parts,
power) are cheaper to buy from someone set up for them.

## Trade

- Every new good is a TradeGoods commodity with a home market: wine and cheese sell best at rich stations, hay and
  fertilizer at colonies, ledgers at banks, medicine at medical stations. Shelf prices already fall as players flood
  one market. That is the anti-farming brake and needs no new code.
- Quality multiplies price at chutes and vendors.
- Goods saved in safes carry over between rounds (seeds and produce are already allowed). Processed goods like wine
  should be allowed too; this also makes the whitelist's "food needs a tag" problem worth fixing.
- Farms count toward the world's development index through `Colonies` (the hydroponics tray hook already exists), so
  farming helps a world reach colony tier.

## What is built

| Step | Built | Simplified or left out |
|---|---|---|
| 1. Growth on the planet's day | Lit and watered growth in planet days, climate bands, grow lamp, quality | No per-character skill |
| 2. Soil and water | Basic and quality fertilizer, speed-gro, retaining soil; three sprinklers, each watering from its own 200 u tank (1 u a plant) | No rain catcher or well yet; fill tanks by hand |
| 3. Processing | One data-driven machine (`WGProcessor` + `wgProcessRecipe`): keg, preserves jar, cheese press, mayonnaise machine, loom, mill, composter, fertilizer barrel, nutrient mixer, oak cask, glass kiln, seed maker. Recipes can need a supply loaded beforehand (`needs`): the keg uses a glass bottle a batch | Cask works anywhere, not only in a cellar |
| 4. Animals | Mood (pet, fed, hungry); happy animals produce faster and lay better eggs; miserable ones stop | Milk and wool have no quality (they are reagents); no warmth or crowding factor |
| 5. Trade | Quality raises the sale price everywhere `PricingSystem` is used; goods carry prices | Not yet TradeGoods commodities with home markets |
| 6. Other income | Bee hive (needs crops nearby), mushroom log, compute rack (8 kW), biogas digester, ore washer, geode cracker | The digester makes compost, not power |
| 7. Orders | Outpost order board: three orders from a weighted pool, paid per item with a quality bonus and a fill bonus | NPC orders only; players can't post their own yet |

Round 2 (2026-11-18) added glass bottles for kegs (glass kiln: a glass sheet into two bottles), water tanks on
sprinklers, the seed maker (a crop into one to three packets of its seeds), and scarecrows (birds peck unguarded outdoor
crops about once in three hours of growing). All machines count toward the outpost producer cap and need power, but
not anyone home. Everything is laid from the blueprint's Farming category.

**Timing is real time.** Crops take `minutes` of growing (lit and watered) in `crops.yml`, and machines take `minutes`
of running in `recipes.yml`, independent of a world's day length. Night still stops outdoor growth.

Eleven crops (cucumber, turnip, pear, plum, tangerine, strawberry, raspberry, blackberry, sunflower, rocknut, coffee) and the glass bottle sprite come from the owner's Paragon Moon project (`Tools/_WG/Farming/port_paragon.py`). It also holds cookware (pot, pan, bowl, cup, platter, peppermill), around 13 sheets of cooked dishes and clay ware to port next.

Round 3 (2026-11-18): a kitchen and the market. Cooking is now its own module (Cooking), ported from Roguetown's hearths in Paragon Moon: pans fry, pots boil stews and soups, the oven bakes, a prep board assembles raw dishes, and food burns if left on the heat. A packing table seals farm goods into the existing trade crates (Luxuries, Supplies, Fabric, Fertilizer), so farm output sells on the live market with its shelf-driven prices instead of a new commodity. The order board asks for fine dishes and home cooking too.

## Small jobs around the loop

Stardew works because there is always something small to do on the way to something big. These are bite-sized jobs
that feed the loop, roughly from cheapest to build to most involved. None is built yet.

**Daily chores (minutes each)**

- **Morning rounds.** Water what the sprinklers miss, pet the animals, collect eggs, empty finished machines. The
  examine text already says what each needs; a "chores" line on the outpost console could list them ("3 kegs ready,
  2 plants dry, 1 animal unpetted").
- **Weeding and pests.** Upstream botany already grows weeds and pests. On an outpost, a weed-free plot could count as
  cared-for for quality, which makes the hoe and spray bottle worth carrying.
- **Feeding the hutch.** Fill feeders with hay. Wheat through the mill as hay bales would make fields feed pens.
- **Scarecrows.** Native birds (the Birds module) peck unguarded crops; a scarecrow guards a radius. A cheap object
  to make and place.
- **Seed saving.** Run your best (gold or iridium) crop through the seed extractor to get seeds that start a quality
  step ahead. Rewards keeping a good line going.

**Errands (a trip)**

- **Foraging.** Wild plants, mushrooms and nests spawn round a world's POIs and caverns each round, per climate.
  Worth picking for the jar and the keg, and for seeds you can't buy.
- **Fishing spots.** Ponds on outposts and wild water on planets: a rod, a short timing game, fish for the smoker
  and the pond. Roe goes into the preserves jar as caviar.
- **Order runs.** Fill a board order from someone else's outpost: buy their wine, carry it to your board. Board
  orders could also ask for goods to be delivered to a named station for more pay.
- **Seed and stock trips.** Some seeds and animals are sold only at certain stations or worlds, so a farmer has a
  reason to travel and to trade with other players for them.
- **Hauling for neighbours.** A player with a ship takes another's crates to market for a cut (ties to TradeGoods
  freight).

**Projects (an evening)**

- **Greenhouse build.** Roof, glass walls, lamps, heaters, sprinklers: a build goal that unlocks winter and frozen
  worlds.
- **Cellar.** A dark room under a roof where casks age faster; a reason to dig and build.
- **Trellis and fruit trees.** Long-term plantings that take many days to start but then give fruit for good.
- **Giant crops.** A 3x3 of one ripe crop can merge into a giant crop worth several times as much.
- **Breeding.** Two happy animals in a pen produce young; rare coats or breeds sell for more.
- **Crop crossing.** Upstream mutation already exists; a gene splicer on the outpost could cross two crops into a
  named variety other players want.

**Shared and social**

- **Harvest fair.** Once a round, players bring their best item of each kind to Central; judges (an NPC or the
  admins) score by quality and price, and the winner gets a ribbon decoration and a small purse.
- **Farmers' market.** A public stall on an outpost where visitors buy what the owner priced; ties to "outposts as
  destinations".
- **Co-op orders.** A board order too big for one farm (200 wheat) that any player can deliver toward, paid per item.
- **Gifts to NPCs.** Station NPCs who like certain goods give better prices or rare seeds to players who bring them.
- **Hired hands.** Pay an NPC crew member (NpcCrew) to water and harvest while you're away, for a wage and a share.

**Other loops to tie in**

- **Cooking for buffs.** Canteen meals from farm goods give a timed buff (speed, stamina, mining yield) that makes
  other jobs go faster.
- **Brewing for morale.** Drinks served at an outpost's bar raise visitors' mood (Standing), and a bar draws visitors.
- **Medicine.** Alien crops from Xenobotany go to the chemistry lab for medicine sold to medical stations.
- **Mining feeds farming.** Sand to glass (bottles, greenhouse walls), clay to pots and retaining soil, coal to heat.
- **Farming feeds industry.** Oil from the oil press as lubricant for machines; cloth for tailoring; wax from hives
  for candles.

Round 5 (2026-11-18):

- **Water by world, never wells.** Rain catchers fill while it rains on them (free, open sky only), moisture vaporators
  condense water on power (best when hot, nothing below freezing), snow melters melt snow and frost on frozen worlds.
  Each feeds the sprinklers within six tiles.
- **Cooking**: knife chopping on the prep board (sliced vegetables, minced meat, fish fillets, dough-ball bait); soups
  want chopped ingredients, as in Paragon; stews of several ingredients (vegetable, hearty, fish chowder, seafood
  bisque, cheese and onion, cream of mushroom, fruit porridge, turnip and cabbage) form when their ingredients stew
  together; soup shows in bowls and pots, tinted to its colour. The outpost range is now a hearth too.
- **Fishing** (module Fishing): cast at a planet's lakes or a built fish pond, wait, reel in on the bite. Thirty fish
  from Paragon and Meridian by climate, with quality from bait.
- **Foraging** (module Foraging): forage spots grow near players on planets by climate and wither if unpicked; some
  give seeds you can't buy, worm casts give bait.

## Next build order

1. Chores line on the outpost console.
2. Player-posted orders and the farmers' market.
3. Real sprites for the machines (they are tinted stand-ins), and a proper pond sprite.
4. Farming skill (deferred by the owner).

## Decisions (owner, 2026-11-18)

1. Crops **stall** outside their climate band; they don't die.
2. Nothing waits for someone to be home: crops grow on their own, and machines run with power and under the producer
   cap whoever is there.
3. A farming skill waits for later.
4. Data mining (the compute rack) stays.
5. Crop and machine times are tuned in **real time** only, not planet days.
