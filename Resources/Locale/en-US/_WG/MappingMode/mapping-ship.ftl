## Ship downloads

wg-mapping-ship-library-download = Ship
wg-mapping-ship-library-download-tooltip = Download this version as a ship: its grid file and a generated vessel prototype.
wg-mapping-ship-library-download-latest-tooltip = Download the newest version as a ship: its grid file and a generated vessel prototype.
wg-mapping-ship-library-no-ship-tooltip = This version isn't one grid saved as a ship. Press to see why.
wg-mapping-ship-library-version = {$text}, ship grid
wg-mapping-ship-no-grid = That version has no grid, so it can't download as a ship.
wg-mapping-ship-several-grids = That version has {$count} grids. A ship is one grid: delete or merge the others and save again, or use Download for the whole map.
wg-mapping-ship-no-grid-file = That version was saved before ship grids were. Open it and save it again.

## Generated vessel prototype

wg-mapping-ship-vessel-header-made = Vessel prototype made in mapping mode from "{$name}". Check every field before it goes in the game.
wg-mapping-ship-vessel-header-grid = Grid: put {$download} at {$path}
wg-mapping-ship-vessel-header-prototype = Prototype: put this file, {$download}, at {$path}
wg-mapping-ship-vessel-header-guesses = Guessed from the grid ({$tiles} tiles), not set by the mapper: {$fields}. Check them.
wg-mapping-ship-vessel-header-set = Set by the mapper in the export wizard: {$fields}.
wg-mapping-ship-vessel-header-taken = The id {$id} is already a vessel in the game. Rename it.
wg-mapping-ship-vessel-description = Describe the ship here.

## Grids list

wg-mapping-ship-menu-grids-none = No grids on any level.
wg-mapping-ship-menu-grids-count = {$count ->
    [one] 1 grid across your levels
   *[other] {$count} grids across your levels
}
wg-mapping-ship-menu-grid-row = Level {$depth}, {$name}: {$tiles} tiles, {$entities} entities
wg-mapping-ship-menu-goto = Go to
wg-mapping-ship-menu-next-grid = Next grid
wg-mapping-ship-menu-next-grid-tooltip = Go to the next grid, top level first, largest first.
wg-mapping-ship-goto-not-yours = That grid isn't in your session.
