# MappingMode phase 2, P6 notes

Choices made in the P6 round (shapes on and off grids, circles, fill or border, dock collapse, tile selection and the
erase fix). Each names the file it lives in.

## Shapes and grids

- The shape's grid is resolved on the client from both ends of the drag, every frame and again at release
  (`WFMappingBuildModeSystem.ResolveShapeGrid`): the grid under the start, else under the end, else a grid whose tile
  bounds (`LocalAABB`) overlap or border the box of cells, measured in that grid's own cells. Several candidates: the
  lowest entity id wins, so the pick is stable while dragging. The candidates come from a world box round the circle
  through both ends, which holds every corner of a rotated grid's box.
- Only Shape resolves this way (`WFMappingDragModeBase.ResolvesGrid`). Erase still works on the grid under the press,
  and Fill on the grid under the click.
- With no grid, the cells are map tiles (floor of the world position) and the preview is drawn in the map's frame. A
  tile shape is sent with `Grid = null` and `Map` set; the server makes a grid whose origin is the shape's bottom-left
  cell, lays the tiles and records the grid as one created root, so undo removes the grid whole and redo brings it
  back (`MappingToolsSystem.DrawNewGrid`). The edit check is `Paste` with `NewGrid = true` and the cells as map tiles,
  so the grid cap and the map area apply with the usual refusal popup.
- Entity shapes never make a grid: the client says so before sending, and the server refuses `SpawnEntities` with no
  grid. On a grid, cells without a tile are skipped; the popup says how many. All cells skipped is a refusal.
  `max_selection_cells` still counts every cell sent, the per-tile and entity caps only the cells used.
- Shift squares rectangles and circles (the end moves out to the longer side). Lines ignore Shift.

## Circle and fill

- `WFMappingShapeKind` is now Line, Rect and Circle; whole or border is a separate `WFMappingShapeFill` (Fill,
  Border) shown as two toggle buttons under the shape picker, for Rect and Circle only. The old FilledRect and
  HollowRect values are gone; nothing stored them.
- No new key function for the fill toggle: a right click in Shape mode with no drag held flips it. Adding a key would
  need a marked `keybinds.yml` edit; the right button was unused there. The Keys panel's Shape line says so.
- `MappingToolsShapes.Ellipse` tests each cell's centre against the ellipse inscribed in the box, in doubled integer
  coordinates so it is exact and mirror symmetric. Rows or columns the test leaves empty (the ends of a thin ellipse)
  get their middle cell or two, so the shape touches all four sides. The border is every filled cell with a side on
  the outside: one cell thick and closed against side-to-side movement.
- The client skips boxes over 4M cells and caches the circle while the drag's corners don't move.
- The cursor readout has a line for the mode's sub-choice ("Drawing: Circle, Border", "Erasing: Tiles").

## Dock collapse

- `WFMappingDockSlot.DockChanged` drives `WFMappingScreenUIController.UpdateDocks`, which collapses a column when it
  holds slots and all of them are undocked. The left column is the palette; the right column is the five tabs. One
  undocked tab doesn't collapse the right column, since the others are still docked there.
- `WFMappingScreen.SetLeftCollapsed`/`SetRightCollapsed` hide the dock, turn the split to automatic with no width and
  no drag handle, and restore a split the player had dragged when it comes back.
- The chat lives in the right column, so while that column is collapsed the chat moves into the viewport's layout at
  the top right (420 by 240) and goes back on restore. The quick bar, mode rail, readout and top bar never move.

## Select

- A tile click did nothing because `MappingSelectionTool.Click` only knew entities: with no selectable entity under
  the cursor it cleared. It now selects the cell under the press when it has a tile (`Area` of one cell).
- Shift+click toggles single cells, kept in a new `MappingSelection.Cells` list beside `Area`. The server resolves each
  cell like a one-cell area (tile, entities whose cell it is, decals), so move, rotate, mirror, delete, copy and cut
  carry them. Taking a cell out of a box turns the box into cells first, up to 4096.
- A box drag that starts in space takes the first grid the mouse reaches. A press's drag check at release uses the
  release position, not the live mouse, so a click stays a click.
- Mirror now moves `Area` and the cells across the extent on the client too, since a box smaller than the extent (a
  box plus picked entities) does move.

## Erase fix

- The server path was sound: `MapperErasesTiles` sends the Space tile as a mapper through the network handler and the
  tile clears, and undo lays it again. What the owner hit is that Erase defaulted to Entities, so clicking or dragging
  over bare floor sent entity removals, which find nothing and say nothing. The target picker sat in the Properties
  tab only.
- Erase now defaults to Everything (entities, then decals, then tiles), and the readout names the target.
- Every draw refusal now pops up and logs at debug level, including "nothing there to change" and a request dropped by
  `CanUse` (not a mapper right now, or over the tools rate). The client logs each draw it sends at debug level.

## Not verified

- The overlays (shape preview in a rotated grid's frame, selected cells, the floating chat) haven't been looked at in
  a real client; the headless test covers the logic only.
