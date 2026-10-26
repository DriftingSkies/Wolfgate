cmd-mappingtools-desc = Opens or closes the mapping tools window.
cmd-mappingtools-help = Usage: {$command} [true|false]

wf-mapping-tools-title = Mapping tools
wf-mapping-tools-action-name = Mapping tools

wf-mapping-tools-select = Enable selection tools
wf-mapping-tools-select-tooltip = While on, left click selects and drags instead of interacting (M). Picking something in the spawn menu pauses it.

wf-mapping-tools-eyedropper = Eyedropper
wf-mapping-tools-eyedropper-tooltip = Click an entity to start placing it with the same rotation, or empty floor to place that tile.

wf-mapping-tools-section-edit = Edit
wf-mapping-tools-section-history = History
wf-mapping-tools-section-view = View
wf-mapping-tools-section-keys = Keys
wf-mapping-tools-section-files = Files
wf-mapping-tools-section-zlevels = Z-levels
wf-mapping-tools-maps = Save / load maps...
wf-mapping-tools-maps-tooltip = Save your grid or map, or open a saved one, with a preview.

wf-mapping-tools-copy = Copy
wf-mapping-tools-cut = Cut
wf-mapping-tools-paste = Paste
wf-mapping-tools-rotate = Rotate
wf-mapping-tools-delete = Delete
wf-mapping-tools-copy-tooltip = Ctrl+C
wf-mapping-tools-cut-tooltip = Ctrl+X
wf-mapping-tools-paste-tooltip = Ctrl+V
wf-mapping-tools-rotate-tooltip = R
wf-mapping-tools-delete-tooltip = Delete
wf-mapping-tools-mirror = Mirror L/R
wf-mapping-tools-mirror-vertical = Mirror T/B
wf-mapping-tools-mirror-tooltip = Mirror the selection left to right (F)
wf-mapping-tools-mirror-vertical-tooltip = Mirror the selection top to bottom (Shift+F)
wf-mapping-tools-undo = Undo
wf-mapping-tools-redo = Redo
wf-mapping-tools-undo-tooltip = Undo your last mapping edit (Ctrl+Z)
wf-mapping-tools-redo-tooltip = Redo (Ctrl+Y or Ctrl+Shift+Z)

wf-mapping-tools-hide-walls = Hide walls
wf-mapping-tools-hide-walls-tooltip = Hides every wall on your screen, so you can see and click what's behind them. Only you see this.

wf-mapping-tools-level = Level {$depth}
wf-mapping-tools-level-ground = Level 0 (ground)
wf-mapping-tools-level-up = Up
wf-mapping-tools-level-down = Down
wf-mapping-tools-level-up-tooltip = Go up a level (Page Up). The level below shows through empty space.
wf-mapping-tools-level-down-tooltip = Go down a level (Page Down)
wf-mapping-tools-level-add-above = Add above
wf-mapping-tools-level-add-below = Add below
wf-mapping-tools-level-remove = Remove
wf-mapping-tools-level-add-above-tooltip = Adds an empty level directly above yours. Levels above it move up one.
wf-mapping-tools-level-add-below-tooltip = Adds an empty level directly below yours. Levels below it move down one.
wf-mapping-tools-level-remove-tooltip = Deletes your level and everything on it. Can't be undone.
wf-mapping-tools-level-remove-confirm = Delete level?

