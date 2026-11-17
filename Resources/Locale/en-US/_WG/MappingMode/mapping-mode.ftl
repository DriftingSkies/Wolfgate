## Sessions

wg-mapping-mode-map-name = Mapping: {$name}
wg-mapping-mode-admin-map-name = Mapping mode admin map
wg-mapping-mode-off = Mapping mode is off.
wg-mapping-mode-no-round = Mapping mode is on, so no round starts.
wg-mapping-mode-not-in-lobby = You have to be in the lobby to do that.
wg-mapping-mode-bad-name = Names are 1 to {$max} letters, digits, spaces, dashes or underscores.
wg-mapping-mode-too-many-sessions = The server is at its limit of {$max} mapping sessions.
wg-mapping-mode-no-session = No session has that code.
wg-mapping-mode-session-full = That session is full ({$max} members).
wg-mapping-mode-created = Created "{$name}". Its join code is {$code}.
wg-mapping-mode-joined = Joined "{$name}".
wg-mapping-mode-left = You left the session.
wg-mapping-mode-kicked = You were removed from "{$name}".
wg-mapping-mode-kick-not-owner = Only the session owner can remove members.
wg-mapping-mode-kick-not-member = That player isn't another member of your session.
wg-mapping-mode-closed = The session "{$name}" was closed.
wg-mapping-mode-owner-now = You now own "{$name}".
wg-mapping-mode-too-many-own-sessions = You can own {$max ->
    [one] one mapping session
   *[other] {$max} mapping sessions
} at a time.
wg-mapping-mode-kicked-no-rejoin = You were removed from "{$name}" and can't join it again.
wg-mapping-mode-new-code = The join code is now {$code}. The old one no longer works.
wg-mapping-mode-new-code-not-owner = Only the session owner can change the join code.

wg-mapping-mode-opened = Opened "{$name}" {$version}. Its join code is {$code}.

## Library

wg-mapping-mode-saved = Saved "{$name}" as {$version}.
wg-mapping-mode-save-failed = The map couldn't be saved.
wg-mapping-mode-save-too-soon = Wait a few seconds between saves.
wg-mapping-mode-autosave-failed = Autosave of "{$name}" failed.
wg-mapping-mode-autosave-not-owner = Only the session owner can change autosave.
wg-mapping-mode-library-full-maps = Your library is full ({$max} maps). Delete a map first.
wg-mapping-mode-library-full-bytes = Your library is full ({$max} MiB). Delete some versions first.
wg-mapping-mode-library-not-found = That map or version isn't in your library.
wg-mapping-mode-library-read-failed = Your library couldn't be read.
wg-mapping-mode-open-failed = "{$name}" couldn't be opened.
wg-mapping-mode-deleted-map = Deleted "{$name}".
wg-mapping-mode-deleted-version = Deleted {$version} of "{$name}".
wg-mapping-mode-delete-failed = That couldn't be deleted.

## Downloads

wg-mapping-mode-download-busy = A download is already running.
wg-mapping-mode-download-done = Downloaded "{$name}" {$version} ({$count ->
    [one] 1 file
   *[other] {$count} files
}).
wg-mapping-mode-download-failed = The download failed.
wg-mapping-mode-download-too-large = A level is over the {$max} MiB download limit.
wg-mapping-mode-download-stalled = The download stopped responding and was dropped.
wg-mapping-mode-download-cancelled = The download was cancelled.
wg-mapping-mode-download-empty = {$file} is empty on the server, so it wasn't sent. Open the map and save it again.

## Limits and refusals

wg-mapping-mode-not-your-map = That isn't your session's map.
wg-mapping-mode-limit-area = Outside the {$size} × {$size} map limit.
wg-mapping-mode-limit-grids = Grid limit reached ({$max}).
wg-mapping-mode-limit-entities = Entity limit reached ({$max}).
wg-mapping-mode-limit-entities-per-tile = Too many entities on that tile (limit {$max}).
wg-mapping-mode-limit-decals-per-tile = Too many decals on that tile (limit {$max}).
wg-mapping-mode-limit-levels = Level limit reached ({$max}).
wg-mapping-mode-limit-prototype = That can't be placed in mapping mode.
wg-mapping-mode-bad-tile = Unknown tile.
wg-mapping-mode-erase-refused = That can't be erased.
wg-mapping-mode-erase-area-tools = Use Erase mode to erase an area.
wg-mapping-mode-denied-stripped = Removed {$count ->
    [one] 1 thing
   *[other] {$count} things
} that can't be used in mapping mode.
wg-mapping-mode-limit-selection = That covers more than {$max} tiles at once.
wg-mapping-mode-limit-edit-entities = That touches more than {$max} entities at once.
wg-mapping-mode-limit-edit-decals = That carries more than {$max} decals at once.
wg-mapping-mode-limit-off-grid = Place things on a grid. Lay a tile first.

