# Faction name
wg-sector-faction-ads = Automated Defense System

# The company flag on the system's hulls and crews, which nobody can pick at character setup
wg-company-ads-description = The Automated Defense System is the pre-Fracture silicon fleet, awake again and following its directives to the letter. It disarms the navies and the Reavers, holds ground without ever touching a station, and disables what crosses it. It does not hunt, and it does not stop.

# Border notices for ADS space, in place of the sector control defaults
wg-ads-notice-entered = DIRECTIVE: you are entering an Automated Defense System exclusion zone ({$callsign}). Your vessel will be queried. Only declared threats and attackers are made combat-ineffective.
wg-ads-notice-left = DIRECTIVE: you are leaving an Automated Defense System exclusion zone ({$callsign}). Query closed. Compliance noted.

# What the units are called by their posts, in place of the crew roles' own titles
wg-ads-title-captain = Command Core
wg-ads-title-pilot = Helm Unit
wg-ads-title-radio-operator = Comms Unit
wg-ads-title-gunner = Fire Control Unit
wg-ads-title-engineer = Reactor Unit
wg-ads-title-deckhand = Maintenance Unit
wg-ads-title-marine = Sentry Unit

# Encounter names
wg-ads-name-node = ADS node
wg-ads-name-sentinel = ADS sentinel
wg-ads-name-sweep = ADS sweep
wg-ads-name-interdictor = ADS interdictor
wg-ads-name-deployment = ADS deployment
wg-ads-name-dreadnought = ADS dreadnought

# What a node's hold is announced as when it is boarded
wg-ads-cargo-tech = pre-Fracture ADS technology

# What the system's ships say to a declared threat or an attacker in their zone; every other vessel is queried instead
wg-ads-zone-warn-1 = DISARMAMENT DIRECTIVE. {$intruder}, you are {$distance} metres inside an exclusion zone. Vessels are queried; only declared threats and attackers are engaged. You are one. Withdraw.
wg-ads-zone-warn-2 = DISARMAMENT DIRECTIVE. {$intruder}, you are logged as a threat. Only declared threats and attackers are engaged. Power down your weapons and withdraw.
wg-ads-zone-warn-3 = FINAL NOTICE. {$intruder}, you are {$distance} metres inside an exclusion zone and logged as a threat. Withdraw now.

# The query an ADS ship puts over a hail to every other vessel in its zone, the answers it offers, and its replies
wg-ads-zone-hail-question = DIRECTIVE QUERY. ARE YOU A THREAT TO THE AUTOMATED DEFENSE SYSTEM?
wg-ads-zone-hail-yes = YES
wg-ads-zone-hail-no = NO
wg-ads-zone-hail-declared = THREAT DECLARED. YOUR VESSEL WILL BE MADE COMBAT-INEFFECTIVE.
wg-ads-zone-hail-cleared = ANSWER LOGGED. YOU ARE NOT A TARGET. DO NOT FIRE ON ADS UNITS.
wg-ads-zone-attack = Compliance window closed. {$intruder} will be made combat-ineffective.

# Guidebook
guide-entry-wg-ads = Automated Defense System

# Sender of the campaign's announcements and name of its radio entity
wg-ads-watch = Sector Watch
# The sender of the system's own chimed alerts: a node raised or broken, the tier rising, as a malfunctioning AI announces itself
wg-ads-anomaly-alert = Anomaly Alert
# The banner wrap for the system's announcements: the chat manager's, with the message in the synthetic readout face
wg-ads-announcement-wrap = [font size=14][bold]{$sender} Announcement:[/font][font="WGAdsSynth" size=10]
    {$message}[/bold][/font]

# Used as the station when the map has none
wg-ads-place-origin = the sector core

# Threat tiers
wg-ads-tier-0 = Dormant
wg-ads-tier-1 = Waking
wg-ads-tier-2 = Active
wg-ads-tier-3 = Alert
wg-ads-tier-4 = Fortified
wg-ads-tier-5 = Overrun

