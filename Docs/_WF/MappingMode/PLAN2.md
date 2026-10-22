# MappingMode plan, phase 2

Phase 2 adds a mapping screen with SDMM-style build modes, a PNG template underlay, a customisable quick-select bar
and map upload. This file holds the verified facts, the decisions and the stages. Paths are repo-relative, `RT` is
`RobustToolbox` (engine v291), and line numbers are at `343e32ec34` (engine `ce2461b5d`).

## Changed or impossible items

1. **Line, Rect and Fill can't use the engine's line and grid placement.**
   - `PlacementTypes.Line` only draws along one axis (`RT/Robust.Client/Placement/PlacementMode.cs:138-166`).
   - `Grid` only draws a filled rectangle (`:170-193`).
   - Only the `AlignTile*` and `AlignSnapgrid*` modes support either (`Modes/*.cs`, `HasLineMode`/`HasGridMode`).
   - Both send one `MsgPlacement` per cell (`PlacementManager.cs:435-463`, `:828-852`). The server's edit throttle
     has a burst of `max_edits_per_second` (60, `WFMappingModeSystem.Limits.cs:22`, `:46-50`), so any shape over 60
     cells loses its tail without a word.

   Shapes and fills go through one new batched MappingTools request instead, `MappingToolsDrawEvent` (S-P1). It is
   checked as a single tool edit and recorded as one undo step.
2. **Number keys 1-7 are the action hotbar's slot keys.** `Hotbar1`-`Hotbar0` are bound to `Num1`-`Num0` in `common`
   (`Resources/keybinds.yml:565-594`, `Content.Client/Input/ContentContexts.cs:135-138`) and the actions bar triggers
   slots on them (`ActionUIController.cs:117-130`). The mapping screen has no actions bar, so the mode keys take 1-7
   at a higher priority while a session is live. The quick-select bar is mouse-only in this phase.
3. **The actions hotbar is not reused for quick select.** A new control is less code than working around it (D below).
4. **A PNG's size can't be read before decoding through ImageSharp.** The sandbox allows only `Image.Load<>`,
   `Width`, `Height` and `Dispose` on `Image` (`RT/Robust.Shared/ContentPack/Sandbox.yml:382-388`); `Identify` is not
   listed. The template reads width and height from the PNG IHDR bytes before decoding. The GPU's maximum texture size
   is engine-internal (`RT/Robust.Client/ResourceManagement/ResourceCache.Preload.cs:286`), so the caps are fixed.
5. **The picked PNG can't be opened again later.** The file dialog hands over a stream, and Client code has no
   `System.IO` path access (PLAN.md design change 7). The template keeps a copy of the image in the client data
   folder next to its settings.
6. **"Under the map" hides the template under laid tiles.** `WorldSpaceBelowWorld` draws below grids
   (`RT/Robust.Shared/Enums/OverlaySpaces.cs:54-57`). The template gets an Under/Over choice:
   - Under: `WorldSpaceBelowWorld`, the default.
   - Over: `WorldSpace`, drawn translucent above entities.

   One overlay declares both spaces and draws in the chosen one, as `WFMappingBoundsOverlay.cs:29` does.
7. **Ctrl+wheel needs a hook in a Shuttles file.** The viewport's only `MouseWheel` override is already taken by the
   Shuttles partial (`Content.Client/_WF/Shuttles/ScalingViewport.ShuttleCamera.cs:10-20`, `ScalingViewport` is
   sealed partial). P1 adds a call there; it is a `_WF` file, so it takes no marker.
8. **Docked panels are not windows, so WindowPopout can't see them.** WindowPopout hooks `WindowRoot.OnChildAdded`
   (`WindowPopoutUIController.cs:30`). Each dock panel gets an Undock button that moves it into a `FancyWindow`.
   WindowPopout then adds its button to that window, and closing the window docks the panel again.
9. **Decal eyedropping and area erase can't use `GetDecalsInRange` on the client.** It is a no-op there
   (`Content.Shared/Decals/SharedDecalSystem.cs:112-116`).
   - The eyedropper reads `DecalGridComponent.ChunkCollection` itself, as `DecalOverlay.cs:39-59` does.
   - Area decal erase is an operation of `MappingToolsDrawEvent`.
10. **Upstream `MappingState`/`MappingScreen` are not reused.** `MappingState` has no caller; the only references are
    `IOnStateEntered<MappingState>` in `ContextMenuUIController.cs:20` and its own overlay. Its prototype tree builder
    is private (`MappingState.cs:215-389`) and builds a button per prototype.

    The palette reuses Wolfgate's own categorised spawn menu instead (`Content.Client/_WF/Spawning`), which already
    has search, categories, favourites (`wf.spawn_favourites`) and a recent list. Its window body moves into a panel.
11. **Uploaded YAML is checked before the loader sees it.**
    - Anchors and aliases are refused. `DataNodeParser` resolves aliases by sharing nodes
      (`RT/Robust.Shared/Serialization/Markdown/DataNodeParser.cs:69-82`), so a crafted file can make reading blow up.
    - Unknown prototypes are refused first. The loader logs an error per missing prototype and fails
      (`RT/Robust.Shared/EntitySerialization/EntityDeserializer.cs:270-307`).
    - A root that isn't a mapping is refused first. `TryReadFile` casts the root to `MappingDataNode`
      (`MapLoaderSystem.cs:82-106`).
12. **An upload made from the lobby has no session to count limits against.** It is checked against the caps a new
    session would carry: `max_grids_per_session`, `max_entities`, the per-tile caps and `map_size`.
13. **No `ContentKeyFunctions` edit.** The engine collects key functions from every `[KeyFunctions]` class
    (`RT/Robust.Shared/Input/BoundKeyMap.cs:82-93`), so a `_WF` class in Shared declares them. `keybinds.yml` still needs
    a marked block for the default keys.
14. **Choosing the screen takes a small marked edit in `GameplayState`.** `LoadMainScreen` is private and picks the
    screen from `ui.layout` (`Content.Client/Gameplay/GameplayState.cs:114-133`).
15. **The SDMM "right erases under cursor" in Place mode skips the engine eraser.** The engine only erases while
    `Eraser` is on, which clears the current placement (`PlacementManager.cs:465-516`). The mode sends `MsgPlacement`
    `RequestEntRemove` itself instead. `MsgPlacement` is public (`RT/Robust.Shared/Network/Messages/MsgPlacement.cs:14`).
    The server gate is unchanged (`SandboxSystem` → `WFMappingModeSystem.AllowPlacement`).

## Verified facts

### Screens: loading, caching, restoring

