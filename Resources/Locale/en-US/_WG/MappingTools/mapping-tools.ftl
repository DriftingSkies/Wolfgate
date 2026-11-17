cmd-mappingtools-desc = Opens or closes the mapping tools window.
cmd-mappingtools-help = Usage: {$command} [true|false]

wg-mapping-tools-title = Mapping tools
wg-mapping-tools-action-name = Mapping tools

wg-mapping-tools-select = Enable selection tools
wg-mapping-tools-select-tooltip = While on, left click selects and drags instead of interacting (M). Picking something in the spawn menu pauses it.

wg-mapping-tools-eyedropper = Eyedropper
wg-mapping-tools-eyedropper-tooltip = Click an entity to start placing it with the same rotation, or empty floor to place that tile.

wg-mapping-tools-section-edit = Edit
wg-mapping-tools-section-history = History
wg-mapping-tools-section-view = View
wg-mapping-tools-section-keys = Keys
wg-mapping-tools-section-files = Files
wg-mapping-tools-section-zlevels = Z-levels
wg-mapping-tools-maps = Save / load maps...
wg-mapping-tools-maps-tooltip = Save your grid or map, or open a saved one, with a preview.

wg-mapping-tools-copy = Copy
wg-mapping-tools-cut = Cut
wg-mapping-tools-paste = Paste
wg-mapping-tools-rotate = Rotate
wg-mapping-tools-delete = Delete
wg-mapping-tools-copy-tooltip = Ctrl+C
wg-mapping-tools-cut-tooltip = Ctrl+X
wg-mapping-tools-paste-tooltip = Ctrl+V
wg-mapping-tools-rotate-tooltip = R
wg-mapping-tools-delete-tooltip = Delete
wg-mapping-tools-mirror = Mirror L/R
wg-mapping-tools-mirror-vertical = Mirror T/B
wg-mapping-tools-mirror-tooltip = Mirror the selection left to right (F)
wg-mapping-tools-mirror-vertical-tooltip = Mirror the selection top to bottom (Shift+F)
wg-mapping-tools-undo = Undo
wg-mapping-tools-redo = Redo
wg-mapping-tools-undo-tooltip = Undo your last mapping edit (Ctrl+Z)
wg-mapping-tools-redo-tooltip = Redo (Ctrl+Y or Ctrl+Shift+Z)

wg-mapping-tools-hide-walls = Hide walls
wg-mapping-tools-hide-walls-tooltip = Hides every wall on your screen, so you can see and click what's behind them. Only you see this.

wg-mapping-tools-level = Level {$depth}
wg-mapping-tools-level-ground = Level 0 (ground)
wg-mapping-tools-level-up = Up
wg-mapping-tools-level-down = Down
wg-mapping-tools-level-up-tooltip = Go up a level (Page Up). The level below shows through empty space.
wg-mapping-tools-level-down-tooltip = Go down a level (Page Down)
wg-mapping-tools-level-add-above = Add above
wg-mapping-tools-level-add-below = Add below
wg-mapping-tools-level-remove = Remove
wg-mapping-tools-level-add-above-tooltip = Adds an empty level directly above yours. Levels above it move up one.
wg-mapping-tools-level-add-below-tooltip = Adds an empty level directly below yours. Levels below it move down one.
wg-mapping-tools-level-remove-tooltip = Deletes your level and everything on it. Can't be undone.
wg-mapping-tools-level-remove-confirm = Delete level?

