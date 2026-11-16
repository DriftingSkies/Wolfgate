# Shipmaster, the Wolfgate admin ship panel

## Opening it
wf-ship-admin-button = Shipmaster
wf-ship-admin-verb = Shipmaster
wf-ship-admin-verb-description = Open Shipmaster on { $ship }.
cmd-shipmaster-desc = Opens Shipmaster, the admin ship panel, on a ship when one is named. With no ship named it starts on the one you are over.
cmd-shipmaster-help = Usage: { $command } [grid entity | here]
cmd-shipmaster-not-grid = "{ $grid }" isn't a grid.
cmd-shipmaster-hint-grid = [grid entity | here]
ent-WFActionShipmaster = Shipmaster
    .desc = Open Shipmaster on the ship you are over.

## Window
wf-ship-admin-title = Shipmaster
wf-ship-admin-search = Search ships, owners or designs...
wf-ship-admin-refresh = Refresh
wf-ship-admin-sort-nearest = Sort: nearest first
wf-ship-admin-sort-name = Sort: name
wf-ship-admin-sort-owner = Sort: owner
wf-ship-admin-sort-aboard = Sort: most aboard
wf-ship-admin-compass-here = here
wf-ship-admin-compass-distance = { $distance } m
wf-ship-admin-filter-ships = Player ships
wf-ship-admin-filter-all = All grids
wf-ship-admin-empty = No ships found.
wf-ship-admin-count = Showing { $shown } of { $total }
wf-ship-admin-count-truncated = Showing { $shown } of the first { $total }; search to narrow it
wf-ship-admin-none-selected = Pick a ship from the list.
wf-ship-admin-row = { $owner } { $state ->
        [online] (online)
        [offline] (offline)
       *[none] { "" }
    } | aboard: { $aboard }
wf-ship-admin-summary = Owner: { $owner } | { $map } | aboard: { $aboard }
wf-ship-admin-unowned = no owner
wf-ship-admin-none = none
wf-ship-admin-locked = Locked
wf-ship-admin-unlocked = Unlocked
wf-ship-admin-needs-flag = Needs the { $flag } permission.

wf-ship-admin-confirm = Confirm?
wf-ship-admin-notify = Tell the owners
wf-ship-admin-notify-tooltip = Sends the old and new owners a chat line saying staff changed the ship. Untick it for a quiet fix; the admin log records it either way.
wf-ship-admin-unclaimed = Unclaimed

## Tabs
wf-ship-admin-tab-overview = Overview
wf-ship-admin-tab-ownership = Ownership
wf-ship-admin-tab-access = Access
wf-ship-admin-tab-repair = Repair
wf-ship-admin-tab-move = Move
wf-ship-admin-tab-systems = Systems
wf-ship-admin-tab-upgrades = Upgrades
wf-ship-admin-tab-danger = Danger

## Tags
wf-ship-admin-tag-locked = locked
wf-ship-admin-tag-docked = docked
wf-ship-admin-tag-inftl = in FTL
wf-ship-admin-tag-snapshot = has repair snapshot
wf-ship-admin-tag-crewlinked = crew ship
wf-ship-admin-tag-company = company ship
wf-ship-admin-tag-hangar = hangar ship
wf-ship-admin-tag-fixing = being fixed

## Access modes and owner kinds
wf-ship-admin-mode-private = Private
wf-ship-admin-mode-faction = Faction
wf-ship-admin-mode-crew = Crew
wf-ship-admin-mode-company = Company
wf-ship-admin-mode-public = Public
wf-ship-admin-owner-deedcard = Deed card
wf-ship-admin-owner-voucher = Deed voucher
wf-ship-admin-owner-account = Account
wf-ship-admin-owner-unstamped = { $name } (no player on record)
wf-ship-admin-owner-stamped = { $name } ({ $username }, { $online ->
        [true] online
       *[false] offline
    })

## Overview tab
wf-ship-admin-scan = Scan value
wf-ship-admin-scan-tooltip = Appraise the ship. Slow on a large hull, so it only runs when asked.
wf-ship-admin-overview-ship = Ship
wf-ship-admin-overview-design = Design
wf-ship-admin-overview-entity = Entity
wf-ship-admin-overview-place = Position
wf-ship-admin-overview-place-value = { $map } ({ $x }, { $y })
wf-ship-admin-overview-mass = Mass
wf-ship-admin-overview-docked = Docked to
wf-ship-admin-overview-value = Value
wf-ship-admin-overview-value-amount = { $value } spesos
wf-ship-admin-overview-not-scanned = not scanned
wf-ship-admin-overview-state = State
wf-ship-admin-overview-owners = Owners
wf-ship-admin-overview-record-owner = Named owner
wf-ship-admin-overview-registered = Shipyard account
wf-ship-admin-overview-account = { $name } ({ $online ->
        [true] online
       *[false] offline
    })
