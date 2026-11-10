# Hideouts design

What the `Hideouts` module is for, what is built, and what comes next. The module README
(`Content.Server/_WF/Hideouts/README.md`) says how the built part works.

## Goal

A player's outpost is their home: somewhere secluded on a world of their choosing that they improve over many rounds,
that keeps one ship of theirs, and that soaks up money for as long as they play. The model is Escape from Tarkov's
hideout: a fixed list of modules, each with levels, each level with a price, a build time and other modules it needs.

Outposts (PR 570) already give the base: founding, building, saves, access, pads, presets, raids, world development.
Hideouts adds the progression on top and changes nothing about how an outpost is founded or saved.

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

**The console.** The bay console lists the hull's hardpoints with a gun picker each, the systems with a count each, the
points before and after, and the price, and enables Apply when the refit fits. With the bay empty it lists the frames.

## Outposts

A module stake used in hand outlines its building on the tile in front of its user, facing the way they face, for ten
seconds (the outline drawn for a save on its way down), or says why it wouldn't fit. Used on that tile, it lays it.

## Testing

`wfhideouttest` (admin) builds a world of its own, stands the caller on it beside a founded outpost with every module
built, the shipwright bay (at its widest), the faction hangar, quarters and workshop laid, a stock Guppy on the hangar
pad to register, and 50 million thalers.

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
