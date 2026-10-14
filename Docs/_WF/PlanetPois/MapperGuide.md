# Planet POI mapper guide

## What exists now

The world builds its randomized POI layout and routes before terrain preloading. All non-isolated
sites belong to one connected network. Production tables combine fourteen authored maps with the
existing procedural expedition dungeons. Asclepiu and Merak each have a 288 × 144 city district,
an inhabited through settlement, an abandoned through settlement, a roadside trader and a fuel
service stop. Fervidus, Thrascias, Aerumna and Carcinoma each add one abandoned roadside shelter.
The cities mix an occupied western service core with a derelict eastern quarter; neither copies
Crocus's station or multilevel setup. Fuel services sell real reactor fuel and welding-fuel cans,
plus repair supplies. They do not provide automatic docking or ship refuelling.

Carcinoma retains the haunted cave layout, with exterior airlocks added during dungeon generation.
Procedural layouts are checked for a usable exterior approach before placement. Sealed rolls are
regenerated with deterministic retry seeds, up to sixteen attempts; if every attempt is sealed, that
site is skipped with a warning and the rest of the network is still built. No walls are demolished.

Reusable content already present includes `FloorAsphalt`, concrete and pavement tiles, Mono's
`RoadSlow`/`RoadStop` decals, Frontier planet-base dungeon interiors and the biome/dungeon tile-pinning
systems. These provide surfaces and placement tools; the PlanetPois planner supplies the connections.
Traders should use the existing Wolfgate Traders module rather than a new shop implementation.

## Scale and the Crocus reference

`Resources/Prototypes/_Mono/Maps/crocus.yml` selects `jianghui_industrial_1.yml` as the Crocus ground
map, with level 0 below and levels 2–5 above. Decoding the ground map's version-7 tile chunks gives
non-Space bounds **x = -105..175, y = -46..93**, or **281 × 140 tiles**, with **24,577 non-Space tiles**.
This measures the ground footprint, not the combined floors, building count or populated area.
The enclosing serialized chunk envelope is **288 × 144** tiles; use that convenient rounded size for a city district of roughly Crocus ground size. Do not copy Crocus's
station setup or six-level stack into a surface POI: the authored loader currently stamps one grid.

Current playable ground radii are 384 Asclepiu, 320 Merak, 256 Fervidus and Thrascias, and 192 Aerumna
and Carcinoma. Their diameters are respectively 768, 640, 512 and 384 tiles. At one metre per tile,
these are compact 0.384–0.768 km playable districts, not literal whole planets. Straight-line trips
are hundreds of metres; detours add distance. The bounding rectangle of a Crocus-size city alone
occupies about 9% of Asclepiu's circular area and 13% of Merak's. A city plus villages and open country
fits most comfortably on those two larger worlds. Do not promise multiple Crocus-size cities on a
384-tile-diameter world.

A centered 288 × 144 city has a corner radius of about 161 tiles. Keep its complete footprint inside
the world and away from the arrival area. The live tables guarantee five sites per habitable world and use 96-tile origin spacing.
Through sites reserve an exterior road-clearance ring; a bounded fallback search handles narrow
city placement regions when random attempts miss them. Increase the world
radius if the intended content is several such cities with substantial rural travel between them.
Entry count is not a guarantee of placement when clear land is insufficient.

## Single-grid map contract

Save one grid (`savegrid`, format category `Grid`) under `Resources/Maps/_WF/PlanetPois/`. The file is a POI fragment, not a game-map or
shipyard registration. Use an unrotated grid with local tile coordinates matching the declared
footprint; placement translates those coordinates without random rotation. Declare `mapPath` in
its table entry. Exactly one of `mapPath` and `dungeon` is required.

`footprint` is a local tile rectangle with exclusive upper bounds. `0,0,64,48` covers x 0..63 and
y 0..47. It must contain every authored non-Space tile and everything placed on those tiles;
include courtyards, paths, fences, storage and projecting equipment. Reserve extra space within it
for furniture clearance. The planner reserves this whole rectangle and never cuts a new road
through its interior. Keep scenery and collision-bearing entities inside it.

