# MappingMode plan

Answers to the design's open points and the review items, then the build stages. Paths are repo-relative; `RT` is
`RobustToolbox`. Line numbers are at `efb86c2d22`.

## Design changes

1. **Placement gate moves from `AdminManager.CanAdminPlace` to `SandboxSystem`'s `AllowPlacementFunc`.** The engine
   checks only that delegate (`RT/Robust.Server/Placement/PlacementManager.cs:62`); `CanAdminPlace` has one caller,
   the delegate (`Content.Server/Sandbox/SandboxSystem.cs:59-74` → `RT/Robust.Server/Console/ConGroupController.cs:16`
   → `Content.Server/Administration/Managers/AdminManager.cs:644`), and gets no `MsgPlacement`, so it can't check the
   target map. The delegate can.
2. **The revert guard can't undo erasures.** `HandleEntRemoveReq` and `HandleRectRemoveReq` raise
   `PlacementEntityEvent(Erase)` and delete the entity on the next line (`PlacementManager.cs:233-239`, `286-292`), and
   neither event can be cancelled. Entity removal (`:225-240`, marked `TODO: Some form of admin check`) deletes any
   entity by NetEntity, including nullspace minds and other players' observers. The placement pre-check is the real
   guard. The revert stays as a backstop for created entities and tiles only.
3. **The decal gate is `AdminFlags.Spawn`, not `Mapping`** (`Content.Server/Decals/DecalSystem.cs:233`, `:262`). It
   gets a `_WF` hook rather than an edit: MappingMode places and removes decals for mappers itself, ordered before
   `DecalSystem`, which then refuses the same request because the mapper lacks `Spawn` (item 5).
4. **`MappingMapsSystem.CanUse` and MappingZLevels' Save z-stack / Open z-stack stay admin-only.** They read and write
   the shared `/Mapping` folder, `/Maps` and `/SharedMaps`, and Open runs the admin `mapping` command, which aghosts
   the caller (`Content.Server/Mapping/MappingCommand.cs:155-159`). Only Move, Add and Remove in MappingZLevels open
   to mappers.
5. **`WFMappingObserver` does not inherit `AdminObserver` or `MobObserverBase`.**
   - `AdminObserver` brings a global CentCom comms console, cargo, a bank, AllAccess, hands and stripping
     (`admin_ghost.yml:23-115`).
   - `MobObserverBase` brings `ActiveRadio globalReceive`, `CollectiveMind hearAll`, `GhostHearing` and
     `Examiner skipChecks` (`observer.yml:84-136`). `skipChecks` returns before the same-map check
     (`Content.Shared/Examine/ExamineSystemShared.cs:103-119`), so a mapper could examine any entity on the server.
   - It takes `MobObserverBase`'s parents `[Incorporeal, BaseMob]` instead (item 3).
6. **Return to lobby goes through a `GameTicker` partial, not `Respawn`.**
   - `PlayerJoinLobby` is private (`GameTicker.Player.cs:206`).
   - `Respawn` is public (`GameTicker.Spawning.cs:340-351`) but writes a Medium "was respawned" admin log and resets
     the Corvax respawn data on every leave.
   - `Content.Server/_WF/MappingMode/GameTicker.MappingMode.cs` adds
     `public void WFMappingReturnToLobby(ICommonSession s) { _mind.WipeMind(s); PlayerJoinLobby(s); }`. This is a new
     file, not an upstream edit.
7. **`System.IO.Path` is not allowed in Client or Shared.** `RT/Robust.Shared/ContentPack/Sandbox.yml:582-618`
   whitelists no `Path`, `File` or `Directory`. Use `ResPath` only.
8. **`EscapeMenu.xaml.cs` needs no edit.** The new button is wired from `EscapeUIController`, as the other WOLFGATE
   buttons are.
9. **Membership survives a disconnect.** The design says both "disconnecting counts as leaving" and "rejoin after a
   reconnect". Resolution:
   - A disconnect or detach despawns the observer and counts the member as absent for the empty-session grace.
   - The member stays in `Members`, so the lobby offers a rejoin.
   - Only Leave or Kick removes membership and passes ownership on.
10. **Moving between sessions clears MappingTools undo history.** Undo history lives per session
    (`MappingToolsSystem.History.cs:32`), so after a switch Ctrl+Z would edit the old session's maps. MappingTools
    gains `ForgetHistory(session)`, called on leave. The clipboard is kept, so pasting into another session works.
11. **Mapper placement refuses some prototypes and erase targets.**
    - Prototypes: unknown, abstract, or in `HideSpawnMenu`. The engine spawns any prototype ID a client sends
      (`PlacementManager.cs:141`, `:174`).
    - Erase targets: maps, actors, and anything not on an editable map.
12. **Admins' Maps window would list every library file.** `MappingMapsSystem.FindSaved` walks the whole data folder
    to depth 6 (`MappingMapsSystem.cs:96-111`) and would list `/Mapping/Library/**`. It skips that folder (one line in
    MappingTools).
13. **The reference footprint is computed on the server, not by loading the file on the client.** A client-side
    ShipPreview load can't serve every source:
    - Packaged clients have no `/Maps`: `Maps` is in `ClientIgnoredResources`
      (`RT/Robust.Packaging/RobustClientPackaging.cs:7-9`).
    - Library levels exist only on the server.
    - ShipPreview loads exactly one grid (`Content.Client/_WF/ShipPreview/ShipPreviewSystem.Load.cs`: category `Grid`
      and `Grids.Count == 1`), so a map file's several grids don't fit.
    - Sending station YAML would break the 2 MiB preview precedent (`MappingMapsSystem.cs:40`).

    Instead:
    1. The server loads the file onto a paused scratch map. It needs that load anyway to check the grid, entity and area
       limits before anything touches the session.
    2. It sends a compact footprint: per grid, its transform relative to the anchor plus its tiles as merged
       rectangles.
    3. The client draws the ghost outline from the footprint.
    4. On confirm, the server re-parents the scratch grids onto the session map at the chosen offset and rotation, then
       deletes the scratch map.

    `TryMergeMap` and `TryLoadGrid` with offset and rotation exist (item 13) but would load the file a second time.
14. **The lobby `ObserveButton` can't be reused.** `LobbyState.UpdateLobbyUi` sets `ObserveButton.Disabled = true`
    whenever the round hasn't started (`Content.Client/Lobby/LobbyState.cs:212`), on `Startup` (`:79`) and on every
    status update (`:185`). The `observe` command refuses in the lobby run level (`ObserveCommand.cs:28-31`).

    The partial hides `ObserveButton` and adds its own `Observe` button in the same menu, visible only with
    `HasFlag(AdminFlags.Admin)`. It sends `WFMappingAdminObserveRequest`, which spawns an `AdminObserver` on the private
    admin map. No upstream edit.
15. **"No verbs" needs one Shared marked edit.** The server builds verbs for any NetEntity a client names. The source
    says access is left to the individual verbs (`Content.Server/Verbs/VerbSystem.cs:30-43`), and execution rebuilds
    them the same way (`Content.Shared/Verbs/SharedVerbSystem.cs:23-39`). Both go through `GetLocalVerbs`
    (`SharedVerbSystem.cs:69-73`), where a marked check returns an empty set for a `WFMappingObserverComponent` user
    unless `force` is set (admin verbs).
16. **Some placeable prototypes add global PVS overrides on `ComponentStartup`, even on a paused map.** This would send
    the entity to every client:
    - `SingularityDistortionComponent` (`Content.Server/Singularity/EntitySystems/SingularitySystem.cs:168-170`)
    - `PointManagerComponent` (`Content.Server/Points/PointSystem.cs:23-28`)

    Mapper placement, paste, reference load and library open refuse or strip entities with a component in a fixed
    deny set. The set is these two, `GameRule` and `StarSystemMap`.

    MapInit-driven overrides can't fire, because session maps are never initialised. Example:
    `GlobalTimeManagerComponent` (`Content.Server/Clock/ClockSystem.cs:30-33`).

## Answers

### 1. GameTicker

**Join with a custom entity.** There is no single call. Copy `JoinAsObserver` (`GameTicker.Spawning.cs:393-402`):

1. `_ticker.PlayerJoinGame(session, silent: true)` (`GameTicker.Player.cs:186-204`). It is public, sets
   `PlayerGameStatus.JoinedGame` (`:191`) and sends `TickerJoinGameEvent`. The client then switches to
   `GameplayState` (`Content.Client/GameTicking/Managers/ClientGameTicker.cs:57`, `:134-136`).
