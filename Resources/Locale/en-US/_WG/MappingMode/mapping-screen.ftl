## Mapping screen: modes, palette, panels and readout

wg-mapping-screen-mode-place = Place
wg-mapping-screen-mode-erase = Erase
wg-mapping-screen-mode-select = Select
wg-mapping-screen-mode-fill = Fill
wg-mapping-screen-mode-shape = Shape
wg-mapping-screen-mode-pick = Pick
wg-mapping-screen-mode-template = Template
wg-mapping-screen-mode-pipe = Pipe
wg-mapping-screen-mode-wrench = Tools
wg-mapping-screen-mode-current = Mode: {$mode}
wg-mapping-screen-rail-button = {$key} {$mode}

wg-mapping-screen-mouse-place = Left: place. Right: erase what's under the cursor. Middle: eyedropper.
wg-mapping-screen-mouse-erase = Click: erase one. Drag: erase an area. Right click: list what's under the cursor, or cancel the drag. Pick what goes in Properties.
wg-mapping-screen-mouse-select = Click an entity or a tile to select it, Shift+click to add or take it out, drag a box, drag the selection to move it.
wg-mapping-screen-mouse-fill = Click: fill the connected tiles of that kind with the selected tile.
wg-mapping-screen-mouse-shape = Drag: draw a line, rectangle or circle of the selected tile or entity. Shift: square or round. Right click: switch fill and border.
wg-mapping-screen-mouse-pick = Click: pick what's under the cursor and go back to Place.
wg-mapping-screen-mouse-template = Drag the template image; the arrows nudge it.
wg-mapping-screen-mouse-pipe = Drag: lay a pipe along the path. Click: a half. Middle click: paint a pipe's whole network. Right click: cancel, or stop placing a device. R: turn.

wg-mapping-screen-too-many = That covers too many tiles; the most is {$max}.
wg-mapping-screen-needs-grid = Start on a grid. Place a tile first to make one.
wg-mapping-screen-shape-needs-entry = Pick a tile or an entity to draw with first.
wg-mapping-screen-shape-entities-need-grid = Entities need a floor. Draw tiles there first.
wg-mapping-screen-fill-needs-tile = Pick a tile to fill with first.
wg-mapping-screen-fill-truncated = The fill stopped at {$max} tiles.

wg-mapping-screen-tab-entities = Entities
wg-mapping-screen-tab-tiles = Tiles
wg-mapping-screen-tab-decals = Decals
wg-mapping-screen-tab-properties = Properties
wg-mapping-screen-tab-tools = Tools
wg-mapping-screen-tab-layers = Layers
wg-mapping-screen-tab-template = Template
wg-mapping-screen-tab-keys = Keys
wg-mapping-screen-dock-palette = Palette

wg-mapping-screen-undock = Undock
wg-mapping-screen-undock-tooltip = Move this panel into its own window.
wg-mapping-screen-undocked = This panel is in its own window.
wg-mapping-screen-dock = Dock it back

wg-mapping-screen-search = Search...
wg-mapping-screen-filter-all = All
wg-mapping-screen-filter-favourites = Favourites
wg-mapping-screen-filter-recent = Recent
wg-mapping-screen-empty = Nothing here.
wg-mapping-screen-favourite-name = ★ {$name}
wg-mapping-screen-favourite = Favourite
wg-mapping-screen-unfavourite = Unfavourite
wg-mapping-screen-add-quick-select = Add to quick select
wg-mapping-screen-quick-select-star = ★
wg-mapping-screen-recent-strip = Recent
wg-mapping-screen-decal-swatch = {$name} ({$hex})

wg-mapping-screen-section-selected = Selected
wg-mapping-screen-section-mode = Mode
wg-mapping-screen-section-levels = Z-levels
wg-mapping-screen-section-session = Session
wg-mapping-screen-section-layers = Show
wg-mapping-screen-nothing-selected = Nothing selected
wg-mapping-screen-kind-entity = Entity {$id}
wg-mapping-screen-kind-tile = Tile {$id}
wg-mapping-screen-kind-decal = Decal {$id}
wg-mapping-screen-rotation = Facing {$direction}
wg-mapping-screen-rotation-degrees = Rotation {$degrees}°
wg-mapping-screen-session = {$name} · code {$code}

wg-mapping-screen-erase-target = Erase
wg-mapping-screen-erase-entity = Entities
wg-mapping-screen-erase-tile = Tiles
wg-mapping-screen-erase-decal = Decals
wg-mapping-screen-erase-everything = Everything
wg-mapping-screen-erase-under-title = Delete under cursor
wg-mapping-screen-erase-under-entry = {$name} ({$prototype})
wg-mapping-screen-erase-under-more = and {$count} more
wg-mapping-screen-shape-kind = Shape
wg-mapping-screen-shape-line = Line
wg-mapping-screen-shape-rect = Rectangle
wg-mapping-screen-shape-circle = Circle
wg-mapping-screen-shape-fill = Draw
wg-mapping-screen-shape-fill-fill = Fill
wg-mapping-screen-shape-fill-border = Border
wg-mapping-screen-shape-fill-tooltip = Right click in Shape mode switches between fill and border.
wg-mapping-screen-readout-erase = Erasing: {$target}
wg-mapping-screen-readout-line = Drawing: line
wg-mapping-screen-readout-shape = Drawing: {$shape}, {$fill}
wg-mapping-screen-readout-pipe = Piping: {$colour}, layer {$layer}, lone halves face {$facing}{$join ->
    [yes] , auto-join
   *[no] {""}
}{$replace ->
    [yes] , replace
   *[no] {""}
}
wg-mapping-screen-readout-pipe-paint = Painting: click a pipe to paint its network {$colour}
wg-mapping-screen-readout-pipe-device = Placing: {$device}, {$colour}, layer {$layer}, facing {$facing}{$join ->
    [yes] , turns to meet pipes
   *[no] {""}
}