wf-mapping-tools-key-toggle = M
wf-mapping-tools-key-toggle-desc = Turn selection tools on or off
wf-mapping-tools-key-click = Click
wf-mapping-tools-key-click-desc = Select an entity
wf-mapping-tools-key-shift-click = Shift+click
wf-mapping-tools-key-shift-click-desc = Add or remove an entity
wf-mapping-tools-key-drag = Drag
wf-mapping-tools-key-drag-desc = Box-select tiles and what's on them
wf-mapping-tools-key-shift-drag = Shift+drag
wf-mapping-tools-key-shift-drag-desc = Add to the box
wf-mapping-tools-key-move = Drag selection
wf-mapping-tools-key-move-desc = Move it
wf-mapping-tools-key-rotate = R
wf-mapping-tools-key-rotate-desc = Rotate the selection or the paste
wf-mapping-tools-key-mirror = F / Shift+F
wf-mapping-tools-key-mirror-desc = Mirror left-right / top-bottom
wf-mapping-tools-key-delete = Delete
wf-mapping-tools-key-delete-desc = Delete the selection
wf-mapping-tools-key-clipboard = Ctrl+C / X / V
wf-mapping-tools-key-clipboard-desc = Copy, cut, paste
wf-mapping-tools-key-keep-pasting = Shift+click
wf-mapping-tools-key-keep-pasting-desc = Paste and keep pasting
wf-mapping-tools-key-clear = Right click
wf-mapping-tools-key-clear-desc = Clear the selection
wf-mapping-tools-key-history = Ctrl+Z / Ctrl+Y
wf-mapping-tools-key-history-desc = Undo, redo (spawn menu too)
wf-mapping-tools-key-level = Page Up / Down
wf-mapping-tools-key-level-desc = Go up or down a level

wf-mapping-maps-title = Maps
wf-mapping-maps-search = Search files
wf-mapping-maps-refresh = Refresh
wf-mapping-maps-source-saved = Your saves
wf-mapping-maps-source-ships = Ships
wf-mapping-maps-source-maps = Maps
wf-mapping-maps-open = Open on a new map
wf-mapping-maps-open-tooltip = Opens the file for editing on a fresh map, like the mapping command.
wf-mapping-maps-open-stack = Open z-stack
wf-mapping-maps-open-stack-tooltip = Opens every level of the z-stack this file belongs to (name_z0.yml, name_z1.yml, ...).
wf-mapping-maps-load-here = Load here
wf-mapping-maps-load-here-tooltip = Drops a copy of the grid where you are standing, on the current map.
wf-mapping-maps-section-save = Save
wf-mapping-maps-name = File name
wf-mapping-maps-save-grid = Save grid
wf-mapping-maps-save-grid-tooltip = Saves the grid you are standing on.
wf-mapping-maps-save-map = Save map
wf-mapping-maps-save-map-tooltip = Saves the whole map you are on, every grid included.
wf-mapping-maps-save-stack = Save z-stack
wf-mapping-maps-save-stack-tooltip = Saves every level of your z-stack into a folder of that name, one map file per level.
wf-mapping-maps-overwrite = Overwrite?
wf-mapping-maps-save-folder = Saves go to {$folder}
wf-mapping-maps-info = {$path} - {$width}x{$height}, {$tiles} tiles
wf-mapping-maps-preview-none = Pick a file to preview it
wf-mapping-maps-preview-missing = That file no longer exists
wf-mapping-maps-preview-too-large = Too large to preview
wf-mapping-maps-preview-whole-map = Whole maps can't be previewed; open it instead
wf-mapping-maps-bad-name = Use letters, numbers, - and _ only
wf-mapping-maps-no-grid = Stand on the grid you want to save
wf-mapping-maps-saved = Saved to {$path}
wf-mapping-maps-save-failed = Couldn't save {$path}
wf-mapping-maps-load-here-failed = Couldn't load that as a single grid

wf-mapping-zlevels-none-above = No level above
wf-mapping-zlevels-none-below = No level below
wf-mapping-zlevels-moved = Level {$depth}
wf-mapping-zlevels-added = Added level {$depth}
wf-mapping-zlevels-removed = Level removed
wf-mapping-zlevels-last = Can't remove the only level
wf-mapping-zlevels-live = Only maps opened for mapping can change levels
wf-mapping-zlevels-saved = Saved {$count ->
    [one] 1 level
   *[other] {$count} levels
} to {$path}
wf-mapping-zlevels-save-failed = Couldn't save every level to {$path}
wf-mapping-zlevels-no-ground = That z-stack has no level 0 (name_z0.yml)
wf-mapping-zlevels-open-failed = Couldn't open {$path}

wf-mapping-tools-nothing-to-undo = Nothing to undo
wf-mapping-tools-nothing-to-redo = Nothing to redo
wf-mapping-tools-undid = Undid {$edit}
wf-mapping-tools-redid = Redid {$edit}

