## Mapping screen: modes, palette, panels and readout

wf-mapping-screen-mode-place = Place
wf-mapping-screen-mode-erase = Erase
wf-mapping-screen-mode-select = Select
wf-mapping-screen-mode-fill = Fill
wf-mapping-screen-mode-shape = Shape
wf-mapping-screen-mode-pick = Pick
wf-mapping-screen-mode-template = Template
wf-mapping-screen-mode-pipe = Pipe
wf-mapping-screen-mode-current = Mode: {$mode}
wf-mapping-screen-rail-button = {$key} {$mode}

wf-mapping-screen-mouse-place = Left: place. Right: erase what's under the cursor. Middle: eyedropper.
wf-mapping-screen-mouse-erase = Click: erase one. Drag: erase an area. Right click: list what's under the cursor, or cancel the drag. Pick what goes in Properties.
wf-mapping-screen-mouse-select = Click an entity or a tile to select it, Shift+click to add or take it out, drag a box, drag the selection to move it.
wf-mapping-screen-mouse-fill = Click: fill the connected tiles of that kind with the selected tile.
wf-mapping-screen-mouse-shape = Drag: draw a line, rectangle or circle of the selected tile or entity. Shift: square or round. Right click: switch fill and border.
wf-mapping-screen-mouse-pick = Click: pick what's under the cursor and go back to Place.
wf-mapping-screen-mouse-template = Drag the template image; the arrows nudge it.
wf-mapping-screen-mouse-pipe = Drag: lay a pipe along the path. Click: a half. Middle click: paint a pipe's whole network. Right click: cancel, or stop placing a device. R: turn.

wf-mapping-screen-too-many = That covers too many tiles; the most is {$max}.
wf-mapping-screen-needs-grid = Start on a grid. Place a tile first to make one.
wf-mapping-screen-shape-needs-entry = Pick a tile or an entity to draw with first.
wf-mapping-screen-shape-entities-need-grid = Entities need a floor. Draw tiles there first.
wf-mapping-screen-fill-needs-tile = Pick a tile to fill with first.
wf-mapping-screen-fill-truncated = The fill stopped at {$max} tiles.

wf-mapping-screen-tab-entities = Entities
wf-mapping-screen-tab-tiles = Tiles
wf-mapping-screen-tab-decals = Decals
wf-mapping-screen-tab-properties = Properties
wf-mapping-screen-tab-tools = Tools
wf-mapping-screen-tab-layers = Layers
wf-mapping-screen-tab-template = Template
wf-mapping-screen-tab-keys = Keys
wf-mapping-screen-dock-palette = Palette

wf-mapping-screen-undock = Undock
wf-mapping-screen-undock-tooltip = Move this panel into its own window.
wf-mapping-screen-undocked = This panel is in its own window.
wf-mapping-screen-dock = Dock it back

wf-mapping-screen-search = Search...
wf-mapping-screen-filter-all = All
wf-mapping-screen-filter-favourites = Favourites
wf-mapping-screen-filter-recent = Recent
wf-mapping-screen-empty = Nothing here.
wf-mapping-screen-favourite-name = ★ {$name}
wf-mapping-screen-favourite = Favourite
wf-mapping-screen-unfavourite = Unfavourite
wf-mapping-screen-add-quick-select = Add to quick select
wf-mapping-screen-quick-select-star = ★
wf-mapping-screen-recent-strip = Recent
wf-mapping-screen-decal-swatch = {$name} ({$hex})

wf-mapping-screen-section-selected = Selected
wf-mapping-screen-section-mode = Mode
wf-mapping-screen-section-levels = Z-levels
wf-mapping-screen-section-session = Session
wf-mapping-screen-section-layers = Show
wf-mapping-screen-nothing-selected = Nothing selected
wf-mapping-screen-kind-entity = Entity {$id}
wf-mapping-screen-kind-tile = Tile {$id}
wf-mapping-screen-kind-decal = Decal {$id}
wf-mapping-screen-rotation = Facing {$direction}
wf-mapping-screen-rotation-degrees = Rotation {$degrees}°
wf-mapping-screen-session = {$name} · code {$code}

wf-mapping-screen-erase-target = Erase
wf-mapping-screen-erase-entity = Entities
wf-mapping-screen-erase-tile = Tiles
wf-mapping-screen-erase-decal = Decals
wf-mapping-screen-erase-everything = Everything
wf-mapping-screen-erase-under-title = Delete under cursor
wf-mapping-screen-erase-under-entry = {$name} ({$prototype})
wf-mapping-screen-erase-under-more = and {$count} more
wf-mapping-screen-shape-kind = Shape
wf-mapping-screen-shape-line = Line
wf-mapping-screen-shape-rect = Rectangle
wf-mapping-screen-shape-circle = Circle
wf-mapping-screen-shape-fill = Draw
wf-mapping-screen-shape-fill-fill = Fill
wf-mapping-screen-shape-fill-border = Border
wf-mapping-screen-shape-fill-tooltip = Right click in Shape mode switches between fill and border.
wf-mapping-screen-readout-erase = Erasing: {$target}
wf-mapping-screen-readout-line = Drawing: line
wf-mapping-screen-readout-shape = Drawing: {$shape}, {$fill}
wf-mapping-screen-readout-pipe = Piping: {$colour}, layer {$layer}, lone halves face {$facing}{$join ->
    [yes] , auto-join
   *[no] {""}
}{$replace ->
    [yes] , replace
   *[no] {""}
}
wf-mapping-screen-readout-pipe-paint = Painting: click a pipe to paint its network {$colour}
wf-mapping-screen-readout-pipe-device = Placing: {$device}, {$colour}, layer {$layer}, facing {$facing}{$join ->
    [yes] , turns to meet pipes
   *[no] {""}
}