2. `_mind.WipeMind(session)` (`Content.Shared/Mind/SharedMindSystem.cs:301-318`), then
   `var mind = _mind.CreateMind(session.UserId, session.Name)` (`:215-224`).
3. Spawn the observer in nullspace: `EntityManager.Spawn(WFMappingObserver)`, which map-initialises it
   (`RT/Robust.Shared/GameObjects/EntityManager.Spawn.cs:112-116`). Then
   `_transform.SetMapCoordinates(observer, new MapCoordinates(Vector2.Zero, rootMapId))`.
   - Spawning straight onto the paused, uninitialised map skips MapInit (`:154`). The CE z-level viewer's action
     needs MapInit (`Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.View.cs:112-116`).
4. `_mind.TransferTo(mind, observer)`, which attaches the session (`Content.Server/Mind/MindSystem.cs` `TransferTo`,
   `SetAttachedEntity` at about `:256`).

`MakeJoinGame` (`Spawning.cs:360`) and `SpawnObserver` (`:407`) are unusable. They spawn on a station, or a ghost
through `GhostSystem.SpawnGhost`.

**Status tracking.** `_playerGameStatuses` (`GameTicker.Lobby.cs:14`, read through `PlayerGameStatuses` `:37`) is
reset on restart (`GameTicker.RoundFlow.cs:1044-1050`).

On reconnect (`GameTicker.Player.cs:85-123`), a mind with a live entity is re-attached; a missing mind sends the player
to the lobby. Because leave wipes the mind, a reconnect lands in the lobby.

**Back to the lobby.** `PlayerJoinLobby` is private (`GameTicker.Player.cs:206-216`). `Respawn` is public
(`Spawning.cs:340-351`) and wipes the mind, but logs a respawn. The plan uses the partial in design change 6. Delete
the observer after the wipe; in `PreRoundLobby` a deleted mind container spawns no ghost
(`Content.Server/Mind/MindSystem.cs:73-74`).

**Staying in `PreRoundLobby`.**

- `PauseStart(bool)` (`GameTicker.Lobby.cs:119-144`) returns false if nothing changed, and announces every change in
  chat.
- `UpdateRoundFlow` returns early while `Paused` (`GameTicker.RoundFlow.cs:1072-1095`, check at `:1081`), so maps are
  never preloaded and the round never starts.
- `RestartRound` (`:951-994`) and `ResettingCleanup` (`:1021-1051`) never touch `Paused`. A re-assert in
  `GameRunLevelChangedEvent` is only a guard against an admin `togglepause`.
- At startup, `GameTicker.PostInitialize` → `RestartRound` (`GameTicker.cs:97-107`) runs from
  `Content.Server/Entry/EntryPoint.cs:165`, after every system's `Initialize`. Force `game.lobbyenabled` in
  `Initialize` (through `Subs.CVar`); otherwise `RestartRound` would `StartRound` at once (`RoundFlow.cs:978-981`).
  Call `PauseStart` from the `GameRunLevelChangedEvent` handler, which fires inside that `RestartRound` (`:974`).

**Late join.**

- `RefreshLateJoinAllowedEvent` (`RoundFlow.cs:484-489`) is raised only from `StartGamePresetRules`
  (`GameTicker.GamePreset.cs:83`) and `SpawnPlayers` (`Spawning.cs:134`). No subscriber exists. Subscribe and
  `Disallow()`; it only matters after a forced start.
- `joingame` and `observe` already refuse in `PreRoundLobby`
  (`Content.Server/GameTicking/Commands/JoinGameCommand.cs:54-57`, `ObserveCommand.cs:28-31`).

**Ready while paused.** `ToggleReady` (`GameTicker.Lobby.cs:164-199`) still records readiness, which only matters if an
admin forces a start. The ready controls are hidden; no server block is needed.

**Restart order** (`GameTicker.RoundFlow.cs`):

| Line | Step |
|---|---|
| `:974` | `RunLevel = PreRoundLobby` raises `GameRunLevelChangedEvent` (setter `:74-87`) |
| `:1026` | `PlayerJoinLobby` for every player (entities still exist) |
| `:1030-1034` | `RoundRestartCleanupEvent` (local, then network) |
| `:1036` | `EntityManager.FlushEntities()` |

Autosave in the `RoundRestartCleanupEvent` handler: maps still exist, and MappingTools already resets there
(`MappingToolsSystem.History.cs:53`, `:255-262`). `RoundEndedEvent` (`:652`) fires only through `EndRound`, so a
lobby `restartroundnow` skips it.

**Risk.** An admin `startround` puts the server `InRound`, where lobby players may `observe`. A ghost's warp list
includes players, so it can reach mappers' observers, but `CanEdit` still refuses its edits. This is accepted per the
design. The option of cancelling `RoundStartAttemptEvent` while the mode is on is noted, not planned.

### 2. Admin gates

**Server.**

| Gate | Checks | Change |
|---|---|---|
| `RT/Robust.Server/Placement/PlacementManager.cs:62` | `AllowPlacementFunc` for every `MsgPlacement` (place, tile, erase, rect erase) | none (engine) |
| `Content.Server/Sandbox/SandboxSystem.cs:59-74` | sandbox on, or `IConGroupController.CanAdminPlace` → `AdminData.CanAdminPlace` = `Spawn` (`Content.Shared/Administration/AdminData.cs:49-52`) | marked edit: before `return false;` call `_wfMapping.AllowPlacement(player, placement)` (S1) |
| `Content.Server/Administration/Managers/AdminManager.cs:644-647` | `Spawn` flag | none (design change 1) |
| `Content.Server/Decals/DecalSystem.cs:233`, `:262` | `Spawn` flag | none; `_WF` hook places or removes for mappers (design change 3) |
| `Content.Server/_WF/MappingTools/MappingToolsSystem.cs:76-79` `CanUse` | `Mapping` flag | admin, or `WFMappingCanUseEvent.Allowed`; plus `WFMappingCanEditEvent` per target map |
| `MappingToolsSystem.History.cs:115` (`RecordPlacement`), `:179` | `CanUse` | follows `CanUse` |
| `MappingToolsSystem.History.cs:229` | `CanUse && Spawn` (mirrors `DecalSystem`) | `CanUse && (Spawn \|\| CanEdit(coords map))` |
| `Content.Server/_WF/MappingTools/MappingMapsSystem.cs:61-64` | `Mapping` flag | unchanged (design change 4) |
| `Content.Server/_WF/MappingTools/MappingZLevelsSystem.cs:55-58` | `Mapping` flag | Move, Add, Remove: `CanUse` + `CanEdit`; Save and Open (`:227`, `:289`) stay admin |

`AllowPlacement(session, msg)` is true when all of these hold:

- the session is a present member;
- for `RequestPlacement`, `RequestRectRemove`: the map of `msg.NetCoordinates` is editable;
- for `RequestEntRemove`: the entity's `MapUid` is editable, and the entity has no `MapComponent` or `ActorComponent`;
- for entity placement: the prototype is known, not abstract and not `HideSpawnMenu`.

`StartPlacement` and `CancelPlacement` pass.

**Client.**

| Gate | Checks | Change |
|---|---|---|
| `Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs:64`, `:73`, `:80` | F5/F6/F8 need `IClientAdminManager.CanAdminPlace()` (`Content.Client/Administration/Managers/ClientAdminManager.cs:60-63`, `Spawn`) | marked edit: `&& !_wfMapping.InSession` |
| `Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs:28` | `SandboxAllowed` = server sandbox or admin active (`Content.Client/Sandbox/SandboxSystem.cs:33-46`) | marked edit: `\|\| _wfMapping.InSession` |
| `Content.Client/Sandbox/SandboxSystem.cs:85-88` `Copy` (P key) | `SandboxAllowed` | none; the MappingTools eyedropper covers it |
| `Content.Client/Commands/MappingClientSideSetupCommand.cs:24` | `ILightManager.LockConsoleAccess` (false by default, `RT/Robust.Client/Graphics/Lighting/LightManager.cs:11`) | not called; MappingMode repeats its steps in code |
| `showsubfloorforever` | `DEBUG` client perm (`Resources/clientCommandPerms.yml:43`, `:69`) | set `SubFloorHideSystem.ShowAll` directly (`Content.Client/Commands/DebugCommands.cs:43-58`) |
| `mappingtools` | `MAPPING` client perm (`clientCommandPerms.yml:79-85`) | call `MappingToolsSystem.SetEnabled` directly |
| `Content.Client/_WF/MappingTools/MappingToolsWindow.xaml:68` Maps button | none | hide unless `HasFlag(AdminFlags.Mapping)` (MappingTools client, `MappingToolsSystem.cs:177`) |

