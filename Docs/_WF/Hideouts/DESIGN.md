# Hideouts design

What the `Hideouts` module is for, what is built, and what comes next. The module README
(`Content.Server/_WF/Hideouts/README.md`) says how the built part works.

## Goal

A player's outpost is their home: somewhere secluded on a world of their choosing that they improve over many rounds,
that keeps one ship of theirs, and that soaks up money for as long as they play. The model is Escape from Tarkov's
hideout: a fixed list of modules, each with levels, each level with a price, a build time and other modules it needs.

Outposts (PR 570) already give the base: founding, building, saves, access, pads, presets, raids, world development.
Hideouts adds the progression on top and changes nothing about how an outpost is founded or saved.

## Getting an outpost

Nobody has to fly down and claim ground. The **outpost registry** console at Caelestinus Central, on the public side of
the shipyard trader's counter, founds an outpost on a free plot of the world the player picks, for credits only
(15,000). The world becomes its home. The same console lands a player's saved outpost back on its home world, and
sends them down to their standing outpost for a small fee (2,500). A kit used on open ground still founds one by hand.

**The outpost band.** Every world but Carcinoma keeps a ring of ground past its points of interest for outposts
(`outpostBand`, 96 tiles): the world's bounds grow by it, and its sites, roads, wrecks and camps stay inside the old
radius, so the plots never take from their pool. The plots (80 tiles square) are laid evenly round the ring, cleared
and pinned. Outposts sprawl from their plot into the band round it. The band costs preload time, since the world's disc
is larger.

## A round with an outpost

1. An outpost is its own grid over carved-out ground, never the planet's tiles, so nobody digs or builds into it from
   outside (Outposts' claim rules keep the ground round it too).
2. It remembers its **home world**: the one it was founded on, saved with it. Every time it comes back it lands there,
   on one of the world's secluded lots if one is free. Moving home costs a fee (250,000) at the Hideout tab and takes
   effect from the next landing.
3. In the lobby, a player whose outpost isn't standing yet sees **"Wake up at <outpost> (it lands on its home world
   first)"** in the join chooser. Picking it charges the landing, drops their latest save on its home world, and wakes
   them in one of its cryopods once it is down. Their hangar ship comes back on its pad as it lands. Outpost crews in
   Crew Setup land the same way.
4. Landing costs the usual load price (a save's value and half again) until a module stands; a home with any module
   lands for a tenth of its value instead, as upkeep.

## Building it up

Every module level costs money and goods, after Tarkov: sheets and cable (`stacks`, by stack type) and parts (`parts`,
by prototype): stock parts, power cells, medkits, toolboxes, boards, gold and uranium for the late levels. Whatever lies
on the outpost counts, in crates and lockers too, and starting the level uses it up. The Hideout tab shows each
requirement as held / needed. Founding an outpost takes only its kit, bought with credits.

**Shells.** Alt-use on a module's stake switches it to lay a shell: floors, walls, windows, doors, power and lighting,
and a bay's cranes and console. Its machines come packed to set out by hand, and what its furniture was worth is paid
back to furnish it as the owner likes. The prefabs are a basis to build on, not a finished room.

**Sprawl.** Groundworks is what lets an outpost grow into a RimWorld-sized base: each level adds floor (1,000, 2,000,
4,000), objects, and how far across the outpost may spread (64 tiles at first, then 96, 128 and 192). The rest is built
by hand with foundation plates, walls and gizmos, or laid with module stakes.

## Where outposts go

Every world takes outposts except Carcinoma. A world's surface prototype says so (`outposts: false` on
`WFSurfaceCarcinoma`), and founding, POI claims and round-start drops all refuse there. Each world keeps three
secluded lots (`outpostLots`, 48 tiles square, clear of roads and sites) that round-start drops land on first;
Carcinoma's are removed.

Open: three lots a world is 15 across the five worlds. If more outposts than that are loaded in a round, the rest fall
back to a random clear site, which is less secluded. Raising `outpostLots` is a one-line change per world.

## Modules

