# MappingMode R2 notes: Link mode

Link mode (key 9) configures signal links, device lists and access without hands, so a downloaded map works. What it
does where the brief left room, and what was verified. Each names the file it lives in.

## What exists

- The signal linker, network configurator and access configurator (`AccessOverrider`) all need hands and an item, so a
  mapping observer can't use them. Nothing in the fork layers configures links or access from a mapping screen.
- `NetworkConfiguratorLinkMenu` (the configurator's port window) could be shown alone, but it toggles one pair per
  click through its BUI and has no Apply; Link mode has its own checkbox window (`Link/WFMappingLinkPortsWindow.cs`)
  that sets the whole pair at once.

## What is networked

- `DeviceLinkSourceComponent` and `DeviceLinkSinkComponent` are networked components without state: the client knows
  they exist, but not `LinkedPorts`, and not ports added at runtime (`DoorSignalControlSystem`, `AirAlarmSystem` and
  about 40 others call `EnsureSinkPorts`/`EnsureSourcePorts` on the server). So the ports window asks the server
  (`MappingToolsLinkPortsRequestEvent`), and the overlay draws signal links from a snapshot
  (`MappingToolsLinksRequestEvent` / `MappingToolsLinksSnapshotEvent`).
- `DeviceListComponent.Devices` and `AccessReaderComponent.AccessLists` are networked, so device-list lines and the
  access window read the client's components.
- Snapshot: requested when the Links layer turns on, when Signals is chosen or a source is selected, when the viewed
  level changes, and every 3 seconds while wanted (at most every 0.5 s). The server pushes a fresh one after every
  link edit, undo and redo. Requests go through `CanUse(request: true)`, so the tools rate applies, and a `Read` edit
  check on the map, so a mapper only reads their session's levels.

## Signals (`MappingToolsSystem.Links.cs`)

- The request names the exact port pairs; the server checks both entities exist on a map with their component, every
  pair names a port prototype on the right side and on that entity, both are on one map within the source's `Range`,
  and `CanEdit` (Edit) on both maps. An empty list unlinks the pair (`RemoveSinkFromSource`), anything else goes
  through `SaveLinks(null, ...)`, so link attempts still run and nothing pops up from the game's linker.
- Unchanged requests are refused with "Nothing to change" rather than recorded.
- Defaults come from the game's `GetDefaults` (which returns nothing when any source port has no default links, as the
  linker does), filtered to the sink's ports.
- Clicking: with no source, a click picks one. With a source, a click on a sink (other than the source) opens the
  window; on another entity that is only a source it switches source; on the source itself it deselects. Doors are
  both source and sink, so pick a new source with Escape first.
- A click picks the clicked entity when it has the component, else the nearest one within 0.45 tiles.

## Devices

- Compatible devices copy the network configurator's own check (`TryAddNetworkDevice`): a `DeviceNetworkComponent`
  with `SavableAddress`. It has no frequency check. Link mode also refuses the holder itself, and devices on another
  map, since `DeviceListSystem.OnMapSave` drops cross-map entries with an error.
- The list is applied whole through `UpdateDeviceList(..., merge: false)`, so `DeviceListUpdateEvent` carries the full
  new list and the air alarm resyncs. Past `DeviceLimit` (32) is refused.
- A click toggles the device (add or remove); right click removes; a drag of 0.75 tiles or more adds every device in
  the box (256 at most).

## Access

- Levels are written to the reader the game checks (`GetMainAccessReader`). Airlocks, windoors, shutters and blast
  doors take their access from electronics in a `board` container that `ContainerFill` only fills at map init, and
  mapping maps are never initialised. Writing the door's own reader then would be lost once the map starts, so the
  door's reader is written and marked with `WFMappingAccessFillComponent` (shared, networked, no fields); at map init
  (`after: ContainerFillSystem`) the marker copies the door's access into the new electronics and removes itself.
  Before this, mappers had to pick a `...Locked` prototype for each access.
- Access is set as one level per list (any one opens), as `SetAccesses` and the access configurator do; empty opens to
  everyone. Undo restores the exact old lists, AND-sets included.
- The window shows what the reader will accept: for a marked door its own lists, for an unmarked door before map init
  the access of the electronics its `ContainerFill` would put in (so `AirlockEngineeringLocked` shows Engineering).
- Groups: each level sits under the smallest access group holding it, so catch-alls like All access don't swallow the
  rest; levels in no group go under Other.
- Apply to selection takes every entity with an `AccessReaderComponent` in the MappingTools selection (area, cells and
  picked entities). Copy from last ticks the levels last applied in this client session.

## Undo

- `MappingEdit` has three new lists: `Links` (source, sink, old and new pairs), `DeviceLists` (holder, old and new
  devices) and `Access` (reader, old and new lists, old and new marker). They hold `MappingEntityRef`s, so they follow
  entities other edits recreate. `SettingChanges` counts them for the undo check's touched entities.

## Client

- `Link/WFMappingLinkSystem.cs` holds the tool, the selection, the snapshot, the windows and the Links layer;
  `Modes/WFMappingLinkMode.cs` is the handler. Tab (`WFMappingLinkCycle`, priority 2 above chat's Tab) is added to
  `common` only while Link mode is current, as Template does with the arrows.
- The overlay (`Link/WFMappingLinkOverlay.cs`) draws amber signal lines and blue device-list lines with small squares
  at the ends; the selected source or list gets an outline and white lines. It is added only while Link mode or the
  Links layer is on.

## Not verified

- The windows, overlay and clicks haven't been looked at in a real client; the tests cover the server requests and
  map init. The headless client sandbox check covers the client code.