## Reference grids

wg-mapping-mode-reference-not-listed = That file can't be loaded as a reference.
wg-mapping-mode-reference-too-large = That file is over the {$max} MiB reference limit.
wg-mapping-mode-reference-failed = That file couldn't be loaded.
wg-mapping-mode-reference-empty = That file has no grids.
wg-mapping-mode-reference-expired = The reference wasn't placed in time and was dropped.
wg-mapping-mode-reference-busy = Another reference is loading. Try again in a moment.
wg-mapping-mode-reference-preview-busy = Too many previews at once. Pick it again in a moment.
wg-mapping-mode-reference-placed = Placed {$count ->
    [one] the reference grid
   *[other] {$count} reference grids
}.

## Admin commands

cmd-wg_mappingmode-no-session = No mapping session has the code {$code}.
cmd-wg_mappingmode-hint-code = <session code>
cmd-wg_mappingmode-hint-player = <player name>
cmd-wg_mappingmode-hint-prototype = <entity prototype>

cmd-wg_mappingmode_list-desc = Lists mapping sessions with their codes, owners, members and maps.
cmd-wg_mappingmode_list-help = Usage: {$command}
cmd-wg_mappingmode_list-none = No mapping sessions.
cmd-wg_mappingmode_list-absent = {$name} (away)
cmd-wg_mappingmode_list-entry = {$code} "{$name}" owner {$owner}; members: {$members}; maps {$maps}; unsaved changes {$dirty ->
    [yes] yes
   *[no] no
}; last saved {$saved}

cmd-wg_mappingmode_tp-desc = Takes you to a mapping session's ground level.
cmd-wg_mappingmode_tp-help = Usage: {$command} <code>
cmd-wg_mappingmode_tp-no-entity = You need to be in game (observe first).

cmd-wg_mappingmode_close-desc = Autosaves a mapping session to its owner's library and closes it.
cmd-wg_mappingmode_close-help = Usage: {$command} <code>
cmd-wg_mappingmode_close-closed = Closed mapping session {$code}.

cmd-wg_mappingmode_kick-desc = Removes a player from their mapping session.
cmd-wg_mappingmode_kick-help = Usage: {$command} <player>
cmd-wg_mappingmode_kick-no-player = No player called {$name}.
cmd-wg_mappingmode_kick-not-member = {$name} isn't in a mapping session.
cmd-wg_mappingmode_kick-kicked = Removed {$name} from mapping session {$code}.

cmd-wg_mappingmode_save-desc = Saves a mapping session to its owner's library.
cmd-wg_mappingmode_save-help = Usage: {$command} <code>
cmd-wg_mappingmode_save-saved = Saved mapping session {$code}.
cmd-wg_mappingmode_save-failed = Couldn't save mapping session {$code}.

cmd-wg_mappingmode_purge-desc = Deletes every entity of one prototype on a mapping session's maps.
cmd-wg_mappingmode_purge-help = Usage: {$command} <code> <prototype>
cmd-wg_mappingmode_purge-no-prototype = No entity prototype called {$prototype}.
cmd-wg_mappingmode_purge-purged = Deleted {$count} {$prototype} from mapping session {$code}.

## Lobby

wg-mapping-mode-lobby-title = Mapping workshop
wg-mapping-mode-lobby-intro = Start a map, or join someone else's with their join code. Maps save to your library on this server.
wg-mapping-mode-lobby-new = New map
wg-mapping-mode-lobby-new-placeholder = Map name
wg-mapping-mode-lobby-create = Create
wg-mapping-mode-lobby-join = Join a session
wg-mapping-mode-lobby-join-placeholder = Join code
wg-mapping-mode-lobby-join-button = Join
wg-mapping-mode-lobby-sessions = Your sessions
wg-mapping-mode-lobby-sessions-none = You aren't in a session.
wg-mapping-mode-lobby-session-entry = "{$name}", owner {$owner}, {$count ->
    [one] 1 member
   *[other] {$count} members
}
wg-mapping-mode-lobby-rejoin = Rejoin
wg-mapping-mode-lobby-maps = My maps
wg-mapping-mode-lobby-refresh = Refresh
wg-mapping-mode-observe = Observe
wg-mapping-mode-observe-tooltip = Admins only. Puts you on a private admin map; use tpto or wg_mappingmode_tp to reach a session.

