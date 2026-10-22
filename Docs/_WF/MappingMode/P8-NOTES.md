# MappingMode phase 2, P8 notes

Pipe mode: what it does where the brief left room, and where the colours and prototypes come from. Each names the file
it lives in.

## What exists

- No pipe painter tool or pipe auto-router exists in this fork or its layers. The spray painter (`SprayPainter`,
  `Resources/Prototypes/Entities/Objects/Tools/spray_painter.yml`) paints pipes from a palette, and `colornetwork`
  (`Content.Server/Sandbox/Commands/ColorNetworkCommand.cs`) paints a formed pipe net by entity id. Neither builds pipes.
- Pipe layers exist: `AtmosPipeLayersComponent` with Primary, Secondary and Tertiary. Each plain pipe names its other
  layers in `alternativePrototypes` (`GasPipeStraightAlt1`, `GasPipeStraightAlt2`, ... in `alt_layers.yml`).

## Colours

The presets are the spray painter's "standard atmos pipes" entries, which are also what Monolith's maps use most:

| Preset | Colour | Spray painter key | Pipes with it in `Resources/Maps` and `Resources/SharedMaps` |
|---|---|---|---|
| Distro | `#0055CC` | `distro` | 41,996 |
| Waste | `#990000` | `waste` | 39,314 |
| Air | `#03FCD3` | `air` | 1,434 |
| Mix | `#947507` | `mix` | 538 |

There is no separate scrubber colour: scrubbers sit on the waste loop, and `#B30000` appears in no map, so the Waste
preset is labelled "Waste (scrubbers)". Custom takes a `#RRGGBB` hex; Auto sends no colour and asks the server for the
colour of the first pipe the path joins (none when it joins nothing). Painting needs a colour, so Auto can't paint.

## Path (`MappingToolsPipes.Extend`, shared)

- The client samples the cell under the mouse every frame while the button is held (`IWFMappingFrameHandler`, called
  from `WFMappingBuildModeSystem.FrameUpdate`), on the grid under the press. Cells off that grid are ignored, so a path
  never crosses grids.
- A jump of more than one cell is bridged with an L, along the axis of the last step first, so a fast drag keeps going
  the way it was going before it turns.
- Reaching a cell already on the path cuts the path back to it, so dragging back undoes the path. A closed loop is
  therefore two drags; the second one joins the first through Auto-join or Replace.
- The server runs the same function over the cells it gets, so a request with diagonal steps is bridged the same way.
  A request whose jumps add up to more than four times the cap is refused before it is walked.

## Plan (`MappingToolsPipes.Plan`, shared)

- Each path cell opens to the path cells before and after it. Cells that are next to each other but not consecutive
  stay apart, as two pipes running side by side do.
- Auto-join adds two things. A cell opens to any pipe that already points at it (a device on the path, a half, an open
  end). And each end of the path runs into the plain pipe straight past it, which gains the side it needs: straight to
  T, bend to T, T to four-way, half to straight or bend. Only the ends upgrade pipes; a path running alongside an
  existing line would otherwise tee into every pipe of it.
