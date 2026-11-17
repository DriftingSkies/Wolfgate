## Issues checker: one title and one fix hint per rule in WGMappingIssueCatalog.

## Ship rules

wg-mapping-issue-ship-grid-count = A ship is one grid on one level; this map has {$grids} {$grids ->
        [one] grid
       *[other] grids
    } on {$levels} {$levels ->
        [one] level
       *[other] levels
    }.
wg-mapping-issue-ship-grid-count-hint = Delete the other grids and levels, or export as a map instead.

wg-mapping-issue-ship-no-name = The grid has no name of its own.
wg-mapping-issue-ship-no-name-hint = Name the grid in its properties; the export uses the map name while it is unnamed.

wg-mapping-issue-ship-no-floor = No floor tile a crew could stand on.
wg-mapping-issue-ship-no-floor-hint = Lay plating or a floor; a ship with only space tiles can't be boarded.

wg-mapping-issue-ship-no-console = No shuttle console.
wg-mapping-issue-ship-no-console-hint = Anchor a shuttle console; without one the ship can't be flown.

wg-mapping-issue-ship-no-dock = No docking port.
wg-mapping-issue-ship-no-dock-hint = Place a shuttle airlock on the hull so the ship can dock with a station.

wg-mapping-issue-ship-no-thruster = No thruster.
wg-mapping-issue-ship-no-thruster-hint = Place at least one thruster facing out from the hull.

wg-mapping-issue-ship-no-gyroscope = No gyroscope.
wg-mapping-issue-ship-no-gyroscope-hint = Place a gyroscope; without one the ship can't turn.

wg-mapping-issue-ship-no-power-source = No power source.
wg-mapping-issue-ship-no-power-source-hint = Place a generator, APU, solar panels or an IRE to feed the power net.

wg-mapping-issue-ship-no-apc = No APC.
wg-mapping-issue-ship-no-apc-hint = Place an APC and wire it; machines and lights draw from it.

wg-mapping-issue-ship-no-battery = No SMES or substation.
wg-mapping-issue-ship-no-battery-hint = Place an SMES or a substation between the power source and the APC.

wg-mapping-issue-ship-no-atmos = No air alarm or gas pipes.
wg-mapping-issue-ship-no-atmos-hint = The ship keeps the air it is saved with; add a distribution loop if the crew needs more.

wg-mapping-issue-ship-no-spawn-point = No spawn point.
wg-mapping-issue-ship-no-spawn-point-hint = Place a spawn point marker where the crew should appear.

wg-mapping-issue-ship-gun-off-hardpoint = {$name} has no compatible hardpoint on its tile.
wg-mapping-issue-ship-gun-off-hardpoint-hint = Anchor a hardpoint of the right class and size under the gun; fire control refuses it otherwise.

wg-mapping-issue-ship-appraisal-zero = The ship appraises at nothing.
wg-mapping-issue-ship-appraisal-zero-hint = Add the parts a ship sells for; an empty grid can't be priced.

wg-mapping-issue-ship-price-floor = The price {$price} is under the floor of {$floor} (appraisal {$appraisal}).
wg-mapping-issue-ship-price-floor-hint = Raise the price to at least the floor, or the shipyard check refuses the vessel.

## Map rules

wg-mapping-issue-map-level-empty = Level {$depth} has no grid.
wg-mapping-issue-map-level-empty-hint = Build on the level or remove it.

wg-mapping-issue-map-doubled = {$count} × {$name} anchored on one tile.
wg-mapping-issue-map-doubled-hint = Erase the extra copies; doubled walls and cables break construction and power.

wg-mapping-issue-map-anchored-no-tile = {$name} is anchored over space.
wg-mapping-issue-map-anchored-no-tile-hint = Lay a tile under it or erase it; it falls off the grid when it loads.

wg-mapping-issue-map-loose-entity = {$name} is off the grid, loose in space.
wg-mapping-issue-map-loose-entity-hint = Move it onto a grid or erase it; a ship file never holds it.

wg-mapping-issue-map-unsaved = {$name} won't be saved with the map. {$spawner ->
        [none] No spawner places it.
       *[other] Place a "{$spawner}" instead.
    }
wg-mapping-issue-map-unsaved-hint = Mobs and other save:false prototypes are dropped by the map file; use a spawner marker.

wg-mapping-issue-map-denied = {$name} isn't allowed in mapping mode.
wg-mapping-issue-map-denied-hint = Erase it; it is removed when the map is opened again.

wg-mapping-issue-map-lint = {$name} ({$id}) can't be mapped here.
wg-mapping-issue-map-lint-hint = The repository's map check refuses it, so the export is refused too. Erase or replace it.

## Closing note of a capped list

wg-mapping-issue-more = {$count} more {$count ->
        [one] issue
       *[other] issues
    } not shown.
wg-mapping-issue-more-hint = Fix the ones above and check again.
