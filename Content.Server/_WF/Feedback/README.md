# Feedback

Player bug reports and suggestions. **Report a bug / Suggest** in the game menu (Esc) opens a window with three tabs:
a bug report form, a suggestion form, and **My reports**, the player's own reports with the status staff gave each
(Pending, Checked, Fixed, Non-Issue) and any message staff wrote back. When staff set a status the player gets a
notice in game: at once if they are online, otherwise when they next log in, and again each login until they close it.

The end of shift survey asks how much the player enjoyed the shift (1 to 5), whether they hit any issues and what
frustrated them. A small chip offers it when they go into cryo (sent by the server) and when they press Disconnect
or Quit in the game menu (leaving waits until they send it or turn it down). The chip has Give feedback, Not now (or
Leave now) and Never show again (`wf.feedback.survey_never`, client side). One survey a round; it is stored, listed,
answered and noticed like any report.

Every report is stored in `wolfgate_feedback_report` with a snapshot of the moment, built for whoever, or whatever,
goes to fix it. The SSymphony panel reads that table and sets statuses.

Entry points: `FeedbackSystem` (server: checks, limits, storing, statuses and notices; `FeedbackSystem.Context.cs`
builds the snapshot), `FeedbackLogBuffer` (the server's latest warnings and errors), `FeedbackUIController`,
`FeedbackWindow`, `FeedbackNoticeWindow`, `FeedbackSurveyChip` and `FeedbackSurveyWindow` (client), `FeedbackRules` (limits, categories, statuses) and
`SymphonyFeedbackSystem` (the panel's endpoints, in the Symphony module).

CVars: `wf.feedback.enabled`, `wf.feedback.screenshots`, `wf.feedback.cooldown` (seconds between reports, 60),
`wf.feedback.daily_limit` (20), `wf.feedback.surveys`, `wf.feedback.admin_notice` (tell online admins in chat).

## For the panel

### `wolfgate_feedback_report`

| Column | Type | |
|---|---|---|
| `wolfgate_feedback_report_id` | integer | The report's number, shown to the player as `#N`. |
| `kind` | varchar(16) | `bug`, `suggestion` or `survey`. |
| `category` | varchar(32) | `gameplay`, `ships`, `combat`, `medical`, `interface`, `map`, `visual`, `audio`, `performance` or `other`. |
| `severity` | varchar(16), null | `minor`, `major` or `blocking`, as the player rated a bug. Null for a suggestion. |
| `title` | varchar(100) | |
| `description` | text | Up to 4000 characters. |
| `steps` | text, null | How to make a bug happen again. |
| `expected` | text, null | What the player expected instead. |
| `rating` | integer, null | Surveys: enjoyment, 1 to 5. A survey's `description` is its issues answer and its title `Shift survey: N/5`. |
| `frustrations` | text, null | Surveys: what frustrated the player. |
| `player_user_id` | uuid | |
| `player_name` | varchar(64) | Account name when sent. |
| `created_at` | timestamptz | |
| `round_id` | integer | Joins `round` and `admin_log`. |
| `server_name` | varchar(128) | `game.hostname`. |
| `build_version` | varchar(128) | `build.version`: the commit a published build was made from. Empty on a dev build. |
| `context` | jsonb | The snapshot, below. |
| `screenshot` | bytea, null | JPEG (or PNG) of the player's screen, at most 768 KB and 1600 px on the long side. Leave it out of list queries. |
| `status` | varchar(16) | `pending`, `checked`, `fixed` or `non_issue`. |
| `staff_message` | text, null | Shown to the player with the status. Cut to 1000 characters when shown. |
| `status_changed_at` | timestamptz, null | |
| `notify_player` | boolean | True while the player has yet to see the status. The game clears it when they close the notice. |

The game inserts rows and, through the calls below, writes the last four columns. Nothing else is ever changed.

### Setting a status

`POST /symphony/feedback/status` with the admin API token (`Authorization: SS14Token <admin.api_token>`):

```json
{ "id": 12, "status": "fixed", "message": "Fixed in tonight's build.", "notify": true, "actor": "Gandalf" }
```

`message`, `notify` (default true) and `actor` (for the server log) are optional. The message replaces the last one;
leave it out for none. Answers `{ "symphony_module", "id", "status", "notify", "player_online" }`, 404 for an unknown
number, 400 for a body it can't read. `player_online` is whether the player is in game now, notified or not. With
`notify` the player is shown the notice now if online, or at login.

With the server down, write `status`, `staff_message`, `status_changed_at` and `notify_player = true` on the row
instead. Players see it at their next login. If the server is up when the row is written this way,
`POST /symphony/feedback/refresh` with `{ "users": ["<uuid>", ...] }` (or no body or `{}`, for everyone online) shows it
now and answers `{ "symphony_module", "online" }`. Malformed JSON, an empty `users` list or a bad id is a 400, never a
refresh of everyone.

`symphony_module` in `/status` is 8 or more on a build with all of this.

### `context`

`schema` is 1. Any part can be `{ "error": "..." }` if building it threw; the report is stored regardless.

- `build`: `fork_id`, `version`, `engine_version`, `manifest_hash`, `symphony_module`, `test_merges` (as in `/status`).
- `server`: `name`, `time_utc`, `uptime_seconds`, `tick`, `tick_rate`, `players`, `round_id`, `run_level`,
  `round_seconds` (null in the lobby), `preset`, `map`, `stations`, `exceptions` (caught since boot).
- `reporter`: `user_id`, `name`, `state` (`lobby`, `ready`, `joining`, `playing`, `ghost`, as `/symphony/players`),
  `ping`, `connected_utc`, `playtime_minutes`, `admin`, `admin_active`, `admin_title`.
- `character`: null in the lobby. Otherwise the body the player is in: `entity` (net id), `prototype`, `name`,
  `description`, `ghost`, `job`, `job_name`, `antag_roles`, `own_body` (when they are a ghost or possessing something),
  `species`, `mob_state`, `damage_total`, `damage` (by type), `buckled_to`, `pulling`, `pulled_by`, `hands`,
  `equipment` (by slot), `status_effects`, `open_uis` (entity and UI key), `containers` (what they are inside,
  innermost first), `components` (every component on the body) and `position`: `map_id`, `map`, `world`,
  `rotation_degrees`, `grid`, `grid_local`, `tile`, `tile_type`, `station`, `parent`, `air` (`pressure_kpa`,
  `temperature_k`, `moles`). Entities are `{ entity, prototype, name }`.
- `nearby`: null in the lobby. `range` (5 tiles), `prototypes` (how many kinds), `entities`: up to 80 of
  `{ prototype, count, nearest }`, nearest first.
- `server_log`: the server's last 40 warnings and errors, oldest first: `time_utc`, `level`, `sawmill`, `message`
  (with the exception and stack trace), `repeats`.
- `survey`: surveys only. `trigger` (`cryo` or `leaving`) and `rating`.
- `admin_log`: the last 40 admin log lines naming the player this round, oldest first: `time_utc`, `type`, `impact`,
  `message`. The rest is in `admin_log` under `round_id`.
- `client`: sent by the client and not checked. `trusted` (always false), `screenshot` (whether one was kept),
  `info` (`time_utc`, `build.*`, `state`, `locale`, `window.size`, `window.mode`, `fps`, `ping`, `ui.scale`,
  `ui.layout`, `ui.theme`, `ui.style`, `ui.focused`, `ui.windows`, `eye.*`, `entity`), and `log`: the client's last
  60 warnings and errors.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Feedback/FeedbackLogBuffer.cs`](FeedbackLogBuffer.cs)
- [`Content.Server/_WF/Feedback/FeedbackReportSummary.cs`](FeedbackReportSummary.cs)
- [`Content.Server/_WF/Feedback/FeedbackSystem.Context.cs`](FeedbackSystem.Context.cs)
- [`Content.Server/_WF/Feedback/FeedbackSystem.cs`](FeedbackSystem.cs)
- [`Content.Server/_WF/Feedback/ServerDbBase.Feedback.cs`](ServerDbBase.Feedback.cs)
- [`Content.Server/_WF/Feedback/ServerDbManager.Feedback.cs`](ServerDbManager.Feedback.cs)

### Shared

- [`Content.Shared/_WF/Feedback/FeedbackMessages.cs`](../../../Content.Shared/_WF/Feedback/FeedbackMessages.cs)
- [`Content.Shared/_WF/Feedback/FeedbackRules.cs`](../../../Content.Shared/_WF/Feedback/FeedbackRules.cs)

### Client

- [`Content.Client/_WF/Feedback/FeedbackClientLog.cs`](../../../Content.Client/_WF/Feedback/FeedbackClientLog.cs)
- [`Content.Client/_WF/Feedback/FeedbackEntryRow.cs`](../../../Content.Client/_WF/Feedback/FeedbackEntryRow.cs)
- [`Content.Client/_WF/Feedback/FeedbackNoticeWindow.xaml`](../../../Content.Client/_WF/Feedback/FeedbackNoticeWindow.xaml)
- [`Content.Client/_WF/Feedback/FeedbackNoticeWindow.xaml.cs`](../../../Content.Client/_WF/Feedback/FeedbackNoticeWindow.xaml.cs)
- [`Content.Client/_WF/Feedback/FeedbackSurveyChip.cs`](../../../Content.Client/_WF/Feedback/FeedbackSurveyChip.cs)
- [`Content.Client/_WF/Feedback/FeedbackSurveyWindow.xaml`](../../../Content.Client/_WF/Feedback/FeedbackSurveyWindow.xaml)
- [`Content.Client/_WF/Feedback/FeedbackSurveyWindow.xaml.cs`](../../../Content.Client/_WF/Feedback/FeedbackSurveyWindow.xaml.cs)
- [`Content.Client/_WF/Feedback/FeedbackSystem.cs`](../../../Content.Client/_WF/Feedback/FeedbackSystem.cs)
- [`Content.Client/_WF/Feedback/FeedbackUIController.cs`](../../../Content.Client/_WF/Feedback/FeedbackUIController.cs)
- [`Content.Client/_WF/Feedback/FeedbackUIController.MenuButton.cs`](../../../Content.Client/_WF/Feedback/FeedbackUIController.MenuButton.cs)
- [`Content.Client/_WF/Feedback/FeedbackWindow.xaml`](../../../Content.Client/_WF/Feedback/FeedbackWindow.xaml)
- [`Content.Client/_WF/Feedback/FeedbackWindow.xaml.cs`](../../../Content.Client/_WF/Feedback/FeedbackWindow.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Feedback/FeedbackTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Feedback/FeedbackTest.cs)

### Localization

- [`Resources/Locale/en-US/_WF/Feedback/feedback.ftl`](../../../Resources/Locale/en-US/_WF/Feedback/feedback.ftl)

### Textures

- [`Resources/Textures/_WF/Feedback/attributions.yml`](../../../Resources/Textures/_WF/Feedback/attributions.yml)
- [`Resources/Textures/_WF/Feedback/bug.png`](../../../Resources/Textures/_WF/Feedback/bug.png)

## Non-modular edits

- [`Content.Client/Lobby/UI/LobbyGui.xaml`](../../../Content.Client/Lobby/UI/LobbyGui.xaml): big green bug report and suggestion entry
- [`Content.Client/Lobby/UI/LobbyGui.xaml.cs`](../../../Content.Client/Lobby/UI/LobbyGui.xaml.cs): bug report and suggestion entry in the lobby
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): bug report and suggestion entry, green so players find it
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs)
  - bug report and suggestion entry in the game menu
  - offer the end of shift survey before leaving
  - offer the end of shift survey before quitting
- [`Content.Client/UserInterface/Systems/MenuBar/GameTopMenuBarUIController.cs`](../../../Content.Client/UserInterface/Systems/MenuBar/GameTopMenuBarUIController.cs): bug report button
- [`Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml`](../../../Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml): green bug report button beside ahelp
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - player bug reports and suggestions
  - counted by who sent it and when for the daily limit, and listed by status
  - player bug reports and suggestions, read by the SSymphony panel
- [`Content.Server/Content.Server.csproj`](../../Content.Server.csproj): a log handler keeps the latest warnings and errors for bug reports; the engine's interface takes Serilog's event

<!-- WOLFGATE-GENERATED END -->