wf-ship-admin-overview-no-owners = Nobody holds the deed and no account is registered.
wf-ship-admin-overview-access = Access
wf-ship-admin-overview-no-access = This grid has no ship access.
wf-ship-admin-overview-mode = Mode
wf-ship-admin-overview-lock = Lock
wf-ship-admin-overview-allowed = Allow list
wf-ship-admin-overview-crew = Crew
wf-ship-admin-overview-company = Company
wf-ship-admin-overview-faction = Hull faction
wf-ship-admin-overview-aboard = Aboard ({ $count })
wf-ship-admin-overview-aboard-more = and { $count } more

## Ownership tab
wf-ship-admin-ownership-give = Hand the ship to a player
wf-ship-admin-ownership-give-note = A transfer takes the ship from every current owner. The new owner gets the deed on their ID card when they carry one without a deed; otherwise the ship is registered to their account alone.
wf-ship-admin-ownership-nobody-picked = Pick a player above.
wf-ship-admin-ownership-picked = Picked: { $name } ({ $username })
wf-ship-admin-ownership-keep-previous = Keep previous owners as crew
wf-ship-admin-ownership-keep-previous-tooltip = Puts each ID card that held the deed on the allow list, so the old owners can still open the doors.
wf-ship-admin-ownership-clear-allow = Clear the allow list
wf-ship-admin-ownership-clear-allow-tooltip = Removes everyone the old owner let aboard. Left unticked, their guests keep their access.
wf-ship-admin-ownership-drop-company = Drop the company link
wf-ship-admin-ownership-drop-company-tooltip = Stops the ship flying for the old owner's player company. It is replaced by the new owner's company when their ID card has one.
wf-ship-admin-ownership-leave-crew = Take it out of Crew mode
wf-ship-admin-ownership-leave-crew-tooltip = The linked crew keeps spawning aboard, but its cards stop opening the doors.
wf-ship-admin-ownership-detach-hangar = Detach from the company hangar
wf-ship-admin-ownership-detach-hangar-tooltip = A hangar ship belongs to its company and can't change hands until it is detached.
wf-ship-admin-ownership-faction-keep = Hull faction: keep
wf-ship-admin-ownership-faction-fromcard = Hull faction: from the new owner's ID
wf-ship-admin-ownership-faction-clear = Hull faction: none
wf-ship-admin-ownership-transfer = Transfer ownership
wf-ship-admin-ownership-add = Add as co-owner
wf-ship-admin-ownership-add-tooltip = Registers the ship to their account as well. Nobody loses it.
wf-ship-admin-ownership-remove = Remove
wf-ship-admin-ownership-remove-last = The last owner can't be removed. Release the ship instead.
wf-ship-admin-ownership-release-heading = Leave it with no owner
wf-ship-admin-ownership-release-note = Every owner loses the ship and its doors unlock. Its helm stays locked, so only staff fly it until it is transferred.
wf-ship-admin-ownership-release = Release ownership
wf-ship-admin-label-previous-owner = Previous owner

## Access tab
wf-ship-admin-access-status = Mode: { $mode } | { $locked ->
        [true] Locked
       *[false] Unlocked
    }
wf-ship-admin-access-no-key = No owner's card can open it, so it can't be locked.
wf-ship-admin-access-lock = Lock the ship
wf-ship-admin-access-unlock = Unlock the ship
wf-ship-admin-access-set-mode = Set mode
wf-ship-admin-access-set-mode-tooltip = Crew and Company only work on a ship already linked to one. Faction needs a hull faction. Public lets anyone through every door that has no rule of its own, locked or not; lockers stay as they are.
wf-ship-admin-access-nobody = Nobody is on the allow list.
wf-ship-admin-access-add-heading = Let a player aboard
wf-ship-admin-access-add-note = Adds the ID card the player carries right now. They need a living body and a card with a crew record.
wf-ship-admin-access-add = Add to the allow list
wf-ship-admin-access-clear = Clear the allow list
wf-ship-admin-access-reset-doors = Reset door rules and code
wf-ship-admin-access-reset-doors-tooltip = Puts every door back on the default rule and clears the ship's door code. Doors in a zone are left alone.
wf-ship-admin-access-console-note = For one door's rule, its list or its code, use the Access tab on the ship's own console. Staff can open it on any ship, and unlock any helm with the console's Unlock verb.
wf-ship-admin-label-staff = Added by staff