| Module | Levels | What it gives | Total |
|---|---|---|---|
| Groundworks | 4 | Opens everything else; raises the floor and object caps | 2,775,000 |
| Hangar | 4 | The hangar apron and beacon; wider ships and cheaper insurance by level | 5,220,000 |
| Powerhouse | 3 | Batteries, solar and wind; later RTGs and a fuel generator | 1,375,000 |
| Quarters | 3 | Bunkhouse with a cryopod; more cryopods allowed by level | 1,210,000 |
| Workshop | 3 | Fabricator shed; later the parts press, silos, mining bots, power exporter | 1,540,000 |
| Medbay | 2 | Medical beds, stasis bed; later an organics printer | 560,000 |
| Vault | 2 | Strongroom with deposit boxes and a deposit terminal | 1,000,000 |
| Defences | 3 | Warning masts and turrets; later flak and an orbital battery | 3,380,000 |
| Company wing (company only) | 3 | Meeting hall, more floor, more cryopods | 12,000,000 |

A full personal outpost is about 17 million. Level 1 of everything is about 860,000, which is the "lived in" stage most
players will reach; the last levels are the long sink. All numbers live in `modules.yml` and are first guesses.

Build times run from one minute to an hour and only tick while the outpost stands in a round, so a long build spans
rounds. Tarkov's real-time timers that run while you are offline were considered and left out: they reward waiting,
not playing.

Each level is one of two kinds:

- A **prefab** level leaves a survey stake at the console. The owner points it at open ground in their claim and the
  building is laid there, front toward them. Where it goes is theirs to choose, which is what makes two outposts look
  different.
- A **grant** level leaves packed machines at the console (the outpost gizmos that already exist) for the owner to
  set up where they like.

## The hangar and the ship

One ship per account. The owner lands a ship on the hangar pad and registers it; from then on it is saved every time it
lands there and at round end if it is home, and it stands on the pad when the outpost next lands. Building a ship up
over rounds is the point.

- The saved state is the last landing. Damage flown home is kept.
- A registered ship that is destroyed away from home is **lost**. It does not come back.
- **Ship insurance** stores the ship as it stands on the pad, for a share of its value. A claim brings that ship back
  on the pad for a share of the insured value and uses the policy up, whether the ship was lost or only damaged.
  Hangar levels lower both shares (25% and 20% down to 10% and 10%).
- A stored ship has no deed, so it can't be sold at a shipyard. A ship registered in the round it was bought still has
  its deed; selling it forgets it.

The pad starts 9 tiles across and goes to 13, 17 and 21 with the Hangar's levels; each level hands out foundation
plates to widen the apron.

## Two kinds of hangar

Both have cranes and a refit console along the back of their apron.


Saved ships invite omniships: one hull built up over many rounds with every gun and every system. The answer is two
hangars with different rules.

- The **faction hangar** (the Hangar module) only takes stock shipyard hulls (`VesselComponent`), and only ones the
  owner's company may fly when the listing names companies. Its refit console swaps guns on the ship's own hardpoints,
  up to what those hardpoints hold at their size, and fits strongboxes; nothing else. Its ships are known quantities.
- The **shipwright bay** (the Shipwright module) takes custom ships, and holds them to a **refit points** budget, after
  Starsector's refit screen.

One ship per account still: it lives in whichever bay it was registered at.

## The shipwright bay

The bay prefab is a 25-tile apron with a beacon, two drydock cranes along its back and the shipwright console. The
cranes are the Drydock module's: every build and refit is a crane job, so it is seen happening, with the arms swinging
out, sparks and tool sounds, and it stops if the hull leaves the pad.

**Refit points.** A hull's budget is its frame's points, or for a hull built by hand 8 plus a point per 10 floor tiles
(at most 30); the bay's levels add 0, 8 and 16. Guns take points by the hardpoint size they need (1, 2, 4, 8, 16 from
superlight to superheavy). Systems take their own points:

| Category | Systems |
|---|---|
| Defence | Light shield, shield, heavy shield (one shield) |
| Industry | Mining laser and mining beam (on hardpoints), salvage magnet, tractor beam, ore processor, autolathe, fabricator |
| Utility | Medbay, cryo pod, cargo telepad, research server, boarding harpoon (on a hardpoint) |
| Mobility | FTL drive, long-range FTL drive |