wf-mapping-tools-edit-move = move
wf-mapping-tools-edit-rotate = rotate
wf-mapping-tools-edit-mirror = mirror
wf-mapping-tools-edit-paste = paste
wf-mapping-tools-edit-cut = cut
wf-mapping-tools-edit-delete = delete
wf-mapping-tools-edit-place = place
wf-mapping-tools-edit-erase = erase
wf-mapping-tools-edit-tile = tile placement
wf-mapping-tools-edit-decal = decal edit
wf-mapping-tools-edit-draw-tiles = tile drawing
wf-mapping-tools-edit-draw-entities = entity drawing
wf-mapping-tools-edit-erase-decals = decal erase
wf-mapping-tools-edit-erase-entities = entity erase
wf-mapping-tools-edit-draw-pipes = pipe laying
wf-mapping-tools-edit-paint-pipes = pipe painting
wf-mapping-tools-edit-place-pipe-device = atmos device placement

wf-mapping-tools-draw-too-many = That covers too many tiles; the most is {$max}.
wf-mapping-tools-draw-bad = That shape can't be drawn.
wf-mapping-tools-draw-bad-tile = That tile doesn't exist.
wf-mapping-tools-draw-bad-prototype = That can't be placed.
wf-mapping-tools-draw-not-allowed = You can't use the mapping tools right now.
wf-mapping-tools-draw-nothing = Nothing there to change.
wf-mapping-tools-protected = Players, ghosts and observers can't be erased.
wf-mapping-tools-draw-needs-grid = Entities need a floor. Draw tiles there first.
wf-mapping-tools-overlaps-grid = That would put tiles on top of another grid.
wf-mapping-tools-draw-no-tiles = None of those cells has a floor to put that on.
wf-mapping-tools-draw-skipped = Skipped {$count} {$count ->
    [one] cell
   *[other] cells
} with no floor.
wf-mapping-tools-pipes-no-floor = Pipes need a floor, and {$count} {$count ->
    [one] cell has
   *[other] cells have
} none.
wf-mapping-tools-pipes-nothing = Nothing to change: the pipes there already connect that way and are that colour.
wf-mapping-tools-pipes-paint-nothing = That network is already that colour.
wf-mapping-tools-pipes-all-skipped = {$skipped ->
    [one] That cell already has a pipe.
   *[other] Those {$skipped} cells already have pipes.
} Turn on Replace to rebuild them.
wf-mapping-tools-pipes-all-blocked = {$blocked ->
    [one] A device or a stack of pipes is in the way there.
   *[other] Devices or stacks of pipes are in the way on those {$blocked} cells.
} They're never rebuilt.
wf-mapping-tools-pipes-all-skipped-blocked = Nothing laid: {$skipped} {$skipped ->
    [one] cell already has a pipe
   *[other] cells already have pipes
} (turn on Replace to rebuild them) and {$blocked} {$blocked ->
    [one] cell holds a device
   *[other] cells hold devices
}.
wf-mapping-tools-pipes-laid-skipped = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$skipped} {$skipped ->
    [one] cell already had a pipe
   *[other] cells already had pipes
} (turn on Replace to rebuild them).
wf-mapping-tools-pipes-laid-blocked = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$blocked} {$blocked ->
    [one] cell holds a device and was
   *[other] cells hold devices and were
} left alone.
wf-mapping-tools-pipes-laid-skipped-blocked = Laid {$laid} {$laid ->
    [one] pipe
   *[other] pipes
}; {$skipped} {$skipped ->
    [one] cell already had a pipe
   *[other] cells already had pipes
} (turn on Replace to rebuild them) and {$blocked} {$blocked ->
    [one] cell holds a device
   *[other] cells hold devices
}.
wf-mapping-tools-pipes-device-bad = That isn't one of the atmos devices.
wf-mapping-tools-pipes-device-no-layer = The {$device} only goes on layer 1, not layer {$layer}.
wf-mapping-tools-pipes-device-no-floor = Devices need a floor. Draw tiles there first.
wf-mapping-tools-pipes-device-blocked = A device or a stack of pipes is already there.
wf-mapping-tools-pipes-device-occupied = A pipe is already there. Turn on Replace to swap it for the device.
wf-mapping-tools-pipes-needs-color = Pick a colour to paint with first.
wf-mapping-tools-pipes-no-pipe = There's no pipe there.