wg-mapping-screen-pipe-colour = Colour
wg-mapping-screen-pipe-preset-distro = Distro
wg-mapping-screen-pipe-preset-waste = Waste (scrubbers)
wg-mapping-screen-pipe-preset-air = Air
wg-mapping-screen-pipe-preset-mix = Mix
wg-mapping-screen-pipe-preset-custom = Custom
wg-mapping-screen-pipe-preset-auto = Auto
wg-mapping-screen-pipe-custom = Hex
wg-mapping-screen-pipe-custom-placeholder = #RRGGBB
wg-mapping-screen-pipe-layer = Layer
wg-mapping-screen-pipe-layer-primary = 1 (primary)
wg-mapping-screen-pipe-layer-secondary = 2 (secondary)
wg-mapping-screen-pipe-layer-tertiary = 3 (tertiary)
wg-mapping-screen-pipe-join = Auto-join
wg-mapping-screen-pipe-join-tooltip = Connect to pipes pointing at the path, and turn a pipe past either end into a T or a four-way.
wg-mapping-screen-pipe-replace = Replace
wg-mapping-screen-pipe-replace-tooltip = Rebuild a plain pipe on the path with the new connections added. Off: leave it as it is.
wg-mapping-screen-pipe-paint = Paint a network
wg-mapping-screen-pipe-paint-tooltip = The next left click paints the clicked pipe and everything connected to it. Middle click does it too.
wg-mapping-screen-pipe-paint-needs-color = Pick a colour to paint with first.
wg-mapping-screen-pipe-devices = Atmos devices
wg-mapping-screen-pipe-devices-hint = Pick one, then left click to place it. Right click goes back to drawing pipes.
wg-mapping-screen-pipe-device-tooltip-suffix = {$name} ({$suffix})
wg-mapping-screen-pipe-facing = Facing: {$facing} (R or Ctrl+wheel turns it)
wg-mapping-screen-pipe-facing-north = north
wg-mapping-screen-pipe-facing-east = east
wg-mapping-screen-pipe-facing-south = south
wg-mapping-screen-pipe-facing-west = west

wg-mapping-screen-layer-walls = Walls
wg-mapping-screen-layer-subfloor = Subfloor (pipes, cables)
wg-mapping-screen-layer-decals = Decals
wg-mapping-screen-layer-markers = Markers
wg-mapping-screen-layer-lights = Lighting
wg-mapping-screen-layer-worldgrid = World grid
wg-mapping-screen-layer-perspective = Depth perspective (lower levels smaller)
wg-mapping-screen-layers-note = Only you see these. Leaving the session puts them back.

wg-mapping-screen-keys-modes = Modes
wg-mapping-screen-keys-mouse = Mouse
wg-mapping-screen-keys-tools = Mapping tools
wg-mapping-screen-keys-ctrl-wheel = Ctrl+wheel
wg-mapping-screen-keys-ctrl-wheel-desc = Turn what you're placing
wg-mapping-screen-key-unbound = (unbound)
wg-mapping-screen-key-WGMappingEnableSelect = Turn selection on or off
wg-mapping-screen-key-WGMappingRotate = Rotate the selection, paste or placement
wg-mapping-screen-key-WGMappingMirror = Mirror left to right
wg-mapping-screen-key-WGMappingMirrorVertical = Mirror top to bottom
wg-mapping-screen-key-WGMappingDelete = Delete the selection
wg-mapping-screen-key-WGMappingCopy = Copy
wg-mapping-screen-key-WGMappingCut = Cut
wg-mapping-screen-key-WGMappingPaste = Paste
wg-mapping-screen-key-WGMappingUndo = Undo
wg-mapping-screen-key-WGMappingRedo = Redo
wg-mapping-screen-key-WGMappingLevelUp = Go up a level
wg-mapping-screen-key-WGMappingLevelDown = Go down a level
wg-mapping-screen-key-WGMappingModeCancel = Cancel, or back to Place
wg-mapping-screen-key-WGMappingQuicksave = Quicksave to your library

wg-mapping-screen-cursor-none = Cursor off the map
wg-mapping-screen-cursor-map = Space {$x}, {$y}
wg-mapping-screen-cursor-cell = Tile {$x}, {$y}
wg-mapping-screen-cursor-grid = Grid {$name} ({$net})
wg-mapping-screen-cursor-tile = Floor {$id}
wg-mapping-screen-cursor-tile-empty = Floor: none
wg-mapping-screen-cursor-entity = {$name} ({$id})

wg-mapping-screen-use-spawner = Use spawner instead
wg-mapping-screen-unsaved-hint = Won't be saved. Use {$spawner}.
wg-mapping-screen-unsaved-use = Use spawner
wg-mapping-screen-unsaved-mark = !

## Menu bar

wg-mapping-screen-bar-mapping = Mapping: session, saving and library
wg-mapping-screen-bar-quicksave = Quicksave ({$key}): save to your library under the session name
wg-mapping-screen-quicksave-wait = Quicksave is ready again in {$seconds}s.