| Fact | Where |
|---|---|
| Every non-abstract `UIScreen` subtype is constructed once at UI init and cached by type. A screen's constructor runs for every client at startup, before entity systems exist | `RT/Robust.Client/UserInterface/UserInterfaceManager.UIScreens.cs:35-50`, called from `UserInterfaceManager.cs:156` |
| `LoadScreen<T>` sets `ActiveScreen` (fires `OnScreenChanged`, `UIScreen.OnLoaded`) and parents the cached instance; `UnloadScreen` removes it and nulls `_activeScreen` without `OnUnloaded` | `UIScreens.cs:52-55`, `:70-84`; `UIScreen.cs:79-87` |
| `DefaultGameScreen`'s constructor sets the autoscale cvars (`AutoscaleMaxResolution` setter writes cvars). A new screen must not set them, or the last constructed screen wins | `DefaultGameScreen.xaml.cs:15`; `RT/.../UIScreen.cs:18-28` |
| `GameplayState.Startup` → `LoadMainScreen` picks `DefaultGameScreen` or `SeparatedChatGameScreen` from `ui.layout`, then `GameplayStateLoadController.LoadScreen()` | `GameplayState.cs:43-48`, `:114-133` |
| `ReloadMainScreen()` is public. It is a no-op if the active screen has no `MainViewport`; otherwise it unloads and loads again. `ui.layout` changes call it | `GameplayState.cs:92-106`, `:48` |
| `Shutdown` unloads whatever screen is active | `GameplayState.cs:79-90`, `:108-112` |
| Controllers hook screen changes through `GameplayStateLoadController.OnScreenLoad`/`OnScreenUnload` | `Content.Client/UserInterface/Systems/Gameplay/GameplayStateLoadController.cs:6-20`; subscribers: Actions, Alerts, Chat, Ghost, Hotbar, Inventory, GameTopMenuBar, Viewport, Vote, `_WF/Cockpit`, `_WF/RoundSetup` |
| The mapping client already runs its setup and teardown from `FrameUpdate`, once the observer and `GameplayState` both exist | `Content.Client/_WF/MappingMode/WFMappingClientSystem.cs:97-107`, `:159-195` |

### Screens: what the controllers expect

| Expects | Where | If missing |
|---|---|---|
| A `MainViewport` widget | `GameplayState.cs:32` (`!`), `ViewportUIController.cs:23` | crash (GameplayState input) |
| A control named `ViewportContainer` (`LayoutContainer`) | `ChatUIController.cs:256-264` (`FindControl` throws) | crash on screen load |
| A widget of exactly type `ChatBox`. Any screen other than Default/Separated goes through `GetWidget<ChatBox>()`, keyed by exact type, so a `ResizableChatBox` doesn't count | `ChatUIController.cs:298-330`, `RT/.../UIScreen.cs:113-116` | throws "Cannot get chat box in screen!" |
| `GameTopMenuBar` | `ConstructionMenuPresenter.cs:46-55` (`GetActiveUIWidget`, throws) on crafting availability changes | crash on attach |
| `ActionsBar`, `AlertsUI`, `GhostGui`, `HotbarGui`, `InventoryGui`, vote container | all `GetActiveUIWidgetOrNull`/null-checked (`ActionUIController.cs:59`, `:771-791`; `AlertsUIController.cs:20`; `HotbarUIController.cs:36-50`; `InventoryUIController.cs:57-64`; `VoteUIController.cs:22-33`) | feature absent, no error |

- **Widget registry.** `UIScreen._widgets` is filled only when a direct child is added. It walks that child's subtree at
  that moment (`UIScreen.cs:151-178`) and is pruned only when a direct child is removed (`:180-201`). Widgets must sit
  in the XAML tree when the screen is built. A widget moved later between nested containers leaves a stale entry, and
  adding it again throws.
- **Hidden controls get no `FrameUpdate`** (engine `Control.DoFrameUpdateRecursive`). Readouts and status lines are
  driven from a `UIController.FrameUpdate` or a system, not from the control.
- **The screen leaves out `HotbarGui`.** Its constructor registers its hands container with `HotbarUIController` at
  construction (`HotbarGui.xaml.cs:11-19`), and the observer has no hands.

### Input: key dispatch, priorities, contexts

- **Dispatch.** `KeyDown` collects every binding in the active context whose combo matches
  (`RT/Robust.Client/Input/InputManager.cs:246-281`). Bindings are sorted by combo size, then by `priority` descending
  (`:885-895`). They fire in order until one reports handled (`:297-303`).
- **Sub-combos.** A larger matched combo cancels plain-key bindings unless `allowSubCombs` (`:267-277`). With Shift
  held, a plain `MouseLeft` binding does not fire when any `Shift+MouseLeft` binding is in context, such as
  `WFMappingSelect` or `EditorLinePlace`. Mode mouse functions need Shift and Control variants.
- **Handlers.** Each binding goes UI first, then the viewport, then content `InputSystem`. The `CommandBinds` handlers
  run in topological order of `BindBefore`/`BindAfter`; the first that returns true stops the rest
  (`RT/Robust.Client/GameObjects/EntitySystems/InputSystem.cs:53-82`,
  `RT/Robust.Shared/Input/Binding/CommandBindRegistry.cs`). The builder has `BindBefore`/`BindAfter`
  (`CommandBinds.cs:107-130`).
- **Typing.** While any control has keyboard focus, keyboard bindings (not `canFocus`) never reach the simulation
  (`RT/Robust.Client/UserInterface/UserInterfaceManager.Input.cs:476-481`). A click on the viewport releases focus
  (`:404-407`). Number keys, R and the arrows therefore can't trigger modes while typing in search or chat, with no
  extra code.
- **Contexts.** `editor` inherits `common` and adds only `EditorLinePlace`, `EditorGridPlace`, `EditorPlaceObject`,
  `EditorCancelPlace` and `EditorRotateObject` (`RT/Robust.Client/Input/EngineContexts.cs:80-85`). Movement is in
  `common` (`:27-31`). The observer's context is `aghost`, which inherits `common` (`ContentContexts.cs:140-156`).
  Functions added to `common` work both in `aghost` and while placing.
- **The `PlacementManager` switches the context while placing.** Becoming active sets the `editor` context; becoming
  inactive restores the entity's (`PlacementManager.cs:89-102`, `:345-356`). `DecalPlacementSystem.SetActive` does the
  same (`Content.Client/Decals/DecalPlacementSystem.cs:192-199`).

### Input: what the placement manager does with each key

`CommandBinds` are registered on joining the server (`PlacementManager.cs:240-331`).

| Function (default key) | Behaviour |
|---|---|
| `EditorPlaceObject` (LMB) | Down: erases (rect if `EraserRect`, else the entity) when `Eraser`, otherwise arms `_placenextframe`. Up: places a non-tile entity. Returns false when inactive (`:271-315`). Tiles are placed every frame while the button is held (`:616-620`) |
| `EditorLinePlace` (Shift+LMB) / `EditorGridPlace` (Ctrl+LMB) | Sets `StartPoint` and `PlacementType`; Ctrl with `Eraser` starts a rectangle erase (`:251-270`, `:630-662`) |
| `EditorCancelPlace` (RMB) | Ends line/grid, else `Clear()` (`:321-329`) |
| `EditorRotateObject` (MMB, priority -1) | `Rotate()` (`:316-320`; `keybinds.yml:164-167`) |