wf-mapping-screen-pipe-colour = Colour
wf-mapping-screen-pipe-preset-distro = Distro
wf-mapping-screen-pipe-preset-waste = Waste (scrubbers)
wf-mapping-screen-pipe-preset-air = Air
wf-mapping-screen-pipe-preset-mix = Mix
wf-mapping-screen-pipe-preset-custom = Custom
wf-mapping-screen-pipe-preset-auto = Auto
wf-mapping-screen-pipe-custom = Hex
wf-mapping-screen-pipe-custom-placeholder = #RRGGBB
wf-mapping-screen-pipe-layer = Layer
wf-mapping-screen-pipe-layer-primary = 1 (primary)
wf-mapping-screen-pipe-layer-secondary = 2 (secondary)
wf-mapping-screen-pipe-layer-tertiary = 3 (tertiary)
wf-mapping-screen-pipe-join = Auto-join
wf-mapping-screen-pipe-join-tooltip = Connect to pipes pointing at the path, and turn a pipe past either end into a T or a four-way.
wf-mapping-screen-pipe-replace = Replace
wf-mapping-screen-pipe-replace-tooltip = Rebuild a plain pipe on the path with the new connections added. Off: leave it as it is.
wf-mapping-screen-pipe-paint = Paint a network
wf-mapping-screen-pipe-paint-tooltip = The next left click paints the clicked pipe and everything connected to it. Middle click does it too.
wf-mapping-screen-pipe-paint-needs-color = Pick a colour to paint with first.
wf-mapping-screen-pipe-devices = Atmos devices
wf-mapping-screen-pipe-devices-hint = Pick one, then left click to place it. Right click goes back to drawing pipes.
wf-mapping-screen-pipe-device-tooltip-suffix = {$name} ({$suffix})
wf-mapping-screen-pipe-facing = Facing: {$facing} (R or Ctrl+wheel turns it)
wf-mapping-screen-pipe-facing-north = north
wf-mapping-screen-pipe-facing-east = east
wf-mapping-screen-pipe-facing-south = south
wf-mapping-screen-pipe-facing-west = west

wf-mapping-screen-layer-walls = Walls
wf-mapping-screen-layer-subfloor = Subfloor (pipes, cables)
wf-mapping-screen-layer-decals = Decals
wf-mapping-screen-layer-markers = Markers
wf-mapping-screen-layer-lights = Lighting
wf-mapping-screen-layer-worldgrid = World grid
wf-mapping-screen-layer-perspective = Depth perspective (lower levels smaller)
wf-mapping-screen-layers-note = Only you see these. Leaving the session puts them back.

wf-mapping-screen-keys-modes = Modes
wf-mapping-screen-keys-mouse = Mouse
wf-mapping-screen-keys-tools = Mapping tools
wf-mapping-screen-keys-ctrl-wheel = Ctrl+wheel
wf-mapping-screen-keys-ctrl-wheel-desc = Turn what you're placing
wf-mapping-screen-key-unbound = (unbound)
wf-mapping-screen-key-WFMappingEnableSelect = Turn selection on or off
wf-mapping-screen-key-WFMappingRotate = Rotate the selection, paste or placement
wf-mapping-screen-key-WFMappingMirror = Mirror left to right
wf-mapping-screen-key-WFMappingMirrorVertical = Mirror top to bottom
wf-mapping-screen-key-WFMappingDelete = Delete the selection
wf-mapping-screen-key-WFMappingCopy = Copy
wf-mapping-screen-key-WFMappingCut = Cut
wf-mapping-screen-key-WFMappingPaste = Paste
wf-mapping-screen-key-WFMappingUndo = Undo
wf-mapping-screen-key-WFMappingRedo = Redo
wf-mapping-screen-key-WFMappingLevelUp = Go up a level
wf-mapping-screen-key-WFMappingLevelDown = Go down a level
wf-mapping-screen-key-WFMappingModeCancel = Cancel, or back to Place
wf-mapping-screen-key-WFMappingQuicksave = Quicksave to your library

wf-mapping-screen-cursor-none = Cursor off the map
wf-mapping-screen-cursor-map = Space {$x}, {$y}
wf-mapping-screen-cursor-cell = Tile {$x}, {$y}
wf-mapping-screen-cursor-grid = Grid {$name} ({$net})
wf-mapping-screen-cursor-tile = Floor {$id}
wf-mapping-screen-cursor-tile-empty = Floor: none
wf-mapping-screen-cursor-entity = {$name} ({$id})

wf-mapping-screen-use-spawner = Use spawner instead
wf-mapping-screen-unsaved-hint = Won't be saved. Use {$spawner}.
wf-mapping-screen-unsaved-use = Use spawner
wf-mapping-screen-unsaved-mark = !

## Menu bar

wf-mapping-screen-bar-mapping = Mapping: session, saving and library
wf-mapping-screen-bar-quicksave = Quicksave ({$key}): save to your library under the session name
wf-mapping-screen-quicksave-wait = Quicksave is ready again in {$seconds}s.
