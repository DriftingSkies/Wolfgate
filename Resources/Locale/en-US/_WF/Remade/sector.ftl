# The Remade in the open sector.

wf-remade-sector-hint-sender = #$!@$ Hymn %$^$#
wf-remade-sector-hint-1 = ...~... nobody is meant to hear this ...~... a dark hull out past {$x}, {$y}, still humming ...~...
wf-remade-sector-hint-2 = ...join the song... a quiet thing near {$x}, {$y} is waiting for hands... join the song...
wf-remade-sector-hint-3 = ...~... whoever takes it will not put it down ...~... {$x}, {$y}, give or take ...~...

wf-remade-encounter-hulk-name = Dark hull {$designation}
wf-remade-encounter-adrift-name = Disabled freighter {$designation}
wf-remade-encounter-adrift-announce = {$name}, dead in space. The power is gone and the crew are sick. Something is wrong with the walls. Anyone close, please come.

wf-remade-choir-title = Chorister
wf-remade-choir-title-leader = Choirmaster

wf-remade-choir-zone-warn-1 = {$intruder}, you are {$distance} metres off a quiet hull. Turn away. It is not for you.
wf-remade-choir-zone-warn-2 = {$intruder}, this is the Choir's. Open the range or you will be sung out.
wf-remade-choir-zone-warn-3 = Last warning, {$intruder}. Leave it be.
wf-remade-choir-zone-attack = {$intruder}, you came for it anyway. Join the song.

wf-remade-sector-reason-wild = a wild Scion is already out there
wf-remade-sector-reason-limit = the sector already holds its limit of Scions
wf-remade-sector-reason-placed = one was placed lately
wf-remade-sector-reason-gone = one was lost lately

wf-remade-signal-name = Signal: {$stage}
wf-remade-signal-adrift = Adrift
wf-remade-signal-stage-Quiet = faint hum
wf-remade-signal-stage-Hymn = hymn
wf-remade-signal-stage-Growth = rising hymn
wf-remade-signal-stage-Harvest = loud hymn
wf-remade-signal-stage-Rapture = roaring hymn
wf-remade-signal-stage-Crescendo = Crescendo

ent-WFMobChoirist = chorister
    .desc = Robed, hooded and humming. It has been told to mind the dark thing in the hold, and it does.

cmd-wf_remade-desc = Places Monolith sites, sets a Scion's resonance, lists the Scions, breaks out on a ship or wipes the Remade.
cmd-wf_remade-help = Usage: {$command} spawn [site] [here] | resonance <value> [scion uid] | status | outbreak [scion uid] | clear | lockdown <player or uid> [seconds] [range]
cmd-wf_remade-hint-sub = Subcommand
cmd-wf_remade-hint-site = Site prototype
cmd-wf_remade-hint-here = Place it 400 m from you
cmd-wf_remade-hint-value = Resonance, 0 to 100
cmd-wf_remade-hint-scion = Scion uid
cmd-wf_remade-no-site = No site called {$site}.
cmd-wf_remade-no-sites = There is no discovery site to place.
cmd-wf_remade-no-body = You need a body on a map to place a site beside you.
cmd-wf_remade-spawn-failed = Could not place {$site}: no clear space near a player, or the encounter would not start.
cmd-wf_remade-spawned = Placed {$site} as encounter {$encounter}. Its Scion appears once the hull is real.
cmd-wf_remade-bad-value = {$value} is not a resonance.
cmd-wf_remade-no-scion = {$scion} is not a Scion.
cmd-wf_remade-none = There is no Scion to set.
cmd-wf_remade-resonance = Set {$scion} to {$value} resonance. Its stage follows within a second.
cmd-wf_remade-status-header = Scions: {$scions}, wild: {$wild}. New wild Scion allowed: {$can}.
cmd-wf_remade-status-scion = {$scion}: {$stage} at {$resonance}, {$state}, grid {$grid}
cmd-wf_remade-status-yes = yes
cmd-wf_remade-cleared = Cleared {$count} Remade entities: stones, shards, Remade, growths, corruption, Signals and sounds. Every client was told to stop.
cmd-wf_remade-outbreak = Broke out on another crewed ship.
cmd-wf_remade-hint-target = Player or entity uid
cmd-wf_remade-hint-seconds = Seconds the doors stay bolted, 60 by default
cmd-wf_remade-hint-range = Metres round them, 9 by default
cmd-wf_remade-no-target = No player or entity called {$target}.
cmd-wf_remade-bad-lockdown = Seconds and range must be positive numbers.
cmd-wf_remade-locked-down = Bolted {$count} doors for {$seconds} s and sounded the alarm.
cmd-wf_remade-lockdown-none = No powered, unbolted door in range: nothing locked down.
cmd-wf_remade-outbreak-failed = No crewed ship to break out on: none off the stone's own grid, outside a sanctuary, with an unseen vent near someone.
cmd-wf_remade-state-none = untracked
cmd-wf_remade-state-wild = wild
cmd-wf_remade-state-disturbed = disturbed
cmd-wf_remade-state-quiet = quiet