`DecalPlacementSystem` binds `EditorPlaceObject`/`EditorCancelPlace` too, and acts only while `_active`
(`DecalPlacementSystem.cs:51-104`).

### MappingTools: how its keys take priority

- `WFMappingSelect` is bound on LMB, and Shift+LMB, with `priority: 1`; `WFMappingSelectCancel` on RMB with
  `priority: 1` (`keybinds.yml:629-644`). They beat the priority-0 `EditorPlaceObject`/`Use`.
- The functions are in `common` only while the tools are enabled (`MappingToolsSystem.cs:141-151`), so they can't
  shadow plain keys for anyone else.
- `WFMappingSelect`'s handler returns false unless `Selecting` (enabled, select on, not placing) or picking
  (`MappingToolsSystem.cs:96`, `:111-114`; `MappingSelectionTool.cs:107-110`). The click then falls through to the
  placement manager.
- The reference placement claims the same functions by `BindBefore(..., typeof(MappingToolsSystem))`, and adds
  `WFMappingReferenceCancel` on Escape at priority 5 (`WFMappingReferenceSystem.cs:61-72`, `keybinds.yml:692-697`).
- Escape order:

  | Function | Priority |
  |---|---|
  | `TextReleaseFocus` | 15 |
  | `CloseModals` | 10 |
  | `WFMappingReferenceCancel` | 5 |
  | (new) `WFMappingModeCancel` | 4 |
  | `EscapeContext` (game menu) | 0 |

  Sources: `keybinds.yml:483-486`, `:12-15`, `:525-527`.

### Actions hotbar: why quick select doesn't reuse it

- `LoadActionAssignments` spawns client action entities onto the local entity and clears all assignments first
  (`Content.Client/Actions/ActionsSystem.cs:303-358`, `:319`, `:334`).
- `AssignSlots` ignores the file's `(hotbar, slot)` pairs and appends action ids in file order
  (`ActionUIController.cs:811-823`). There are no pages or named sets.
- Nothing saves assignments. `SaveActionAssignments` is commented out as a sandbox problem
  (`Content.Client/Commands/ActionsCommands.cs:7-33`) and absent from `ActionsSystem`.
- Filling an empty slot from the current placement exists (`Content.Client/Mapping/MappingSystem.cs:41-125`, raised at
  `ActionUIController.cs:672-675`), as do drag-swap and right-click clear (`:583-606`, `:653-659`).
- Actions die with the entity: a rejoin spawns a new observer, and the bar is relinked from its `ActionsComponent`
  (`ActionsSystem.cs:246-270`).

**Decision:** a new `WFMappingQuickBar` with its own store. Reuse would still need a YAML writer for
`BaseActionComponent`, pages, named sets, and restoring after every observer change, which is more code than the bar.
The default set is read from `/mapping_actions.yml` (its `StartPlacementActionEvent` entries). The mapping screen has
no `ActionsBar`, and `WFMappingClientSystem.Setup` stops loading `mapping_actions.yml` into the actions bar while the
mapping screen is in use.

### File dialog, PNG decoding, client data folder

- **Dialog.** `IFileDialogManager.OpenFile`/`GetFileAndName(filters, FileAccess, FileShare?)` return a stream, and the
  file name for the latter (`RT/Robust.Client/UserInterface/IFileDialogManager.cs:31-60`). Pass `FileAccess.Read`, as
  `InstrumentMenu.xaml.cs:132` does; `CustomMarkingLibraryWindow.cs:408` leaves the default ReadWrite. Guard against
  the window being disposed while the dialog is open (`InstrumentMenu.xaml.cs:134-138`).
- **Decoding.** These are whitelisted (`Sandbox.yml:378-388`; `RT/Robust.Client/Graphics/Texture.cs:90-110`):
  - `Image.Load<Rgba32>(Stream)`, used by `Content.Client/_WF/CustomMarkings/CustomMarkingPng.cs:16-31`
  - `Texture.LoadFromPNGStream(stream, name, params)`, used by `Content.Client/_WF/Headshot/HeadshotSystem.cs:55-66`
  - `Texture.LoadFromImage`
- **Data folder.** `IWritableDirProvider` offers `CreateDir`, `Exists`, `Delete`, `DirectoryEntries`, plus extension
  methods `OpenRead`, `OpenWrite`, `OpenWriteText`, `ReadAllText`, `WriteAllBytes` (`IWritableDirProvider.cs:25-70`,
  `WritableDirProviderExt.cs:21-148`). Paths are `ResPath`.
- **YAML.** YamlDotNet `YamlStream`, `YamlMappingNode`, `YamlSequenceNode` and `YamlScalarNode` are whitelisted
  (`Sandbox.yml:1666-1700`), and `System.Text.Json` is not, apart from two attributes (`:779`). Client persistence uses
  YAML through `YamlStream`.
- **Tests.** `Content.Tests` references `Content.Client` (`Content.Tests.csproj:20`), so pure client classes can have
  unit tests.

### Chunked upload: what the download code gives

- **Server.** `WFMappingDownloadSystem` keeps one transfer per player in a dictionary. It sends 2 chunks in flight,
  takes acks in order, drops a transfer after a 30 s stall, and cancels on disconnect or membership end
  (`Content.Server/_WF/MappingMode/WFMappingDownloadSystem.cs:28-53`, `:73-86`, `:169-211`, `:218-243`).
- **Assembler.** `WFMappingDownloadAssembler` is pure and has unit tests. Its caps are constants: 256 KiB chunks and a
  32 MiB file (`:26-28`). It accepts only a `z<D>.yml` name (`:50-59`), so upload gets its own assembler with a
  per-transfer byte cap.
- **Network limits** (PLAN.md §6). Lidgren takes up to 16 MiB per message, and a client→server entity event has no
  other cap. The server checks each chunk's size again after the engine has read it, and memory is bounded by the
  16 MiB message limit.
- **Loading from bytes.** `MapLoaderSystem.TryLoadGeneric(Stream, source, ...)` and
  `TryLoadGeneric(MappingDataNode, source, ...)` exist (`RT/Robust.Shared/EntitySerialization/Systems/MapLoaderSystem.Load.cs:106-114`,
  `:116-222`), so an upload is never written to a temporary file before validation.
- **Reference path.** It loads onto a paused, uninitialised scratch map (`MergeMap`, `InitializeMaps=false`,
  `PauseMaps=true`), deletes loose entities, strips the deny set, checks grid, entity, per-tile and decal caps, then
  computes bounds (`WFMappingModeSystem.Reference.cs:98-159`, `:182-280`). Loads are serialised by the global
  `reference_cooldown` (`:119-127`).
- **Library save.** `WFMappingLibrarySystem.SaveVersion` takes a session, but only uses its levels; it writes through
  `_zLevels.TrySaveLevels`, prunes and enforces `max_maps`/`max_library_bytes` (`WFMappingLibrarySystem.cs:323-399`).