## Repair tab
wf-ship-admin-repair-fix-heading = Fix
wf-ship-admin-repair-fix-note = A fix never deletes what players moved or built. Hull and parts come back only where the repair snapshot says something is missing.
wf-ship-admin-repair-not-scanned = Not scanned. Scan to count what is missing or damaged.
wf-ship-admin-repair-damage = Missing hull tiles: { $tiles } | missing parts: { $parts } | damaged parts: { $damaged }
wf-ship-admin-repair-progress = Fixing: { $done } of { $total } hull and damage items done.
wf-ship-admin-repair-scan = Scan damage
wf-ship-admin-repair-fix = Fix the ticked parts
wf-ship-admin-repair-part-structures = Damaged parts
wf-ship-admin-repair-part-structures-tooltip = Heals every damaged wall, machine and fitting, and clears crash faults on APCs.
wf-ship-admin-repair-part-hull = Missing hull and parts
wf-ship-admin-repair-part-hull-tooltip = Re-lays hull tiles that are gone and rebuilds parts that are gone, from the repair snapshot. Needs a snapshot.
wf-ship-admin-repair-part-air = Air
wf-ship-admin-repair-part-air-tooltip = Rebuilds the ship's atmosphere, which also puts out fires on the floor.
wf-ship-admin-repair-part-fires = Burning objects
wf-ship-admin-repair-part-fires-tooltip = Puts out everything and everyone on fire aboard.
wf-ship-admin-repair-part-power = Power
wf-ship-admin-repair-part-power-tooltip = Fills every SMES, substation and APC and switches the breakers on.
wf-ship-admin-repair-part-fuel = Fuel
wf-ship-admin-repair-part-fuel-tooltip = Fills and lights generators, swaps spent AME jars and fuel rods, and trims reactors.
wf-ship-admin-repair-part-bolts = Door bolts
wf-ship-admin-repair-part-bolts-tooltip = Raises the bolts on every door, except doors the owner sealed.
wf-ship-admin-repair-part-consoles = Consoles
wf-ship-admin-repair-part-consoles-tooltip = Clears stuck pilots and reconnects the helm, gunnery and hardpoints.
wf-ship-admin-repair-snapshot-heading = Repair snapshot
wf-ship-admin-repair-has-snapshot = This ship has a repair snapshot.
wf-ship-admin-repair-no-snapshot = This ship has no repair snapshot, so missing hull and parts can't be rebuilt.
wf-ship-admin-repair-snapshot-note = The snapshot is what the ship is rebuilt from: taken when it was bought or spawned.
wf-ship-admin-repair-retake = Re-take snapshot
wf-ship-admin-repair-retake-tooltip = Replaces the snapshot with the ship as it stands now. Do this after a player rebuilt it on purpose; the old snapshot is gone for good.
wf-ship-admin-repair-restore = Restore to snapshot
wf-ship-admin-repair-restore-tooltip = Deletes and rebuilds every part that moved since the snapshot and re-lays every hull tile. Player changes are lost.

