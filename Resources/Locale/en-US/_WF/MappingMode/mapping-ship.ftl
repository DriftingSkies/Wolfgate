## Ship downloads

wf-mapping-ship-library-download = Ship
wf-mapping-ship-library-download-tooltip = Download this version as a ship: its grid file and a generated vessel prototype.
wf-mapping-ship-library-download-latest-tooltip = Download the newest version as a ship: its grid file and a generated vessel prototype.
wf-mapping-ship-library-no-ship-tooltip = This version isn't one grid saved as a ship. Press to see why.
wf-mapping-ship-library-version = {$text}, ship grid
wf-mapping-ship-no-grid = That version has no grid, so it can't download as a ship.
wf-mapping-ship-several-grids = That version has {$count} grids. A ship is one grid: delete or merge the others and save again, or use Download for the whole map.
wf-mapping-ship-no-grid-file = That version was saved before ship grids were. Open it and save it again.

## Generated vessel prototype

wf-mapping-ship-vessel-header-made = Vessel prototype made in mapping mode from "{$name}". Check every field before it goes in the game.
wf-mapping-ship-vessel-header-grid = Grid: put {$download} at {$path}
wf-mapping-ship-vessel-header-prototype = Prototype: put this file, {$download}, at {$path}
wf-mapping-ship-vessel-header-guesses = Guessed from the grid ({$tiles} tiles), not set by the mapper: {$fields}. Check them.
wf-mapping-ship-vessel-header-set = Set by the mapper in the export wizard: {$fields}.
wf-mapping-ship-vessel-header-taken = The id {$id} is already a vessel in the game. Rename it.
wf-mapping-ship-vessel-description = Describe the ship here.

## Grids list

wf-mapping-ship-menu-grids-none = No grids on any level.
wf-mapping-ship-menu-grids-count = {$count ->
    [one] 1 grid across your levels
   *[other] {$count} grids across your levels
}
wf-mapping-ship-menu-grid-row = Level {$depth}, {$name}: {$tiles} tiles, {$entities} entities
wf-mapping-ship-menu-goto = Go to
wf-mapping-ship-menu-next-grid = Next grid
wf-mapping-ship-menu-next-grid-tooltip = Go to the next grid, top level first, largest first.
wf-mapping-ship-goto-not-yours = That grid isn't in your session.