## Library list

wg-mapping-mode-library-loading = Loading your library...
wg-mapping-mode-library-empty = No saved maps yet.
wg-mapping-mode-library-never = never
wg-mapping-mode-library-usage = {$maps}/{$maxMaps} maps, {$used}/{$max} MiB
wg-mapping-mode-library-map-info = {$count ->
    [one] 1 version
   *[other] {$count} versions
}, last saved {$saved}
wg-mapping-mode-library-version = {$version}, {$saved}, {$kind}, {$levels ->
    [one] 1 level
   *[other] {$levels} levels
}, {$size} MiB
wg-mapping-mode-kind-manual = saved
wg-mapping-mode-kind-auto = autosave
wg-mapping-mode-library-open = Open
wg-mapping-mode-library-open-latest-tooltip = Open the newest version as a new session.
wg-mapping-mode-library-download = Download
wg-mapping-mode-library-download-latest-tooltip = Download the newest version to your computer.
wg-mapping-mode-library-versions = Versions
wg-mapping-mode-library-hide-versions = Hide versions
wg-mapping-mode-library-delete = Delete
wg-mapping-mode-library-delete-map-tooltip = Delete this map and every version of it.

## Download progress

wg-mapping-mode-download-progress = Downloading "{$name}" {$version}: file {$file} of {$files}, {$percent}%
wg-mapping-mode-download-ready = Downloaded "{$name}" {$version}.
wg-mapping-mode-download-cancel = Cancel
wg-mapping-mode-download-open-folder = Open folder
wg-mapping-mode-download-bad-data = The download arrived damaged and was stopped.
wg-mapping-mode-download-write-failed = The download couldn't be written to your data folder.
wg-mapping-mode-download-verify-failed = {$file} came out a different size from the download, so the download was dropped.
wg-mapping-mode-download-target = Downloads go to
wg-mapping-mode-download-target-data = Data folder
wg-mapping-mode-download-target-save-to = Save to...
wg-mapping-mode-download-target-tooltip = Data folder puts every file in Mapping/<map>/<version>/ of the game's data folder, with Open folder. Save to... asks where to save each file; an export is one zip, so it asks once. The game can only write outside its data folder through that dialog.
wg-mapping-mode-download-asking = Save as {$file} ({$kind}, file {$index} of {$count}): type the whole name, the dialog doesn't add the extension.
wg-mapping-mode-download-kind-map = the map
wg-mapping-mode-download-kind-level = one level
wg-mapping-mode-download-kind-grid = the ship's grid
wg-mapping-mode-download-kind-vessel = the ship's vessel prototype
wg-mapping-mode-download-kind-zip = the export package
wg-mapping-mode-download-saved-row = Saved {$saved} of {$count} files of "{$name}" {$version}.
wg-mapping-mode-download-saved = Saved {$count ->
    [one] 1 file
   *[other] {$count} files
}.
wg-mapping-mode-download-save-skipped = Saved {$saved} of {$count} files; the others were cancelled in the save dialog.
wg-mapping-mode-download-save-failed = {$file} couldn't be saved there.

## Mapping window

