# MappingMode R4 notes: shared templates

"Can you make uploaded templates show for everyone." A member shares the PNG template they hold with the whole
session; everyone sees it at the same place. What it does where the brief left room, and what was verified.

## Shape

- `WFMappingPngHeader` and `WFMappingTemplateSettings` (with `WFMappingTemplateLayer`) moved from the client's
  `Template/` to `Content.Shared/_WF/MappingMode`, unchanged but for a `Check` overload taking its own file cap and
  `ToState`/`ApplyState`/`Clamp`. The server uses both; neither decodes anything.
- Shared: `WFMappingTemplateEvents.cs` (`WFMappingTemplateState`, `WFMappingTemplateInfo`, the upload, download,
  transform and unshare events) and `WFMappingTemplateCVars.cs` (`template_max_bytes`, 8 MiB, replicated and clamped
  to the upload assembler's 8 MiB hard cap; `template_cooldown`, 10 s). They sit in their own cvar class so the
  shared `WFMappingModeCVars` file isn't touched.
- Server: `WFMappingTemplateShareSystem` holds the transfers; the template itself is `WFMappingSession.Template`
  (`WFMappingSessionTemplate`: bytes, size, sharer, placement, and which members' clients hold it).
  `WFMappingSessionInfo.Template` carries it in the session state.
- Client: `Template/WFMappingTemplateSystem.Share.cs` beside the existing system, which now keeps the player's own
  template and the shared one apart.

## Choices

- **Shared wins.** While a shared template is held it is the one drawn, moved and shown in the panel; the player's
  own stays loaded behind it (the panel says so), keeps saving to the data folder, and comes back when the shared one
  is removed. Load still loads the player's own; Share sends it. Clear only ever clears the player's own; Unshare
  removes the shared one for everyone.
- **What is shared.** Centre, scale X/Y, pixels per tile, rotation, layer and level. Pixels per tile isn't in the
  brief's list, but without it two members would see different sizes. Opacity, show, lock and Keep proportions stay
  per client; a newly shown shared template starts with the player's own values for them when they have a template.
- **Transfers.** Upload reuses `WFMappingUploadAssembler` both ways (128 KiB chunks, two in flight, acks in order).
  One share per player at a time; the cooldown is spent when the begin is accepted. A share is bound to the session
  the player was in at the begin and refused if they left it before the last chunk. The server checks signature and
  IHDR at the end against 8192 a side and 4096² pixels and the cvar cap; nothing is decoded on the server.
- **Delivery.** Pushed, not requested: every 0.25 s of game time the server looks for present members whose client
  doesn't hold the current template, keyed by the member's observer, so a rejoin (new observer) gets it again. The
  sharer is counted as holding it. A stalled delivery (30 s without an ack) is dropped and started again on the next
  check. A delivery stops when the member leaves, goes absent, disconnects, the template is replaced or unshared, or
  the session closes.
- **Placement.** Requests are sent at most 10 a second while dragging or editing (the server allows 15). The server
  clamps with `WFMappingTemplateSettings.Clamp`, the level to the session's existing levels, and sends the result to
  every present member except the sender, who gets it only when the clamp changed what it asked for. Session state
  sent later carries the current placement; the client takes it from there only for a template it hasn't seen, so a
  state message built before its own move can't snap it back. Incoming moves are ignored while the player drags; the
  drag's end sends its result.
- **Unsaved.** Sharing and unsharing resend the session state and mark the session changed; a move marks it changed
  too, so autosaves keep the template.
- **Saving.** Every save of a session (manual, autosave, close) writes `template.png` and `template.yml` (the shared
  placement, in the settings format) into the version folder after the levels, so `max_library_bytes` counts them; a
  failed write fails the save. Opening a version reads them back if the PNG passes the same checks, as shared by the
  player who opened it, clamped to the opened levels. Downloads and uploads never list or send them.
- **Messages.** Share, unshare and refusals reach the player as the usual result popups; the other present members
  are told who shared or removed a template. Strings are in `mapping-template.ftl`.

## Verified

- Builds of `Content.Server`, `Content.Client`, `Content.Tests` and `Content.IntegrationTests` in DebugOpt with
  nullable warnings as errors.
- Unit tests: `WFMappingTemplateSettingsTest` gains the file cap overload, the state round trip and `Clamp`.
- `WFMappingTemplateShareTest`: the real client loads and shares a 1×1 PNG; the server holds the bytes, the session
  state carries it, the dummy member's delivery starts; a move far past every limit is clamped and reaches the client;
  a non-PNG, a 9000-pixel IHDR and an over-cap begin are refused; a save writes both files; the client's Unshare
  clears it everywhere and its own template shows again; opening the saved version restores it, and Unshare from the
  other member clears it and stops the delivery.

## Not exercised

- Two real clients dragging at once, and a large PNG over a real connection; the headless pair has one client.