## Move tab
wf-ship-admin-move-status = { $map } ({ $x }, { $y }) | speed: { $speed } | jump drive: { $ftl }
wf-ship-admin-move-ftl-ready = ready
wf-ship-admin-move-ftl-available = ready
wf-ship-admin-move-ftl-invalid = unknown
wf-ship-admin-move-ftl-starting = spooling up
wf-ship-admin-move-ftl-travelling = in transit
wf-ship-admin-move-ftl-arriving = arriving
wf-ship-admin-move-ftl-cooldown = cooling down
wf-ship-admin-move-heading = Move the ship
wf-ship-admin-move-note = A move is instant. The ship is undocked first, lands in the nearest clear spot and comes to rest. Stations and planets can't be moved from here.
wf-ship-admin-move-to-me = Bring it to me
wf-ship-admin-move-x = X
wf-ship-admin-move-y = Y
wf-ship-admin-move-to-position = Send to this position
wf-ship-admin-move-next-to = Send next to
wf-ship-admin-move-dock-at = Dock at
wf-ship-admin-move-spot = a spot on { $map }
wf-ship-admin-move-free-heading = Free a stuck ship
wf-ship-admin-move-undock = Undock
wf-ship-admin-move-undock-tooltip = Releases every dock on the ship.
wf-ship-admin-move-halt = Halt
wf-ship-admin-move-halt-tooltip = Stops a ship that is drifting or spinning.
wf-ship-admin-move-clear-pilots = Clear pilots
wf-ship-admin-move-clear-pilots-tooltip = Takes everyone off the helms, including pilots who are stuck on one.
wf-ship-admin-move-reset-ftl = Reset jump cooldown
wf-ship-admin-move-reset-ftl-tooltip = Ends the wait after a jump so the ship can jump again at once.
wf-ship-admin-move-ftl-lock-on = Travel with docked ships: turn on
wf-ship-admin-move-ftl-lock-off = Travel with docked ships: turn off
wf-ship-admin-move-ftl-lock-tooltip = While on, the ship is carried along when a ship it is docked to jumps.

## Systems tab
wf-ship-admin-systems-name = Name
wf-ship-admin-systems-rename = Rename
wf-ship-admin-systems-radar = Radar signature
wf-ship-admin-systems-colour-hint = #RRGGBB
wf-ship-admin-systems-set-colour = Set colour
wf-ship-admin-systems-hidden = Hidden from radar
wf-ship-admin-systems-hidden-tooltip = Other ships' radars don't show it at all.
wf-ship-admin-systems-label-hidden = Label hidden
wf-ship-admin-systems-label-hidden-tooltip = Radars show the hull but not its name.
wf-ship-admin-systems-shields = Shields
wf-ship-admin-systems-shields-status = Shield strength: { $percent }%
wf-ship-admin-systems-shields-none = This ship has no shield generator.
wf-ship-admin-systems-shields-on = Raise shields
wf-ship-admin-systems-shields-off = Drop shields
wf-ship-admin-systems-shields-restore = Restore to full
wf-ship-admin-systems-shields-restore-tooltip = Puts every shield generator back at full strength and ends a recharge or overload, so the shields come straight back up if they are powered and raised.
wf-ship-admin-systems-shields-strength-hint = 0 to 100
wf-ship-admin-systems-shields-set = Set strength %
wf-ship-admin-systems-shields-set-tooltip = Sets how much strength the shields have left. Below a hundred it wears off as the generators heal.
wf-ship-admin-systems-power = Power
wf-ship-admin-systems-power-on = Power on
wf-ship-admin-systems-power-off = Power off
wf-ship-admin-systems-power-off-tooltip = Drains every battery aboard and switches the breakers off. The crew gets it back by flipping breakers.
wf-ship-admin-systems-protect = Protections
wf-ship-admin-systems-protect-blastproof = Blast-proof
wf-ship-admin-systems-protect-blastproof-tooltip = Explosions and EMPs don't harm the ship and blasts don't break its floors.
wf-ship-admin-systems-protect-pacified = Pacified
wf-ship-admin-systems-protect-pacified-tooltip = Nobody aboard can fight.
wf-ship-admin-systems-protect-cleanupimmune = Safe from cleanup
wf-ship-admin-systems-protect-cleanupimmune-tooltip = The cleanup of abandoned grids never deletes it.
wf-ship-admin-systems-protect-gunsdisabled = Ship guns disabled
wf-ship-admin-systems-protect-gunsdisabled-tooltip = Its ship guns don't fire.
wf-ship-admin-systems-protect-ftlunrestricted = No jump limits
wf-ship-admin-systems-protect-ftlunrestricted-tooltip = It jumps whatever its mass, with other ships close by, and into no-jump zones.

## Upgrades tab
wf-ship-admin-upgrades-refits = Refits
wf-ship-admin-upgrades-refits-note = Brings a refit line up to a tier at no cost. The ship then owns that tier, as if it had bought it at a drydock.
wf-ship-admin-upgrades-tier = tier { $tier } of { $max }
wf-ship-admin-upgrades-to-tier = To tier { $tier }
wf-ship-admin-upgrades-parts = Machine parts
wf-ship-admin-upgrades-fit = Fit the best parts
wf-ship-admin-upgrades-fit-tooltip = Swaps the parts in every machine aboard for the best of their kind.
wf-ship-admin-upgrades-modifiers = Hull modifiers
wf-ship-admin-upgrades-modifiers-note = A hull modifier swaps parts of the ship for others. It can't be taken back.
wf-ship-admin-upgrades-modify = Apply modifier

