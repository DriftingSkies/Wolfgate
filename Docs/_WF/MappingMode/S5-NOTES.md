# S5 notes: the two server gaps, the tests and the gate

## Delete grid strands the mapper (S3 gap)

- The engine deletes a grid that loses its last tile (`Robust.Server` `MapSystem.HandleGridEmpty`, directed
  `EmptyGridEvent`) and everything parented to it. `GridDeletionContainerSystem` already owns the
  (`MapGridComponent`, `EntityTerminatingEvent`) pair and queues every child for deletion, so a terminating hook comes
  too late and can't be added anyway.
- MappingMode now subscribes to the broadcast `EmptyGridEvent` ordered `before` the engine's `MapSystem`
  (`WFMappingModeSystem.Riders.cs`). The event bus sorts ordered broadcast and directed subscriptions together, so it
  runs while the grid still exists. While the mode is on it moves every mapping observer, actor and ghost parented to
  the grid onto the grid's map at the same world position and rotation. The tile backstop calls the same helper before
  it deletes a grid made past the cap.
- Delete grid wasn't undoable either: the edit recorded tile changes against the grid's uid, and once the engine
  deleted the grid undo skipped every tile. MappingTools now turns a delete (or cut) whose selection holds every tile
  of its grid into one deleted group whose root is the grid itself:
  - before the grid is saved and deleted, players, ghosts and anything holding one are moved off it onto the map
    (`MoveRidersOff`), so they are neither deleted nor copied into the snapshot;
  - undo loads the snapshot onto the holding map and moves the grid back to its map and pose, with its tiles, decals,
    atmosphere and entities;
  - the restored grid has a new uid, so every recorded edit and clipboard pointing at the old uid is remapped
    (`RemapGrid`), and refs of entities inside a restored group follow too (`MappingEntityGroup.Inner`), so edits made
    before the delete still undo on the restored grid.
- A grid root is saved with `CanSplit` off (and set back after saving and after the restore). Otherwise a grid whose
  tiles form separate islands, which MappingTools edits allow, splits while it loads onto the holding map and the
  split-off part is deleted with the holding map.
- Reference loads and library opens can't strand an observer: observers are only ever on session levels or the admin
  map, scratch maps hold nothing but the loaded grids, and denied entities are stripped before anyone enters.
- Not covered: a grid emptied some other way than a whole-grid delete (erasing its last tile with the spawn menu,
  undoing a paste that made a new grid) is still deleted by the engine. The mapper is moved off it, but redoing such an
  edit can't bring that grid back.

## Admin map flag (S3 gap)

- `WFMappingSessionStateEvent` carries `AdminObserver`: the player is attached to an admin observer that the Observe
  entry spawned. The server tracks those entities, sends the state right after spawning one, and the lobby state sent on
  return clears it.
- The Mapping window shows `Return to lobby` only with that flag (it used to show for any admin without a session).
- The flag follows the observer rather than the admin map, so an admin who teleports into a session with
  `wf_mappingmode_tp` keeps the way back. Turning the mode off now returns these observers wherever they are, not only
  those still on the admin map.

## Found by the tests

- Closing a session left its CE z-network behind, still listing the deleted levels. The network is a global PVS
  override, so every client got a state with dead entities and PVS threw ("An item with the same key has already been
  added"). `WFMappingModeSystem.DeleteMaps` now deletes the levels' networks with them; closing a session and a failed
  library open use it.
- Lobby auto-votes (map and preset, `_Mono/AutoVote`) still run with the mode on. The tests turn `vote.autovote_enabled`
  off because dummy sessions can't receive vote messages. On a live mapping server those votes are pointless; turning
  them off with the mode would be a small follow-up (not done here).

## Tests

Unit tests (`Content.Tests/_WF/MappingMode/`), integration tests (`Content.IntegrationTests/Tests/_WF/MappingMode/`).

- Integration tests turn `console.loginlocal` off while the mode is on, since the test client and dummy players connect
  from loopback and would otherwise be hosts with every admin flag. The client is demoted with `ReloadAdmin` unless the
  test needs an admin. The mode and `console.loginlocal` are put back at the end, votes stay off, and the pair is
  dirty in any case.
- Requests a dummy player "sends" are raised as `EntitySessionMessage<T>` through reflection (`RaiseAs`), since the
  type is internal to the engine. The real client is used where the reply matters (download chunks, the reference
  footprint token, the admin flag).