**Ungated already.**

- Spawn windows: `Content.Client/_WF/Spawning/WolfgateSpawnUIController.cs:37`,
  `RT/Robust.Client/UserInterface/Controllers/Implementations/TileSpawningUIController.cs:86`.
- Action bar placement: `Content.Client/Mapping/MappingSystem.cs:127-144`.
- Mapping tools toggle action: `Content.Client/_WF/MappingTools/MappingToolsSystem.cs:254-261`.
- Engine client placement: `RT/Robust.Client/Placement/PlacementManager.cs:796-851`, no admin check.
- Decal sends: `Content.Client/Decals/DecalPlacementSystem.cs:50-93`, `:131`.
- `ActionsSystem.LoadActionAssignments` (`Content.Client/Actions/ActionsSystem.cs:303-334`), which needs a local
  entity.
- The spawn keys sit in the `common` input context, which `aghost` inherits (`Content.Client/Input/ContentContexts.cs:140`,
  `:165-168`).

**Confirmed.** A client with `AdminData == null` can open every spawn window, use the mapping action bar and send
`MsgPlacement` and decal requests. Only F5, F6, F8 and the decal window's open check stop it on the client. The server
refuses everything through `AllowPlacementFunc` and `DecalSystem`'s `Spawn` check. The mapper flag is networked state:
the local entity has `WFMappingObserverComponent`.

### 3. Observer

**What matters, from the parents.**

- `Incorporeal` (`Resources/Prototypes/Entities/Mobs/Player/observer.yml:1-34`): no-clip movement
  (`MovementIgnoreGravity`, `CanMoveInAir`, kinematic `InAir` body, `GhostImpassable` fixture), `CEZPhysics`
  without fall (`autoStep: false`, `velocityGravity: false`), and `CEZLevelGhostMover`.
- `BaseMob` (`Resources/Prototypes/Entities/Mobs/base.yml:3-53`): `InputMover`, `Eye`, `ContentEye`,
  `MindContainer`, `Actions` (needed for the action bar), `Examiner`.
- Both are `save: false`, which is inherited: `MapSavable` has no `[NeverPushInheritance]`
  (`RT/Robust.Shared/Prototypes/EntityPrototype.cs:137`). The serializer skips them
  (`RT/Robust.Shared/EntitySerialization/EntitySerializer.cs:195`), so observers are never saved into library files.

**What goes without `Ghost`.**

- The Ghost visibility layer and the eye mask (`Content.Server/Ghost/GhostSystem.cs:119-127`, `:201-212`): the
  observer stays on the Normal layer. Members on the same map see each other; mappers don't see aghosts.
- The warp menu, return-to-body and ghostnado: every handler requires `GhostComponent`
  (`Content.Server/Ghost/GhostSystem.cs:284-375`).