- A cell already holding one plain pipe on the layer is rebuilt with its old sides plus the new ones when Replace is
  on, and kept as it is when Replace is off. A kept pipe joins the path only where it already points at it. Devices
  (anything with pipe nodes that isn't one of the fifteen plain pipe prototypes), and cells holding more than one pipe
  entity on the layer, are never rebuilt.
- A path cell that ends up with no sides gets a half (see "Fixes" below); it used to get nothing.

## Server (`MappingToolsSystem.Pipes.cs`)

- The shape for a set of sides is the plain pipe whose base direction (`PipeShapeHelpers.ToBaseDirection`) turned by 0,
  90, 180 or 270 degrees gives exactly that set, the same rotation `PipeNode` applies. Pipes are spawned at the cell
  centre with that local rotation, so they anchor with the right connections at once.
- The layer's prototypes come from the primary pipe's `AtmosPipeLayersComponent.AlternativePrototypes`, read on the
  server, never from the client. `WFMappingCanEditEvent.Prototypes` names the primary ids: the `...Alt1` and `...Alt2`
  pipes are `HideSpawnMenu`, which MappingMode's prototype check refuses, and they are the same pipe on another layer.
- A placed pipe on a cell with no floor refuses the whole draw instead of skipping the cell as entity shapes do. A
  skipped cell would leave the pipes on both sides open towards nothing.
- Colours: an explicit colour paints every pipe laid or rebuilt. Auto paints the new pipes with the joined pipe's colour
  and keeps the colour of rebuilt ones. A rebuilt pipe whose sides don't change isn't respawned, only painted if the
  colour differs.
- The edit puts the rebuilt pipes in a deleted group (applied first, so the new pipe never overlaps the old one) and
  the new pipes in a created group. Painting is a new `MappingEdit.PipeColors` list of colour changes on the same
  entities, so undo and redo don't respawn anything for it. Undo checks count those changes as touched entities.
- The check is `Paste` with the new pipes as entities and entity cells, the rebuilt pipes as `Moving` (so the per-tile
  count doesn't count a pipe that is about to go) and the path length as selection cells.
- Network paint floods from the pipe nodes on the clicked cell: the chosen layer if a pipe is there, else the first
  layer that has one. It follows sides that face each other with the same node group and layer, exactly the rule
  `PipeNode.GetReachableNodes` joins nets by, so it works on a paused, uninitialised mapping map without waiting for
  node groups to form. It goes through devices' pipe nodes the same way, so it stops where a pump or valve splits the
  net. Only entities with `AtmosPipeColorComponent` are painted. The flood stops at 16384 entities for everyone; for
  mappers the edit check refuses it whole past `max_edit_entities`.

## Client

- `WFMappingPipeMode` (`Modes/`) is the handler; the settings live on `WFMappingBuildModeSystem` beside the shape and
  erase settings, and Properties shows them in Pipe mode. The preview is the shape overlay, in the preset colour (grey
  for Auto, red past the cell cap).
- Right click drops a drag, or disarms Paint a network. Escape does the same, then goes back to Place.
- Pipes are subfloor; the Layers tab shows subfloor by default.

## Noticed, not changed

- `AlignAtmosPipeLayers` (`Content.Client/Atmos/AlignAtmosPipeLayers.cs`) swaps the placement's prototype for the
  `...Alt` one when the cursor is over layer 2 or 3, and `WFMappingModeSystem.AllowPlace` refuses those for mappers
  because they are `HideSpawnMenu`. So a mapper placing a pipe or atmos device on layer 2 or 3 in Place mode is likely
  refused (read from the code, not tried). Pipe mode doesn't go through placement and isn't affected.

## Not verified

- The preview, the Properties rows and the drag in a real client haven't been looked at; the tests cover the server and
  the pure parts.

## Fixes: "Those pipes are already like that"

The refusal fired whenever a draw changed nothing, for reasons that weren't that. Found with tests
(`MappingToolsPipesTest`, unit and integration):

- A single click is a path of one cell. It has no path neighbours and no ends, so with nothing pointing at it `Plan`
  gave it no sides, `TryShape` failed on the empty set and the cell was dropped. This was the common one.
- A drag across a plain pipe with Replace off kept that pipe, and a path cell whose only path neighbour was a kept pipe
  not pointing at it got no sides either. A two- or three-cell drag across a pipe laid nothing at all.
- Cells holding a device (or several pipes on the layer) and cells kept with Replace off were folded into one "kept"
  count with the dropped lone cells, and when nothing else changed the message blamed the pipes.
- Not causes, checked: the client's path is grid-local (`TileIndicesFor` on the press's grid, rotated grids included),
  the release adds the last cell, and `PipesAt` only sees nodes on the drawn layer, so pipes on other layers don't
  count as present.

Now:

- A cell left with no sides becomes a half: towards its path neighbour when it has one (the kept pipe it was cut off
  by); a lone click opens to a pipe pointing at it, else (with Auto-join) to a plain pipe beside it, which gains the
  side, else to the facing (`WFMappingPipeSpec.Facing`, R or Ctrl+wheel in Pipe mode, north by default).
- `MappingPipePlan.Skipped` counts plain pipes kept with Replace off and `Blocked` counts devices and stacks.
  `MappingToolsPipes.ReportKey` picks the message: a draw that changed nothing says whether the cells all had pipes
  (turn on Replace), all held devices, both, or really had nothing to change; a draw that changed something and
  skipped cells says how many were laid and skipped. A clean draw stays quiet, as before.
- Network paint has its own "already that colour" message.
- `MappingToolsSystem.LastPipeReport` keeps the last pipe message's Fluent id, for the tests.

## Atmos devices box

- Properties shows an icon grid in Pipe mode, one button per `MappingToolsPipeDevices.Ids` entry that exists and isn't
  `HideSpawnMenu`; the tooltip is the name with its suffix. The list: `GasVentPump`, `GasPassiveVent`,
  `GasVentScrubber`, `GasDualPortVentPump`, `GasPort`, `GasOutletInjector`, `GasPressurePump`, `GasVolumePump`,
  `GasPassiveGate`, `GasPressureRegulator`, `GasValve`, `SignalControlledValve`, `PressureControlledValve`,
  `GasFilter`, `GasFilterFlipped`, `GasMixer`, `GasMixerFlipped`, `GasThermoMachineHeater`,
  `GasThermoMachineFreezer`, `HeatExchanger`, `HeatExchangerBend`, `GasPipeSensor`, `GasPipeHalf`. There is no pipe
  cap prototype; the half is the cap. The `...On`, `...Enabled`, gas-specific filter and sensor variants are left out
  to keep the grid small; Place mode still has them.
- Picking one arms it. A left click sends `MappingDrawOp.PipeDevice` with the id and the pipe spec; right click or
  Escape disarms it, and arming Paint a network disarms it too. The hovered cell shows the icon turned to the facing
  and a tick on the facing side. The client can't know a device's ports (`PipeNode` is server-only), so the preview
  shows the facing, not the turn auto-join will pick.
- The server takes only ids in the list, with pipe nodes. Ports at rest are the union of the prototype's `PipeNode`
  directions. With Auto-join, `MappingToolsPipes.DeviceTurns` scores each quarter turn by pipes pointing at the cell
  (weighted 8) and plain pipes beside it that could gain a side (1), and of the best takes the nearest to the facing,
  so a pump between two straights keeps the flow direction its facing gives. Without Auto-join the facing decides.
- A plain pipe beside a port that doesn't point at the cell is rebuilt with that side added, keeping its own colour.
  The device takes the preset colour; Auto takes the colour of the plain pipe it replaces or a pipe it joins.
- Layers: the prototype's `AtmosPipeLayers.AlternativePrototypes` entry when there is one (pumps, valves, sensors, the
  half), else the device is created uninitialised, `SetPipeLayer` moves it, and only then is it initialised and
  anchored, so `PipeRestrictOverlap` sees the right layer. A device whose `NumberOfPipeLayers` doesn't reach the layer,
  or whose layers are locked (the radiator has one layer), is refused by name. The check's `Prototypes` names the
  primary id, as pipe draws do, so the hidden `...Alt1`/`...Alt2` ids pass MappingMode's prototype check only through
  this path.
- On the cell: nothing on the layer, or one plain pipe with Replace on (swapped for the device), else refused: a pipe
  with Replace off, a device or a stack. No floor is refused. The edit check is `Paste` like a pipe draw.
- Undo: the swapped pipe and the rebuilt neighbours are the deleted group, the device and the new neighbours the
  created group.

## Not verified (devices)

- The device grid, its tooltips, the hover icon and R/Ctrl+wheel in a real client haven't been looked at. A rotated
  device icon may not match how the device's own sprite turns (vents draw with `noRot`).