- **Version names.** Folders end `_manual|_auto` (`WFMappingLibraryNames.cs:36-37`, `:63-65`, `:93-100`). Pruning
  removes `Auto` first and treats every other kind like manual (`WFMappingPrune.cs:20-24`).

### Smaller facts used by the plan

| Fact | Where |
|---|---|
| Layer toggles | decal overlay `DecalSystem.ToggleOverlay` (`Content.Client/Decals/DecalSystem.cs:32-45`), `MarkerSystem.MarkersVisible`, `SubFloorHideSystem.ShowAll`, `ILightManager.Enabled`, MappingTools wall hiding (`MappingToolsSystem.cs:337-370`) |
| Entity under a point | `GameplayStateBase.GetClickedEntity(MapCoordinates)` (`Content.Client/Gameplay/GameplayStateBase.cs:104`) |
| Bound keys as text | `IInputManager.GetKeyFunctionButtonString(function)` (`RT/Robust.Client/Input/IInputManager.cs:110`), for the hotkey panel |
| Decal placement API | `DecalPlacementSystem.UpdateDecalInfo`/`SetActive` (`DecalPlacementSystem.cs:182-199`) |
| Tile placement | `PlacementOption = "AlignTileAny"`, `IsTile = true`, `TileType` (`TileSpawningUIController.cs:44-54`) |
| Eyedropper | `MappingToolsSystem.Pick` (entity with rotation, else tile) (`MappingToolsSystem.cs:284-318`) |
| Batched edits with undo | `MappingEdit` (tile changes, created and deleted groups, decals added and removed), `Commit` and `Push` (`MappingToolsSystem.History.cs:92-170`). Live-spawned entities are recorded as a `Created` group of refs with yaml id -1 (`:332-352`) |
| Tool edits' limits | `WFMappingCanEditEvent` (`Content.Server/_WF/MappingTools/WFMappingPermissionEvents.cs:47-100`), handled by `WFMappingModeSystem.OnCanEdit`/`CheckWorkLimits`/`CheckEditLimits` (`WFMappingModeSystem.cs:206`, `.Limits.cs:229-258`). It has no prototype field; mapper placement's prototype rule is `IsDenied` (`.Limits.cs:201-206`) |
| Spawn menu | `WolfgateSpawnWindow` (`FancyWindow`) holds search, category tree, virtualised grid, favourites and a 40-entry recent list in its own body (`Content.Client/_WF/Spawning/WolfgateSpawnWindow.xaml.cs:20-120`). `WolfgateSpawnUIController` drives placement (`WolfgateSpawnUIController.cs:78-113`) |

## Decisions

### B. The mapping screen

- **`WFMappingScreen : InGameScreen`** in `Content.Client/_WF/MappingMode/Screen/`. Its XAML tree, all present at
  construction:
  - `LayoutContainer Name="ViewportContainer"` holding `controls:MainViewport Name="MainViewport"`.
  - A `SplitContainer` (left dock | centre | right dock) over it.
  - `menuBar:GameTopMenuBar` top-left over the viewport.
  - `chat:ChatBox Name="Chat"` (plain `ChatBox`) at the bottom of the right dock.
  - Hosts, each `Access="Public"`:
    - `PaletteHost` (left dock)
    - `ModeRail` (top centre, 7 toggle buttons)
    - `QuickBarHost` (bottom centre)
    - `RightTabs` (`TabContainer`: Properties, Tools, Layers, Template, Keys), with `TemplateHost` inside the
      Template tab
    - `StatusHost` (bottom-left: cursor readout and limits line)

  It overrides `ChatBox => Chat`. `SetChatSize` is a no-op. It doesn't set autoscale cvars, and does nothing in its
  constructor that needs entity systems; controllers fill the hosts on first `OnScreenLoad`. No control is named
  `CloseButton`, `ContentsContainer`, `TitleLabel` or `WindowHeader`.
- **Choosing the screen.** `WFMappingScreenUIController.WantsMappingScreen` is `WFMappingClientSystem.InSession`.
  `GameplayState.LoadMainScreen` asks it first (marked edit below). `WFMappingClientSystem.Setup` and `Teardown` call
  `ReloadMainScreen()` when `CurrentState is GameplayState`, and Teardown only when the active screen is
  `WFMappingScreen`. Leaving to the lobby needs nothing, because `GameplayState.Shutdown` unloads the screen. A
  `ui.layout` change during a session reloads into the mapping screen.
- **What stays the same.** Admins using the `mapping` command, and everyone outside the mode, keep the default screen
  and the floating MappingTools window.
- **Docking.**
  - `MappingToolsWindow`'s body moves into `MappingToolsPanel`, and the window hosts the panel.
    `MappingToolsSystem.SetDock(Control? host)` puts the panel in the host instead of opening the window.
  - `WolfgateSpawnWindow`'s body moves into `WolfgateSpawnPanel` the same way.
  - Every docked panel has an Undock button that opens a `WFMappingDockWindow` (a `FancyWindow`) holding the panel;
    closing the window docks it back. Moving a panel takes it out of its host first, which never touches the screen's
    widget registry because the panels are not `UIWidget`s.

### B. Build modes

Seven modes, switched with keys 1-7, the rail or the API: `Place`, `Erase`, `Select`, `Fill`, `Shape`, `Pick` and
`Template`.

- **Router.** `WFMappingBuildModeSystem` is a client `EntitySystem`. It holds the current mode, a handler per mode
  (`IWFMappingModeHandler`), the current palette entry and the shape and erase sub-choices.
- **Key functions** (`WFMappingKeyFunctions`, `[KeyFunctions]`, Shared). These are added to `common` on Setup and
  removed on Teardown, as `MappingToolsSystem.SetFunctionsActive` does:

  | Function | Keys | Priority |
  |---|---|---|
  | `WFMappingMode1`-`WFMappingMode7` | `Num1`-`Num7` | 1, above `Hotbar*` |
  | `WFMappingModePrimary` | LMB, Shift+LMB, Ctrl+LMB; `canFocus` | 2 |
  | `WFMappingModeSecondary` | RMB; `canFocus` | 2 |
  | `WFMappingModeMiddle` | MMB; `canFocus` | 2 |
  | `WFMappingModeCancel` | Escape | 4 |
  | `WFMappingTemplateNudgeUp`/`Down`/`Left`/`Right` | arrows, plain and Shift; `canRepeat` | (default) |

  The nudge functions are added and removed by the Template handler's `Enter`/`Exit`. The arrows are free in `aghost`:
  `Arcade*` are `human` only (`ContentContexts.cs:112-115`), and the `TextCursor*` functions act only with focus.
- **Handlers return false when they don't claim a click,** so the click falls through to the reference placement,
  MappingTools selection and placement manager in their usual order.
- **Ctrl+wheel** over the viewport goes to `WFMappingBuildModeSystem.TryWheel(delta)`, from
  `ScalingViewport.ShuttleCamera.cs`, before the shuttle camera check.

Each mode in detail:

