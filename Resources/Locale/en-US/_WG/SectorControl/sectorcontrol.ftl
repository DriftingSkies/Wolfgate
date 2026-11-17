# Notices to a ship crossing a border; a faction may replace them with its own lines taking the same arguments
sector-control-notice-sender = Sector Watch
sector-control-notice-entered = WARNING: entering {$faction} controlled space ({$callsign}).
sector-control-notice-left = Leaving {$faction} controlled space. Now in open space at {$callsign}.

# Sector map: the top-bar button and key, the game menu entry and its window; distance is km from the sector centre
wg-sector-map-button = Sector map
wg-sector-map-button-tooltip = Open the sector map.
ui-options-function-open-sector-map = Open sector map
wg-sector-map-window-title = Sector map
wg-sector-map-centre-on-me = Centre on me
wg-sector-map-whole-sector = Whole sector
wg-sector-map-scale = {$width} km across
wg-sector-map-no-data = No territory data
wg-sector-map-hover-held = {$callsign} ({$distance} km): {$faction}
wg-sector-map-hover-open = {$callsign} ({$distance} km): open space
wg-sector-map-hover-contested = {$callsign} ({$distance} km): contested by {$faction}, {$time}
wg-sector-map-hover-challenged = {$callsign} ({$distance} km): held by {$holder}, challenged by {$faction}, {$time}
wg-sector-map-hover-disputed = {$callsign} ({$distance} km): disputed, {$faction} ships and a player ship are both there
wg-sector-map-hover-retaking = {$callsign} ({$distance} km): held by {$faction}, being retaken, {$time}
wg-sector-map-hover-clearing = {$callsign} ({$distance} km): clearing, drones return in {$time}
wg-sector-map-legend-clearing = Clearing: no drones yet
wg-sector-map-clearing-label = Clearing {$time}
wg-sector-map-legend-disputed = Disputed: timers paused
wg-sector-map-legend-retaking = Being retaken
wg-sector-map-retake-label = Retaking {$time}

# Sector map hover card for a blip; distance and bearing are from the player, or from the origin when they are not on the map
wg-sector-map-kind-station = Station
wg-sector-map-kind-ship = Ship
wg-sector-map-category-traffic = Traffic
wg-sector-map-category-patrol = Patrol
wg-sector-map-category-threat = Threat
wg-sector-map-category-distress = Distress
wg-sector-map-card-range = {$distance} km, bearing {$bearing}
wg-sector-map-card-range-core = {$distance} km from the sector core, bearing {$bearing}
wg-sector-map-card-held = {$callsign}: held by {$faction}
wg-sector-map-card-open = {$callsign}: open space
wg-sector-map-card-contested = Contested by {$faction}, {$time}
wg-sector-map-card-challenged = {$callsign}: held by {$holder}, challenged by {$faction}, {$time}
wg-sector-map-card-disputed = Disputed with {$faction}: timers paused
wg-sector-map-card-retaking = Being retaken, {$time}
wg-sector-map-card-clearing = Clearing, drones return in {$time}

# wg_sectormap command
cmd-wg_sectormap-desc = Opens or closes the sector map.
cmd-wg_sectormap-help = Usage: {$command}

# wg_sector command
cmd-wg_sector-desc = Shows and edits faction territory on the sector map.
cmd-wg_sector-help = Usage: {$command} status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | challenge <faction> <q> <r> <minutes> | clear [faction] | callsign <x> <y>
cmd-wg_sector-hint-sub = Subcommand
cmd-wg_sector-hint-faction = Faction id
cmd-wg_sector-hint-q = Cell q
cmd-wg_sector-hint-r = Cell r
cmd-wg_sector-hint-minutes = Minutes until the contest ends
cmd-wg_sector-hint-challenge-minutes = Minutes until the challenge ends
cmd-wg_sector-hint-x = Map x in metres
cmd-wg_sector-hint-y = Map y in metres
cmd-wg_sector-status-header = Sector control: enabled {$enabled}, map {$map}, cell size {$size} m.
cmd-wg_sector-status-faction = {$faction} ({$name}): {$cells} cells {$callsigns}
cmd-wg_sector-status-contest = Contested {$callsign} by {$faction}, {$seconds} s left.
cmd-wg_sector-status-challenge = Challenged {$callsign} by {$faction}, held by {$holder}, {$seconds} s left.
cmd-wg_sector-status-retake = Retaking {$callsign}, {$seconds} s left.
cmd-wg_sector-claimed = {$faction} now holds {$callsign}.
cmd-wg_sector-claim-failed = {$faction} cannot claim {$callsign}.
cmd-wg_sector-freed = {$callsign} is free.
cmd-wg_sector-not-held = {$callsign} is not held or contested.
cmd-wg_sector-contested = {$faction} contests {$callsign}.
cmd-wg_sector-contest-failed = {$faction} cannot contest {$callsign}.
cmd-wg_sector-challenged = {$faction} challenges {$callsign}.
cmd-wg_sector-challenge-failed = {$faction} cannot challenge {$callsign}.
cmd-wg_sector-bad-minutes = Not a number of minutes: {$arg}
cmd-wg_sector-cleared = Cleared {$count} cells.
cmd-wg_sector-bad-position = Not a position: {$x} {$y}
cmd-wg_sector-callsign = {$callsign} is cell ({$q}, {$r}), centre at {$x} {$y}.
cmd-wg_sector-unknown-faction = Unknown faction: {$faction}
cmd-wg_sector-bad-cell = Not a cell: {$q} {$r}
