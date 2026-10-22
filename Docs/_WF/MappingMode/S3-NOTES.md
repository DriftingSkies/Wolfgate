# S3 notes: assumptions made while building the client

## Gaps the server leaves

- **Delete grid has no server request.** The Session tab sends a MappingTools `MappingToolsDeleteEvent` over the
  grid's whole tile box (from `MapGridComponent.LocalAABB`). The empty grid is then deleted by the engine
  (`game.delete_empty_grids`), and the delete is one undoable edit. Grids over 256 × 256 cells are refused on the
  client, since MappingTools ignores bigger selections (`MaxArea`).
- **Deleting the grid under the mapper probably deletes the mapper.** The observer moves onto grids it crosses (grid
  traversal), so it is a child of the grid it stands over. When that grid empties, the engine deletes it with its
  children. In `PreRoundLobby` `MindSystem` spawns no ghost, so the player would be left in game with no entity and
  no way back except reconnecting. The Select tool's Delete over a whole grid has the same problem. The server should
  move present observers onto the map before a grid empties, or own the delete through a `WFMappingDeleteGridRequest`.
  Not exercised.
- **The client can't tell the admin map from any other.** The Mapping button and window show to every admin in
  game while the mode is on. Without a session the window offers only `Return to lobby`
  (`WFMappingAdminLeaveRequest`), which the server accepts for any admin entity that isn't a session observer.

## Setup and teardown

- Setup runs from `FrameUpdate` once the local entity has `WFMappingObserverComponent` and the state is
  `GameplayState`, since the two arrive in either order. Teardown runs on `LocalPlayerDetachedEvent`, or when the
  component is gone.
- Setup sets `MarkersVisible`, turns lighting off, sets `SubFloorHideSystem.ShowAll` (the server honours the request
  without an admin check), loads `/mapping_actions.yml`, opens MappingTools and adds the bounds overlay. It does not
  move subfloor sprites to the overlay draw depth as `showsubfloorforever` does.
- Teardown turns lighting back on whatever it was before, closes MappingTools, clears the placement and closes the
  entity and tile spawn windows. The decal window stays open: `DecalPlacerUIController.CloseWindow` is private and
  the plan lists no edit for it.

## Results

- Results show as a status line, green or red: in the lobby panel, and in the Mapping window. Cursor popups only
  exist in `GameplayState` (`PopupUIController`), so the lobby can't pop them; in game the server already pops them
  and the window line is extra. The last result stays until the next one.
- Client-side refusals (a bad name, no grid under the mapper, a damaged download) use the same line.

## Lobby

- `LobbyGui.WFApplyMappingMode` hides `ReadyButton.Parent`, `CustomizeButton`, `ObserveButton`, `CallVoteButton`,
  `ActiveCrews`, `StartTime` and every child of `CharacterHero`, and puts the panel in `CharacterHero`. Turning the
  cvar off while in the lobby puts everything back.
- The admin Observe entry sits right after the hidden `ObserveButton` and follows `AdminStatusUpdated`.
- Join codes are upper-cased on send. Session names are checked with `WFMappingModeLimits.IsValidName` before
  sending and refused with the server's own string.
- The library is requested when the panel appears and on Refresh; maps are listed newest first, versions fold out.
  Deletes take a second click (`ConfirmButton`).

## Mapping window

- Opened from the game menu's `Mapping` entry. Session tab: name, code with Copy, status line (entities with thousands
  separators), members with Remove for the owner, autosave step (owner only), Delete grid, Load reference, Leave
  (second click). Save tab: name (filled with the session name once per session; blank lets the server use the
  session name), Save now, last save in local time, time to the next autosave check, unsaved changes. Library tab:
  the library without Open, Download of any version, Delete.
- The game menu hides Character, Round setup and Sector map whenever the mode is on.

## Reference placement

- Picking a file sends `WFMappingReferencePickRequest`; the footprint starts placement and closes the window.
- The anchor is the mouse position floored to a tile corner on the mapper's map. The outline turns red when the
  footprint's bounds leave the `map_size` square (the same box the server checks).
- Click, R and right click reuse `WFMappingSelect`, `WFMappingRotate` and `WFMappingSelectCancel`, bound before
  MappingTools. They only exist while the MappingTools window is open, so placement adds them to the `common`
  context if missing and removes them afterwards unless MappingTools is open by then.
- Escape needed a new key function, `WFMappingReferenceCancel` (Escape, priority 5: above the game menu's
  `EscapeContext`, below `CloseModals` and text focus). It is only in the context while placing.
- Placement ends on the `reference-placed` or `reference-expired` result, on cancel, on a spawn-menu pick and on
  teardown. A refused confirm keeps it running.

## Download

- One assembler per transfer. A file is written once complete, under `/Mapping/<MapName>/<version>/z<D>.yml`, and the
  last chunk is acked only after the write. The map name, version folder and level file name are checked first.
- A damaged chunk or a failed write sends `WFMappingDownloadCancelRequest`, deletes the files this download wrote and
  drops that transfer's chunks still in flight. A server cancel deletes them too.
- The folder isn't opened automatically; the download row offers `Open folder` (`UserData.OpenOsWindow`).

## Other

- `MapsButton` and its section heading in the MappingTools window show only with `AdminFlags.Mapping`.
- `modules.py --write` created `Content.Server/_WF/MappingMode/README.md` with the TODO overview (S4 writes it, and
  `--check` fails until then) and listed an S1 file in the MappingTools README.
- Built and sandbox-checked only; nothing ran against a server.