wg-mapping-mode-escape-button = Mapping
wg-mapping-mode-menu-title = Mapping
wg-mapping-mode-menu-tab-session = Session
wg-mapping-mode-menu-tab-grids = Grids
wg-mapping-mode-menu-tab-reference = Reference
wg-mapping-mode-menu-tab-save = Save
wg-mapping-mode-menu-tab-library = Library
wg-mapping-mode-menu-no-session = You aren't in a mapping session.
wg-mapping-mode-menu-return = Return to lobby
wg-mapping-mode-menu-return-tooltip = Leave the admin map and go back to the mapping lobby.
wg-mapping-mode-menu-code = Join code: {$code}
wg-mapping-mode-menu-copy = Copy
wg-mapping-mode-menu-copied = Copied the join code.
wg-mapping-mode-menu-new-code = New code
wg-mapping-mode-menu-new-code-tooltip = Replace the join code. The old one stops working; members stay in.
wg-mapping-mode-menu-status = Grids {$grids}/{$maxGrids} · Entities {$entities}/{$maxEntities} · Area {$size} × {$size}
wg-mapping-mode-menu-testing = Testing: {$testers} ({$minutes} min left)
wg-mapping-mode-menu-members = Members
wg-mapping-mode-menu-member = {$name}{$owner ->
    [yes] {" "}(owner)
   *[no] {""}
}{$away ->
    [yes] {" "}(away)
   *[no] {""}
}
wg-mapping-mode-menu-kick = Remove
wg-mapping-mode-menu-autosave = Autosave
wg-mapping-mode-menu-autosave-off = Off
wg-mapping-mode-menu-autosave-step = Every {$minutes ->
    [one] minute
   *[other] {$minutes} minutes
}
wg-mapping-mode-menu-autosave-owner-only = Only the owner can change this.
wg-mapping-mode-menu-delete-grid = Delete the grid under me
wg-mapping-mode-menu-delete-grid-tooltip = Shows the grid you're over in red and asks before deleting it. Undo brings it back.
wg-mapping-mode-menu-delete-grid-none = You aren't over a grid.
wg-mapping-mode-menu-delete-grid-too-large = That grid is too big to delete at once. Select and delete it in parts.
wg-mapping-mode-menu-delete-grid-row = Delete
wg-mapping-mode-menu-delete-grid-row-tooltip = Takes you to this grid and shows it in red before you confirm.
wg-mapping-mode-menu-delete-grid-preview = Delete {$name}? {$tiles} tiles and {$entities} entities go with it.
wg-mapping-mode-menu-delete-grid-preview-unknown = Delete this grid? Everything on it goes with it.
wg-mapping-mode-menu-delete-grid-preview-hint = Red tiles and boxes are what gets deleted. Undo brings it back.
wg-mapping-mode-menu-delete-grid-confirm = Delete grid
wg-mapping-mode-menu-delete-grid-cancel = Cancel
wg-mapping-mode-menu-new-grid = New grid
wg-mapping-mode-menu-new-grid-tooltip = The next tiles you lay off every grid start a new grid. Otherwise they join the nearest grid.
wg-mapping-mode-menu-reference-hint = Pick a file and press Place. R turns it, click puts it down, Escape or right click drops it.
wg-mapping-mode-menu-reference-search = Search
wg-mapping-mode-menu-reference-place = Place
wg-mapping-mode-menu-reference-pick = Pick a file to preview it.
wg-mapping-mode-menu-reference-loading = Loading preview...
wg-mapping-mode-menu-reference-stats = {$grids ->
    [one] 1 grid
   *[other] {$grids} grids
}, {$width} by {$height} tiles, {$tiles} floor, {$entities} entities, {$size} MiB
wg-mapping-mode-menu-reference-source-saved = My library
wg-mapping-mode-menu-reference-source-ships = Ship grids
wg-mapping-mode-menu-reference-source-maps = Maps
wg-mapping-mode-menu-leave = Leave session
wg-mapping-mode-menu-leave-tooltip = Leave this session for good. Save first; you can only come back with the join code.
wg-mapping-mode-menu-save-name = Save as
wg-mapping-mode-menu-save-now = Save now
wg-mapping-mode-menu-save-hint = Saves go into your own library under this name.
wg-mapping-mode-menu-last-saved = Last saved {$time}.
wg-mapping-mode-menu-never-saved = Not saved yet.
wg-mapping-mode-menu-next-autosave = Next autosave check in {$time}.
wg-mapping-mode-menu-autosave-disabled = Autosave is off.
wg-mapping-mode-menu-dirty = Unsaved changes.
wg-mapping-mode-menu-clean = No unsaved changes.

## Map limit overlay

wg-mapping-mode-bounds-label = {$size} × {$size} map limit

## Mode bar

wg-mapping-modebar-handle-tooltip = Drag to move the mode bar. It snaps to the edges of the view, and stands upright on a side edge.
wg-mapping-modebar-menu-screen = Screen
wg-mapping-modebar-menu-reset = Reset layout
wg-mapping-modebar-menu-reset-tooltip = Put the mode bar back in its default place, bottom left.

# Entities a map file leaves out, such as mobs: shown when one is placed and in the palette
wg-mapping-mode-unsaved-spawner = {$name} won't be saved with the map. Place {$spawner} instead.
wg-mapping-mode-unsaved-none = {$name} won't be saved with the map. Nothing spawns it yet; it needs a spawner prototype.