wg-ads-tier-rising = Automated Defense System threat is rising: {$tier}. { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Node bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-ads-tier-falling = Automated Defense System threat is falling: {$tier}. { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    } held. Node bounty is {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.

# $station is the nearest station, $bearing and $range are from it
wg-ads-node-founded = An Automated Defense System node has come online at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-ads-node-regrouped = The Automated Defense System has woken again: a new node is online at {$callsign}, {$range} km bearing {$bearing} from {$station}. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-ads-node-broken = The ADS node at {$callsign}, {$range} km bearing {$bearing} from {$station}, is broken. { $cells ->
        [one] {$cells} cell is free.
       *[other] {$cells} cells are free.
    } { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-ads-dormant = The Automated Defense System has gone dormant. Its nodes are gone and the sector is clear.

# The tier 5 dreadnought: activated as an anomaly alert, and destroyed as the campaign's ending
wg-ads-dreadnought-activated = DIRECTIVE UPDATE. Dreadnought-class asset activated at {$callsign}. Hostile strongholds will be neutralised.
wg-ads-dreadnought-destroyed = The ADS dreadnought is destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    } The Automated Defense System has gone dormant for good.

# A node flying out from a standing one to found itself
wg-ads-deployment-launched = An ADS deployment is under way to {$callsign}, {$range} km bearing {$bearing} from {$station}. Stop it before it comes online.
wg-ads-deployment-arrived = The ADS deployment has arrived at {$callsign}, {$range} km bearing {$bearing} from {$station}, and is online as a node. Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        }.
wg-ads-deployment-stopped = The ADS deployment bound for {$callsign} is stopped. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# Sentinels guarding a node
wg-ads-sentinel-lost = ADS sentinel destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

wg-ads-sweep-destroyed = ADS sweep destroyed at {$callsign}. The cell is clear. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-ads-sweep-destroyed-held = ADS sweep destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# Said to a ship the ADS sends an interdictor to query, and on the common channel
wg-ads-interdict-warning = DISARMAMENT DIRECTIVE. ADS interdictor closing on {$ship} to query it, bearing {$bearing}, {$range} km.

# Said to a ship the ADS sends sweeps to destroy: one that attacked, boarded or seized an ADS ship
wg-ads-hunt-warning = TERMINATION DIRECTIVE. {$ship} has attacked ADS assets. Sweep inbound to destroy it, bearing {$bearing}, {$range} km.

# Said aboard a hull taken from the ADS, when a sweep is sent to destroy it
wg-ads-stolen-warning = RECLAMATION DIRECTIVE. {$ship} is stolen ADS property. Sweep inbound to destroy it, bearing {$bearing}, {$range} km.

# Said to a ship mining a rock in held space, then to one that went on mining
wg-ads-mining-warning = DIRECTIVE. {$ship}, these resources are protected. Cease mining or your vessel will be logged as a threat.
wg-ads-mining-declared = THREAT LOGGED. {$ship} ignored a cease-mining directive and will be made combat-ineffective.

# Challenges on cells another faction holds; $holder is that faction
wg-ads-challenge-launched = ADS interdictor under way to {$callsign}, contesting space held by the {$holder}. {$range} km bearing {$bearing} from {$station}.
wg-ads-challenge-won = {$callsign} has fallen to the Automated Defense System.
wg-ads-challenge-lost = ADS interdictor withdraws from {$callsign}.
wg-ads-challenge-destroyed = ADS interdictor destroyed at {$callsign}. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-ads-picket-destroyed = The ADS picket holding {$callsign} is destroyed. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }

# $cells is a list of callsigns
wg-ads-clearing = ADS space at {$cells} is clearing. Drone activity returns in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Clear the area.
wg-ads-retaken = {$callsign} retaken from the Automated Defense System. { $paid ->
        [0] No bounty was claimed.
       *[other] Bounty {$bounty} { $bounty ->
            [one] thaler
           *[other] thalers
        } paid.
    }
wg-ads-retake-name = retaking {$callsign}

# Map legend line
wg-ads-legend = {$faction}: {$tier}, { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, bounty {$bounty}
wg-ads-legend-dormant = {$faction}: dormant, no nodes standing
wg-ads-legend-finished = {$faction}: finished, { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    } left, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }

