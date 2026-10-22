# S2 notes: assumptions made while building the library, autosave and download

## Library layout and names

- Library map names use the session-name rule (`^[A-Za-z0-9 _-]{1,48}$`, trimmed) and also refuse Windows device
  names (`CON`, `NUL`, `COM1`, ...). Session creation now validates with the same rule, so a session name is always a
  valid library name; autosave falls back to the session code if it somehow isn't.
- Map folders are matched ignoring case (Windows folders do), and a save reuses the existing folder's spelling.
- Version folders must round-trip exactly (`v0001_20261018-142501_manual`); anything else in a map folder is ignored.
  Numbers past 9999 get more digits. The stamp is UTC, truncated to the second.
- A version without `z0.yml` is listed but can't be opened or downloaded. A map folder with no versions isn't listed
  and doesn't count towards `max_maps`.
- `MappingMapsSystem.FindSaved` skips `/Mapping/Library`, so the admin Maps window no longer lists libraries. The
  reference list (S1) still walks the player's own library folder four deep; it wasn't narrowed.

## Saving

- A manual save needs the player to be present in a session; it goes into their own library under the name sent, or
  the session name when the name is blank. One per 10 s per player; a refused save says so.
- `LastSaved` updates on any member's save, but only the owner's manual save (or an autosave) clears `Dirty`, since
  autosaves go to the owner's library.
- Caps: `max_maps` refuses a save that would start a new map. `max_library_bytes` refuses a save when the library is
  already at the cap, and otherwise checks after writing: library before + new version - versions the prune would
  remove. If that's over the cap the new version is deleted and the save fails, so prunable autosaves make room first.
- Pruning never removes the newest version (the one just written), even when `max_versions` is 1 and it is an
  autosave. Within that, oldest `auto` first, then oldest `manual`.
- A failed or refused save deletes its version folder; any exception is logged and reported, never thrown.
- A scan of a player's library is cached for 30 s for listings and dropped by every save, prune and delete made
  here. Saves, opens, downloads and deletes always rescan.

## Autosave

- The step is in minutes (Off, 1, 2, 5, 10, 15). New sessions start on the step nearest `autosave_interval` (120 s → 2).
  The interval is the step clamped to `autosave_min_interval`..`autosave_max_interval`.
- The first check is one interval plus a random 0–50 % later. When a check comes due, the next one is scheduled one
  interval on; a dirty session joins the queue, a clean one only gets its state resent (so the client's "next
  autosave" stays current). One queued session is saved per tick.
- Changing the step is owner-only, uses the 10/s tools throttle, and reschedules the next check one interval from now.
- Autosaves tell the owner (if online) only when they fail: one "autosave failed" result plus the reason. Success
  shows up as `LastSaved` in the session state and a fresh library listing.
- `AutosaveAll` (round restart) saves every dirty session at once, not through the queue.
- Session state now carries `AutosaveMinutes`, `NextAutosave` (game time, `IGameTiming.CurTime`; null when off),
  `LastSaved` (UTC) and `Dirty`. `Dirty` turning on resends the state once.

## Opening

- Open is a lobby request like create/join: same checks and 1 per 3 s throttle, and the session cap is checked before
  anything loads. It leaves the player's old membership first, as create does.
- Every level loads with `StoreYamlUids`, not map-initialised and paused (`MappingZLevelsSystem.TryLoadLevels`), then
  denied-component entities are stripped from each map's children, and the levels are named, paused again, registered
  as session maps and tagged. A single level gets no z-network, as a new session has none. `Dirty` is cleared after.
- The new session is named after the library map; its autosaves continue that map's sequence in the owner's library.
- Levels over `max_levels`, or content past the current entity/grid caps, still open; the caps then refuse further
  growth. There is no size cap on opening beyond `max_library_bytes`, and a large file loads synchronously.
- A failed load deletes every level it had loaded.

## Delete

- Deleting a map's last version removes the map folder. Delete shares the 2/s list throttle. Open sessions are
  unaffected (they live in memory).

## Download

- A transfer is one file: each level of the version gets its own transfer id, sent one after another, with
  `FileIndex`/`FileCount` on every chunk. The server reads a whole file (≤ 32 MiB) into memory when its turn comes.
- Two chunks in flight; acks must arrive in order (`Chunk == acked count`), anything else is ignored. 30 s without an
  ack drops the download.
- Ends: done (result event), client cancel, disconnect, the player's session membership ending (leave, kick, close;
  raised as `WFMappingMembershipEndedEvent`), a stall or a read error. Every end but disconnect sends
  `WFMappingDownloadCancelledEvent` so the client drops partial files.
- `WFMappingDownloadAssembler` is one transfer (one file); it validates the file name as a level file (`z{D}.yml`)
  so the client can't be made to write elsewhere. `WFMappingLibraryNames.DownloadPath` gives the client's target,
  `/Mapping/<MapName>/<version>/z<D>.yml`.
- Download requests: 1/s, one at a time per player; works from the lobby or a session.

## Not exercised

- No client exists yet (S3), so save, autosave, open, delete and download ran only through the build and a headless
  boot with the mode on (`wf_mappingmode_list` / `wf_mappingmode_save` on no sessions). Integration tests are S5.
