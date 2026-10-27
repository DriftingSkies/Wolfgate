# MappingTools

Editing tools for mapping in game. The `mapping` command opens a Mapping tools window; the pointer button next to the
eraser on the mapping action bar, or `mappingtools`, opens and closes it. Its Enable selection tools button (or M) makes left click select instead of interact: click an entity to select it and
Shift+click to add or remove one, click and drag anywhere else to box-select tiles with everything on them, then drag
the selection to move it, R to rotate it clockwise, Delete to remove it, and Ctrl+C /
Ctrl+X / Ctrl+V to copy, cut and paste it (R turns the paste preview, Shift+click keeps pasting). Picking something in
the spawn menu pauses Select. Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z) undo and redo while the window is open, and the
history also covers tiles, entities and decals placed or erased with the spawn menu. The Eyedropper picks the next clicked entity (with its rotation), or the floor tile under the cursor, and starts
placing it; unlike the sandbox P key it works in any round. Mirror L/R (F) and Mirror T/B (Shift+F) flip the selection in place. The Maps window (Save / load maps...)
lists your saves and the game's ships and maps with a preview of the selected one, saves the grid or map you stand on
into `Mapping/` in the server data folder, opens a file on a new map as `mapping` does, or drops a grid where you
stand. A Hide walls toggle hides every wall on your client only, so what's behind them can be seen and
clicked. The Z-levels section lists the levels of the map you are on and moves you between them (click a level, Up /
Down or Page Up / Page Down); the level below shows through empty space, as in a round. Add above / Add below put an
empty level next to yours, turning a plain map into a stack, and Remove deletes yours. The Maps window's Save z-stack
writes every level to `Mapping/<name>/<name>_z<depth>.yml` (depth 0 is the ground), and Open z-stack opens every level
of the stack the selected file belongs to. It needs the Mapping admin flag.

Upstream's mapping screen (`MappingState`) is unreachable since upstream stopped switching to it, so these tools sit on
the normal game screen instead. MappingMode's own mapping screen docks the same tools panel (`MappingToolsPanel`,
through `MappingToolsSystem.SetDock`) instead of opening the window.

The draw request, `MappingToolsDrawEvent`, lays one tile type over a set of cells, spawns one prototype at each cell's
centre with a rotation, removes the decals on the cells, removes the entities on the cells (`EraseEntities`: grid
entities by position, loose ones off every grid by the centre of their bounds, and with no grid the cells are map
tiles), lays plain gas pipes along a path or paints a pipe network, as one undoable edit.

No edit touches a player, a ghost, a mapping observer (with or without its player), a body holding a mind, or anything
parented under one (`MappingProtectionSystem`). Selections leave them out, area erase and pipe replacement skip or
refuse them, undo doesn't move or delete them, and deleting a grid, a z-level or an entity moves them off first.
Entities whose prototype never saves (mobs, `save: false`) can be deleted but not copied, and undo can't restore
them. MappingMode's Fill, Shape, Pipe and area erase send it; anyone who may use the tools may send it, up
to 16384 distinct cells.

The link requests set signal links between a source and a sink (`MappingToolsLinkEvent`), add devices to or take them
out of a device list (`MappingToolsDeviceListEvent`) and set the access of readers (`MappingToolsAccessEvent`), each
as one undoable edit through the game's own systems; `MappingToolsLinksRequestEvent` returns the signal links of a map,
which aren't networked. MappingMode's Link mode sends them.

Entry points: `MappingToolsSystem` (server; the requests, the per-mapper history and clipboard, and the placement and
decal hooks), `MappingProtectionSystem` (server; who no edit touches), `MappingToolsSystem` (client; the window, keys and on/off state), `MappingSelectionTool` and
`MappingSelectionOverlay` (client selection and previews), `MappingToolsCommand` and `MappingToolsMath` (the shared
rotation maths).

## Design notes

- Every change is a `MappingEdit`: tile changes, entity moves, created and deleted entity groups, removed and added
  decals, pipe colours, signal links, device lists and access. Applying one forward or backward is the same code, which the tools also use for the first application.
- Deleted entities are saved with the engine's `EntitySerializer`, so undo and paste recreate them with their contents
  and settings. References to entities outside the saved set are dropped, so a restored button loses a link to a door
  that wasn't deleted with it. Restored entities get new uids; `MappingEntityRef` lets older edits follow them.
- Moves keep the same entities, so their links and state are untouched. Anchored entities stay anchored the whole way
  (unanchoring would make cables cut themselves); a `ReAnchorEvent` tells node networks and airtightness about the new
  cell, and the client re-smooths walls that moved while anchored, since smoothing only reacts to anchoring. Only
  direct children of the grid are selected; grids, players, ghosts and anything holding a player are skipped.