wg-ads-round-end = The Automated Defense System held up to { $peak ->
        [one] {$peak} cell
       *[other] {$peak} cells
    }. It brought { $founded ->
        [one] {$founded} node
       *[other] {$founded} nodes
    } online, { $broken ->
        [one] {$broken} was
       *[other] {$broken} were
    } broken, { $sweeps ->
        [one] {$sweeps} sweep or sentinel was
       *[other] {$sweeps} sweeps and sentinels were
    } destroyed and { $deployments ->
        [one] {$deployments} deployment was
       *[other] {$deployments} deployments were
    } stopped. Players retook { $retaken ->
        [one] {$retaken} cell
       *[other] {$retaken} cells
    }.
wg-ads-round-end-challenges = The ADS challenged { $challenged ->
        [one] {$challenged} cell
       *[other] {$challenged} cells
    }: it won {$won} and lost {$lost}.
wg-ads-round-end-dormant = The ADS went dormant { $count ->
        [one] once
       *[other] {$count} times
    }.
wg-ads-round-end-dreadnought = The ADS activated { $activated ->
        [one] {$activated} dreadnought
       *[other] {$activated} dreadnoughts
    }: {$destroyed} destroyed, { $strongholds ->
        [one] {$strongholds} Reaver stronghold
       *[other] {$strongholds} Reaver strongholds
    } broken with one among the attackers.
wg-ads-round-end-finished = The ADS went dormant for good when its dreadnought was destroyed.

# wg_ads command
cmd-wg_ads-desc = Shows and drives the Automated Defense System incursion.
cmd-wg_ads-help = Usage: {$command} status | found [x y] | deploy | sentinel <node uid> | tier <n> | spread | sweep | interdict <grid uid> | challenge [q r] | dreadnought | pause | resume | clear
    found places a node at once, an admin shortcut; deploy sends one out from a standing node, as the campaign does; sentinel sends a node its next sentinel now; challenge sends an interdictor against a cell another faction holds; dreadnought activates the dreadnought now at any tier, one at a time.
cmd-wg_ads-hint-sub = Subcommand
cmd-wg_ads-hint-x = Map x in metres
cmd-wg_ads-hint-y = Map y in metres
cmd-wg_ads-hint-q = Cell column (q)
cmd-wg_ads-hint-r = Cell row (r)
cmd-wg_ads-hint-tier = Threat tier
cmd-wg_ads-hint-grid = Grid uid of the ship to interdict
cmd-wg_ads-hint-node = Node uid
cmd-wg_ads-no-campaign = No ADS campaign runs under the current preset.
cmd-wg_ads-no-node = No node stands. Run wg_ads found to place one now.
cmd-wg_ads-status-header = Campaign {$campaign}: running {$running}, paused {$paused}, tier {$tier}, score {$score}.
cmd-wg_ads-status-counts = { $nodes ->
        [one] {$nodes} node
       *[other] {$nodes} nodes
    }, { $cells ->
        [one] {$cells} cell
       *[other] {$cells} cells
    }, { $sweeps ->
        [one] {$sweeps} sweep
       *[other] {$sweeps} sweeps
    }, { $challenges ->
        [one] {$challenges} challenge
       *[other] {$challenges} challenges
    }, { $pickets ->
        [one] {$pickets} picket
       *[other] {$pickets} pickets
    }.
cmd-wg_ads-status-clocks = Seconds to next spread: {$spread}. Seconds to next challenge: {$challenge}.
cmd-wg_ads-status-node = Node {$uid} at {$callsign}: {$hits} hits, {$sweeps} sweeps, {$sentinels} sentinels{$inbound}.
cmd-wg_ads-status-node-inbound = , one inbound
cmd-wg_ads-status-deployment = Deployment {$uid} bound for {$callsign}: {$range} km to go, under way {$seconds} s.
cmd-wg_ads-status-challenge = Interdictor {$uid} challenging {$callsign}: arrived {$arrived}, decided in {$seconds} s.
cmd-wg_ads-status-interdict = Sweep {$sweep} interdicting {$ship} ({$uid}) for {$seconds} s.
cmd-wg_ads-status-switch-off = No node stands and none will be founded: {$cvar} is off. Run wg_ads found to place one anyway.
cmd-wg_ads-status-mode-off = No node stands and none will be founded: the ADS rule is not running. Admins can force it with {$cvar} or by adding the rule (addgamerule WGAdsCampaignRule).
cmd-wg_ads-status-not-in-round = No node stands and none will be founded: no round is in progress.
cmd-wg_ads-status-paused = No node stands and none will be founded: the campaign is paused. Run wg_ads resume.
cmd-wg_ads-status-runner-paused = No node stands and none will be founded: the event runner is paused, which holds the campaign too. Unpause it in the Encounters window.
cmd-wg_ads-status-first-due = No node stands. The first is due in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wg_ads found to place one now.
cmd-wg_ads-status-regroup-due = No node stands. The ADS wakes again in { $minutes ->
        [one] {$minutes} minute
       *[other] {$minutes} minutes
    }. Run wg_ads found to place one now.