- **Place.**
  - LMB and Shift/Ctrl+LMB are not claimed, so the engine's placement and its own line and grid work as today.
  - RMB with an entity selected erases the clicked entity (`MsgPlacement RequestEntRemove`); with a tile selected it
    lays Space under the cursor (`MappingToolsDrawEvent`). With a decal selected RMB is not claimed, so
    `DecalPlacementSystem` erases.
  - MMB eyedrops: `Pick`, extended with decals.
  - Ctrl+wheel rotates (`IPlacementManager.Direction`, or the decal rotation).
  - R rotates the placement when MappingTools' `WFMappingRotate` has nothing to rotate (`BindAfter`).
- **Erase.** Sub-choice entity, tile or decal.
  - Click: entity `RequestEntRemove`, tile Space, decal `RequestDecalRemovalEvent`.
  - Drag: entity `RequestRectRemove` (server `AllowRectErase` and `max_selection_cells`); tile and decal a
    `MappingToolsDrawEvent` over the rectangle.
  - The drag rectangle is drawn by `WFMappingShapeOverlay`.
- **Select.** Calls `MappingToolsSystem.SetSelecting(true)` on Enter and false on Exit, and claims nothing. The
  existing `WFMappingSelect` handles input. The MappingTools Select button switches the mode through a new
  `SelectingChanged` event.
- **Fill.** Tiles only; LMB on a grid.
  - The target type is the tile under the cursor, empty included.
  - It flood-fills 4-connected cells of that type, bounded by the grid's tile AABB and by the `map_size` square.
  - The fill stops at 4096 cells, `WFMappingModeLimits.MaxDrawCells`, which matches `max_selection_cells`, and says
    so.
  - Off-grid clicks are refused with a popup.
  - It sends `MappingToolsDrawEvent(SetTiles)`.
- **Shape.** Sub-choice line, filled rectangle or hollow rectangle.
  - Drag from press cell to release cell, with a live preview.
  - Tiles: `SetTiles`. Entities: `SpawnEntities` at cell centres with the placement direction.
  - Lines are Bresenham, so diagonals work.
- **Pick.** LMB eyedrops (entity with rotation, else decal, else tile), raises `PrototypePicked`, then returns to
  Place with the picked entry.
- **Template.** Handler supplied by P2.
- **Escape** returns to Place, or in Select clears the selection. If neither applies it returns false and the game
  menu opens.

### B. Palette, properties, readout, layers, keys

- **Palette.** Tabs for Entities, Tiles and Decals.
  - Entities is `WolfgateSpawnPanel`, with the existing favourites cvar and recent list.
  - Tiles is `WFMappingTilePalette`: search, All, Favourites and Recent, from `ITileDefinitionManager`
    `ContentTileDefinition`s.
  - Decals is `WFMappingDecalPalette`: search, tag categories, Favourites, Recent, plus colour, rotation, snap,
    z-index and cleanable as in `DecalPlacerWindow`.
  - Tile and decal favourites and recents are stored in `/Mapping/palette.yml` (client data folder).
  - A star and a right-click item "Add to quick select" raise `WFMappingBuildModeSystem.QuickSelectRequested(entry)`.
  - Selecting an entry calls `BeginPlacing(entry)`, which sets Place mode.
  - A "recent" strip of the last 12 entries of any kind sits under the tabs.
- **Properties panel.**
  - Shows the selected entry's icon, name and id, the rotation, the placement-mode dropdown
    (`IPlacementManager.AllModeNames`) and Replace.
  - Shows the mode and its sub-choice.
  - Z-level list with up and down, using the `MappingZLevelsSystem` calls the tools panel uses.
  - Session name and code, and the limits line from `WFMappingSessionInfo.Limits`.
- **Cursor readout.** Tile coordinates, the grid's name and NetEntity, the tile id, and the entity under the cursor
  (`GetClickedEntity`). Updated from `WFMappingScreenUIController.FrameUpdate` at most 10 times a second.
- **Layers.**
  - Toggles: walls (MappingTools hide walls), subfloor, decals (`ToggleOverlay`), markers, lights.
  - The defaults match Setup (markers on, lights off, subfloor shown).
  - Teardown restores every toggle, the decal overlay included.
- **Keys tab.** Mode keys, mouse actions per mode and the MappingTools keys, with their current bindings from
  `GetKeyFunctionButtonString`.

### A. Template

- **Loading.**
  - `WFMappingTemplateSystem` (client): Load opens `GetFileAndName(png filter, FileAccess.Read)`.
  - Refused before decoding when the stream is longer than 16 MiB, or when the IHDR width or height is over 8192 or
    width × height is over 16,777,216 (4096²).
  - Decoded with `Texture.LoadFromPNGStream`, inside try/catch.
  - One template at a time; loading replaces it and disposes the old texture.
- **Settings** (`WFMappingTemplateSettings`, pure):
  - offset X/Y in tiles
  - uniform scale and X/Y multipliers (0.01 to 100)
  - rotation in degrees
  - opacity (0 to 1)
  - pixels per tile (default 32)
  - locked, visible, layer (Under or Over)
  - z depth: the level it belongs to, set at load; it is drawn only on that level
- **Persistence.**
  - `/Mapping/Templates/<key>/template.png` (the bytes as loaded) and `settings.yml`.
  - `<key>` is the session name passed through the library name rules (`WFMappingModeLimits.IsValidName`, otherwise
    hex of its UTF-8 bytes).
  - Written at most once a second after a change, from the system's `FrameUpdate`.
  - Restored on Setup when the folder exists.
  - Nothing is networked.
- **Overlay.** `WFMappingTemplateOverlay` declares `WorldSpaceBelowWorld | WorldSpace` and draws in the chosen one,
  using `DrawTextureRect` with a `Box2Rotated` and the opacity as colour alpha, only on the eye's map at the stored
  depth.
- **Template mode.**
  - LMB drag moves it (ignored while locked).
  - Ctrl+wheel scales by 5 % a notch.
  - The arrows nudge by one image pixel, eight with Shift.
- **Panel** (in `TemplateHost`): Load, Clear, Show, Lock, Under/Over, numeric fields for every setting, and Reset.

### C. Upload

- **Cvars.**
  - `wf.mapping_mode.max_upload_bytes`: default 1.5 MiB, `SERVER | REPLICATED` so the client can refuse early.
    Clamped to `WFMappingUploadAssembler.MaxFileBytes`, a fixed 8 MiB.
  - `wf.mapping_mode.upload_cooldown`: 30 s, `SERVERONLY`.
- **Protocol.**
  1. Client: `WFMappingUploadBeginRequest(name, bytes, chunkCount)`.
  2. Server checks, in order: mode on, the per-player throttle (1 per `upload_cooldown`), no upload in progress for
     that player, the name valid after `WFMappingLibraryNames` trimming, `bytes` ≤ cap, a chunk count that matches,
     and `max_maps` (when the name is new). It then answers `WFMappingUploadReadyEvent(transferId)`, or refuses with
     `WFMappingResultEvent`.
  3. Client: `WFMappingUploadChunkEvent(transferId, index, data)`, two in flight. Server:
     `WFMappingUploadAckEvent(transferId, index)` per accepted chunk.
  4. Server, when the file is complete: validates and stores it, then sends `WFMappingUploadDoneEvent(map, version)`
     and the library.
  5. Either side may cancel (`WFMappingUploadCancelRequest`, `WFMappingUploadCancelledEvent`). The transfer is dropped
     after a 30 s stall and on disconnect.