Every number is in `Resources/Prototypes/_WF/Hideouts/shipwright.yml`. Points are shared across categories for now; the
role split (Combat, Industry, Logistics) is the next step once the totals feel right.

**Frames.** 26 stock hulls from the civilian and faction shipyards, from the Rook (51 tiles) to the Tanuki (346), at
about 60% of the ship's price. The largest frame is the ceiling: no shipwright ship's budget goes past its 35 points,
however many the bay's levels add, and no hand-built hull bigger than its floor registers. The cranes
build a frame from a single keel tile: every tile, then everything that was anchored on it, without its guns and
systems. A frame refits at half a repair's crane time, a hull built by hand at one and a half, so frames are the fast
road and custom hulls the free one.

**Hardpoints.** Mounts are part of the refit too. The owner swaps a hardpoint for another size or type (its gun comes
off with it), takes one off, or places a new one on a free tile on the hull's edge, picked on the console's plan of the
hull. A hardpoint takes points of its own by size (1, 1, 2, 3, 6) and costs 1,500 to 25,000 to lay; a frame's own
hardpoints take none until swapped, which is part of what makes frames the cheap road. A hull carries at most two
hardpoints plus one per 12 floor tiles, or its frame's own count if that is more.

**Cargo and the strongbox.** A hangar ship keeps its structure, machines and fittings, not its cargo: crates, loose
items and what lockers and other storage hold are taken off when it comes back. The ship strongbox (a Utility system, 3
points, two at most) is the exception: an 8 by 6 store bolted to the deck whose contents come back with the ship. It
saves far less often than a safety deposit box, only when the ship lands in its hangar, and it keeps out what a deposit
box keeps out and what an outpost already keeps. What it keeps is stamped (`WFShipSaveOriginComponent`), so outposts,
deposit boxes and round-end persistent items don't keep it as well.

**Hand-built hulls.** Any hull landed on the bay can be registered: one the owner holds, or one nobody owns. Everything
aboard it that counts (guns on hardpoints, anything a system recognises) is "not fitted" until the owner pays to have
it **certified**: the cranes look each part over, for 750 a part, and only if it all fits the points.

**What stops the cheat.**
- A gun the shipwright didn't fit or certify won't fire on a shipwright ship.
- A shipwright ship that carries anything unfitted, or goes over its points or a system's limit, isn't saved when it
  lands. The last save that passed is what comes back next round, so building a gun on by hand mid-round costs the
  owner nothing permanent and gains them nothing permanent.
- Counting goes through containers, so a gun packed in a crate still counts.
- Refits cost money: the part (guns at 1.5 times their price) plus labour for every crane item, removals included.

**Upgrades.** Both refit consoles also sell the drydock's refit lines on the parked ship: durasteel armour plating,
thruster, gyroscope and power part tiers, and atmospheric thrusters, fitted by the cranes at the drydock's prices.
Anything else a ship needs is built aboard by hand, and certified at the bay on a custom ship.

**The console.** The bay console lists the hull's hardpoints with a gun picker each, the systems with a count each, the
points before and after, and the price, and enables Apply when the refit fits. With the bay empty it lists the frames.

## Outposts

A module stake used in hand outlines its building on the tile in front of its user, facing the way they face, for ten
seconds (the outline drawn for a save on its way down), or says why it wouldn't fit. Used on that tile, it lays it.

## Prefabs and themes

The module prefabs are drawn in `Tools/_WF/Hideouts/build_prefabs.py` after the planetary hospital: rooms with a floor
each, windows along the outer walls, a porch out front, and furniture, clutter and floor decals (hazard lines, marked
bays, scuffs) so a module looks lived in the moment it lands. `Docs/_WF/Hideouts/prefabs.png` is the review sheet.
A prefab's decals are laid with it, turned the way it is; a shell leaves them out with the furniture.

