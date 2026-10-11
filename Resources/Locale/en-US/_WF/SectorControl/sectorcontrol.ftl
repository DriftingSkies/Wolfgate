# Notices to a ship crossing a border; a faction may replace them with its own lines taking the same arguments
sector-control-notice-sender = Sector Watch
sector-control-notice-entered = WARNING: entering {$faction} controlled space ({$callsign}).
sector-control-notice-left = Leaving {$faction} controlled space. Now in open space at {$callsign}.

# Sector map: the top-bar button and key, the game menu entry, its window and the shuttle console mode
wf-sector-map-button = Sector map
wf-sector-map-button-tooltip = Open the sector map.
ui-options-function-open-sector-map = Open sector map
wf-sector-map-window-title = Sector map
wf-sector-map-mode = SECTOR
wf-sector-map-centre-on-me = Centre on me
wf-sector-map-whole-sector = Whole sector
wf-sector-map-scale = {$width} km across
wf-sector-map-ring = {$km} km
wf-sector-map-no-data = No territory data
wf-sector-map-hover-held = {$callsign}: {$faction}
wf-sector-map-hover-open = {$callsign}: open space
wf-sector-map-hover-contested = {$callsign}: contested by {$faction}, {$time}

# wf_sectormap command
cmd-wf_sectormap-desc = Opens or closes the sector map.
cmd-wf_sectormap-help = Usage: {$command}

# wf_sector command
cmd-wf_sector-desc = Shows and edits faction territory on the sector map.
cmd-wf_sector-help = Usage: {$command} status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | clear [faction] | callsign <x> <y>
cmd-wf_sector-hint-sub = Subcommand
cmd-wf_sector-hint-faction = Faction id
cmd-wf_sector-hint-q = Cell q
cmd-wf_sector-hint-r = Cell r
cmd-wf_sector-hint-minutes = Minutes until the contest ends
cmd-wf_sector-hint-x = Map x in metres
cmd-wf_sector-hint-y = Map y in metres
cmd-wf_sector-status-header = Sector control: enabled {$enabled}, map {$map}, cell size {$size} m.
cmd-wf_sector-status-faction = {$faction} ({$name}): {$cells} cells {$callsigns}
cmd-wf_sector-status-contest = Contested {$callsign} by {$faction}, {$seconds} s left.
cmd-wf_sector-claimed = {$faction} now holds {$callsign}.
cmd-wf_sector-claim-failed = {$faction} cannot claim {$callsign}.
cmd-wf_sector-freed = {$callsign} is free.
cmd-wf_sector-not-held = {$callsign} is not held or contested.
cmd-wf_sector-contested = {$faction} contests {$callsign}.
cmd-wf_sector-contest-failed = {$faction} cannot contest {$callsign}.
cmd-wf_sector-bad-minutes = Not a number of minutes: {$arg}
cmd-wf_sector-cleared = Cleared {$count} cells.
cmd-wf_sector-bad-position = Not a position: {$x} {$y}
cmd-wf_sector-callsign = {$callsign} is cell ({$q}, {$r}), centre at {$x} {$y}.
cmd-wf_sector-unknown-faction = Unknown faction: {$faction}
cmd-wf_sector-bad-cell = Not a cell: {$q} {$r}