- **Validation** (server, `WFMappingUploadValidator`, all inside try/catch):
  1. Decode as UTF-8.
  2. Scan with YamlDotNet's `Parser` and refuse any anchor or alias, and more than one document.
  3. Parse with `DataNodeParser`; the root must be a mapping with `meta.format` and an `entities` sequence.
  4. Sum the entities and refuse above `max_entities`.
  5. Refuse unknown or abstract `proto` ids, listing up to 5.
  6. Get the global load slot (`reference_cooldown`, shared with reference loads; "busy, try again" otherwise).
  7. Create a paused, uninitialised scratch map, registered as `Scratch` with no session.
  8. `TryLoadGeneric(MappingDataNode, "upload:<user>", MergeMap)`.
  9. Delete loose entities and strip the deny set.
  10. Check the caps: grids ≤ `max_grids_per_session`, entities ≤ `max_entities`, the per-tile entity and decal caps,
      and every grid's world AABB inside `map_size`.
  11. Save the scratch map as `z0.yml` of a new version of kind `Upload` (folder suffix `_upload`) through a
      `SaveVersion` that takes a level list.
  12. Delete the scratch map in `finally`.

  The raw bytes are never written to disk. The library only holds the engine's re-save of the stripped map.
- **Client.**
  - `WFMappingUploadClientSystem`: one upload at a time, progress, cancel.
  - An Upload button and `WFMappingUploadRow` (progress, then Open) in the lobby panel and the Mapping window's
    Library tab.
  - The file name from `GetFileAndName` is cut after the last `/` or `\`, loses `.yml`, and is filtered to the name
    alphabet as the default map name (pure helper).
  - Open sends the existing `WFMappingOpenRequest(map, version)`.
- **Admin log** for every upload (Low), with name, size and result.

### D. Quick select

- **Store.** `WFMappingQuickSelectStore` (pure, client): sets, each with a name (≤ 32) and pages. A page holds 10
  slots, each empty or `{kind: entity|tile|decal, id, color?}`. At most 10 sets and 10 pages per set; the active set
  and page are remembered.
- **File.** YAML at `/Mapping/quickselect.yml`, written on every change, at most once a second. Read failures fall
  back to defaults and keep the bad file as `quickselect.bad.yml`.
- **Defaults.** Parsed from `/mapping_actions.yml` (`ContentFileReadText`): every `StartPlacementActionEvent` with
  `entityType` or `tileId`, in file order, 10 per page, in a set named "Default". Eraser and non-placement actions are
  skipped.
- **Bar.** `WFMappingQuickBar` in `QuickBarHost`.
  - Set dropdown, with new, rename and delete; page tabs, with a plus button.
  - Slots: click begins placing, drag swaps slots (`DragDropHelper`), right-click menu removes or moves to a page.
  - Additions come from the palette star or menu (`QuickSelectRequested`) and the eyedropper (`PrototypePicked`,
    with an "add picks to quick select" toggle on the bar). An addition goes into the first empty slot of the current
    page.
  - Ids no longer known are drawn disabled and kept.

### E. Extras included

Cursor readout, the rotation and placement-mode indicator (properties panel), layer toggles, the recent strip, the
focus rules (free from the engine, see Input), and the Keys tab. All are in P1.

## Stages

New `_WF` files need no markers. Edits to other Wolfgate modules (MappingTools, Spawning, Shuttles) are `_WF` files
and need none either; each is listed under the stage that owns it. No stage edits `RobustToolbox`.

### Ownership and parallelism

- P1 runs first. Its **first commit** is the contract, so P2 and P3 can start from it.
- P2 and P3 run in parallel with the rest of P1, because the three stages own disjoint files.
- Each stage has its own Fluent file. Every key function and `keybinds.yml` line belongs to P1.

The contract (P1 first commit):

| File | Contents |
|---|---|
| `Content.Shared/_WF/MappingMode/WFMappingKeyFunctions.cs` | every phase 2 key function, nudges included |
| `Resources/keybinds.yml` | the marked block below |
| `Content.Client/_WF/MappingMode/Modes/WFMappingBuildMode.cs` | `enum WFMappingBuildMode { Place = 1, Erase, Select, Fill, Shape, Pick, Template }`, `WFMappingEraseTarget`, `WFMappingShapeKind` |
| `Content.Client/_WF/MappingMode/Modes/IWFMappingModeHandler.cs` | `Mode`, `Enter()`, `Exit()`, `bool Primary(in PointerInputCmdArgs)`, `bool Secondary(...)`, `bool Middle(...)`, `bool Wheel(float delta)`, `bool Cancel()` |
| `Content.Client/_WF/MappingMode/Palette/WFMappingPaletteEntry.cs` | `enum WFMappingPaletteKind { Entity, Tile, Decal }`, `readonly record struct WFMappingPaletteEntry(WFMappingPaletteKind Kind, string Id, Color? Color = null)` |
| `Content.Client/_WF/MappingMode/Modes/WFMappingBuildModeSystem.cs` | stubs with final signatures: `Mode`, `SetMode`, `RegisterHandler`, `BeginPlacing(WFMappingPaletteEntry)`, `event Action<WFMappingBuildMode>? ModeChanged`, `event Action<WFMappingPaletteEntry>? PrototypePicked`, `event Action<WFMappingPaletteEntry>? QuickSelectRequested` |
| `Content.Client/_WF/MappingMode/Screen/WFMappingScreen.xaml(.cs)` | the hosts named above |

### P1: screen, modes, palette, properties, readout, layers (one Opus agent)

**Shared**

| File | Change |
|---|---|
| `Content.Shared/_WF/MappingMode/WFMappingKeyFunctions.cs` | new |
| `Content.Shared/_WF/MappingMode/WFMappingModeEvents.cs` | `WFMappingModeLimits.MaxDrawCells = 4096` |
| `Content.Shared/_WF/MappingTools/MappingToolsShapes.cs` | new, pure: `Line(a, b)`, `Rect(a, b, hollow)`, `Flood(start, Func<Vector2i, int?> typeAt, Box2i bounds, int cap, out bool truncated)` |
| `Content.Shared/_WF/MappingTools/MappingToolsEvents.cs` | `MappingToolsDrawEvent(NetEntity Grid, MappingDrawOp Op, List<Vector2i> Cells, string? TileId, byte TileRotationMirroring, string? Prototype, Angle Rotation)` and `enum MappingDrawOp { SetTiles, SpawnEntities, EraseDecals }` |

**Server**

- `Content.Server/_WF/MappingTools/MappingToolsSystem.Draw.cs` (new), the `MappingToolsDrawEvent` handler:
  - `CanUse(request: true)`; a fixed sanity cap of 16384 cells for admins.
  - Rejects duplicate cells, an unknown tile or prototype, and an abstract prototype.
  - Builds a `WFMappingCanEditEvent`:
    - kind `Paste` for adds, `Edit` for `EraseDecals`
    - `Grid`, `Cells` (bounding box), `Tiles`, `EntityCells`, `Entities`, `SelectionCells`, `Decals`
    - `Prototypes` (new field)
  - Applies the change:
    - tiles through a `MappingEdit` and `Commit`
    - entities spawned anchored at cell centres, recorded as one `Created` group and `Push`ed
    - decals removed per cell through `DecalSystem`, recorded in `DecalsRemoved`
  - Then `RaiseEdited` and an admin log line.
- `Content.Server/_WF/MappingTools/MappingToolsSystem.cs`: subscribe the draw event in `Initialize`.
- `Content.Server/_WF/MappingTools/WFMappingPermissionEvents.cs`: `public List<string>? Prototypes;` on
  `WFMappingCanEditEvent`.
- `Content.Server/_WF/MappingMode/WFMappingModeSystem.Limits.cs`: `CheckEditLimits` refuses when any
  `ev.Prototypes` entry is unknown or `IsDenied` (`wf-mapping-mode-refused-prototype`, existing key or new in P1's
  ftl).

**Client** (`Content.Client/_WF/MappingMode/`)

- Screen:
  - `Screen/WFMappingScreen.xaml`, `Screen/WFMappingScreen.xaml.cs`
  - `Screen/WFMappingScreenUIController.cs`: `WantsMappingScreen`, filling the hosts on `OnScreenLoad`, dock and
    undock, readout and status `FrameUpdate`
  - `Screen/WFMappingDockWindow.cs`
- Modes:
  - `Modes/WFMappingBuildMode.cs`, `Modes/IWFMappingModeHandler.cs`, `Modes/WFMappingBuildModeSystem.cs`
  - `Modes/WFMappingPlaceMode.cs`, `Modes/WFMappingEraseMode.cs`, `Modes/WFMappingSelectMode.cs`,
    `Modes/WFMappingFillMode.cs`, `Modes/WFMappingShapeMode.cs`, `Modes/WFMappingPickMode.cs`
  - `Modes/WFMappingShapeOverlay.cs`
- Palette:
  - `Palette/WFMappingPaletteEntry.cs`, `Palette/WFMappingPalette.xaml(.cs)`, `Palette/WFMappingTilePalette.cs`,
    `Palette/WFMappingDecalPalette.xaml(.cs)`
  - `Palette/WFMappingPaletteStore.cs`: pure YAML read and write of favourites and recents
- Panels:
  - `Panels/WFMappingPropertiesPanel.xaml(.cs)`, `Panels/WFMappingLayersPanel.xaml(.cs)`, `Panels/WFMappingKeysPanel.cs`,
    `Panels/WFMappingCursorReadout.cs`
- `WFMappingClientSystem.cs`:
  - Setup: add the mode functions, call `ReloadMainScreen`, skip `LoadActionAssignments` (the mapping screen is always
    used in a session), dock the tools.
  - Teardown: remove the functions, restore the layer toggles, reload the screen.

**Client, other Wolfgate modules**

| File | Change |
|---|---|
| `Content.Client/_WF/MappingTools/MappingToolsPanel.xaml(.cs)` | new; the body of `MappingToolsWindow` |
| `Content.Client/_WF/MappingTools/MappingToolsWindow.xaml(.cs)` | hosts the panel |
| `Content.Client/_WF/MappingTools/MappingToolsSystem.cs` | `SetDock(Control?)`, `SelectingChanged`/`PickingChanged` events, decal pick, `Picked` event |
| `Content.Client/_WF/Spawning/WolfgateSpawnPanel.xaml(.cs)` | new; the body of the window |
| `Content.Client/_WF/Spawning/WolfgateSpawnWindow.xaml(.cs)` | hosts the panel |
| `Content.Client/_WF/Spawning/WolfgateSpawnUIController.cs` | unchanged API; it wires the panel through the window |
| `Content.Client/_WF/Shuttles/ScalingViewport.ShuttleCamera.cs` | before the shuttle check: `if (Parent is MainViewport && _entityManager.TrySystem<WFMappingBuildModeSystem>(out var modes) && modes.TryWheel(args.Delta.Y)) { args.Handle(); return; }` |

**Locale**

- `Resources/Locale/en-US/_WF/MappingMode/mapping-screen.ftl` (new)
- `Resources/Locale/en-US/_WF/MappingTools/mapping-tools.ftl`: draw edit names and refusals

**Upstream edits**

1. `Content.Client/Gameplay/GameplayState.cs`
   - using block: `using Content.Client._WF.MappingMode.Screen; // WOLFGATE(MappingMode)`
   - at the top of `LoadMainScreen` (before line 116):
     ```
     // WOLFGATE(MappingMode) START: mapping observers get the mapping screen
     if (_uiManager.GetUIController<WFMappingScreenUIController>().WantsMappingScreen)
     {
         _uiManager.LoadScreen<WFMappingScreen>();
         _loadController.LoadScreen();
         return;
     }
     // WOLFGATE END
     ```
