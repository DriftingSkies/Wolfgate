## Issues checker: one title and one fix hint per rule in WFMappingIssueCatalog.

## Ship rules

wf-mapping-issue-ship-grid-count = A ship is one grid on one level; this map has {$grids} {$grids ->
        [one] grid
       *[other] grids
    } on {$levels} {$levels ->
        [one] level
       *[other] levels
    }.
wf-mapping-issue-ship-grid-count-hint = Delete the other grids and levels, or export as a map instead.

wf-mapping-issue-ship-no-name = The grid has no name of its own.
wf-mapping-issue-ship-no-name-hint = Name the grid in its properties; the export uses the map name while it is unnamed.

wf-mapping-issue-ship-no-floor = No floor tile a crew could stand on.
wf-mapping-issue-ship-no-floor-hint = Lay plating or a floor; a ship with only space tiles can't be boarded.

wf-mapping-issue-ship-no-console = No shuttle console.
wf-mapping-issue-ship-no-console-hint = Anchor a shuttle console; without one the ship can't be flown.

wf-mapping-issue-ship-no-dock = No docking port.
wf-mapping-issue-ship-no-dock-hint = Place a shuttle airlock on the hull so the ship can dock with a station.

wf-mapping-issue-ship-no-thruster = No thruster.
wf-mapping-issue-ship-no-thruster-hint = Place at least one thruster facing out from the hull.

wf-mapping-issue-ship-no-gyroscope = No gyroscope.
wf-mapping-issue-ship-no-gyroscope-hint = Place a gyroscope; without one the ship can't turn.

wf-mapping-issue-ship-no-power-source = No power source.
wf-mapping-issue-ship-no-power-source-hint = Place a generator, APU, solar panels or an AME to feed the power net.

wf-mapping-issue-ship-no-apc = No APC.
wf-mapping-issue-ship-no-apc-hint = Place an APC and wire it; machines and lights draw from it.

wf-mapping-issue-ship-no-battery = No SMES or substation.
wf-mapping-issue-ship-no-battery-hint = Place an SMES or a substation between the power source and the APC.

wf-mapping-issue-ship-no-atmos = No air alarm or gas pipes.
wf-mapping-issue-ship-no-atmos-hint = The ship keeps the air it is saved with; add a distribution loop if the crew needs more.

wf-mapping-issue-ship-no-spawn-point = No spawn point.
wf-mapping-issue-ship-no-spawn-point-hint = Place a spawn point marker where the crew should appear.

wf-mapping-issue-ship-gun-off-hardpoint = {$name} has no compatible hardpoint on its tile.
wf-mapping-issue-ship-gun-off-hardpoint-hint = Anchor a hardpoint of the right class and size under the gun; fire control refuses it otherwise.

wf-mapping-issue-ship-appraisal-zero = The ship appraises at nothing.
wf-mapping-issue-ship-appraisal-zero-hint = Add the parts a ship sells for; an empty grid can't be priced.

wf-mapping-issue-ship-price-floor = The price {$price} is under the floor of {$floor} (appraisal {$appraisal}).
wf-mapping-issue-ship-price-floor-hint = Raise the price to at least the floor, or the shipyard check refuses the vessel.

## Map rules

wf-mapping-issue-map-level-empty = Level {$depth} has no grid.
wf-mapping-issue-map-level-empty-hint = Build on the level or remove it.

wf-mapping-issue-map-doubled = {$count} × {$name} anchored on one tile.
wf-mapping-issue-map-doubled-hint = Erase the extra copies; doubled walls and cables break construction and power.

wf-mapping-issue-map-anchored-no-tile = {$name} is anchored over space.
wf-mapping-issue-map-anchored-no-tile-hint = Lay a tile under it or erase it; it falls off the grid when it loads.

wf-mapping-issue-map-loose-entity = {$name} is off the grid, loose in space.
wf-mapping-issue-map-loose-entity-hint = Move it onto a grid or erase it; a ship file never holds it.

wf-mapping-issue-map-unsaved = {$name} won't be saved with the map. {$spawner ->
        [none] No spawner places it.
       *[other] Place a "{$spawner}" instead.
    }
wf-mapping-issue-map-unsaved-hint = Mobs and other save:false prototypes are dropped by the map file; use a spawner marker.

wf-mapping-issue-map-denied = {$name} isn't allowed in mapping mode.
wf-mapping-issue-map-denied-hint = Erase it; it is removed when the map is opened again.

## Closing note of a capped list

wf-mapping-issue-more = {$count} more {$count ->
        [one] issue
       *[other] issues
    } not shown.
wf-mapping-issue-more-hint = Fix the ones above and check again.
