# Faction name
wg-sector-faction-reavers = Ashfall Reavers

# The Underground black market's company, which the Reavers leave alone
wg-company-underground-description = The Underground is the black market and the people who work out of it: fugitives, fixers and their boss. The Reavers leave its ships and crews alone.

# Border notices for Reaver space, in place of the sector control defaults
wg-reaver-notice-entered = WARNING: entering Ashfall Reaver controlled space ({$callsign}). Their patrols attack on sight and will come looking for you.
wg-reaver-notice-left = Leaving Ashfall Reaver controlled space ({$callsign}).

# Sender of the announcements and name of the radio entity
wg-reaver-watch = Sector Watch

# Used as the station when the map has none
wg-reaver-place-origin = the sector core

# Threat tiers
wg-reaver-tier-0 = None
wg-reaver-tier-1 = Scattered
wg-reaver-tier-2 = Organised
wg-reaver-tier-3 = Entrenched
wg-reaver-tier-4 = Dominant
wg-reaver-tier-5 = Overrun

wg-reaver-tier-rising = Reaver threat is rising: {$tier}. { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Stronghold bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-reaver-tier-falling = Reaver threat is falling: {$tier}. { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Stronghold bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.

# $station is the nearest station, $bearing and $range are from it
wg-reaver-founded = Reavers have built a stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-reaver-regrouped = Reavers have regrouped and built a new stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-reaver-broken = The Reaver stronghold at {$callsign}, {$range} km bearing {$bearing} from {$station}, is broken. { $cells ->
        [one] {$cells} cell is free.
       *[other] {$cells} cells are free.
    } { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-patrol-destroyed = Reaver patrol destroyed at {$callsign}. The cell is clear. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-patrol-destroyed-held = Reaver patrol destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-retaken = {$callsign} retaken from the Reavers. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
# $cells is a list of callsigns
wg-reaver-clearing = Reaver space at {$cells} is clearing. Drone activity returns in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Clear the area.
wg-reaver-defeated = The Ashfall Reavers are broken. Their strongholds are gone and the sector is clear.
wg-reaver-retake-name = retaking {$callsign}
wg-reaver-blockade = Reavers are moving to blockade {$station} at {$callsign}. They take the cell in {$minutes} minutes unless traffic keeps the lane.

# A stronghold flying out from a standing one to found itself; $station, $bearing and $range are as above
wg-reaver-convoy-launched = A Reaver convoy is under way to {$callsign}, {$range} km bearing {$bearing} from {$station}. Stop it before it digs in.
wg-reaver-convoy-destroyed = The Reaver convoy bound for {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# The tier 5 citadel: setting out, stopped on the way, raised and fallen; $station, $bearing and $range are as above
wg-reaver-citadel-launched = The Reavers are raising a citadel at {$callsign}. {$range} km bearing {$bearing} from {$station}. Stop it before it digs in.
wg-reaver-citadel-convoy-destroyed = The Reaver citadel convoy bound for {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-citadel-raised = The Reavers have raised a citadel at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-reaver-citadel-fallen = The Reaver citadel at {$callsign} has fallen. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    } The Ashfall Reavers are finished.

# A hub's batteries and reinforcements
wg-reaver-battery-destroyed = A Reaver gun battery at {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-reinforcement-destroyed = Reaver reinforcements bound for {$callsign} are destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# A smuggling funding payout
wg-reaver-funded = Smuggling money is reaching the Ashfall Reavers. Expect more of their ships.

wg-reaver-raid-warning-pa =Reaver raiders are heading for {$ship}, bearing {$bearing}, {$range} km.
wg-reaver-raid-station-warning = Reaver raiders are heading for {$station}, bearing {$bearing}, {$range} km.
wg-reaver-raid-hunting = Reaver raiders are hunting {$ship}, bearing {$bearing}, {$range} km.
wg-reaver-intercept-warning = Reaver patrol turning to intercept {$ship}, bearing {$bearing}, {$range} km.

# Another faction challenging a Reaver cell; $faction is the challenger
wg-reaver-response-destroyed = Reaver gunships destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-reaver-cell-lost = {$callsign} lost to the {$faction}.

# Map legend line
wg-reaver-legend = {$faction}: {$tier}, { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, bounty {$bounty}{$levels}{$citadel}
wg-reaver-legend-broken = {$faction}: broken, no strongholds standing
wg-reaver-legend-finished = {$faction}: finished, { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    } left, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }
wg-reaver-legend-levels = , { $count ->
        [one] level {$levels}
       *[other] levels {$levels}
    }
wg-reaver-legend-citadel = , citadel at {$callsign}

wg-reaver-round-end = Reavers held up to { $peak ->
        [one] {$peak} cell
       *[other] {$peak} cells
    }. They founded { $founded ->
        [one] {$founded} stronghold
       *[other] {$founded} strongholds
    }, { $broken ->
        [one] {$broken} was
       *[other] {$broken} were
    } broken, { $patrols ->
        [one] {$patrols} patrol was
       *[other] {$patrols} patrols were
    } destroyed, { $convoys ->
        [one] {$convoys} convoy was
       *[other] {$convoys} convoys were
    } stopped and { $raids ->
        [one] {$raids} raid was
       *[other] {$raids} raids were
    } sent.{$citadels} Players retook { $retaken ->
        [one] {$retaken} cell
       *[other] {$retaken} cells
    }.

# Added to the round-end line when a citadel was raised; it starts with a space
wg-reaver-round-end-citadels = {" "}They raised { $raised ->
        [one] {$raised} citadel
       *[other] {$raised} citadels
    } and {$fallen} fell.

wg-reaver-round-end-broken = The Reavers were broken { $count ->
        [one] once
       *[other] {$count} times
    }.
wg-reaver-round-end-finished = The Reavers were finished when their citadel fell.
wg-reaver-round-end-lost = The Reavers lost { $count ->
        [one] {$count} cell
       *[other] {$count} cells
    } to challenges.

# wg_reavers command
cmd-wg_reavers-desc = Shows and drives the Ashfall Reaver campaign.
cmd-wg_reavers-help = Usage: {$command} status | found [x y] | convoy | citadel | reinforce <stronghold uid> | tier <n> | spread | patrol | raid | intercept <grid uid> | respond <q> <r> | pause | resume | clear
    found places a level 0 hub at once, an admin shortcut; convoy sends one out from a standing stronghold, as the campaign does; citadel sends the citadel out now at any tier, one at a time; reinforce sends a stronghold its next reinforcement now; respond sends a response pack to a challenged Reaver cell.
cmd-wg_reavers-hint-sub = Subcommand
cmd-wg_reavers-hint-x = Map x in metres
cmd-wg_reavers-hint-y = Map y in metres
cmd-wg_reavers-hint-tier = Threat tier
cmd-wg_reavers-hint-grid = Grid uid of the ship to intercept
cmd-wg_reavers-hint-stronghold = Stronghold uid
cmd-wg_reavers-hint-q = Cell column (q)
cmd-wg_reavers-hint-r = Cell row (r)
cmd-wg_reavers-bad-cell = Not a cell: {$q} {$r}
cmd-wg_reavers-respond = A response pack is scrambling to {$callsign}.
cmd-wg_reavers-respond-failed = No response pack could be sent: the cell is not a challenged Reaver cell, a pack already defends it, or no stronghold has clear space beside it.
cmd-wg_reavers-no-campaign = No Reaver campaign runs under the current preset.
cmd-wg_reavers-status-header = Campaign {$campaign}: running {$running}, paused {$paused}, tier {$tier}, score {$score}.
cmd-wg_reavers-status-counts = { $strongholds ->
        [one] {$strongholds} stronghold
       *[other] {$strongholds} strongholds
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, { $patrols ->
        [one] {$patrols} patrol
       *[other] {$patrols} patrols
    }, { $raids ->
        [one] {$raids} raid
       *[other] {$raids} raids
    }.
cmd-wg_reavers-status-clocks = Seconds to next spread: {$spread}. Seconds to next raid: {$raid}.
cmd-wg_reavers-status-population = Population: {$population} (now {$live}), bracket from {$bracket} players.
cmd-wg_reavers-status-funding = Smuggling funding: {$crates} crates in the war chest (of {$chest}), {$needed} to a payout, {$funded} paid out this round.
cmd-wg_reavers-no-stronghold = No stronghold stands. Run wg_reavers found to place one now.
cmd-wg_reavers-status-switch-off = No stronghold stands and none will be founded: {$cvar} is off. Run wg_reavers found to place one anyway.
cmd-wg_reavers-status-mode-off = No stronghold stands and none will be founded: the Contested Sector game mode is not running. Admins can force it with {$cvar} or by adding the rule (addgamerule WGReaverCampaignRule).
cmd-wg_reavers-status-not-in-round = No stronghold stands and none will be founded: no round is in progress.
cmd-wg_reavers-status-paused = No stronghold stands and none will be founded: the campaign is paused. Run wg_reavers resume.
cmd-wg_reavers-status-runner-paused = No stronghold stands and none will be founded: the event runner is paused, which holds the campaign too. Unpause it in the Encounters window.
cmd-wg_reavers-status-first-due = No stronghold stands. The first is due in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wg_reavers found to place one now.
cmd-wg_reavers-status-regroup-due = No stronghold stands. The Reavers regroup in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wg_reavers found to place one now.
cmd-wg_reavers-status-founding-overdue = No stronghold stands. A founding is due now and retries every minute until a cell qualifies. Run wg_reavers found to place one now.
cmd-wg_reavers-status-founding-untimed = No stronghold stands and the founding clock has not started. Run wg_reavers found to place one now.
cmd-wg_reavers-status-stronghold = Stronghold {$uid} at {$callsign}: level {$level}, {$prototype}, {$hits} hits, {$patrols} patrols, {$reinforcements} reinforcements, {$batteries}/{$slots} batteries{$state}.
cmd-wg_reavers-status-stronghold-inbound = , one inbound
cmd-wg_reavers-status-stronghold-scattered = , broken apart
cmd-wg_reavers-status-stronghold-citadel = , the citadel
cmd-wg_reavers-status-citadel-convoy = Citadel convoy {$uid} bound for {$callsign}: {$range} km to go, under way {$seconds} s.
cmd-wg_reavers-status-finished = The Reavers are finished: their citadel fell. Nothing more is founded, launched or sent this round; what stands fights on until broken. Run wg_reavers clear to lift it.
cmd-wg_reavers-status-citadel-due = At Overrun: the citadel sets out in {$seconds} s, once a held cell qualifies.
cmd-wg_reavers-status-reinforcement = Reinforcement {$uid} for {$callsign}, level {$level}: {$phase}, {$range} km from the hub, out {$seconds} s.
cmd-wg_reavers-status-intercept = Patrol {$patrol} intercepting {$ship} ({$uid}) for {$seconds} s.
cmd-wg_reavers-status-mark = {$ship} ({$uid}) is marked for {$seconds} s more.
cmd-wg_reavers-status-convoy-out = A convoy is under way; the next founding waits until it arrives or is stopped.
cmd-wg_reavers-status-convoy = Convoy {$uid} bound for {$callsign}: {$range} km to go, under way {$seconds} s.
cmd-wg_reavers-founded = Founded stronghold {$uid} at {$callsign}.
cmd-wg_reavers-bad-stronghold = Not a standing stronghold: {$arg}
cmd-wg_reavers-reinforce = Reinforcement {$uid} sent to the stronghold at {$callsign}.
cmd-wg_reavers-reinforce-failed = No reinforcement could be sent: the stronghold is at its tier's level cap, one is already on its way, or there is no clear space for it.
cmd-wg_reavers-found-failed = No stronghold could be founded there.
cmd-wg_reavers-convoy = Convoy {$uid} sent to found a stronghold at {$callsign}.
cmd-wg_reavers-citadel = Citadel convoy {$uid} sent to raise a citadel at {$callsign}.
cmd-wg_reavers-citadel-failed = No citadel could be sent: the campaign has none, one stands or is under way, the Reavers are finished, no held cell qualifies, or no stronghold has clear space beside it.
cmd-wg_reavers-convoy-failed = No convoy could be sent: one is already under way, or no held cell qualifies, or no stronghold has clear space beside it.
cmd-wg_reavers-bad-position = Not a position: {$x} {$y}
cmd-wg_reavers-bad-tier = Not a tier from 0 to {$max}: {$arg}
cmd-wg_reavers-tier-set = Tier set to {$tier}.
cmd-wg_reavers-spread = Claimed or contested one cell.
cmd-wg_reavers-spread-failed = No cell could be claimed.
cmd-wg_reavers-patrol = Patrol sent from the stronghold at {$callsign}.
cmd-wg_reavers-patrol-failed = No patrol could be launched.
cmd-wg_reavers-raid = Raid launched.
cmd-wg_reavers-raid-failed = No raid could be launched.
cmd-wg_reavers-bad-grid = Not a grid: {$arg}
cmd-wg_reavers-intercept = A patrol is turning to intercept {$ship}.
cmd-wg_reavers-intercept-failed = No patrol could be sent to intercept {$ship}.
cmd-wg_reavers-paused = Campaign paused.
cmd-wg_reavers-resumed = Campaign resumed.
cmd-wg_reavers-cleared = Cleared the campaign: {$count} encounters ended.

# Encounter names
wg-reaver-name-stronghold = Reaver stronghold {$designation}
wg-reaver-name-citadel = Reaver citadel {$designation}
wg-reaver-name-level = {$name} (level {$level})
wg-reaver-name-reinforcement = Reaver reinforcements {$designation}
wg-reaver-name-battery = Reaver gun battery {$designation}
wg-reaver-name-patrol = Reaver patrol {$designation}
wg-reaver-name-raid = Reaver raiders {$designation}
wg-reaver-name-response = Reaver response pack
wg-reaver-name-boarders = Reaver boarders {$designation}

# Game mode
wg-reaver-preset-title = Contested Sector
wg-reaver-preset-description = Standard sector with two threats on the map: the Ashfall Reavers build strongholds and spread, the Automated Defense System holds ground of its own, and the two fight over it.