2. `Resources/keybinds.yml`, after the existing MappingMode block (line 697):
   ```
   # WOLFGATE(MappingMode) START: build mode keys for the mapping screen
   - function: WFMappingMode1   # ... WFMappingMode7, Num1..Num7, priority: 1
   - function: WFMappingModePrimary   # MouseLeft canFocus priority 2; again with mod1 Shift, and with mod1 Control
   - function: WFMappingModeSecondary # MouseRight canFocus priority 2
   - function: WFMappingModeMiddle    # MouseMiddle canFocus priority 2
   - function: WFMappingModeCancel    # Escape priority 4
   - function: WFMappingTemplateNudgeUp # Up canRepeat; again with mod1 Shift (and Down, Left, Right)
   # WOLFGATE END
   ```
   The comments above are plan notes; the real entries are plain YAML.

### P2: template, quick select, persistence (one agent, parallel after P1's first commit)

All files are new. P2 touches none of P1's files.

- Template (`Content.Client/_WF/MappingMode/Template/`):
  - `WFMappingTemplateSystem.cs`: state, load, persistence, restore on `WFMappingClientSystem.SessionUpdated` or on
    the first frame in a session, clear on `SessionEnded`; registers the handler
  - `WFMappingTemplateMode.cs`: the `IWFMappingModeHandler`; adds and removes the nudge functions in Enter and Exit,
    with its own `CommandBinds` `Register<WFMappingTemplateSystem>`
  - `WFMappingTemplateOverlay.cs`
  - `WFMappingTemplatePanel.xaml(.cs)`: put into `WFMappingScreen.TemplateHost` on `OnScreenLoad`
  - `WFMappingTemplateSettings.cs`: pure; clamping, YAML read and write
  - `WFMappingPngHeader.cs`: pure; the IHDR reader