Sockets describe actual usable entrances, not approximate map centres. `offset` names the border
tile of the footprint and `direction` points outward: West at the left edge, East at right minus
one, South at bottom, North at top minus one. Provide an open floor or usable doorway at each
socket. Match the road's full width, including shoulders, with a clear approach and enough clear
border tiles on either side of the socket. For the production paved roads, that is five carriageway
tiles plus one shoulder on each side: seven clear tiles. Avoid sockets at corners. Keep walls,
locked doors, pumps, rocks, fencing and spawn points away from the approach.

- `Roadside`: a branch reaches the entrance. Put the building and service forecourt beside the road;
  vehicles must not drive through the building to continue along the network. A fuel station uses
  this mode, with pumps set back from the entrance and enough room to enter, turn and leave.
- `Through`: at least two distinct sockets, preferably on opposite sides. Author a continuous open
  street between them at the configured width. A village entrance on one side and exit on another
  belongs here. The planner connects the external approaches; it does not build the internal street.
- `Isolated`: intentionally disconnected. Use only for places whose isolation is part of the design,
  such as an inaccessible ruin. Omitting the field defaults to `Roadside`.

Roof buildings deliberately and leave streets, courtyards and forecourts unroofed. The loader
preserves explicit source `Roof` tiles; it does not roof an entire settlement. Weather exposure
therefore follows the authored roof coverage. Enclosed hazardous-world rooms also need their own
working life support and pressure boundaries; a roof flag is not atmospheric protection.

Remove `Station`, `StationData`, `StationJobs`, station spawning/ownership setup, `CEStationZLevels`,
`Shuttle`, `FTL`, ship navigation, docking and other shuttle-only controllers. Avoid latejoin or
job spawn points and round-start station setup. The planet supplies map atmosphere, lighting,
gravity and its z-stack. Do not give the temporary source map a competing world network. Grid
physics and mapper-generated supporting components are fine; furniture, doors, lighting, local
power and an existing trader are ordinary POI contents. Do not add an independent `gameMap` entry.

## Adding content

The following example is a mapping specification, **not an active prototype**. Its proposed map
path must be created and verified before copying it into production. The through-village map
would contain x 0..63 and y 0..47, with a seven-tile-clear street centred on y 24 across the map.

```yaml
- type: wfPlanetPoiTable
  id: WFPoisExampleVillage
  minCount: 1
  maxCount: 1
  roads:
    tile: FloorAsphalt
    width: 5
    shoulderTile: FloorConcrete
    shoulderWidth: 1
  entries:
  - mapPath: /Maps/_WF/PlanetPois/asclepiu_village_01.yml
    name: wf-poi-asclepiu-village
    footprint: 0,0,64,48
    minCount: 1
    maxCount: 1
    connection: Through
    sockets:
    - offset: 0,24
      direction: West
    - offset: 63,24
      direction: East
```

For a roadside service map of 40 × 32 tiles, use `footprint: 0,0,40,32`, `connection: Roadside`
and an entrance such as `[20, 0]` facing `South`. Author the forecourt behind that entrance,
the service building farther to one side, and a turning area. The exterior route must remain
outside the reserved rectangle. A city entry should use `minCount: 1` and `maxCount: 1` when the
world's site budget includes one guaranteed city. Entry guarantees are drawn before weighted
choices; `weight` controls additional selections, while `maxCount` caps duplicates. Raise the
table count budget to include the sum of guaranteed entries. Include localized signal names in
`Resources/Locale/en-US/_WF/PlanetPois/pois.ftl`.

Existing dungeon entries remain valid without new fields. Their reserved space and inferred
entrances provide expedition connections; authored socket layouts are the way to guarantee a
specific village street or service driveway. Register new map paths only once the files exist.
Maps inside `_WF/PlanetPois` are discovered by the module generator; regenerate module
documentation after adding them. Only maps outside `_WF` need an `unmarked` entry. New themes usually need only a map, one localized name and one table entry.

## Road dressing

A table's `roads` block may name a `dressing` (`wfRoadDressing`, in `road_dressing.yml`). It is applied to the
generated network only, never inside a site's footprint, so a site furnishes its own streets.

- `kerbs` maps where the road lies from a shoulder tile (`North`, `SouthEast`, `InnerNorthWest`, ...) to the tile
  that shows a kerb on that side. A case left out keeps the network's `shoulderTile`.