- Ghost interaction blocks (`Content.Shared/Ghost/SharedGhostSystem.cs:21-28`): replaced by
  `WFMappingObserverSystem` (Shared, so the client doesn't predict them). It cancels these on
  `WFMappingObserverComponent`: `InteractionAttemptEvent`, `UseAttemptEvent`, `PickupAttemptEvent`,
  `DropAttemptEvent`, `EmoteAttemptEvent`, `SpeakAttemptEvent`, `ThrowAttemptEvent`, `AttackAttemptEvent`.
- Ghost speech becomes dead chat (`Content.Server/Chat/Systems/ChatSystem.cs:191-196`). A non-ghost would speak IC,
  local to the map (`:1143-1175`); `SpeakAttemptEvent` blocks it, so mappers use OOC and LOOC.

**Must-haves.**

- `Physics ignorePaused: true` (as `admin_ghost.yml:29-31`): session maps are paused, and paused movers lose
  `ActiveInputMover` (`Content.Server/Physics/Controllers/MoverController.cs:59-65`, `:98-103`).
- `Eye drawFov: false`, and `ContentEye maxZoom 8.916104` (as `admin_ghost.yml:7-8`).

**`WFMappingObserver`** (`Resources/Prototypes/_WF/MappingMode/observer.yml`):

```yaml
- type: entity
  parent: [ Incorporeal, BaseMob ]
  id: WFMappingObserver
  name: mapper
  save: false
  categories: [ HideSpawnMenu ]
  components:
  - type: Sprite                 # ghost sprite, so members see each other
    sprite: Mobs/Ghosts/ghost_human.rsi
    color: "#fff8"
    layers:
    - state: animated
      shader: unshaded
  - type: ContentEye
    maxZoom: 8.916104, 8.916104
  - type: Eye
    drawFov: false
  - type: Input
    context: "aghost"
  - type: Physics
    ignorePaused: true
    bodyType: Kinematic
  - type: Examiner               # defaults: same-map and range checks stay on
  - type: WFMappingObserver
```

**Server MappingTools** (`MappingToolsSystem.cs:170-177`) only refuses to select ghosts and actors; it never requires a
ghost. Its ghost-dependent paths are the admin-only Maps Open and Load and z-stack Open, which run `mapping`.

**Other exits closed.**

- `ghost` and `suicide` reach `GhostSystem.OnGhostAttempt` (`Content.Server/Ghost/GhostCommand.cs:28-56`,
  `Content.Server/Chat/SuicideSystem.cs:109`). MappingMode handles the broadcast `GhostAttemptHandleEvent`
  (`GhostSystem.cs:654-659`) with `Result = false` when the mind's entity is a mapping observer.
- `joingame` and `observe` refuse in the lobby run level (item 1).

### 4. CE z-levels

**Members of a map's stack.**

- `CESharedZLevelsSystem.TryGetMapNetwork(map, out network)`
  (`Content.Shared/_CE/ZLevels/Core/EntitySystems/CESharedZLevelsSystem.Maps.cs:23-44`), then
  `network.Comp.ZLevels` (depth → map).
- MappingTools already wraps this: `MappingZLevelsSystem.GetLevels(map, out ground)`
  (`Content.Server/_WF/MappingTools/MappingZLevelsSystem.cs:64-83`), bottom-up; a lone map is a stack of one.

**Events.**

- `CEMapAddedIntoZNetworkEvent` (by-ref, directed on the map,
  `Content.Server/_CE/ZLevels/Core/CEZLevelsSystem.Maps.cs:79-80`) is already subscribed for `CEZMapComponent`
  (`Content.Server/_CE/ZLevels/Mapping/CEZLevelMappingSystem.cs:21`).
- `CEZLevelMapNetworkUpdatedEvent` (directed on the network, `:83`) is subscribed by
  `Content.Server/_CE/ZLevels/Roof/CEZLevelsRoofSystem.Maps.cs:18`.
- There is no removal event. Maps leave a network only in `WfRestack`, which deletes the old network
  (`Content.Server/_WF/MappingTools/CEZLevelsSystem.MappingTools.cs:18-33`), and `DeleteMapNetwork` (`Maps.cs:109-124`).
- Every restack makes a new network entity, so a cached network uid goes stale.

**Plan.**

- Each session map gets `WFMappingSessionMapComponent { SessionId }`, which is `[UnsavedComponent]`
  (`RT/Robust.Shared/GameObjects/ComponentAttributes.cs:31`) so library files never carry it.
- `MappingZLevelsSystem` raises the broadcast `WFMappingStackChangedEvent(List<EntityUid> levels)` after `TryAddLevel`,
  `TryRemoveLevel` and stack loading. MappingMode copies the tag from any tagged level to the untagged ones.
- `CanEdit` is then O(1): the tag on `Transform(x).MapUid` equals the player's session.
- `EntityTerminatingEvent` on the tag drops the map from its session.

### 5. Network events

**Several systems may subscribe the same broadcast or network event.** The only refusal is the same subscriber twice
(`RT/Robust.Shared/GameObjects/EntityEventBus.Broadcast.cs:226-239`). Session-arg subscriptions wrap
`EntitySessionMessage<T>` (`RT/Robust.Shared/GameObjects/EventBusExt.cs:17-33`), as both `DecalSystem` and
MappingTools use.

**Ordering is a topological sort over every `before`/`after`** (`EntityEventBus.Ordering.cs:58-113`). The only
constraint is that one system uses the same ordering on every subscription to one event (`:76-93`). So MappingMode can
subscribe `RequestDecalPlacementEvent` and `RequestDecalRemovalEvent` with `after: [MappingToolsSystem]`,
`before: [DecalSystem]`. MappingTools must snapshot the decals before MappingMode adds or removes them; its existing
`before: [DecalSystem]` subscription is at `MappingToolsSystem.History.cs:51-52`.

**There is no cancel.** The events are plain `EntityEventArgs` (`Content.Shared/Decals/SharedDecalSystem.cs:129-150`)
and every handler runs. Mutating `ev.Coordinates` would work but is a hack. Not needed: because `DecalSystem` refuses
non-`Spawn` sessions (`DecalSystem.cs:233`, `:262`), MappingMode applies mapper decals itself:

- placement: `DecalSystem.TryAddDecal` plus the same `CrayonDraw` log;
- removal: `GetDecalsInRange(grid, ev.Coordinates.Position)` + `RemoveDecal`, mirroring `:270-290`.

Admins keep `DecalSystem`'s own path, and nothing is applied twice.

### 6. Net limits

There is no `net.max_message_size`.

| Limit | Where |
|---|---|
| One Lidgren message ≤ 16 MiB | `RT/Lidgren.Network/Lidgren.Network/Lidgren.Network/NetConstants.cs:44`, enforced on receive in `NetPeer.Fragmentation.cs:107-110` |
| 64 concurrent fragment groups per connection | `NetConstants.cs:45` |
| 32 MiB of incomplete reassembly per connection | `net.max_fragment_reassembly_bytes_per_connection`, `RT/Robust.Shared/CVars.cs:158-159` |
| 30 s fragment timeout | `net.fragment_group_timeout`, `CVars.cs:164-165` |

`MsgEntity` writes the serialized event with no cap of its own (`RT/Robust.Shared/Network/Messages/MsgEntity.cs:34-37`,
`:53-58`). Precedent: the Maps preview sends up to 2 MiB in one event (`MappingMapsSystem.cs:40`, `:154-166`).

**Chunk sizing.** 256 KiB chunks are far under every limit. Entity events share the reliable-ordered channel, so pace
them by acknowledgement: at most 2 chunks in flight, and the client acks each chunk (`WFMappingDownloadAckEvent`).

`ITransferManager` (`RT/Robust.Shared/Network/Transfer/ITransferManager.cs`) is the engine's high-bandwidth
alternative, but it needs `CVars.TransferHttp` configured; it is not used.

### 7. Client file writing

All usable from `Content.Client`:

| API | Defined | Precedent |
|---|---|---|
| `IWritableDirProvider.CreateDir` (recursive, `Directory.CreateDirectory`) | `RT/Robust.Shared/ContentPack/IWritableDirProvider.cs:25`; `WritableDirProvider.cs:34-38` | `Content.Client/_WF/CustomMarkings/UI/CustomMarkingLibraryWindow.cs:447` |
| `OpenWrite` | | `CustomMarkingLibraryWindow.cs:449` |
| `OpenWriteText` | `WritableDirProviderExt.cs:58` | `Content.Client/Changelog/ChangelogManager.cs:45` |
| `OpenOsWindow` | `IWritableDirProvider.cs:107` | `CustomMarkingLibraryWindow.cs:454`, `Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs:190` |

`Stream`, `MemoryStream`, `FileMode`, `FileAccess` and `FileShare` are whitelisted (`Sandbox.yml:582-618`).
`System.IO.Path` is **not**; use `ResPath` (design change 7). `DateTime`, `Guid` and regex types are whitelisted
(`Sandbox.yml:782`, `:1183`, `:1223`).

### 8. Lobby injection

**Order.** `LobbyState.Startup` sets the public `LobbyState.Lobby` (`Content.Client/Lobby/LobbyState.cs:38`, `:52`).
Controllers' `IOnStateEntered` runs after `Startup`: `StartupInternal` (`RT/Robust.Client/State/StateManager.cs:49`),
then `OnStateChanged` (`:51`).

**Container.** `CharacterHero` (`Content.Client/Lobby/UI/LobbyGui.xaml:192-197`): the large right-hand area.
`WFLobbyLayout HeroLayout` expects exactly two children (`Content.Client/_WF/RoundSetup/WFLobbyLayout.cs:33`), so the
panel goes inside `CharacterHero`, not beside it.

**Hide.**

| Control | XAML line | Access | Note |
|---|---|---|---|
| `CharacterPreview` | `:194` | public | |
| `ReadyButton.Parent` (holds `RoundSetupButton` and `ReadyButton`) | `:164-169` | via public child | hide the parent: `LobbyState.RoundSetup.cs:64` re-shows `RoundSetupButton` on every crew update |
| `CustomizeButton` | `:173` | public | |
| `ObserveButton` | `:175` | public | |
| `CallVoteButton` | `:178` | private | a restart vote would end every session |
| `ActiveCrews` | `:185` | private | |
| `StartTime` | `:161` | public | |

Keep Options, AHelp, Leave, `ServerInfo` and chat.

**Access.** Unnamed-access XAML names are private on a sealed class (default `Protected`;
`RT/Robust.Client.NameGenerator/XamlUiPartialClassGenerator.cs:98`, `:228-230`). So:

- `Content.Client/_WF/MappingMode/LobbyGui.MappingMode.cs`: a partial of `LobbyGui` in namespace
  `Content.Client.Lobby.UI`, like `Content.Client/_WF/RoundSetup/LobbyGui.RoundSetup.cs`. It exposes
  `WFApplyMappingMode(Control panel)`.
- `WFMappingLobbyUIController : UIController, IOnStateEntered<LobbyState>` calls it on `state.Lobby` when the
  replicated cvar is on.

No upstream edit.

### 9. Escape menu

Pattern: an XAML comment marker plus a public `Button` (`Content.Client/Options/UI/EscapeMenu.xaml:10-18`), and a marked
START/END block in `Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs:76-106` that wires
`OnPressed`.

`MappingButton` follows it. Visibility is set on `_escapeWindow.OnOpen` (attach can come after the state change), from
`WFMappingClientSystem.InSession`. In the mode the same block also hides `CharacterButton`, `RoundSetupButton` and
`SectorMapButton`.

### 10. Map save and load

**`TrySaveMap(EntityUid, ResPath)`** (`RT/Robust.Shared/EntitySerialization/Systems/MapLoaderSystem.Save.cs:129-135`):

- Writes to `UserData` and creates the folder (`MapLoaderSystem.cs:52-58`).
- With all entities pre-init it sets `ExpectPreInit` (`Save.cs:41-44`), so paused, uninitialised maps save cleanly.

**`TryLoadMap(ResPath, out map, out grids, opts)`** (`MapLoaderSystem.LoadMap.cs:25-45`):

- Reads `UserData` first, then content (`MapLoaderSystem.cs:108-120`). A library path never exists in content.
- `InitializeMaps` defaults to false (`RT/Robust.Shared/EntitySerialization/Options.cs:64`), and uninitialised loaded
  maps are paused (`RT/Robust.Shared/EntitySerialization/EntityDeserializer.cs:1071-1084`).
- Use `StoreYamlUids = true` like `mapping`.
- A new empty map from `CreateMap(runMapInit: false)` is paused too (asserted at `Content.Server/Mapping/MappingCommand.cs:173`).

**Z-stack save** (`MappingZLevelsSystem.TrySaveStack`, `:258-287`) saves each level with `TrySaveMap` to the fixed
`MappingZLevels.StackPath(name, depth)` (`Content.Shared/_WF/MappingTools/MappingZLevelsEvents.cs:17-20`), then prunes
stale level files.

**Z-stack open** (`OnOpen`, `:289-341`) opens the ground through the admin `mapping` command, loads the other levels
with `TryLoadMap`, then calls `WfRestack(levels, ground)`.

**Reuse.** Refactor two public methods out of these:

- `TrySaveLevels(List<EntityUid> levels, int ground, Func<int, ResPath> pathFor)`; `TrySaveStack` calls it.
- `TryLoadLevels(SortedDictionary<int, ResPath> files, out List<EntityUid> levels, out int ground)`. It loads every
  level, the ground included, with `TryLoadMap`, then restacks and raises `WFMappingStackChangedEvent`.

Library save and open call these. The admin `OnOpen` keeps its `mapping` path.

### 11. Precedents

- **Per-user server file:** `/round-setups/{NetUserId}.json` (`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.Saved.cs:26`).
  `NetUserId.ToString()` is the Guid "D" form (`RT/Robust.Shared/Network/NetUserId.cs:44`).
- **Autosave loop:** `Content.Server/Mapping/MappingSystem.cs:56-87`. It compares `_timing.RealTime` with a
  per-target next time, skips map-initialised targets, creates the folder and calls `TrySaveMap`.
- **Dummy sessions for tests:** `pair.Server.AddDummySessions(n)` (`RT/Robust.UnitTesting/RobustIntegrationTest.cs:892-906`;
  used in `Content.IntegrationTests/Tests/Round/JobTest.cs:197`).

### 12. Leaks

**Chat.**

| Channel | Scope | Where |
|---|---|---|
| IC and LOOC recipients | same `MapID` only | `ChatSystem.cs:1158-1159` |
| `ExpandICChatRecipientsEvent` adders | AI, cameras, vehicles (`StationAiSystem.cs:53`, `SurveillanceCameraMicrophoneSystem.cs:23`, `_WF/Vehicles/Core/VehicleChatRelaySystem.cs:23`) | relay only from their own entities |
| Dead chat | `GhostComponent` holders and admins (`ChatSystem.cs:1059-1065`, `Content.Server/Chat/Managers/ChatManager.cs:228-234`) | mappers aren't ghosts |
| Radio | global only with `ActiveRadio` (`Content.Server/Radio/EntitySystems/RadioSystem.cs:223-230`) | not on the observer |
| Collective mind | `hearAll` (`ChatSystem.cs:477`) | not on the observer |
| OOC | global | by design |

**Ghost listing.** `GhostSystem` lists players only to `GhostComponent` holders (`GhostSystem.cs:300-325`). Mappers
have none.

**Remaining cross-map read.** Examine by NetEntity, closed by `Examiner` defaults (design change 5).

**PVS.** CE z-level viewers only add eyes on levels of the viewer's own network (`CEZLevelsSystem.View.cs:128-139`).

### 13. Map loader APIs (`RT/Robust.Shared/EntitySerialization/Systems`)

**`TryLoadGrid(MapId map, ResPath file, out Entity<MapGridComponent>? grid, DeserializationOptions? options = null, Vector2 offset = default, Angle rot = default)`**
(`MapLoaderSystem.Load.cs:275-331`).

- `MergeMap = map`, category `Grid`. It fails unless the file has exactly one grid.
- An overload creates its own map: `TryLoadGrid(ResPath, out map, out grid, …)` (`:333`, body `:356-375`).

**`TryMergeMap(MapId mapId, ResPath file, out HashSet<Entity<MapGridComponent>>? grids, DeserializationOptions? options = null, Vector2 offset = default, Angle rot = default)`**
(`MapLoaderSystem.LoadMap.cs:162-215`).

- Every child of the file's map is re-parented through `Matrix3Helpers.CreateTransform(offset, rot)` (`:217-275`), and
  the file's map entity is deleted. Map-level components such as parallax and atmosphere are lost.
- Grid-maps (a map that is also a grid, as planets are) are refused with an error (`:241-245`).
- Merged entities get MapInit only if the target is initialised, and take the target's pause state (`:277-299`). A
  paused, uninitialised session map stays that way.
- The load can throw from `StartEntities` (`MapLoaderSystem.Load.cs:197-206`). Wrap every load.

Both read `UserData` before content (`MapLoaderSystem.cs:108-120`), so a listed path is the only guard.

### 14. ShipPreview's client loader

- `ShipPreviewSystem.TryLoad(handle, ResPath)` reads the file with `MapLoaderSystem.TryReadFile`
  (`Content.Client/_WF/ShipPreview/ShipPreviewSystem.cs:115-118`). `TryLoadText` parses YAML the server sent (`:124-132`).
- It deserializes with `EntityDeserializer` (`InitializeMaps = false`, `PauseMaps = true`) and accepts only one grid
  (`ShipPreviewSystem.Load.cs:21-70`).
- It loads onto a per-handle client map, whose ids are negative (`ShipPreviewSystem.cs:51`, `:255-275`).
- It parks the grid at the origin and counts tiles with `GetAllTilesEnumerator` (`:166-195`).

So it covers `/SharedMaps` ships only (design change 13). The outline instead reuses the drawing approach of
`Content.Client/_WF/MappingTools/MappingSelectionOverlay.cs`.

### 15. PVS overrides

**Session-only.** `GameTicker` adds a session override for the player's own mind
(`GameTicker.Player.cs:36`, `Content.Server/Mind/MindSystem.cs:332`). Nothing overrides observers or session maps.

**Global, and relevant.**

- CE z-network entities: `CEPvsOverrideSystem.cs:24-26`. A map's network entity is sent to everyone. It holds only map
  uids and depths, no content. Accepted.
- Entities in design change 16.

The rest fire only on use (holopad, laser pointer, ship shields, explosions, FTL) or in rounds.

**Cross-map checks.**

| Path | Result | Where |
|---|---|---|
| Pointing | range check fails across maps | `Content.Server/Pointing/EntitySystems/PointingSystem.cs:108-116`, `:194`; also blocked through `PointAttemptEvent` (`Content.Shared/Pointing/SharedPointingSystem.cs:21-28`), added to the observer's blocked events |
| Emotes | blocked | `EmoteAttemptEvent` |
| Local chat | same map | item 12 |
| Dead chat, ghost lists | none | not ghosts |

### 16. What the observer without hands gives

`AdminObserver` *has* hands, pulling, stripping and consoles (`admin_ghost.yml:23-115`); `WFMappingObserver` has none of
them. With no `Hands`, `Puller`, `ComplexInteraction` or `Stripping`, and every attempt event cancelled (item 3), the
result is:

- no pickup, pull or strip;
- no `ActivatableUI` (it needs `CanInteract`);
- no interaction verbs (`SharedVerbSystem.cs:76-80` caches `CanAccess` and `CanInteract`).

What is left is examine, kept same-map by default `Examiner`, and verb listing, closed by design change 15.

### 17. Level registry

- `CEMapAddedIntoZNetworkEvent` (directed on the map, `CEZLevelsSystem.Maps.cs:79-80`) is already taken for
  `CEZMapComponent` (`CEZLevelMappingSystem.cs:21`). A new level has no MappingMode component yet, so a directed
  subscription can't see it.
- `CEZLevelMapNetworkUpdatedEvent` (`:83`) is taken for `CEZMapNetworkComponent` (`CEZLevelsRoofSystem.Maps.cs:18`).
- So the registry is fed by MappingTools' own broadcast `WFMappingStackChangedEvent`. It is raised after `WfRestack` in
  `TryAddLevel`, `TryRemoveLevel` and `TryLoadLevels`, the only paths that change a session's stack.
- `max_levels` is checked in the `WFMappingCanEditEvent` that `OnAdd` raises before it adds a level.

### 18. Counting per tile

- **All entities on a tile:** `EntityLookupSystem.GetLocalEntitiesIntersecting(gridUid, Vector2i tile, float enlargement, flags)`
  (`RT/Robust.Shared/GameObjects/Systems/EntityLookupSystem.LocalQueries.cs:62`) with enlargement 0, filtered to
  entities whose local position rounds to the tile (`MappingToolsMath.CellOf`).
- **Anchored entities only:** `SharedMapSystem.GetAnchoredEntitiesEnumerator`
  (`RT/Robust.Shared/GameObjects/Systems/SharedMapSystem.Grid.cs:1203`) is cheaper.
- **Decals:** `DecalSystem.GetDecalsInRange(grid, centre, 0.5f)` (`Content.Server/Decals/DecalSystem.cs:340`).
- **Session entity total:** recomputed at most once a second by walking the session maps' `Transform` children, plus
  increments from placements in between.
- **Grid count:** the `MapGridComponent` children of the session's maps.
- **New grid detection:** a tile placement with no grid under the coordinates, the same condition as
  `PlacementManager.cs:197-205`.

### 19. Nothing but mapping

**Every world loader is round-start driven.**

- `GameTicker.LoadMaps` (`RoundFlow.cs:105-160`) is called only from `StartRound` (`:419`) and the preload
  (`:1095`), which `Paused` blocks (`:1081`).
- Stations, so the station-triggered loaders, come only from loaded game maps (`StationSystem.cs:54`
  `PostGameMapLoad`):
  - arrivals (`ArrivalsSystem.cs:97`, `:102`)
  - emergency shuttle and CentComm (`EmergencyShuttleSystem.cs:84-88`)
  - CE station z-levels (`CEZLevelsSystem.cs:36`)
  - shipyard preview (`_Mono/Shipyard/ShipyardPreviewSystem.cs:33`)
  - grid preloader (`GridPreloader/GridPreloaderSystem.cs:37`)
  - The Dark (`_Starlight/Shadekin/StationTheDark.cs:18`)
  - public transit (`_NF/PublicTransit/PublicTransitSystem.cs:51`, `:57`)
- Game rules (sector, POIs, planets) start only through `StartGamePresetRules` in `StartRound`. `NfAdventureRuleSystem`
  raises `StationsGeneratedEvent` (`Content.Server/_NF/GameRule/NfAdventureRuleSystem.cs:369`).
- Encounters, ADS and sector systems return unless `RunLevel == InRound`:
  - `WFEncounterSchedulerSystem.cs:159`
  - `WFAdsCampaignSystem.cs:134`, `:199`
  - `WFSectorTerritorySystem.cs:104`
  - `WFSectorActivitySystem.cs:61`
  - `WFSectorNoticeSystem.cs:53`
- Round setup and NPC crew spawn their ships from lobby or round actions, not on a timer.

**So with `Paused` asserted before the first tick, boot has zero maps.** `PauseStart` runs in the
`GameRunLevelChangedEvent` raised by the startup `RestartRound` (item 1).

**Hooks to keep it so.**

- **`LoadingMapsEvent`** (`RoundFlow.cs:147`): `ev.Maps.Clear()` while the mode is on. A forced `startround` then makes
  one empty default map (`:149-153`) instead of a station. Game rules from the preset still run on a forced start
  (noted, not blocked, per the design).
- **`WFRoundSetupRequest`** (`Content.Server/_WF/RoundSetup/WFRoundSetupSystem.cs:85`): `OnRequest` returns while the
  mode is on. It is the only network entry to round setup, scenarios and ship purchase before a round.
- **Shipyard and traders:** BUIs need interaction, which the observer can't do.
- **Sector map:** client-only, with no data in the mode. Its escape-menu entry is hidden.

**Admin observer map.** One paused map, created on the first admin observe and tracked by the registry as `Admin` (not
a session). Reference loads use short-lived scratch maps (design change 13). Both are deleted by a restart flush.

## Stages

New `_WF` files need no markers. Every upstream edit is listed with its marker.

### S1 server core

**Shared** (`Content.Shared/_WF/MappingMode/`)

- `WFMappingModeCVars.cs`: `[CVarDefs]` (precedent `Content.Shared/_WF/AdsIncursion/AdsIncursionCVars.cs`).
  - `wf.mapping_mode.enabled`: `SERVER | REPLICATED`.
  - `wf.mapping_mode.map_size`: `SERVER | REPLICATED`, so the client can draw the border.
  - All `SERVERONLY`:
    - autosave: `autosave_interval`, `autosave_min_interval`, `autosave_max_interval`
    - library: `max_versions`, `max_maps`, `max_library_bytes`
    - sessions: `max_members`, `max_sessions`, `max_levels`, `empty_session_grace`
    - grids: `grids_per_player`, `max_grids_per_session`
    - contents: `max_entities_per_tile`, `max_decals_per_tile`, `max_entities`
    - other: `max_reference_bytes`, `max_edits_per_second`
- `WFMappingThrottle.cs`: a pure per-key token bucket (rate, burst, `TryTake(now)`), used for every throttle in the
  design.
- `WFMappingObserverComponent.cs`: `[RegisterComponent, NetworkedComponent]` marker; the client's mapper flag.
- `WFMappingObserverSystem.cs`: cancels the attempt events listed in item 3, plus `PointAttemptEvent`.
- `WFMappingReferenceEvents.cs`:
  - Client → server: `WFMappingReferenceListRequest`, `WFMappingReferencePickRequest(Path)`,
    `WFMappingReferenceConfirmRequest(Token, Vector2 Offset, int Turns)`, `WFMappingReferenceCancelRequest(Token)`.
  - Server → client: `WFMappingReferenceListEvent(List<MappingMapFile>)`,
    `WFMappingReferenceFootprintEvent(Token, List<WFMappingFootprintGrid>)`, where each grid is its `Vector2` offset
    and `Angle` relative to the anchor, plus its tiles as `List<Box2i>`.
- `WFMappingFootprint.cs`: pure. Merges tile cells into rectangles (row runs, then stacked runs) and computes the bounds
  of rotated footprints.
- `WFMappingAdminEvents.cs`: `WFMappingAdminObserveRequest` and `WFMappingAdminLeaveRequest`.
- `WFMappingModeEvents.cs`: `[Serializable, NetSerializable]` events.
  - Client → server: `WFMappingCreateRequest(Name)`, `WFMappingJoinRequest(Code)`, `WFMappingRejoinRequest(SessionId)`,
    `WFMappingLeaveRequest`, `WFMappingKickRequest(NetUserId)`.
  - Server → client: `WFMappingSessionStateEvent` (own session: name, code, members, owner; plus rejoinable sessions),
    `WFMappingResultEvent(LocKey, args)`.
- `WFMappingSessionCodes.cs`: pure. Alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`, length 6, `Generate(IRobustRandom
  or System.Random, ISet<string> taken)`, `IsValid`.

**Server** (`Content.Server/_WF/MappingMode/`)

- `WFMappingSession.cs`: `Id`, `Code`, `Name`, `Owner`, `Members` (NetUserId → joined time), `Present` (NetUserId →
  observer), `Dirty`, `LastSaved`, `NextAutosave`, `EmptySince`, `Source`.
- `WFMappingSessionMapComponent.cs`: `[RegisterComponent, UnsavedComponent]` `SessionId`.
- `WFMappingModeSystem.cs`: mode and permissions.
  - Cvar: on, set `game.lobbyenabled true`, `events.enabled false`, `shuttle.auto_call_time 0`; off, end every
    session and `PauseStart(false)`.
  - `GameRunLevelChangedEvent`: on `PreRoundLobby`, `if (!Paused) PauseStart(true)`.
  - `RefreshLateJoinAllowedEvent`: `Disallow()`.
  - `RoundRestartCleanupEvent`: autosave dirty sessions, then clear.
  - Predicates `CanMap(session)`, `CanEdit(session, mapUid)`, `AllowPlacement(session, MsgPlacement)`.
  - Handlers for `WFMappingCanUseEvent` and `WFMappingCanEditEvent`, and for `GhostAttemptHandleEvent`.
- `WFMappingModeSystem.Sessions.cs`: sessions.
  - Create (empty paused map, tagged), join, rejoin, leave and kick (item 1 calls).
  - Member limit; owner handover to the longest-present member.
  - Grace expiry in `Update` (autosave if dirty, then delete the session's maps).
  - `PlayerStatusChanged` (disconnect → absent); `WFMappingObserverComponent` + `PlayerDetachedEvent` → absent
    (covers aghost by admins); `WFMappingStackChangedEvent` → re-tag.
  - Calls `MappingToolsSystem.ForgetHistory` on leave.
- `WFMappingModeSystem.Guard.cs`: guard and dirty tracking.
  - `PlacementEntityEvent(Create)` / `PlacementTileEvent` backstop: delete or restore with a log line when not
    `CanEdit`.
  - Decal network hook (`after: [MappingToolsSystem]`, `before: [DecalSystem]`).
  - Marks sessions dirty on `TileChangedEvent`, placement events, decal hook and `WFMappingEditedEvent`. All are
    broadcast, so several subscribers are fine.
- `GameTicker.MappingMode.cs`: partial `GameTicker` in `Content.Server.GameTicking`, adding `WFMappingReturnToLobby`.
- `WFMappingModeSystem.Registry.cs`: map registry.
  - `Dictionary<EntityUid, (Kind, SessionId?)>` for session maps, the admin map and scratch maps, kept in sync with the
    `WFMappingSessionMapComponent` tag.
  - Registers on create, open, `WFMappingStackChangedEvent` and reference confirm; unregisters on `EntityTerminatingEvent`.
  - Every request resolves its target map here and is dropped with a log line if it resolves to no session or another
    session.
- `WFMappingModeSystem.Limits.cs`: limits, called from `AllowPlacement`, the decal hook and the `WFMappingCanEditEvent`
  handler.
  - **Throttles:** placement and tiles `max_edits_per_second`; MappingTools 10/s; create and join 1 per 3 s; library
    list 2/s; reference pick and confirm 1 per 5 s; kicks.
  - **Grids:** the cap is `grids_per_player × members`, at most `max_grids_per_session`. A new-grid tile placement
    above it is refused; the backstop deletes a grid created anyway. Splits are counted but not reverted.
  - **Entities:** `max_entities_per_tile` and `max_entities` (item 18); `max_decals_per_tile`.
  - **Area:** every tile inside `[-size/2, size/2)` on each level, checked on placement and on transformed paste, move
    and reference boxes.
  - **Prototypes:** the deny set from design change 16.
  - Every refusal sends `WFMappingResultEvent` with the limit's locale key.
  - Exposes `WFMappingLimitsState` (grids, entities, area) for the Mapping window's status line.
- `WFMappingModeSystem.Reference.cs`: reference loads (design change 13).
  - **List:** `MappingMapsSystem.ListFiles` filtered to `Ships` and `Maps` for non-admins, plus the requester's own
    library levels.
  - **Pick:**
    1. Check the path is listed and the size is under `max_reference_bytes`.
    2. Load it onto a scratch map: `TryLoadGrid(ResPath, out map, out grid)` for a grid file, `TryLoadMap` for a map
       file, `StoreYamlUids`, inside a try/catch.
    3. Strip deny-set entities and drop entities parented to the map that aren't grids.
    4. Count grids, entities and tiles and refuse over the caps.
    5. Send the footprint and keep one pending scratch map per player for 60 s.
  - **Confirm:** check area and caps at the final offset and rotation, re-parent each grid onto the session map with
    `TransformSystem.SetCoordinates` (keeping the relative layout), delete the scratch map, register and mark dirty.
  - **Cancel, timeout or leave:** delete the scratch map.
- `WFMappingModeSystem.Admin.cs`: admin side.
  - `WFMappingAdminObserveRequest` (needs `AdminFlags.Admin`): create or reuse the admin map, then join as in item 1,
    spawning `GameTicker.AdminObserverPrototypeName`.
  - `WFMappingAdminLeaveRequest`: return to the lobby.
  - `LoadingMapsEvent`: `Maps.Clear()` while the mode is on.
- `Commands/WFMappingModeCommands.cs`: `[AdminCommand(AdminFlags.Mapping)]` commands, each admin-logged.
  - `wf_mappingmode_list`, `wf_mappingmode_tp <code>` (direct `SetMapCoordinates` to the root map, no console chaining),
    `wf_mappingmode_close <code>` (autosave to the owner, then close), `wf_mappingmode_kick <name>`,
    `wf_mappingmode_save <code>`.
  - Precedent: `Content.Server/_WF/Encounters/Commands/WFEncounterCommand.cs`.
- **Isolation:** every session, library and reference action goes to the admin log (`LogType.Action`). Every save, load
  and download is wrapped in try/catch. A session whose map fails to load is torn down. Mode off: stop accepting,
  autosave, return everyone, delete the maps.

**Other `_WF` modules** (no markers)

- `Content.Server/_WF/RoundSetup/WFRoundSetupSystem.cs` `OnRequest` (`:85`): return while
  `wf.mapping_mode.enabled`.

**MappingTools** (`_WF` files; no markers)

- `Content.Server/_WF/MappingTools/WFMappingPermissionEvents.cs` (new): broadcast local events.
  - `WFMappingCanUseEvent(Session){Allowed}`.
  - `WFMappingCanEditEvent(Session, MapUid, Kind, Grid?, Box2i? Cells, int Entities, int Tiles, bool NewGrid){Cancelled, Reason}`.
    `Kind` is `Read`, `Edit`, `Paste`, `AddLevel` or `RemoveLevel`. The counts and box let MappingMode apply the
    limits; pasting off every grid sets `NewGrid`.
  - `WFMappingStackChangedEvent(Levels)`.
  - `WFMappingEditedEvent(MapUid)`.
- `MappingToolsSystem.cs`:
  - `CanUse`: admin, or the event.
  - `CanEdit(session, uid)`: checks the grid's map in Move, Mirror, Copy and Delete, and the target map in Paste.
  - Raise `WFMappingEditedEvent` in `Commit`.
- `MappingToolsSystem.History.cs`:
  - `:229`: the decal predicate from item 2.
  - `StepHistory`: `CanEdit` on the edit's grids; raise `WFMappingEditedEvent`.
  - Add public `ForgetHistory(session)`.
- `MappingZLevelsSystem.cs`: Move, Add and Remove use `CanUse` + `CanEdit`; raise `WFMappingStackChangedEvent`.

**Upstream edit**

- `Content.Server/Sandbox/SandboxSystem.cs`:
  - `using Content.Server._WF.MappingMode; // WOLFGATE(MappingMode)`
  - `[Dependency] private WFMappingModeSystem _wfMapping = default!; // WOLFGATE(MappingMode)`
  - Above `return false;` at `:73`:

    ```csharp
    // WOLFGATE(MappingMode): mappers place, erase and lay tiles on their own session's maps
    if (_wfMapping.AllowPlacement(player, placement))
        return true;
    ```

- `Content.Shared/Verbs/SharedVerbSystem.cs`:
  - `using Content.Shared._WF.MappingMode; // WOLFGATE(MappingMode)`
  - After `:73` in `GetLocalVerbs`:

    ```csharp
    // WOLFGATE(MappingMode): mapping observers get no verbs; admin requests are forced and keep theirs
    if (!force && HasComp<WFMappingObserverComponent>(user))
        return verbs;
    ```

### S2 library, versions, autosave, download

**Shared** (`Content.Shared/_WF/MappingMode/`)

- `WFMappingLibraryNames.cs`: pure.
  - Map name regex `^[A-Za-z0-9 _-]{1,48}$`, trimmed.
  - Version folder `v{NNNN}_{yyyyMMdd-HHmmss}_{manual|auto}`, with format and `TryParse`.
  - Level file `z{D}.yml`, with format and parse.
  - `ResPath` builders for `/Mapping/Library/<NetUserId>/<MapName>/<version>/`.
- `WFMappingPrune.cs`: pure. `SelectForRemoval(versions, max)`: oldest `auto` first, then oldest `manual`.
- `WFMappingDownloadAssembler.cs`: pure.
  - Per transfer id: file name, chunk count, received chunks, total ≤ 32 MiB.
  - Rejects out-of-range or duplicate chunks.
  - `TryComplete(out byte[])`.
- `WFMappingLibraryEvents.cs`.
  - Client → server: `WFMappingLibraryRequest`, `WFMappingSaveRequest(Name)`, `WFMappingOpenRequest(MapName, Version?)`,
    `WFMappingDeleteRequest(MapName, Version?)`, `WFMappingDownloadRequest(MapName, Version?)`,
    `WFMappingDownloadAckEvent(TransferId, Chunk)`.
  - Server → client: `WFMappingLibraryEvent(List<WFMappingLibraryMap>)`,
    `WFMappingDownloadChunkEvent(TransferId, MapName, Version, FileName, Chunk, ChunkCount, byte[] Data)`.

**Server** (`Content.Server/_WF/MappingMode/`)

- `WFMappingLibrarySystem.cs`:
  - Listing comes from folder names.
  - Manual save goes to the saver's library (10 s per-player rate limit). It continues the sequence, enforces
    `max_maps`, then prunes to `max_versions`.
  - Open creates a session from one version; opening is limited to the requester's own library folder.
  - Delete a map or a version.
- `WFMappingModeSystem.Autosave.cs`: an `Update` loop (`MappingSystem.cs:56-87` pattern).
  - Each session has an owner-picked step: Off, 1, 2, 5, 10 or 15 min (request `WFMappingAutosaveStepRequest`, owner
    only). It is clamped to `autosave_min_interval` .. `autosave_max_interval`, with a random initial offset.
  - Sessions that are due and dirty join a queue; one is saved per tick as `auto` into the owner's library, then
    pruned.
  - `max_library_bytes` is summed before each save.
- `WFMappingDownloadSystem.cs`: one transfer per player. Reads with `UserData.OpenRead`, sends 256 KiB chunks with 2 in
  flight, advances on ack, and cancels on disconnect.

**MappingTools** (no markers)

- `MappingZLevelsSystem.cs`: extract `TrySaveLevels` and `TryLoadLevels` (item 10); `TrySaveStack` delegates.
- `MappingMapsSystem.cs`: `FindSaved` skips `/Mapping/Library`.

### S3 client

**Client** (`Content.Client/_WF/MappingMode/`)

- `WFMappingClientSystem.cs`:
  - `InSession` = the local entity has `WFMappingObserverComponent`.
  - Holds session state, the library list and the rejoin list from the network events.
  - **Setup** runs once the local entity is an observer **and** the state is `GameplayState`. Steps:
    `MarkerSystem.MarkersVisible = true`, `ILightManager.Enabled = false`, `SubFloorHideSystem.ShowAll = true`,
    `ActionsSystem.LoadActionAssignments("/mapping_actions.yml", false)`, `MappingToolsSystem.SetEnabled(true)`.
  - **Teardown** on `LocalPlayerDetachedEvent` reverses the flags, calls `SetEnabled(false)`,
    `IPlacementManager.Clear()`, and closes the spawn and mapping windows.
  - **Download writer:** `WFMappingDownloadAssembler` → `UserData.CreateDir` + `OpenWrite` to
    `/Mapping/<MapName>/<version>/z<D>.yml` (`ResPath` only) → ack → offer `OpenOsWindow`.
- `WFMappingLobbyUIController.cs` (`IOnStateEntered<LobbyState>`, `IOnStateExited<LobbyState>`).
- `WFMappingLobbyPanel.xaml`, `WFMappingLobbyPanel.xaml.cs`: New map, Join, My maps, Your sessions.
- `LobbyGui.MappingMode.cs`: the partial from item 8. It also hides `ObserveButton` and inserts an admin-only
  `Observe` button that sends `WFMappingAdminObserveRequest` (design change 14).
- `WFMappingReferenceSystem.cs`, `WFMappingReferenceOverlay.cs`: the reference placement mode.
  - Started from Load reference with the footprint event.
  - Draws translucent footprint rectangles and the bounding box at the cursor, snapped to tiles. The drawing follows
    `MappingSelectionOverlay`.
  - R turns it, click confirms with `WFMappingReferenceConfirmRequest`, Escape cancels.
  - Picking in the spawn menu cancels it, as Select does.
- `WFMappingBoundsOverlay.cs`: whenever the local entity is an observer, draws a dashed square of `map_size`, a corner
  label (`wf-mapping-mode-bounds-label`) and a tint outside it.
- The `WFMappingMenuWindow` Session tab adds:
  - a status line from `WFMappingSessionStateEvent` (grids, entities, area);
  - the owner's autosave step picker;
  - `Delete grid` (with confirm) for the grid under the mapper;
  - `Load reference` (list and pick).
- An admin on the admin map gets a `Return to lobby` entry in the same window.
- `WFMappingMenuUIController.cs`.
- `WFMappingMenuWindow.xaml`, `WFMappingMenuWindow.xaml.cs`: Session, Save and Library tabs. Not named `CloseButton`,
  `ContentsContainer`, `TitleLabel` or `WindowHeader`.

**MappingTools client** (no marker)

- `Content.Client/_WF/MappingTools/MappingToolsSystem.cs`: `MapsButton.Visible = _admin.HasFlag(AdminFlags.Mapping)`.

**Upstream edits**

- `Content.Client/Options/UI/EscapeMenu.xaml`, after `:18`:

  ```xml
  <!-- WOLFGATE(MappingMode): session, save and library window while mapping -->
  <Button Access="Public" Name="MappingButton" Text="{Loc 'wf-mapping-mode-escape-button'}" Visible="False" />
  ```

- `Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`:
  - `using Content.Client._WF.MappingMode; // WOLFGATE(MappingMode)`
  - Block after `:106`:

    ```csharp
    // WOLFGATE(MappingMode) START: mapping session window; crew and character entries hidden while mapping
    ...
    // WOLFGATE END
    ```

- `Content.Client/UserInterface/Systems/Sandbox/SandboxUIController.cs`:
  - `using Content.Client._WF.MappingMode; // WOLFGATE(MappingMode)`
  - `[UISystemDependency] private readonly WFMappingClientSystem _wfMapping = default!; // WOLFGATE(MappingMode)`
  - At `:64`, `:73` and `:80`, each:

    ```csharp
    // WOLFGATE(MappingMode) START: mappers open the spawn menus
    // if (!_admin.CanAdminPlace())
    if (!_admin.CanAdminPlace() && !_wfMapping.InSession)
    // WOLFGATE END
    ```

- `Content.Client/UserInterface/Systems/DecalPlacer/DecalPlacerUIController.cs`:
  - Using and `[UISystemDependency]` as above, marked `// WOLFGATE(MappingMode)`.
  - At `:28`:

    ```csharp
    // WOLFGATE(MappingMode) START: mappers open the decal placer
    // else if(_sandbox.SandboxAllowed)
    else if (_sandbox.SandboxAllowed || _wfMapping.InSession)
    // WOLFGATE END
    ```

### S4 prototypes, locale, docs

- `Resources/Prototypes/_WF/MappingMode/observer.yml`: `WFMappingObserver` (item 3).
- `Resources/Locale/en-US/_WF/MappingMode/mapping-mode.ftl`: every lobby, window, popup and result string, plus
  `wf-mapping-mode-escape-button`.
- `Content.Server/_WF/MappingMode/README.md`: hand-written overview.
- `python Tools/_WF/Ci/modules.py --write`: generated README sections. `Docs/_WF/NONMODULAR.md` gets no new entries:
  every upstream edit is tagged with the module.
- No `unmarked` entries are needed; every upstream file touched can hold a marker.

### S5 tests and gate

**Unit tests** (`Content.Tests/_WF/MappingMode/`)

- `WFMappingSessionCodesTest.cs`: alphabet, length, uniqueness against a taken set.
- `WFMappingLibraryNamesTest.cs`: name validation; version folder and level file round trips.
- `WFMappingPruneTest.cs`: `auto` before `manual`, oldest first, under and over the limit.
- `WFMappingDownloadAssemblerTest.cs`: in and out of order, duplicate, out of range, over 32 MiB, completion.
- `WFMappingThrottleTest.cs`: rate, burst and refill.
- `WFMappingFootprintTest.cs`: rectangle merge covers exactly the input cells; rotated bounds.

**Integration tests** (`Content.IntegrationTests/Tests/_WF/MappingMode/`)

All use `PoolSettings { InLobby = true, Dirty = true }`, set `wf.mapping_mode.enabled` in the test and reset it at the
end. No `Destructive`. Extra players come from `pair.Server.AddDummySessions`.

- `WFMappingSessionTest.cs`: create, join by code, member limit, leave, owner handover, grace expiry deletes the maps.
- `WFMappingPermissionTest.cs`:
  - `AllowPlacement` refuses another session's map, nullspace and actor erase targets, and `HideSpawnMenu` prototypes.
  - `WFMappingCanEditEvent` cancels a MappingTools `Move` on another session's grid.
  - The backstop removes a forced create.
- `WFMappingLibraryTest.cs`: save → `v0001`, then `v0002`; autosave marks `auto`; pruning honours `max_versions`;
  open from library restores the level count; download chunks reassemble to the file bytes.
- `WFMappingTickerTest.cs`:
  - The ticker stays in `PreRoundLobby` and `Paused` across several seconds and a `RestartRound`.
  - With the mode on, after a restart the only maps are the test pool's own, and none are added until a session
    exists.
  - `LoadingMapsEvent` clears the map list.
- `WFMappingLimitsTest.cs`:
  - grid cap refuses a new-grid tile;
  - per-tile and session entity caps;
  - an out-of-bounds tile is refused;
  - `max_levels`;
  - a deny-set prototype is refused;
  - throttles drop excess requests.
- `WFMappingReferenceTest.cs`:
  - a `/SharedMaps` grid loads onto the session map at an offset and rotation, keeping its tile count;
  - a map file keeps its grids' relative layout;
  - over-cap and oversize references are refused, and the scratch map is deleted.
- `WFMappingAdminTest.cs`: the `wf_mappingmode_*` commands (list, close autosaves, kick); the admin observe map is
  registered as `Admin`.

**Gate**

- Build `Content.Server`, `Content.Client`, `Content.Tests` and `Content.IntegrationTests` with
  `-p:WarningsAsErrors=nullable`.
- Run the new unit and integration tests, plus the existing `Tests/_WF/MappingTools` tests (CanUse and z-level changes).
- `dotnet run --project Content.YAMLLinter -c Release`.
- Headless server: grep its log for `[ERRO]` and `[FATL]` with the mode on.
- `Content.Client.exe --headless`: check for `Sandbox violation`.
- `python Tools/_WF/Ci/modules.py --check`, and `--pr-check origin/main`.
