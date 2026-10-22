# MappingMode phase 2, P1 notes

Assumptions and choices made while building P1 where PLAN2.md left room. Each names the file it lives in.

## Screen and controller

- `WantsMappingScreen` is `InSession && SetUp` of `WFMappingClientSystem`, not `InSession` alone. `Setup` sets
  `SetUp` before it reloads the screen and `Teardown` clears it before reloading, so the reload picks the right
  screen whichever order the observer and `GameplayState` arrive in. `GameplayState.Startup` therefore loads the
  chosen layout first and `Setup` swaps to the mapping screen a frame later.
- `Setup` reloads the screen only when the active screen isn't already the mapping screen; `Teardown` only when it
  is, and not while the system shuts down (disconnect), where `GameplayState.Shutdown` unloads the screen anyway.
- The controller rebuilds every host on each `OnScreenLoad` of the mapping screen and drops them on
  `OnScreenUnload`. Entity systems are recreated on reconnect, so panels never outlive the load they were built for.
- Extra hosts beyond the plan's list: `PropertiesHost`, `ToolsHost`, `LayersHost` and `KeysHost`, one per tab of
  `RightTabs`. `TemplateHost` is left empty for P2.
- Docking: every panel sits in a `WFMappingDockSlot` (header with Undock). Undock moves the slot's content into a
  `WFMappingDockWindow`; closing the window or the slot's Dock button brings it back. P2 can dock its own panel with
  `WFMappingScreenUIController.AddDocked(host, panel, titleLocKey)`.
- The tools panel is docked by the controller (`MappingToolsSystem.SetDock(slot.Content)` on load); `Teardown`
  calls `SetDock(null)` after turning the tools off, so admins using `mapping` later get the floating window again.
- The chat sits under the right tabs in a vertical split. `SetChatSize` is a no-op, so the stored chat sizes of
  the other layouts don't apply.

## Build modes

- Place mode keeps the selected entry while other modes run, but stops the engine (or decal) placement on Exit and
  starts it again on Enter. So no placement ghost shows in Erase, Fill or Shape, and those modes still know the
  entry.
- Escape in Place mode drops the selected entry when there is one (claims the key), and only falls through to the
  game menu with nothing selected. The plan only listed the other modes' Escape.
- Placement started elsewhere (the tools' eyedropper button, a pick, the old spawn window) is noticed in
  `WFMappingBuildModeSystem.FrameUpdate` and adopted as the entry, switching to Place. A placement cleared elsewhere
  clears an entity or tile entry.
- The tools' Select and Eyedropper buttons switch to Select and Pick through `SelectingChanged`/`PickingChanged`.
  Pick mode arms the tools' eyedropper on Enter; the pick disarms it, which returns to Place.
- Clicks are not claimed while a reference grid is being placed, so the reference placement keeps working in every
  mode.
- Erase and Shape claim the right button (it drops a drag) and Fill and Pick claim it too, so no context menu opens
  in those modes. Middle button is claimed only in Place (eyedropper).
- Shape and Fill need a grid under the press; they don't start a new grid in space. Use Place to lay the first
  tile. Decals can't be drawn as shapes.
- Ctrl+wheel and R rotate the entry: entity and tile entries turn the placement direction (kept while placement is
  stopped, so shapes use it), decal entries turn the decal rotation by 90 degrees.
- Shape entities are spawned at cell centres with the placement direction; whether they anchor follows their
  prototype (anything that anchors itself when placed does), as the engine placement does.
- Fill compares tile types only, not rotation. Its bounds are the grid's tile AABB cut to the map area seen in grid
  space. A truncated fill still sends the cells it found and says so.
- Area erase of entities is the engine's `RequestRectRemove`, sent 0.02 tiles inside the dragged cells so entities
  on the neighbouring cells stay.

## Draw request (server)

- `SetTiles` with Space is checked as `Edit`; any other tile as `Paste`. When clearing would empty the whole grid
  (and the grid isn't a map), the edit deletes the grid as one root, as Delete does, so undo brings it back whole.
- Tiles that already match are left out of the edit; tile variants are picked like the tile system does.
- `SpawnEntities` and every draw also count their cells as `SelectionCells`, so `max_selection_cells` caps them for
  mappers. The 16384-cell cap applies to everyone, admins included.
- Mapping mode's refusal for an unknown or denied prototype reuses `wf-mapping-mode-limit-prototype`.

## Palette

- The Entities tab is a second `WolfgateSpawnPanel` with the replace, erase and placement mode row hidden (the
  properties panel has the placement mode and Replace). Its recent list is per panel, as in the window.
- The "star" for quick select is the ★ button in the properties panel (adds the selected entry). Each entry's
  right-click menu has "Add to quick select"; tiles and decals also get Favourite there. Entities keep their
  existing favourite star (the spawn menu's cvar).
- `/Mapping/palette.yml` holds tile and decal favourites and recents (40 each) and the 12-entry strip. It is written
  on every change; read failures give an empty store.
- Tile icons whose sprite path sits inside an `.rsi` folder are read through the RSI's state: loading the raw PNG
  logs a warning, which fails integration tests.
- The decal tab keeps the decal placer's settings: colour (with Use colour), auto settings, snap, cleanable,
  rotation and z-index. Changing the colour of the selected decal places it again with the new colour.

## Panels and readout

- The cursor readout shows a blank line set ("Cursor off the map") whenever the mouse isn't over the game viewport.
  It refreshes at most 10 times a second; the properties and layers tabs at most 4 times a second, and only while
  shown.
- The limits line reuses `wf-mapping-mode-menu-status` from the Mapping window.
- Layers live in `WFMappingClientSystem.Layers.cs`; `Setup` and `Teardown` reset them all (walls shown, decals
  shown, then subfloor, markers and lighting for mapping or for the normal game).

## Not done here

- No tests: P4 owns `MappingToolsShapesTest`, `MappingToolsDrawTest` and `WFMappingScreenTest`.
- No README or generated docs: P5.