- Quick select (`Content.Client/_WF/MappingMode/QuickSelect/`):
  - `WFMappingQuickSelectStore.cs`: pure; model, YAML, `mapping_actions.yml` defaults
  - `WFMappingQuickSelectSystem.cs`: load and save, listens to `QuickSelectRequested` and `PrototypePicked`
  - `WFMappingQuickBar.cs`, `WFMappingQuickSlot.cs`: put into `QuickBarHost`
- Locale: `Resources/Locale/en-US/_WF/MappingMode/mapping-template.ftl`,
  `Resources/Locale/en-US/_WF/MappingMode/mapping-quickselect.ftl`

### P3: upload (one agent, parallel with P1 and P2)

**Shared** (`Content.Shared/_WF/MappingMode/`)

| File | Change |
|---|---|
| `WFMappingUploadEvents.cs` | new; the six events above |
| `WFMappingUploadAssembler.cs` | new, pure; transfer id, chunk count, per-transfer byte cap, the `Add` and `TryComplete` contract of the download assembler |
| `WFMappingModeCVars.cs` | `MaxUploadBytes`, `UploadCooldown` |
| `WFMappingLibraryNames.cs` | `WFMappingVersionKind.Upload`, `_upload` suffix in format and parse; `UploadNameFromFile(string)` |

**Server** (`Content.Server/_WF/MappingMode/`)

| File | Change |
|---|---|
| `WFMappingUploadSystem.cs` | new; transfers, throttle, stall, disconnect, admin log |
| `WFMappingUploadValidator.cs` | new; steps 1-5 of validation; pure except for `IPrototypeManager` |
| `WFMappingModeSystem.Reference.cs` | extract `TryReserveLoad()` (the global cooldown) and `TryLoadOntoScratch(MappingDataNode or ResPath, ...)`, used by reference pick and upload |
| `WFMappingLibrarySystem.cs` | `SaveVersion` takes `(List<EntityUid> levels, int ground, NetUserId user, string mapName, kind, string logName)`; the session overload wraps it; new `SaveUpload` |

**Client** (`Content.Client/_WF/MappingMode/`)

| File | Change |
|---|---|
| `WFMappingUploadClientSystem.cs` | new; a separate system, not a partial of `WFMappingClientSystem` (owned by P1) |
| `WFMappingUploadRow.cs` | new |
| `WFMappingLobbyPanel.xaml(.cs)`, `WFMappingLobbyUIController.cs` | Upload button and row |
| `WFMappingMenuWindow.xaml(.cs)`, `WFMappingMenuUIController.cs` | Library tab Upload button and row |
| `WFMappingLibraryList.cs` | `Upload` kind label |

**Locale:** `Resources/Locale/en-US/_WF/MappingMode/mapping-upload.ftl` (new; includes `wf-mapping-mode-kind-upload`).

No upstream edits.

### P4: tests (one agent, after P1-P3 land)

Unit (`Content.Tests/_WF/`):

| File | Covers |
|---|---|
| `MappingTools/MappingToolsShapesTest.cs` | Line (axis, diagonal, single cell, reversed ends); Rect filled and hollow cell sets and counts; Flood (enclosed room, leak stops at bounds, empty-type fill, cap and `truncated`, start outside bounds) |
| `MappingMode/WFMappingQuickSelectStoreTest.cs` | write/read round trip; defaults from a `mapping_actions.yml` sample (order, kinds, skipped eraser); malformed YAML gives defaults; caps on sets, pages and name length; unknown kinds dropped |
| `MappingMode/WFMappingTemplateSettingsTest.cs` | round trip; clamping; session-name key; `WFMappingPngHeader` (valid 1×1, oversize side, oversize area, non-PNG, truncated header) |
| `MappingMode/WFMappingUploadAssemblerTest.cs` | in-order and out-of-order chunks, duplicate, out of range, chunk over size, total over cap, mismatched count |
| `MappingMode/WFMappingLibraryNamesTest.cs` | add `upload` kind format/parse and `UploadNameFromFile` cases |
| `MappingMode/WFMappingPruneTest.cs` | add: an upload is pruned like a manual save |

Integration (`Content.IntegrationTests/Tests/_WF/`). Use `WFMappingModeTestUtil.Settings`; never `Destructive`:

- `MappingMode/WFMappingUploadTest.cs`:
  - The YAML is a session map saved to a string with `TrySaveMap(TextWriter)`, uploaded through the events as a
    dummy player.
  - Stored as `v0001_*_upload/z0.yml` and listed; Open makes a session.
  - Refusals: over size, anchors, missing prototype (no error log expected), over the grid cap, while busy, inside the
    cooldown.
  - A denied component is stripped.
  - No scratch map is left behind.
- `MappingTools/MappingToolsDrawTest.cs`:
  - SetTiles over a rectangle, then undo.
  - SpawnEntities line counts.
  - A mapper over `max_selection_cells` or the per-tile cap is refused.
  - A denied or `HideSpawnMenu` prototype is refused.
  - EraseDecals removes, and undo restores.
- `MappingMode/WFMappingScreenTest.cs` (connected client):
  - Create a session from the client: the active screen is `WFMappingScreen`, the mode functions are in the active
    context, and `GetWidget<ChatBox>`, `GetWidget<MainViewport>` and `GetWidget<GameTopMenuBar>` are non-null.
  - `ui.layout` toggle stays on the mapping screen.
  - Leave: back on the screen `ui.layout` names, and the functions are gone.
  - Mode claims: in Erase mode the first handler of `WFMappingModePrimary` claims, and in Place mode it does not
    (handled flag through `InputSystem.HandleInputCommand`, or `IInputManager.KeyDown`/`KeyUp` as in
    `WFCockpitTest`).

### P5: docs, locale check, generated docs, gate (one agent)

- `Content.Server/_WF/MappingMode/README.md`: hand-written overview. Add the mapping screen, modes, template, quick
  select, upload, the new cvars, and move fixed items out of the known gaps.
- `Content.Server/_WF/MappingTools/README.md`: the draw request.
- `Content.Client/_WF/Spawning/README.md`: the panel split.
- `Docs/_WF/MappingMode/DESIGN.md`: a short phase 2 section pointing here.
- `python Tools/_WF/Ci/modules.py --write`, then `--check` and `--pr-check origin/main`. The two upstream edits must
  show under MappingMode.
- Build, warnings as errors for nullable: Content.Shared, Content.Server, Content.Client, Content.Tests and
  Content.IntegrationTests in DebugOpt.
- `dotnet run --project Content.YAMLLinter -c Release`.
- Start a headless server with `wf.mapping_mode.enabled true` and grep the log for `[ERRO]`/`[FATL]`.
- `bin/Content.Client/Content.Client.exe --headless` and grep for `Sandbox violation`. Collection expressions into
  `List<T>` and `string += char` are banned in Client and Shared.
- Run the P4 tests. Report failures with output.
