# The Remade in the open sector.

wg-remade-sector-hint-sender = #$!@$ Hymn %$^$#
wg-remade-sector-hint-1 = ...~... nobody is meant to hear this ...~... a dark hull out past {$x}, {$y}, still humming ...~...
wg-remade-sector-hint-2 = ...join the song... a quiet thing near {$x}, {$y} is waiting for hands... join the song...
wg-remade-sector-hint-3 = ...~... whoever takes it will not put it down ...~... {$x}, {$y}, give or take ...~...

wg-remade-encounter-hulk-name = Dark hull {$designation}
wg-remade-encounter-adrift-name = Disabled freighter {$designation}
wg-remade-encounter-adrift-announce = {$name}, dead in space. The power is gone and the crew are sick. Something is wrong with the walls. Anyone close, please come.

wg-remade-choir-title = Chorister
wg-remade-choir-title-leader = Choirmaster

wg-remade-choir-zone-warn-1 = {$intruder}, you are {$distance} metres off a quiet hull. Turn away. It is not for you.
wg-remade-choir-zone-warn-2 = {$intruder}, this is the Choir's. Open the range or you will be sung out.
wg-remade-choir-zone-warn-3 = Last warning, {$intruder}. Leave it be.
wg-remade-choir-zone-attack = {$intruder}, you came for it anyway. Join the song.

wg-remade-sector-reason-wild = a wild Scion is already out there
wg-remade-sector-reason-limit = the sector already holds its limit of Scions
wg-remade-sector-reason-placed = one was placed lately
wg-remade-sector-reason-gone = one was lost lately

wg-remade-signal-name = Signal: {$stage}
wg-remade-signal-adrift = Adrift
wg-remade-signal-stage-Quiet = faint hum
wg-remade-signal-stage-Hymn = hymn
wg-remade-signal-stage-Growth = rising hymn
wg-remade-signal-stage-Harvest = loud hymn
wg-remade-signal-stage-Rapture = roaring hymn
wg-remade-signal-stage-Crescendo = Crescendo

ent-WGMobChoirist = chorister
    .desc = Robed, hooded and humming. It has been told to mind the dark thing in the hold, and it does.

cmd-wg_remade-desc = Places Monolith sites, sets a Scion's resonance, lists the Scions, breaks out on a ship or wipes the Remade.
cmd-wg_remade-help = Usage: {$command} spawn [site] [here] | resonance <value> [scion uid] | status | outbreak [scion uid] | clear | lockdown <player or uid> [seconds] [range]
cmd-wg_remade-hint-sub = Subcommand
cmd-wg_remade-hint-site = Site prototype
cmd-wg_remade-hint-here = Place it 400 m from you
cmd-wg_remade-hint-value = Resonance, 0 to 100
cmd-wg_remade-hint-scion = Scion uid
cmd-wg_remade-no-site = No site called {$site}.
cmd-wg_remade-no-sites = There is no discovery site to place.
cmd-wg_remade-no-body = You need a body on a map to place a site beside you.
cmd-wg_remade-spawn-failed = Could not place {$site}: no clear space near a player, or the encounter would not start.
cmd-wg_remade-spawned = Placed {$site} as encounter {$encounter}. Its Scion appears once the hull is real.
cmd-wg_remade-bad-value = {$value} is not a resonance.
cmd-wg_remade-no-scion = {$scion} is not a Scion.
cmd-wg_remade-none = There is no Scion to set.
cmd-wg_remade-resonance = Set {$scion} to {$value} resonance. Its stage follows within a second.
cmd-wg_remade-status-header = Scions: {$scions}, wild: {$wild}. New wild Scion allowed: {$can}.
cmd-wg_remade-status-scion = {$scion}: {$stage} at {$resonance}, {$state}, grid {$grid}
cmd-wg_remade-status-yes = yes
cmd-wg_remade-cleared = Cleared {$count} Remade entities: stones, shards, Remade, growths, corruption, Signals and sounds. Every client was told to stop.
cmd-wg_remade-outbreak = Broke out on another crewed ship.
cmd-wg_remade-hint-target = Player or entity uid
cmd-wg_remade-hint-seconds = Seconds the doors stay bolted, 60 by default
cmd-wg_remade-hint-range = Metres round them, 9 by default
cmd-wg_remade-no-target = No player or entity called {$target}.
cmd-wg_remade-bad-lockdown = Seconds and range must be positive numbers.
cmd-wg_remade-locked-down = Bolted {$count} doors for {$seconds} s and sounded the alarm.
cmd-wg_remade-lockdown-none = No powered, unbolted door in range: nothing locked down.
cmd-wg_remade-outbreak-failed = No crewed ship to break out on: none off the stone's own grid, outside a sanctuary, with an unseen vent near someone.
cmd-wg_remade-state-none = untracked
cmd-wg_remade-state-wild = wild
cmd-wg_remade-state-disturbed = disturbed
cmd-wg_remade-state-quiet = quiet