wg-mapping-tools-key-toggle = M
wg-mapping-tools-key-toggle-desc = Turn selection tools on or off
wg-mapping-tools-key-click = Click
wg-mapping-tools-key-click-desc = Select an entity
wg-mapping-tools-key-shift-click = Shift+click
wg-mapping-tools-key-shift-click-desc = Add or remove an entity
wg-mapping-tools-key-drag = Drag
wg-mapping-tools-key-drag-desc = Box-select tiles and what's on them
wg-mapping-tools-key-shift-drag = Shift+drag
wg-mapping-tools-key-shift-drag-desc = Add to the box
wg-mapping-tools-key-move = Drag selection
wg-mapping-tools-key-move-desc = Move it
wg-mapping-tools-key-rotate = R
wg-mapping-tools-key-rotate-desc = Rotate the selection or the paste
wg-mapping-tools-key-mirror = F / Shift+F
wg-mapping-tools-key-mirror-desc = Mirror left-right / top-bottom
wg-mapping-tools-key-delete = Delete
wg-mapping-tools-key-delete-desc = Delete the selection
wg-mapping-tools-key-clipboard = Ctrl+C / X / V
wg-mapping-tools-key-clipboard-desc = Copy, cut, paste
wg-mapping-tools-key-keep-pasting = Shift+click
wg-mapping-tools-key-keep-pasting-desc = Paste and keep pasting
wg-mapping-tools-key-clear = Right click
wg-mapping-tools-key-clear-desc = Clear the selection
wg-mapping-tools-key-history = Ctrl+Z / Ctrl+Y
wg-mapping-tools-key-history-desc = Undo, redo (spawn menu too)
wg-mapping-tools-key-level = Page Up / Down
wg-mapping-tools-key-level-desc = Go up or down a level

wg-mapping-maps-title = Maps
wg-mapping-maps-search = Search files
wg-mapping-maps-refresh = Refresh
wg-mapping-maps-source-saved = Your saves
wg-mapping-maps-source-ships = Ships
wg-mapping-maps-source-maps = Maps
wg-mapping-maps-open = Open on a new map
wg-mapping-maps-open-tooltip = Opens the file for editing on a fresh map, like the mapping command.
wg-mapping-maps-open-stack = Open z-stack
wg-mapping-maps-open-stack-tooltip = Opens every level of the z-stack this file belongs to (name_z0.yml, name_z1.yml, ...).
wg-mapping-maps-load-here = Load here
wg-mapping-maps-load-here-tooltip = Drops a copy of the grid where you are standing, on the current map.
wg-mapping-maps-section-save = Save
wg-mapping-maps-name = File name
wg-mapping-maps-save-grid = Save grid
wg-mapping-maps-save-grid-tooltip = Saves the grid you are standing on.
wg-mapping-maps-save-map = Save map
wg-mapping-maps-save-map-tooltip = Saves the whole map you are on, every grid included.
wg-mapping-maps-save-stack = Save z-stack
wg-mapping-maps-save-stack-tooltip = Saves every level of your z-stack into a folder of that name, one map file per level.
wg-mapping-maps-overwrite = Overwrite?
wg-mapping-maps-save-folder = Saves go to {$folder}
wg-mapping-maps-info = {$path} - {$width}x{$height}, {$tiles} tiles
wg-mapping-maps-preview-none = Pick a file to preview it
wg-mapping-maps-preview-missing = That file no longer exists
wg-mapping-maps-preview-too-large = Too large to preview
wg-mapping-maps-preview-whole-map = Whole maps can't be previewed; open it instead
wg-mapping-maps-bad-name = Use letters, numbers, - and _ only
wg-mapping-maps-no-grid = Stand on the grid you want to save
wg-mapping-maps-saved = Saved to {$path}
wg-mapping-maps-save-failed = Couldn't save {$path}
wg-mapping-maps-load-here-failed = Couldn't load that as a single grid

wg-mapping-zlevels-none-above = No level above
wg-mapping-zlevels-none-below = No level below
wg-mapping-zlevels-moved = Level {$depth}
wg-mapping-zlevels-added = Added level {$depth}
wg-mapping-zlevels-removed = Level removed
wg-mapping-zlevels-last = Can't remove the only level
wg-mapping-zlevels-live = Only maps opened for mapping can change levels
wg-mapping-zlevels-saved = Saved {$count ->
    [one] 1 level
   *[other] {$count} levels
} to {$path}
wg-mapping-zlevels-save-failed = Couldn't save every level to {$path}
wg-mapping-zlevels-no-ground = That z-stack has no level 0 (name_z0.yml)
wg-mapping-zlevels-open-failed = Couldn't open {$path}

wg-mapping-tools-nothing-to-undo = Nothing to undo
wg-mapping-tools-nothing-to-redo = Nothing to redo
wg-mapping-tools-undid = Undid {$edit}
wg-mapping-tools-redid = Redid {$edit}