## Danger tab
wf-ship-admin-danger-aboard = Players aboard: { $count }
wf-ship-admin-danger-eject-heading = Empty the ship
wf-ship-admin-danger-eject-note = Brings every player aboard, and whatever they are pulling, to where you stand.
wf-ship-admin-danger-eject = Bring everyone aboard to me
wf-ship-admin-danger-strip-heading = Strip to a bare hull
wf-ship-admin-danger-strip-note = Removes the owner, every deed, the access setup, crew slots and the ship's late-join station, leaving an unclaimed hull like one on the used-ship lot. What is aboard stays.
wf-ship-admin-danger-strip = Strip the ship
wf-ship-admin-danger-delete-heading = Delete the ship
wf-ship-admin-danger-delete-note = Deletes the ship and everything aboard except players, who are never deleted with it.
wf-ship-admin-danger-bring = Bring players aboard to me first
wf-ship-admin-danger-bring-tooltip = Left unticked, players are left floating where the ship was.
wf-ship-admin-danger-delete = Delete the ship

## Chat lines to players
wf-ship-admin-notice-lost = Staff have taken { $ship } out of your ownership.
wf-ship-admin-notice-gained = Staff have made you an owner of { $ship }.
wf-ship-admin-notice-access = Staff have changed who can board { $ship }.