- Mirroring flips angles (left-right negates them, top-bottom reflects them about east-west). Pipe fittings that
  aren't symmetric take the quarter turn that gives them the mirrored connections; one-sided decals (`...Ne`, `...E`)
  swap to their mirrored ID when it exists; rotatable tiles take the rotate/mirror state that matches Clyde's
  rendering. Other lopsided sprites (a chair's armrest, a sign's text) can't be mirrored and keep their look.
- Pasting off every grid lands on the grid the clipboard came from, or on a new grid if that one is gone.
- No edit, undo or redo may lay a tile on a cell another grid already has a tile on (`FillsOverOtherGrid`); each
  grid only checks its own tiles, so a paste or move off the side of one grid could otherwise stack two grids.
- A draw checks its cells, grid and tile or prototype first, then asks `WFMappingCanEditEvent` with its box, cell
  count and, for entities, the cells and prototype, so MappingMode can apply its selection, per-tile and prototype
  limits. Tiles that already match are left out; clearing every tile of a grid deletes the grid as one root, so undo
  brings it back whole. `MappingToolsShapes` (shared) makes the lines, rectangles and flood fills the client sends.
- A pipe draw (`MappingToolsSystem.Pipes.cs`) takes the path in order, fills diagonal jumps with an L, and gives each
  cell the plain pipe (half, straight, bend, T, four-way, or their layer alternatives) that opens to its path
  neighbours. `MappingToolsPipes.Plan` (shared, pure) adds the joins: pipes pointing at a cell, and the plain pipe past
  each end of the path, which is rebuilt with the extra side. A plain pipe on the path is rebuilt with its old sides
  added, or kept when Replace is off; a device is never rebuilt. Rebuilt pipes go in the edit's deleted group and new
  ones in its created group. Paint changes `AtmosPipeColorComponent` in place, as `MappingEdit.PipeColors`.
- A device placement (`MappingDrawOp.PipeDevice`, `MappingToolsSystem.PipeDevices.cs`) takes one id from
  `MappingToolsPipeDevices.Ids` and nothing else. The layer's prototype is the device's `AtmosPipeLayers` alternative,
  or the device moved to the layer before it anchors; `MappingToolsPipes.DeviceTurns` picks the turn that meets the
  most pipes. The edit check names the primary id, so the hidden `...Alt` prototypes pass MappingMode's prototype check.
- A pipe network is found by a flood over anchored pipe nodes whose sides face each other on one layer, the rule
  `PipeNode` joins nets by, so it doesn't depend on node groups having formed on a paused map.
- Grid splitting is paused while an edit applies, so moving tiles away from the hull leaves one grid.
- Z-stacks are CE z-networks. Each change rebuilds the stack's network (`CEZLevelsSystem.WfRestack`) around the
  ground level, which keeps depth 0; levels shift away from it. Only stacks whose maps are all uninitialized can change,
  so live stations and planets are left alone. Undo history doesn't cover adding or removing levels.
