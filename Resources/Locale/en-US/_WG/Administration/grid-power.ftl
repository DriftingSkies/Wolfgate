# Wolfgate grid power: SS13's "Make all areas powered/unpowered"

## Window
wg-admin-tab-grid-power = Grid Power
wg-grid-power-title = Grid Power
wg-grid-power-info = Unpowered drains every SMES, substation and APC battery and switches APC breakers off. Crews recover by flipping breakers back on. Powered fills them and turns everything back on.
wg-grid-power-all-label = All grids:
wg-grid-power-all-powered = All areas powered
wg-grid-power-all-unpowered = All areas unpowered
wg-grid-power-powered = Powered
wg-grid-power-unpowered = Unpowered
wg-grid-power-confirm = Confirm?
wg-grid-power-search = Search grids or maps...
wg-grid-power-announce = Announce
wg-grid-power-refresh = Refresh
wg-grid-power-row = {$map} | APC breakers on: {$on}/{$apcs} | Batteries: {$batteries} | Stored: {$percent}%
wg-grid-power-count = Showing {$shown} of {$total} grids
wg-grid-power-empty = No grids with power equipment found.

## Announcements (SS13 wording)
wg-grid-power-target-all = the sector
wg-grid-power-off-sender = Critical Power Failure
wg-grid-power-on-sender = Power Systems Nominal
wg-grid-power-off-announcement = Abnormal activity detected in {$target}'s powernet. As a precautionary measure, its power will be shut off for an indeterminate duration.
wg-grid-power-on-announcement = Power has been restored to {$target}. We apologize for the inconvenience.

## gridpower command
cmd-gridpower-desc = SS13's "Make all areas powered/unpowered" for one grid or every grid.
cmd-gridpower-help = Usage: {$command} <on|off> <all|grid entity> [announce: true/false]
cmd-gridpower-invalid-args = Expected on or off, a grid entity or "all", and optionally true or false for the announcement.
cmd-gridpower-invalid-grid = "{$grid}" isn't a grid.
cmd-gridpower-done = {$state} {$count} grids.
cmd-gridpower-powered = Powered
cmd-gridpower-unpowered = Unpowered
cmd-gridpower-hint-state = <on|off>
cmd-gridpower-hint-target = <all|grid entity>
cmd-gridpower-hint-announce = [announce: true/false]