- `lineNorthSouth` and `lineEastWest` are the decals drawn down the middle of straight stretches, every
  `lineEvery` tiles.
- `light` stands on the shoulder every `lightSpacing` tiles on alternating sides; `brokenLight` replaces it
  `brokenLightChance` of the time. `junctionLight` stands on two corners where three or more roads meet.
- `scatter` rules drop pieces along the straight stretches: `where` is `Lane`, `Kerb` (the lane against the kerb, for
  vehicles) or `Shoulder`, `per100` is how many
  per hundred tiles of road, `axis` limits a rule to roads running `NorthSouth` or `EastWest`, and `align` turns the
  piece to point along the road. The tiles a piece covers come from its fixtures.
- `decals` rules scatter marks the same way.

RMC's civilian vehicle props are drawn from the side only. Sound ones belong on `EastWest` roads; wrecks may go
anywhere, since a wreck slewed across a lane reads well. The `WFRoadProp*` army vehicles are drawn from every side
and can be aligned on any road. A rule with `align` must not name a prototype whose `Transform` has `noRot`
(`PlanetRoadDressingTest` checks the shipped ones). A piece is only placed where every tile it covers is road (or
shoulder), none is the middle tile of the road, and no other blocking piece is within seven tiles.

The generator draws a site's own streets with the same asphalt and pavement tiles (`map_author.py`, `road()`), so a
site meets the network without a seam.

## Shipped map pack

Both habitable tables guarantee one city, one inhabited settlement, one abandoned settlement,
one trading post and one fuel service. Positions and connecting routes change between rounds.
The four hazard tables each guarantee their authored shelter and retain randomized expedition sites.

- Asclepiu: `asclepiu_city` (288 × 144), `asclepiu_garden_village` (64 × 48),
  `asclepiu_derelict_village` (80 × 56), `asclepiu_roadside_trader` (40 × 32),
  `asclepiu_fuel_service` (48 × 40).
- Merak: `merak_caravan_city` (288 × 144), `merak_caravan_camp` and
  `merak_abandoned_work_camp` (64 × 48 each), `merak_roadside_trader` (40 × 32),
  `merak_fuel_service` (48 × 40).
- Other worlds: `fervidus_rim_shelter`, `thrascias_survey_shelter`, `aerumna_salvage_yard`
  and `carcinoma_ruin_shelter` (48 × 40 each), all abandoned.

Actual game-sprite previews: [Asclepiu city](Previews/asclepiu_city.webp),
[Merak city](Previews/merak_caravan_city.webp), [garden village](Previews/asclepiu_garden_village.webp)
and [Merak fuel stop](Previews/merak_fuel_service.webp). These are static map renders, without
runtime planetary weather or generated exterior roads. Regenerate with `Content.MapRenderer`
after changing a map; the optional Python previews are simpler tile plans.

## Content roadmap

The generated layouts below are starter authored content. Their shape, equipment and services are
explicit; mapper refinement should focus on varied interiors, richer signage, decoration and playtesting.

1. **Asclepiu city district, 288 × 144.** One per world; two to four clear road exits, with a complete
   authored internal street network. Add a civic square, clinic, housing, small industry and trader
   frontage. Use maintained asphalt/concrete, grass and shoreline vegetation. The lore describes a
   stable breathable biosphere, nutrient-rich shorelines and old nuclear survey signatures; ruins
   and research fit beside a living settlement. Keep the arrival zone separate.
2. **Asclepiu through villages, 64 × 48 and 80 × 56 variants.** Opposite-side road sockets, a main
   street, several buildings and an outdoor meeting space. Produce at least two layouts before
   adding more weighted copies, so repeated settlements are visibly different.
3. **Habitable-world trader posts, 40 × 32.** One roadside driveway and a turning yard, covered
   shop/counter and storage. Use the existing trader system and make stock/services an explicit
   content decision. On Merak, use portable structures, dust shields and freight storage.
4. **Habitable-world fuel/service stops, 48 × 40.** One roadside socket; pumps, parking and repair
   space off the through route. Choose and verify actual usable fuel supplies and repair equipment,
   rather than relying on decorative pumps. Add sheltered stock and a trader only where intended.
