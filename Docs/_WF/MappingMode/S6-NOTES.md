# S6 notes: review fixes

Fixes for the ten findings in `REVIEW.md`, each with a Resolution line there. This file records the choices.

## Choices

- Tile guard (1): the stored tile change carries its tick and is cleared every tick. A placement claims it only on
  the same grid and cell, resolved like the engine's `PlaceNewTile` (the coordinates' own grid, else the grid found
  at the map position). MappingTools' history claims tile changes the same way, so a no-op placement can't put
  another session's tile into a mapper's undo either.
- Empty sessions (2): a session closes as soon as its last member leaves or is kicked, autosaving if dirty. The
  grace only covers sessions whose members are all away. `max_sessions_per_player` counts sessions the player owns
  other than the one they are in, since starting another ends that membership. As membership is one session per
  player, it is a backstop more than a live limit. Mapping admins are exempt.
- Decals (3): pastes check every added decal's cell against `max_decals_per_tile`, and a copy or paste carrying more
  than `max_edit_decals` is refused. No per-session decal total: a paste is the only way to multiply decals, and the
  per-cell cap bounds it. Moves and mirrors carry decals without a per-cell check; they don't add any.
- Reference size (4): ships under `/SharedMaps` top out at 1.2 MB (`_WF/Encounters/jupiter.yml`); `/Maps` has 31
  files over 1 MB and 12 over 3 MiB. A 3 MiB default would still admit `packed`, `loop`, `elkridge`, `train`,
  `omega` and `saltern`, so the default is 1.5 MiB: every ship gets in with headroom, 21 of the 31 `/Maps` files over
  1 MiB are refused, and of the stations only `centcomm` and `saltern` remain, which the entity and grid caps then
  refuse straight after the load if they are over. The same cap applies to a player's own library levels. The load stays synchronous; `reference_cooldown`
  (2 s, server-wide) refuses any pick until that long after the last load, so loads can't run back to back. Pending
  references of other members count against the caps, at the load and at the confirm.
- Tool work (5): MappingTools fills in the request's cells, touched entities (contents included) and decals on
  `WFMappingCanEditEvent`, and MappingMode refuses past the cvars, with the usual popup and status line. A delete or
  cut that covers a whole grid saves the grid as one root, so it is checked by entities only: Delete the grid under
  me keeps working for grids over 64 x 64. Area erase checks the rect size and the entities in it before the engine
  acts. History limits come from the same event with a new kind, `Limits`; MappingTools trims after every push and
  every same-tick merge, always keeping the newest edit. Bytes are estimated from the saved data nodes (key and
  value lengths plus a little per node), not by writing YAML. Admins keep 100 steps and no byte budget.
- Undo restore: every group of one apply now loads onto one holding map, so a merged area erase no longer makes a
  map per entity. Erasures are still saved one group per entity as the engine raises them; with the 2,000-entity
  erase cap that stays bounded.
- Undo caps (6): a step passes the entities it would add, net of those it deletes, so swapping steps aren't refused
  at the cap. A step whose maps are all gone raises no check, as before.
- Kicks (8): a kick by the owner or by `wf_mappingmode_kick` bans the player from that session for its life; there
  is no unkick. Mapping admins can still join. New code is a confirm button for the owner, shares the kick throttle
  and keeps every member in.
- Per tile (9): counts use the world position of everything the lookup finds on the cell, whatever the parent.
  Container contents still aren't counted. Pastes still pass one cell per root, so a pasted root's children are
  not added to the count. Mappers can't place entities off a grid; tiles off a grid still make one.
- Verbs (10): the marked block checks `ISharedAdminManager.IsAdmin(user)`. A de-adminned admin counts as a mapper.
- Votes: `vote.autovote_enabled`, `vote.map_enabled` and `vote.preset_enabled` are forced off with the other cvars,
  which covers both automatic and player-started map and preset votes. Admins can still start a vote. No
  `VoteManager` edit was needed. Like the other forced cvars, they aren't restored when the mode is turned off.
- `colornetwork` (7): only the missing `return`; mappers don't get the command on their own maps.

## Not covered

- No test for the verb change, the vote cvars or the history byte budget. The new-code button was built and
  sandbox-checked but not clicked in a client.