wg-mapping-tools-edit-move = move
wg-mapping-tools-edit-rotate = rotate
wg-mapping-tools-edit-mirror = mirror
wg-mapping-tools-edit-paste = paste
wg-mapping-tools-edit-cut = cut
wg-mapping-tools-edit-delete = delete
wg-mapping-tools-edit-place = place
wg-mapping-tools-edit-erase = erase
wg-mapping-tools-edit-tile = tile placement
wg-mapping-tools-edit-decal = decal edit
wg-mapping-tools-edit-draw-tiles = tile drawing
wg-mapping-tools-edit-draw-entities = entity drawing
wg-mapping-tools-edit-erase-decals = decal erase
wg-mapping-tools-edit-erase-entities = entity erase
wg-mapping-tools-edit-draw-pipes = pipe laying
wg-mapping-tools-edit-paint-pipes = pipe painting
wg-mapping-tools-edit-place-pipe-device = atmos device placement

wg-mapping-tools-draw-too-many = That covers too many tiles; the most is {$max}.
wg-mapping-tools-draw-bad = That shape can't be drawn.
wg-mapping-tools-draw-bad-tile = That tile doesn't exist.
wg-mapping-tools-draw-bad-prototype = That can't be placed.
wg-mapping-tools-draw-not-allowed = You can't use the mapping tools right now.
wg-mapping-tools-draw-nothing = Nothing there to change.
wg-mapping-tools-protected = Players, ghosts and observers can't be erased.
wg-mapping-tools-draw-needs-grid = Entities need a floor. Draw tiles there first.
wg-mapping-tools-overlaps-grid = That would put tiles on top of another grid.
wg-mapping-tools-draw-no-tiles = None of those cells has a floor to put that on.
wg-mapping-tools-draw-skipped = Skipped {$count} {$count ->
    [one] cell
   *[other] cells
} with no floor.
wg-mapping-tools-pipes-no-floor = Pipes need a floor, and {$count} {$count ->
    [one] cell has
   *[other] cells have
} none.
wg-mapping-tools-pipes-nothing = Nothing to change: the pipes there already connect that way and are that colour.
wg-mapping-tools-pipes-paint-nothing = That network is already that colour.
wg-mapping-tools-pipes-all-skipped = {$skipped ->
    [one] That cell already has a pipe.
   *[other] Those {$skipped} cells already have pipes.
} Turn on Replace to rebuild them.
wg-mapping-tools-pipes-all-blocked = {$blocked ->
    [one] A device or a stack of pipes is in the way there.
   *[other] Devices or stacks of pipes are in the way on those {$blocked} cells.
} They're never rebuilt.
wg-mapping-tools-pipes-all-skipped-blocked = Nothing laid: {$skipped} {$skipped ->
    [one] cell already has a pipe
   *[other] cells already have pipes
} (turn on Replace to rebuild them) and {$blocked} {$blocked ->
    [one] cell holds a device
   *[other] cells hold devices
}.
wg-mapping-tools-pipes-laid-skipped = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$skipped} {$skipped ->
    [one] cell already had a pipe
   *[other] cells already had pipes
} (turn on Replace to rebuild them).
wg-mapping-tools-pipes-laid-blocked = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$blocked} {$blocked ->
    [one] cell holds a device and was
   *[other] cells hold devices and were
} left alone.
wg-mapping-tools-pipes-laid-skipped-blocked = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$skipped} {$skipped ->
    [one] cell already had a pipe
   *[other] cells already had pipes
} (turn on Replace to rebuild them) and {$blocked} {$blocked ->
    [one] cell holds a device
   *[other] cells hold devices
}.
wg-mapping-tools-pipes-device-bad = That isn't one of the atmos devices.
wg-mapping-tools-pipes-device-no-layer = The {$device} only goes on layer 1, not layer {$layer}.
wg-mapping-tools-pipes-device-no-floor = Devices need a floor. Draw tiles there first.
wg-mapping-tools-pipes-device-blocked = A device or a stack of pipes is already there.
wg-mapping-tools-pipes-device-occupied = A pipe is already there. Turn on Replace to swap it for the device.
wg-mapping-tools-pipes-needs-color = Pick a colour to paint with first.
wg-mapping-tools-pipes-no-pipe = There's no pipe there.