5. **Merak caravan town/district, up to 288 × 144; through camps, 64 × 48.** Paved main road with
   concrete shoulders, dusty forecourts, water storage and flexible modular buildings. Lore says
   permanent settlements were ruled out by severe earthquakes: make this a temporary logistics
   town or abandoned settlement rather than an unexplained permanent metropolis. Two or more
   exits must join a traversable authored street.
6. **Fervidus extraction/refuge stations, 48 × 40 to 80 × 56.** Dark pavement tracks, basalt scenery,
   enclosed work rooms, cooling and airlocks. Two sockets for transit depots, one for a roadside
   mining station. Avoid open residential villages: heat, lava and toxic atmosphere dominate.
7. **Thrascias research shelters, 48 × 40 to 64 × 48.** Cleared-snow routes, insulated labs, thermal
   equipment and pressure-safe entrances. The lore is a Frezon gas giant; Wolfgate deliberately
   implements a frozen ground layer. Treat these as that gameplay abstraction, not a claim that
   the original lore has a natural solid surface.
8. **Aerumna expeditions, 40 × 32 to 64 × 48.** Chromite paths and protected industrial/cave access.
   Current Wolfgate terrain is chromite with cave clearings and 3g gravity, while the older guide
   describes Chimera biomass. Resolve that discrepancy before building a settled city theme.
9. **Carcinoma salvage sites, 40 × 32 to 64 × 48.** Dirt tracks, flesh encroachment, abandoned
   infrastructure and quarantine barriers. Roadside or Through when connected; explicitly
   Isolated only for intentionally unreachable horror sites. This world is unsanctioned.

## Reproducing and refining the maps

Run `python Tools/_WF/PlanetPois/generate.py` from the repository root to regenerate the fourteen maps
and live table entries from `cities.py`, `settlements.py` and the shared `map_author.py` helpers.
The generator requires PyYAML; `--check` compares the generated files without rewriting them.
Optional `--preview bin/poi-map-previews` also writes tile-plan PNGs and requires Pillow.
Generation checks for overlapping fixtures and inaccessible doors or road entrances. Review the
actual game renders and playtest before accepting layout changes.
Edit the layout source for repeatable changes; direct mapper edits require an explicit decision
about retaining that map outside future generation so they are not overwritten.

The two city sources intentionally use five-wide trunk streets and three-wide local loops,
with four cardinal exits. Asclepiu uses grass, timber homes, clinics, markets, gardens and derelict
industrial blocks. Merak uses desert ground, modular steel cabins, freight and water storage,
with abandoned old buildings reflecting the earthquake lore. Inhabited means operating traders
and furnished usable spaces; it does not promise a simulated civilian population.

Through villages and caravan camps include opposite-side exits and accessible buildings on each
side of the street. Abandoned variants retain salvage and broken roofs without resident traders.
Roadside service maps keep the building and loading yard beside a single branch driveway.
Fuel and repair merchants mirror the existing fuel and tool vendor catalogues through the Trader
system; they do not depend on a mapped powered vending machine. Hazard shelters are exploration sites, not safe breathable
settlements, and keep the corresponding world's terrain and route palette.

## Fixture and acceptance

`/Maps/_WF/PlanetPois/Tests/through_service.yml` is a **test-only geometry fixture**, not a village
or service station. It contains a 16 × 12 concrete rectangle, an asphalt through strip at y 5..7,
West `[0,6]` and East `[15,6]` sockets, and roofing only at x 5..7/y 1..3 beside the street. It has
one anchored wall at [6,2] and a road marking at [8,6], but no trader, pumps, furniture or production entry. It exists to exercise stamping, sockets
and selective roof copying with known coordinates.

Before enabling a finished map: inspect it in the mapper, walk through every socket and internal
route, verify roofs and weather exposure, test doors/power/air/traders/fuel, and place multiple
variants in a randomized world. Confirm streets do not overwrite structures or protected terrain
and that all non-isolated signals can be reached along routes. Start a headless server and inspect
errors, run the Release YAML linter, and check the PR's CI results. For changed Client/Shared
behavior, run a real client and check for sandbox violations. A successful YAML parse alone does
not establish playability or service functionality.