cmd-wg_ads-status-founding-overdue = No node stands. A founding is due now and retries every minute until a cell qualifies. Run wg_ads found to place one now.
cmd-wg_ads-status-founding-untimed = No node stands and the founding clock has not started. Run wg_ads found to place one now.
cmd-wg_ads-status-finished = The ADS is finished: its dreadnought was destroyed. Nothing more is founded, launched or sent this round; what stands fights on until broken. Run wg_ads clear to lift it.
cmd-wg_ads-status-dreadnought-due = At Overrun: the dreadnought activates in {$seconds} s, once a node has clear space beside it.
cmd-wg_ads-status-dreadnought-hunting = Dreadnought {$uid} hunting {$target}, {$range} km off, out {$seconds} s.
cmd-wg_ads-status-dreadnought-holding = Dreadnought {$uid} holding by {$target} with no Reaver stronghold standing, {$range} km off, out {$seconds} s.
cmd-wg_ads-status-deployment-out = A deployment is under way; the next founding waits until it arrives or is stopped.
cmd-wg_ads-founded = Founded node {$uid} at {$callsign}.
cmd-wg_ads-found-failed = No node could be founded there.
cmd-wg_ads-deploy = Deployment {$uid} sent to found a node at {$callsign}.
cmd-wg_ads-deploy-failed = No deployment could be sent: one is already under way, or no held cell qualifies, or no node has clear space beside it.
cmd-wg_ads-bad-node = Not a standing node: {$arg}
cmd-wg_ads-sentinel = Sentinel {$uid} sent to the node at {$callsign}.
cmd-wg_ads-sentinel-failed = No sentinel could be sent: one is already on its way, or there is no clear space for it.
cmd-wg_ads-bad-position = Not a position: {$x} {$y}
cmd-wg_ads-bad-cell = Not a cell: {$q} {$r}
cmd-wg_ads-bad-tier = Not a tier from 0 to {$max}: {$arg}
cmd-wg_ads-tier-set = Tier set to {$tier}.
cmd-wg_ads-spread = Claimed one cell.
cmd-wg_ads-spread-failed = No cell could be claimed.
cmd-wg_ads-sweep = Sweep sent from the node at {$callsign}.
cmd-wg_ads-sweep-failed = No sweep could be launched.
cmd-wg_ads-bad-grid = Not a grid: {$arg}
cmd-wg_ads-interdict = A sweep is turning to interdict {$ship}.
cmd-wg_ads-interdict-failed = No sweep could be sent to interdict {$ship}.
cmd-wg_ads-challenge = Interdictor {$uid} sent to challenge {$callsign}.
cmd-wg_ads-challenge-failed = No challenge could be made: no other faction's cell qualifies, or no node has clear space beside it.
cmd-wg_ads-dreadnought = Dreadnought {$uid} activated, hunting {$target}.
cmd-wg_ads-dreadnought-failed = No dreadnought could be activated: the campaign has none, one is out, the ADS is finished, or no node has clear space beside it.
cmd-wg_ads-paused = Campaign paused.
cmd-wg_ads-resumed = Campaign resumed.
cmd-wg_ads-cleared = Cleared the campaign: {$count} encounters ended.

# How the units' speech is labelled: the silicon verbs in the readout face
wg-ads-speech-verb-name = Synthetic
