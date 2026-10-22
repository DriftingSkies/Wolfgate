# MappingMode phase 2, P2 notes

Assumptions and choices made while building P2 where PLAN2.md or the stage brief left room. Each names the file it
lives in. P2 touches no P1 or P3 file; it hooks in through `RegisterHandler`, the build mode events,
`AddDocked` and the screen's `TemplateHost` and `QuickBarHost`.

## Hooking in

- Both systems subscribe to `GameplayStateLoadController.OnScreenLoad`/`OnScreenUnload` themselves (no extra UI
  controller). On load of a `WFMappingScreen` the template system docks its panel into `TemplateHost` with
  `AddDocked(..., "wf-mapping-template-title")` and the quick-select system adds its bar to `QuickBarHost`. On unload
  both orphan what they added; P1's controller also orphans the docked slot, which is harmless twice.
- `WFMappingTemplateSystem.Initialize` registers the Template handler, so the rail's Template button and key 7 work
  once the system exists.

## Template (`Template/`)

- Position is the image's centre in world tiles, not a corner, so rotation and Ctrl+drag scaling pivot on it.
  Rotation is counter-clockwise degrees kept in (-180, 180] (`WFMappingTemplateSettings`).
- Scale is ScaleX and ScaleY (0.01 to 100) with a "Keep proportions" lock (`Uniform`, on by default). With it on,
  editing one field scales the other by the same factor, so a non-square ratio survives. Drags and the wheel always
  scale both by the same factor. Pixels per tile (1 to 1024, default 32) sets the size at scale 1.
- Load centres the image on the view (rounded to whole tiles) and records the current z-level as its depth; it is
  drawn only on maps at that depth (`CEZMapComponent.Depth`, 0 off any z-stack). Reset does the same, keeping the
  depth.
- Ctrl+wheel scales by 5 % a notch. Plain wheel can't: P1's `TryWheel` only passes the wheel to a handler while
  Ctrl is held, and the brief said P2 changes no P1 file.
- Ctrl+drag scales by the ratio of the mouse's distance from the centre now and at the press; Shift+drag turns by
  the angle swept about the centre. Ctrl wins when both are held. Right click or Escape during a drag puts it back.
- Locked stops dragging, the wheel and the arrows (each says so with a popup); the panel's fields stay editable, as a
  typed value is deliberate.
- The nudge key functions are added to `common` in `Enter` and removed in `Exit`; their `CommandBinds` are
  registered once under `WFMappingTemplateSystem` in `Initialize` (registering per Enter would log the engine's
  "already registered" warning if Exit were ever skipped). Nudges move along the world axes by one image pixel at
  the current scale, eight with Shift.
- Files: `/Mapping/Templates/<key>/template.png` and `settings.yml`, `<key>` from `WFMappingModeLimits.IsValidName`
  on the trimmed session name, else the hex of its UTF-8 bytes. The PNG is written once at load; settings at most
  once a second after a change, and on leaving the session. Clear deletes both files.
- The session is followed from `FrameUpdate`: whenever `SetUp` is true and the session's name differs from the one
  loaded, the old template is saved and dropped and the new name's folder is restored. So a renamed session shows
  the template saved under its new name (none, at first). `SessionEnded` saves and drops it.
- A restored file is checked against the same caps as a picked one; an unreadable or oversized saved file is
  skipped silently (logged at debug level).
- Load failures are popups at the cursor. The texture uses the default (nearest) sampling, so pixel art stays
  crisp when zoomed in.
- The overlay is added only while a texture exists. It draws in `WorldSpaceBelowWorld` (Under) or `WorldSpace`
  (Over) with the opacity as alpha; the outline is drawn in `WorldSpace` while Template mode is on, dimmed when
  locked, even when the image is hidden.

## Quick select (`QuickSelect/`)

- A slot holds a `WFMappingPaletteEntry` plus an optional label (`WFMappingQuickSlotEntry`). "Rename" on a slot sets
  the label shown in its tooltip; "Use the original name" clears it. The brief asked for remove or rename on right
  click; the plan's "move to page" is in the same menu.
- Caps: 10 sets, 10 pages per set, 10 slots per page, names 32 characters (trimmed, control characters dropped),
  ids 128 characters. Extras in a file are dropped; a bad slot reads as empty.
- "Malformed" means the text doesn't parse, the root isn't a mapping, or `sets` is missing or holds no set. Then the
  file is renamed to `/Mapping/quickselect.bad.yml` (replacing an older one) and the defaults are used. A missing
  file gives the defaults without writing until something changes.
- Defaults: every action in `/mapping_actions.yml` whose `event` has `entityType` or `tileId` and no `eraser: true`,
  in file order without repeats, ten to a page, in a set named by `wf-mapping-quickselect-default-set`. The event's
  `!type:` tag isn't checked; only `StartPlacementActionEvent` has those fields. The Space tile stays in.
- Additions go to the first empty slot of the shown page. An entry already on that page isn't added twice (the
  popup names its slot). A full page refuses with a popup.
- "Add picks" (stored as `addPicks`) adds every eyedropper pick to the shown page, silently, skipping repeats and
  stopping when the page is full.
- Drag: press on a slot and release over another to swap them; the press counts as a click only when released on
  the same slot within 6 UI pixels. This is done in the slot's own key handlers instead of `DragDropHelper`, which
  needs a per-frame update and a drag shadow the bar doesn't need.
- Deleting a set asks through a one-item menu; the last set and the last page of a set can't be deleted. A page tab
  is deleted from its right-click menu.
- Slots whose id is no longer known show no icon, a red border and a "no longer exists" tooltip, and clicking one
  says so instead of placing; they are kept.
- The slot whose entry is being placed has a highlighted border (follows `EntryChanged`).
- The store is read on first use (the bar's first build or the first addition) and written at most once a second
  after a change, and on shutdown.

## Not done here

- No tests: P4 owns them. `WFMappingPngHeader`, `WFMappingTemplateSettings` and `WFMappingQuickSelectStore` are pure
  and can be unit tested from `Content.Tests`.
- No README or generated docs: P5.