A module has several layouts (a level's `layouts` in `modules.yml`): alt-click the stake and pick one under Layout.
Every layout of a module holds the same machines; they differ in rooms and furniture. Quarters come as a bunkhouse, a
cabin or a dormitory; the workshop as a machine shop or a garage; the powerhouse as a battery house or a generator
shed; the medbay as a ward or a clinic; the vault as a strongroom or a counting house; the company wing as a hall or a
lodge.

Living spaces are modules of one level each, with two layouts apiece: a canteen (mess hall, diner), a farm (greenhouse,
open field), a garden (courtyard, walk), a lounge (bar, rec room) and a library (reading room, study).

An APC hangs on the wall beside its room, as the hospital's do, with its cables under that wall, and the MV feed comes
out at the front door for the owner to tie in. The generator stops if a cable doesn't reach the APC.

A stake lays its module in a theme (`wfHideoutTheme`, `themes.yml`): alt-click the stake and pick one under Materials.
A theme swaps the prefab's walls, windows, doors and its floors (main, accent, clean and the porch) as they are laid,
for its price on top of the module, taken when the stake is used and paid back if the module can't go there. The
prefabs are drawn in the colony materials, the theme that costs nothing; the others are brick and parquet, a timber
lodge, station steel, concrete and reinforced plating. Machines, furniture, cranes and pads are the same in each.

## The blueprint

The console's Blueprint tab is a plan of the outpost to paint on, after a map editor: its floor drawn tile by tile,
what stands on it marked (walls, windows, doors, other things), and the claim's edge. A palette of brushes
(`wfBlueprintBrush`, `blueprint.yml`) lists floors, walls and windows, doors, power and light, furniture, and the roof
and removal tools. Left paints with the brush, box or outline tool, right drags the view, the wheel zooms; entity
brushes face the way the Facing button says. Strokes collect on a plan, with undo, up to 800 at a time, and the plan's
price shows as it grows. Build plan has it built at once, paid from the builder's bank.

- Floor painted over the outpost's floor re-lays it; painted past its edge it extends the outpost, under the rules
  foundation plates follow (inside the claim, open ground, the span and the tile cap).
- An entity brush sets its entity on a floor tile nothing else stands on, within the object cap. Cables and carpets
  lie under whatever else is there.
- Roof and open-sky brushes set a tile's roof. The removal brush takes away what the brushes lay, nothing else, and
  pays nothing back.
- The whole plan's price is taken first; what a refused stroke cost is paid back, and the first refusal is reported.
- A brush costs its listed price, and an entity brush never less than one and a half times what its entity sells for,
  so nothing painted sells at a profit.

The owner and the outpost's listed builders can build from it.

The palette borrows from the mapping tools: a search box, a section filter, and each brush shown with its own
sprite. Besides the brushes listed by hand (`wfBlueprintBrush`), catalogue shelves (`wfBlueprintCatalogue`,
`blueprint_catalogue.yml`) list entities, floor tiles and decals straight from the game's prototypes, each a brush
named as its prototype is, at the shelf's price: seating, more furniture, plants and trees, street lights and lamps,
statues and fountains, fences and railings, fires and hearths, banners and curtains, wall art (paintings, posters,
signs), rugs, and floor decals (markings, trim, flowers and bushes, wear). Adding a decoration is one line on a shelf.
A shelf marked `under` shares a tile with whatever else stands there: rugs, and what hangs on a wall. Decals are laid
turned the way the plan faces, up to eight a tile, and a Remove decals brush takes them off.

The shipwright console and the faction hangar's console have an Interior button that opens the same blueprint over
the hull in the bay. On a ship, floor only re-lays the hull's own tiles and roofs can't be painted; walls, doors,
furniture, decorations and decals go anywhere on it. Nothing a brush lays is a gun or a system, so the fit and its
points are untouched.

## Saving and loading

A hideout keeps itself: it is given a free save slot of its owner's without a manifest being filed, so the console's
Manifests tab is hidden while hideouts are on. Where every slot already holds another outpost it is not saved.

Serialising an outpost costs main-thread time for every object on it, so an autosave is spread over ticks
(`WFOutpostSystem.SlicedSave`), the way a sold hull is copied. The first tick takes the outpost itself and everything
loose on it, so what moves is saved as it stood that tick; the anchored structures, which are most of an outpost,
follow at 3 ms a tick, each with what it holds. A save is torn if a structure is gone before its turn or something
already saved has since moved into one; a torn save is dropped and tried again in 30 seconds, and after two the
outpost is saved at once. The round's last saves are taken at once. In the integration test a warmed-up save of 136
objects took 46 ms over 10 ticks, 13 ms the longest, against 54 ms in one tick saved at once.

Loading is still done at once, on a paused staging map, when the owner's outpost lands: about 0.16 ms an object, once
a round for each outpost. The engine's deserialiser has no way to load part of a file, so spreading it would mean
saving an outpost as several files and landing them in turn.

## Trade

Every outpost founded from the registry comes with a hideout trade hub packed at its console. It runs off its own
supply, like the hangar beacon, so the Trade tab works under open sky before there is any power. The Trade tab no
longer lists the wall-panel crate or the foundation crate, which the blueprint replaces, nor the trade hub, which
could only be ordered with a hub already standing.

## Testing

`wfhideouttest` (admin) builds a world of its own, stands the caller on it beside a founded outpost with every module
built, the shipwright bay (at its widest), the faction hangar, quarters and workshop laid, a stock Guppy on the hangar
pad to register, and 50 million thalers.

`wfhideouttest gallery` builds the same world as a gallery instead: paved streets round the outpost with every layout
of every module along them (the two aprons left out), each laid in the next material theme, a survey stake of every
module at the caller's feet to try layouts and themes with, and the same money for the Blueprint tab. Either way the
command grows the ground under its patch first, since a world only grows ground near players.

## Company outposts

Built so far: an outpost founded by a member of a company is that company's (Outposts already does this), and modules
marked `company` only build there. The Company wing is the one company module.

Planned, in the order they are worth building:

1. **Shared purse.** A company outpost's levels are paid from the company account (`WolfgateCompany`), by members
   whose rank allows it, so the sink is a team effort.
2. **More hangars.** One hangar ship per account stays the rule; a company outpost gets a Hangar annex module whose
   levels add pads, each registered to a member. Needs the stored ship keyed to a pad as well as an account.
3. **Company-only top levels.** A fourth Powerhouse and Defences level, a drydock bay that repairs a parked ship over
   time, a trade office that posts the company's contracts.
4. **Themes.** A theme swaps the walls, floors and furniture a prefab is laid with (frontier timber, corporate white,
   military concrete, salvage). The prefab layouts stay the same; `build_prefabs.py` writes one grid per theme from the
   same layout, and the module level picks the grid by the outpost's theme. Personal outposts get the plain themes for
   a fee, companies get their own colours.
5. **Standing.** A company outpost counts double toward its world's development (Colonies), so a company has a reason
   to settle one world rather than spread.

## Open questions

- Planet birds (`WFMobBirdVerdaSongbird`) trip a PVS "empty state" error log during long planet tests; the hideout tests
  clear them off their world. The cause in the Birds module is not found yet.
- The outpost save cost grows with sprawl: a maxed outpost can hold over 4,000 objects, about a second of main-thread
  work per save at Outposts' benchmark. Saves may need to be spread over ticks.

- Refit roles: one shared pool of points today; splitting it by role is planned.
- A frame build that is called off leaves the half-built hull on the pad, registered or not. It can be registered
  and refitted like any hull, but nothing finishes the frame for it.
- Frames are rebuilt from prototypes, so device links on the stock ship (door buttons, for instance) don't come with
  them.

- Object caps. Groundworks, the Vault and the Company wing raise the object cap from 2,000 to as much as 4,250. A save
  costs about 0.21 ms an object on the main thread (Outposts' save benchmark), so a full company outpost is a hitch of
  nearly a second on every save. The cap bumps may need to come down, or saves need to be split across ticks.
- Prefabs have no preview outline yet. The stake refuses with a reason; an outline like the save-load one
  (`WFOutpostPendingLoadOverlay`) would be better.
- A prefab can't be moved once laid. Its parts can be taken apart like anything built by hand.
- Insurance prices the ship at what a save keeps of it (no cash, no mobs). Cargo aboard counts, so insuring a full hold
  is a way to keep cargo; whether that is wanted or needs a cargo exclusion is undecided.
- Selling an outpost pays for its anchored structures, so a prefab laid from a paid level sells for a fraction of the
  level's price. Levels are not refunded.
