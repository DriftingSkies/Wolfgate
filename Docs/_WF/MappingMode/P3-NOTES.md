# P3 notes: upload

Stage P3 of PLAN2.md. What was built, and the choices the plan left open.

## Built

- Shared: `WFMappingUploadEvents` (begin, ready, chunk, ack, done, cancel request, cancelled), the pure
  `WFMappingUploadAssembler`, cvars `max_upload_bytes` (1.5 MiB, replicated, clamped to the 8 MiB `MaxFileBytes`)
  and `upload_cooldown` (30 s), `WFMappingVersionKind.Upload` with the `_upload` suffix, `UploadNameFromFile`.
- Server: `WFMappingUploadSystem`, `WFMappingUploadValidator`, `TryReserveLoad`/`TryLoadOntoScratch` in the
  reference partial (reference pick now uses them), `SaveVersion` over a level list with the session overload
  wrapping it, `SaveUpload`, `HasRoomForMap`.
- Client: `WFMappingUploadClientSystem`, `WFMappingUploadRow`, Upload buttons and rows in the lobby panel and the
  Mapping window's Library tab, the `upload` kind label.
- Test: `Content.IntegrationTests/Tests/_WF/MappingMode/WFMappingUploadTest.cs`.

## Choices

- **Dialog.** `GetFileAndName` rather than `OpenFile`, since the default map name comes from the file name. Same
  dialog, same `.yml` filter, `FileAccess.Read`.
- **Map name.** The upload goes under `UploadNameFromFile(file)` with no name prompt; an existing map of that name
  gets a new version. Nothing usable left gives `Upload`.
- **Chunks.** 128 KiB, two in flight, so the 8 MiB hard cap is at most 64 chunks.
- **Caps.** An upload is always checked against the caps of a new session (`max_grids_per_session`,
  `max_entities`, per-tile caps, `map_size` where the grids sit), wherever it was sent from, because it goes to the
  library and only ever opens as a new session. `TryLoadOntoScratch` still takes a session for reference loads.
- **Cooldown.** Spent when the server accepts the begin, not on a refused one. A refusal because the global load
  slot is busy gives it back, since that isn't the player's doing. A silent 2/s request throttle sits in front, as the
  other handlers have.
- **Every begin is answered.** A refused begin sends `WFMappingUploadCancelledEvent(0)` plus the result, so the client
  never waits on nothing; the client also gives up after 40 s without a word.
- **Validation extras.** YAML nesting deeper than 64 is refused during the event scan, since `DataNodeParser`
  recurses per level and a deep file could overflow the stack. Prototype ids go through the same renames and deletions
  the loader applies (`BeforeEntityReadEvent`), so a file with a migrated id isn't refused. A file without grids is
  refused with the reference's "no grids" message.
- **What is stored.** The engine's save of the stripped scratch map. The file's own map entity and its components
  don't survive the merge onto the scratch map, as with reference loads.
- **Outcomes for tests and admins.** `WFMappingUploadSystem.ActiveTransfer(user)` and `LastOutcome(user)` (the last
  result's Fluent id) are public, since a dummy session can't receive the result events.
- **Open from the Mapping window.** The upload row's Open is hidden there, as the library list's is: opening needs the
  lobby.

## Unknown prototypes

A file using prototypes this server lacks is no longer refused outright; the player may load it without them.

- **Flow.** When validation finds unknown prototypes and nothing else wrong, the server keeps the assembled bytes for
  `upload_confirm_seconds` (120, server only) and sends `WFMappingUploadNeedsConfirmEvent`: up to 25 names, sorted,
  the total count, and the entities that would go. Nothing is loaded yet. The upload row shows the warning with Load
  anyway, which sends `WFMappingUploadConfirmRequest(DropMissing: true)`, and Cancel, which sends the usual cancel.
  The done event and result say how many entities were dropped.
- **Server decides.** The confirm carries no names. The bytes are validated again, the missing set worked out again,
  stripped, and stored exactly as a clean upload (scratch load, deny strip, caps). A confirm without `DropMissing` is
  ignored and the upload keeps waiting. The drop is logged at Info with the player and the count.
- **What goes.** `StripMissing` removes the missing prototypes' groups, then, round after round, every entity whose
  Transform parent was removed. Removed uids also leave the root `maps`/`grids`/`orphans`/`nullspace` lists and
  survivors' container `ents` lists and `ent` slots, since the loader logs an error for an unknown uid and the
  integration tests fail on error logs. Other references to a removed entity (device links and the like) still log
  that error and load as invalid. `meta.entityCount` becomes the remaining count. The entity cap is checked on the
  file before stripping.
- **Discarded.** Timeout (the result says it waited too long), disconnect, cancel, the end of the player's session
  membership, and a new begin from the same player, which replaces it. A confirm that arrives after the deadline
  but before the tick that drops it is treated as expired.
- **Client.** A new `Confirming` stage that the client's 40 s stall timer skips; the server's deadline ends it. The
  warning sits in a wrapping `RichTextLabel` in place of the clipped label, as plain text so prototype ids can't carry
  markup.
- **Tests.** `WFMappingUploadTest` covers the pending question and its counts (with an item parented to a missing
  wall), an ignored confirm without the flag, an expired one, and the stored version opening with only the surviving
  item. `WFMappingUploadClientTest` runs the warning and Load anyway through the real client. `StripMissing` has unit
  tests in `Content.Tests/_WF/MappingMode/WFMappingUploadValidatorTest.cs`.