## Results
wf-ship-admin-result-no-permission = You don't have the permission for that.
wf-ship-admin-result-gone = That ship no longer exists.
wf-ship-admin-result-unknown = The panel can't do that yet.
wf-ship-admin-result-scanned = Appraised { $ship }.
wf-ship-admin-result-not-ship = That grid is a station or a planet's surface, so it can't have an owner.
wf-ship-admin-result-no-access = That grid has no ship access. Give it an owner first.
wf-ship-admin-result-offline = That player isn't connected. A ship can only be handed to someone who is.
wf-ship-admin-result-hangar = That is a company hangar ship. Tick "Detach from the company hangar" to hand it over.
wf-ship-admin-result-transfer-failed = The deed could not be assigned.
wf-ship-admin-result-transferred = Transferred { $ship } from { $from } to { $to }.
wf-ship-admin-result-already-owner = { $name } already owns it.
wf-ship-admin-result-owner-added = { $name } now co-owns { $ship }.
wf-ship-admin-result-not-owner = That owner is no longer on the ship.
wf-ship-admin-result-last-owner = That is the last owner. Release the ship instead.
wf-ship-admin-result-owner-removed = Removed { $name } as an owner of { $ship }.
wf-ship-admin-result-no-owner = That ship has no owner to release it from.
wf-ship-admin-result-released = Released { $ship }. It has no owner now.
wf-ship-admin-result-lock-refused = No owner's card can open it, so locking would shut them out. Give it an owner with an ID card first.
wf-ship-admin-result-locked = Locked { $ship }.
wf-ship-admin-result-unlocked = Unlocked { $ship }.
wf-ship-admin-result-mode-unavailable = { $mode } mode isn't available: the ship has no linked crew, company or hull faction for it.
wf-ship-admin-result-mode = Set { $ship } to { $mode } mode.
wf-ship-admin-result-allow-no-body = That player has no living body to carry an ID card.
wf-ship-admin-result-allow-no-card = { $name } carries no ID card.
wf-ship-admin-result-allow-no-record = { $name }'s ID card has no crew record, so no door can be keyed to it.
wf-ship-admin-result-allow-already = { $name } is already on the list, or their card holds the deed.
wf-ship-admin-result-allow-added = Added { $name } to the allow list of { $ship }.
wf-ship-admin-result-allow-missing = That entry is no longer on the list.
wf-ship-admin-result-allow-removed = Removed { $name } from the allow list of { $ship }.
wf-ship-admin-result-allow-cleared = Cleared { $count } from the allow list of { $ship }.
wf-ship-admin-result-doors-reset = Reset { $count } doors on { $ship } and cleared its code.
wf-ship-admin-result-fix-nothing = Nothing is ticked to fix.
wf-ship-admin-result-fix-running = A fix is already running on that ship.
wf-ship-admin-result-fix-started = Fixing { $ship }: { $count } hull and damage items queued.
wf-ship-admin-result-fixed = Fixed { $ship }: { $tiles } hull tiles, { $parts } parts rebuilt, { $healed } parts healed.
wf-ship-admin-result-snapshot = Re-took the repair snapshot of { $ship }.
wf-ship-admin-result-no-snapshot = That ship has no repair snapshot to restore.
wf-ship-admin-result-restored = Restored { $ship } to its repair snapshot.
wf-ship-admin-result-move-station = That grid is a station or a planet's surface. The panel only moves ships.
wf-ship-admin-result-move-no-body = You have no body on a map to bring it to.
wf-ship-admin-result-move-bad-position = That position isn't valid.
wf-ship-admin-result-move-bad-target = Pick another grid to send it to.
wf-ship-admin-result-move-failed = No clear spot was found there.
wf-ship-admin-result-moved = Moved { $ship } to { $where }.
wf-ship-admin-result-docked = Docked { $ship } at { $where }.
wf-ship-admin-result-undocked = Undocked { $ship }.
wf-ship-admin-result-halted = Halted { $ship }.
wf-ship-admin-result-pilots-cleared = Cleared the pilots of { $ship }.
wf-ship-admin-result-ftl-lock-on = { $ship } now travels with ships it is docked to.
wf-ship-admin-result-ftl-lock-off = { $ship } no longer travels with ships it is docked to.
wf-ship-admin-result-ftl-ready = Its jump drive is ready already.
wf-ship-admin-result-ftl-jumping = It is in the middle of a jump. Wait for it to arrive.
wf-ship-admin-result-ftl-reset = Reset the jump cooldown of { $ship }.
wf-ship-admin-result-bad-name = A ship's name needs 1 to { $max } characters.
wf-ship-admin-result-rename-failed = The ship could not be renamed.
wf-ship-admin-result-renamed = Renamed { $old } to { $ship }.
wf-ship-admin-result-bad-colour = That isn't a colour. Use hex, like #FFC000.
wf-ship-admin-result-iff = Changed the radar signature of { $ship }.
wf-ship-admin-result-no-shields = That ship has no shields to switch.
wf-ship-admin-result-shields-on = Raised the shields of { $ship }.
wf-ship-admin-result-shields-off = Dropped the shields of { $ship }.
wf-ship-admin-result-bad-strength = Shield strength is a number from 0 to 100.
wf-ship-admin-result-shields-restored = Restored the shields of { $ship } to full strength.
wf-ship-admin-result-shields-strength = Set the shields of { $ship } to { $percent }% strength.
wf-ship-admin-result-no-power = That ship has no batteries or APCs.
wf-ship-admin-result-power-on = Powered { $ship }.
wf-ship-admin-result-power-off = Unpowered { $ship }.
wf-ship-admin-result-protect-on = Switched { $protect } on for { $ship }.
wf-ship-admin-result-protect-off = Switched { $protect } off for { $ship }.
wf-ship-admin-result-bad-refit = That refit or tier doesn't exist.
wf-ship-admin-result-refit-no-parts = The ship has no parts the { $refit } line covers.
wf-ship-admin-result-refit-started = Refitting { $ship }: { $refit }, { $count } parts queued.
wf-ship-admin-result-refitted = Refitted { $ship }: { $refit } is now tier { $tier }.
wf-ship-admin-result-fitted = Fitted the best parts to { $count } machines on { $ship }.
wf-ship-admin-result-bad-modifier = That hull modifier doesn't exist.
wf-ship-admin-result-modified = Applied { $modifier } to { $ship }.
wf-ship-admin-result-ejected = Brought { $count } from { $ship } to you.
wf-ship-admin-result-stripped = Stripped { $ship } to a bare unclaimed hull.
wf-ship-admin-result-deleted = Deleted { $ship }.
wf-ship-admin-note-no-snapshot = It has no repair snapshot, so missing hull and parts were left out.
wf-ship-admin-note-account-only = Registered to their account only: { $reason }
wf-ship-admin-note-crew = It is the ship of crew "{ $crew }", which keeps spawning aboard.
wf-ship-admin-note-unlocked = It was unlocked, as no owner's card could open it yet; it locks itself once one can.
wf-ship-admin-note-untold = Not connected, so not told: { $names }.