- History holds the last 100 edits per mapper and is dropped on disconnect and round restart. Placement-menu edits in
  one tick (a line or grid placement, or a replace) are one undo step. A `WFMappingCanEditEvent` of kind `Limits` lets
  another module set a lower step count and a byte budget per player (MappingMode does for mappers); the oldest edits
  go first. Each request's event carries its selected cells, touched entities and decals, and undo and redo carry the
  entities and grids they would bring back, so a handler can refuse them by size.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/MappingTools/CEZLevelsSystem.MappingTools.cs`](CEZLevelsSystem.MappingTools.cs)
- [`Content.Server/_WF/MappingTools/MappingEdit.cs`](MappingEdit.cs)
- [`Content.Server/_WF/MappingTools/MappingMapsSystem.cs`](MappingMapsSystem.cs)
- [`Content.Server/_WF/MappingTools/MappingProtectionSystem.cs`](MappingProtectionSystem.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.Apply.cs`](MappingToolsSystem.Apply.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.cs`](MappingToolsSystem.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.Draw.cs`](MappingToolsSystem.Draw.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.History.cs`](MappingToolsSystem.History.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.Links.cs`](MappingToolsSystem.Links.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.PipeDevices.cs`](MappingToolsSystem.PipeDevices.cs)
- [`Content.Server/_WF/MappingTools/MappingToolsSystem.Pipes.cs`](MappingToolsSystem.Pipes.cs)
- [`Content.Server/_WF/MappingTools/MappingZLevelsSystem.cs`](MappingZLevelsSystem.cs)
- [`Content.Server/_WF/MappingTools/WFMappingPermissionEvents.cs`](WFMappingPermissionEvents.cs)

### Shared

- [`Content.Shared/_WF/MappingTools/MappingMapsEvents.cs`](../../../Content.Shared/_WF/MappingTools/MappingMapsEvents.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsEvents.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsEvents.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsLinkEvents.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsLinkEvents.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsMath.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsMath.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsPipeDevices.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsPipeDevices.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsPipes.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsPipes.cs)
- [`Content.Shared/_WF/MappingTools/MappingToolsShapes.cs`](../../../Content.Shared/_WF/MappingTools/MappingToolsShapes.cs)
- [`Content.Shared/_WF/MappingTools/MappingZLevelsEvents.cs`](../../../Content.Shared/_WF/MappingTools/MappingZLevelsEvents.cs)
- [`Content.Shared/_WF/MappingTools/ToggleMappingToolsActionEvent.cs`](../../../Content.Shared/_WF/MappingTools/ToggleMappingToolsActionEvent.cs)
- [`Content.Shared/_WF/MappingTools/WFMappingAccessFillComponent.cs`](../../../Content.Shared/_WF/MappingTools/WFMappingAccessFillComponent.cs)

### Client

- [`Content.Client/_WF/MappingTools/MappingMapsSystem.cs`](../../../Content.Client/_WF/MappingTools/MappingMapsSystem.cs)
- [`Content.Client/_WF/MappingTools/MappingMapsWindow.xaml`](../../../Content.Client/_WF/MappingTools/MappingMapsWindow.xaml)
- [`Content.Client/_WF/MappingTools/MappingMapsWindow.xaml.cs`](../../../Content.Client/_WF/MappingTools/MappingMapsWindow.xaml.cs)
- [`Content.Client/_WF/MappingTools/MappingSelectionOverlay.cs`](../../../Content.Client/_WF/MappingTools/MappingSelectionOverlay.cs)
- [`Content.Client/_WF/MappingTools/MappingSelectionTool.cs`](../../../Content.Client/_WF/MappingTools/MappingSelectionTool.cs)
- [`Content.Client/_WF/MappingTools/MappingToolsCommand.cs`](../../../Content.Client/_WF/MappingTools/MappingToolsCommand.cs)
- [`Content.Client/_WF/MappingTools/MappingToolsPanel.xaml`](../../../Content.Client/_WF/MappingTools/MappingToolsPanel.xaml)
- [`Content.Client/_WF/MappingTools/MappingToolsPanel.xaml.cs`](../../../Content.Client/_WF/MappingTools/MappingToolsPanel.xaml.cs)
- [`Content.Client/_WF/MappingTools/MappingToolsSystem.cs`](../../../Content.Client/_WF/MappingTools/MappingToolsSystem.cs)
- [`Content.Client/_WF/MappingTools/MappingToolsWindow.xaml`](../../../Content.Client/_WF/MappingTools/MappingToolsWindow.xaml)
- [`Content.Client/_WF/MappingTools/MappingToolsWindow.xaml.cs`](../../../Content.Client/_WF/MappingTools/MappingToolsWindow.xaml.cs)
- [`Content.Client/_WF/MappingTools/MappingZLevelsSystem.cs`](../../../Content.Client/_WF/MappingTools/MappingZLevelsSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingMapsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingMapsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsDrawTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsDrawTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsLinkTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsLinkTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsPipesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsPipesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingToolsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/MappingTools/MappingZLevelsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/MappingTools/MappingZLevelsTest.cs)

### Unit tests

- [`Content.Tests/_WF/MappingTools/MappingToolsPipesTest.cs`](../../../Content.Tests/_WF/MappingTools/MappingToolsPipesTest.cs)
- [`Content.Tests/_WF/MappingTools/MappingToolsShapesTest.cs`](../../../Content.Tests/_WF/MappingTools/MappingToolsShapesTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/MappingTools/mapping-tools.ftl`](../../../Resources/Locale/en-US/_WF/MappingTools/mapping-tools.ftl)

## Non-modular edits

- [`Content.Client/Commands/MappingClientSideSetupCommand.cs`](../../../Content.Client/Commands/MappingClientSideSetupCommand.cs): open the mapping tools window
- [`Content.Shared/Input/ContentKeyFunctions.cs`](../../../Content.Shared/Input/ContentKeyFunctions.cs): Select tool, clipboard, undo and z-level keys for the mapping tools
- [`Resources/clientCommandPerms.yml`](../../../Resources/clientCommandPerms.yml): client-only command that opens the mapping tools window
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml): Select tool, clipboard, undo and z-level keys for the mapping tools
- [`Resources/mapping_actions.yml`](../../../Resources/mapping_actions.yml): button next to the eraser that opens or closes the mapping tools

<!-- WOLFGATE-GENERATED END -->
