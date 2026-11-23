# Shipmaster, the Wolfgate admin ship panel

## Opening it
wg-ship-admin-button = Shipmaster
wg-ship-admin-verb = Shipmaster
wg-ship-admin-verb-description = Open Shipmaster on { $ship }.
cmd-shipmaster-desc = Opens Shipmaster, the admin ship panel, on a ship when one is named. With no ship named it starts on the one you are over; "spawn" opens it on the spawn page.
cmd-shipmaster-help = Usage: { $command } [grid entity | here | spawn]
cmd-shipmaster-not-grid = "{ $grid }" isn't a grid.
cmd-shipmaster-hint-grid = [grid entity | here | spawn]

cmd-shiprefit-desc = Brings a refit line of any ship up to a tier, free, as Shipmaster's refit does.
cmd-shiprefit-help = Usage: { $command } <grid entity | here> <refit id> <tier>
cmd-shiprefit-hint-grid = <grid entity | here>
cmd-shiprefit-hint-refit = <refit id>
cmd-shiprefit-hint-tier = <tier>
ent-WGActionShipmaster = Shipmaster
    .desc = Open Shipmaster on the ship you are over.

## Window
wg-ship-admin-title = Shipmaster
wg-ship-admin-search = Search ships, owners or designs...
wg-ship-admin-refresh = Refresh
wg-ship-admin-sort-nearest = Sort: nearest first
wg-ship-admin-sort-name = Sort: name
wg-ship-admin-sort-owner = Sort: owner
wg-ship-admin-sort-aboard = Sort: most aboard
wg-ship-admin-compass-here = here
wg-ship-admin-compass-distance = { $distance } m
wg-ship-admin-compass-other-map = jump
wg-ship-admin-page-ships = Ships
wg-ship-admin-page-spawn = Spawn a vessel
wg-ship-admin-filter-ships = Ships and ports
wg-ship-admin-filter-all = All grids
wg-ship-admin-filter-note = Ships and ports lists owned and bought ships plus the ports and bases they dock at. All grids adds every other grid: wrecks, debris and planets. The send and dock lists on the Move tab follow this filter.
wg-ship-admin-jump-tooltip = Jump to this ship
wg-ship-admin-empty = No ships found.
wg-ship-admin-count = Showing { $shown } of { $total }
wg-ship-admin-count-truncated = Showing { $shown } of the first { $total }; search to narrow it
wg-ship-admin-none-selected = Pick a ship from the list.
wg-ship-admin-row = { $owner } { $state ->
        [online] (online)
        [offline] (offline)
       *[none] { "" }
    } | aboard: { $aboard }
wg-ship-admin-summary = Owner: { $owner } | { $map } | aboard: { $aboard }
wg-ship-admin-unowned = no owner
wg-ship-admin-none = none
wg-ship-admin-locked = Locked
wg-ship-admin-unlocked = Unlocked
wg-ship-admin-needs-flag = Needs the { $flag } permission.

wg-ship-admin-confirm = Confirm?
wg-ship-admin-notify = Tell the owners
wg-ship-admin-notify-tooltip = Sends the old and new owners a chat line saying staff changed the ship. Untick it for a quiet fix; the admin log records it either way.
wg-ship-admin-unclaimed = Unclaimed

## Tabs
wg-ship-admin-tab-overview = Overview
wg-ship-admin-tab-ownership = Ownership
wg-ship-admin-tab-access = Access
wg-ship-admin-tab-repair = Repair
wg-ship-admin-tab-move = Move
wg-ship-admin-tab-systems = Systems
wg-ship-admin-tab-upgrades = Upgrades
wg-ship-admin-tab-danger = Danger

## Tags
wg-ship-admin-tag-locked = locked
wg-ship-admin-tag-docked = docked
wg-ship-admin-tag-inftl = in FTL
wg-ship-admin-tag-snapshot = has repair snapshot
wg-ship-admin-tag-crewlinked = crew ship
wg-ship-admin-tag-company = company ship
wg-ship-admin-tag-hangar = hangar ship
wg-ship-admin-tag-fixing = being fixed

## Access modes and owner kinds
wg-ship-admin-mode-private = Private
wg-ship-admin-mode-faction = Faction
wg-ship-admin-mode-crew = Crew
wg-ship-admin-mode-company = Company
wg-ship-admin-mode-public = Public
wg-ship-admin-owner-deedcard = Deed card
wg-ship-admin-owner-voucher = Deed voucher
wg-ship-admin-owner-account = Account
wg-ship-admin-owner-unstamped = { $name } (no player on record)
wg-ship-admin-owner-stamped = { $name } ({ $username }, { $online ->
        [true] online
       *[false] offline
    })

## Overview tab
wg-ship-admin-scan = Scan value
wg-ship-admin-scan-tooltip = Appraise the ship. Slow on a large hull, so it only runs when asked.
wg-ship-admin-overview-ship = Ship
wg-ship-admin-overview-design = Design
wg-ship-admin-overview-entity = Entity
wg-ship-admin-overview-place = Position
wg-ship-admin-overview-place-value = { $map } ({ $x }, { $y })
wg-ship-admin-overview-mass = Mass
wg-ship-admin-overview-docked = Docked to
wg-ship-admin-overview-value = Value
wg-ship-admin-overview-value-amount = { $value } spesos
wg-ship-admin-overview-not-scanned = not scanned
wg-ship-admin-overview-state = State
wg-ship-admin-overview-owners = Owners
wg-ship-admin-overview-record-owner = Named owner
wg-ship-admin-overview-registered = Shipyard account
wg-ship-admin-overview-account = { $name } ({ $online ->
        [true] online
       *[false] offline
    })
wg-ship-admin-overview-no-owners = Nobody holds the deed and no account is registered.
wg-ship-admin-overview-access = Access
wg-ship-admin-overview-no-access = This grid has no ship access.
wg-ship-admin-overview-mode = Mode
wg-ship-admin-overview-lock = Lock
wg-ship-admin-overview-allowed = Allow list
wg-ship-admin-overview-crew = Crew
wg-ship-admin-overview-company = Company
wg-ship-admin-overview-faction = Hull faction
wg-ship-admin-overview-aboard = Aboard ({ $count })
wg-ship-admin-overview-aboard-more = and { $count } more

## Ownership tab
wg-ship-admin-ownership-give = Hand the ship to a player
wg-ship-admin-ownership-give-note = A transfer takes the ship from every current owner. The new owner gets the deed on their ID card when they carry one without a deed; otherwise the ship is registered to their account alone.
wg-ship-admin-ownership-nobody-picked = Pick a player above.
wg-ship-admin-ownership-picked = Picked: { $name } ({ $username })
wg-ship-admin-ownership-keep-previous = Keep previous owners as crew
wg-ship-admin-ownership-keep-previous-tooltip = Puts each ID card that held the deed on the allow list, so the old owners can still open the doors.
wg-ship-admin-ownership-clear-allow = Clear the allow list
wg-ship-admin-ownership-clear-allow-tooltip = Removes everyone the old owner let aboard. Left unticked, their guests keep their access.
wg-ship-admin-ownership-drop-company = Drop the company link
wg-ship-admin-ownership-drop-company-tooltip = Stops the ship flying for the old owner's player company. It is replaced by the new owner's company when their ID card has one.
wg-ship-admin-ownership-leave-crew = Take it out of Crew mode
wg-ship-admin-ownership-leave-crew-tooltip = The linked crew keeps spawning aboard, but its cards stop opening the doors.
wg-ship-admin-ownership-detach-hangar = Detach from the company hangar
wg-ship-admin-ownership-detach-hangar-tooltip = A hangar ship belongs to its company and can't change hands until it is detached.
wg-ship-admin-ownership-faction-keep = Hull faction: keep
wg-ship-admin-ownership-faction-fromcard = Hull faction: from the new owner's ID
wg-ship-admin-ownership-faction-clear = Hull faction: none
wg-ship-admin-ownership-transfer = Transfer ownership
wg-ship-admin-ownership-add = Add as co-owner
wg-ship-admin-ownership-add-tooltip = Registers the ship to their account as well. Nobody loses it.
wg-ship-admin-ownership-remove = Remove
wg-ship-admin-ownership-remove-last = The last owner can't be removed. Release the ship instead.
wg-ship-admin-ownership-name = Name
wg-ship-admin-ownership-rename = Rename
wg-ship-admin-ownership-affiliation = Faction and company
wg-ship-admin-ownership-affiliation-note = Changes who the ship flies for without changing its owner.
wg-ship-admin-ownership-set-faction = Set hull faction
wg-ship-admin-ownership-set-faction-tooltip = The faction the hull flies for: its radar label colour, Faction access and which NPCs treat it as a friend.
wg-ship-admin-ownership-set-company = Set player company
wg-ship-admin-ownership-company-gone = The company you picked no longer exists. Pick again.
wg-ship-admin-ownership-set-company-tooltip =The player company the ship flies for: its radar colour and Company access. While it flies for one, its hull faction is set aside.
wg-ship-admin-ownership-release-heading = Leave it with no owner
wg-ship-admin-ownership-release-note = Every owner loses the ship and its doors unlock. Its helm stays locked, so only staff fly it until it is transferred.
wg-ship-admin-ownership-release = Release ownership
wg-ship-admin-label-previous-owner = Previous owner

## Access tab
wg-ship-admin-access-status = Mode: { $mode } | { $locked ->
        [true] Locked
       *[false] Unlocked
    }
wg-ship-admin-access-no-key = No owner's card can open it, so it can't be locked.
wg-ship-admin-access-lock = Lock the ship
wg-ship-admin-access-unlock = Unlock the ship
wg-ship-admin-access-set-mode = Set mode
wg-ship-admin-access-set-mode-tooltip = Crew and Company only work on a ship already linked to one. Faction needs a hull faction. Public lets anyone through every door that has no rule of its own, locked or not; lockers stay as they are.
wg-ship-admin-access-nobody = Nobody is on the allow list.
wg-ship-admin-access-add-heading = Let a player aboard
wg-ship-admin-access-add-note = Adds the ID card the player carries right now. They need a living body and a card with a crew record.
wg-ship-admin-access-add = Add to the allow list
wg-ship-admin-access-clear = Clear the allow list
wg-ship-admin-access-reset-doors = Reset door rules and code
wg-ship-admin-access-reset-doors-tooltip = Puts every door back on the default rule and clears the ship's door code. Doors in a zone are left alone.
wg-ship-admin-access-console-note = For one door's rule, its list or its code, use the Access tab on the ship's own console. Staff can open it on any ship, and unlock any helm with the console's Unlock verb.
wg-ship-admin-label-staff = Added by staff

## Repair tab
wg-ship-admin-repair-fix-heading = Fix
wg-ship-admin-repair-fix-note = A fix never deletes what players moved or built. Hull and parts come back only where the repair snapshot says something is missing.
wg-ship-admin-repair-not-scanned = Not scanned. Scan to count what is missing or damaged.
wg-ship-admin-repair-damage = Missing hull tiles: { $tiles } | missing parts: { $parts } | damaged parts: { $damaged }
wg-ship-admin-repair-progress = Fixing: { $done } of { $total } hull and damage items done.
wg-ship-admin-repair-scan = Scan damage
wg-ship-admin-repair-fix = Fix the ticked parts
wg-ship-admin-repair-part-structures = Damaged parts
wg-ship-admin-repair-part-structures-tooltip = Heals every damaged wall, machine and fitting, and clears crash faults on APCs.
wg-ship-admin-repair-part-hull = Missing hull and parts
wg-ship-admin-repair-part-hull-tooltip = Re-lays hull tiles that are gone and rebuilds parts that are gone, from the repair snapshot. Needs a snapshot.
wg-ship-admin-repair-part-air = Air
wg-ship-admin-repair-part-air-tooltip = Rebuilds the ship's atmosphere, which also puts out fires on the floor.
wg-ship-admin-repair-part-fires = Burning objects
wg-ship-admin-repair-part-fires-tooltip = Puts out everything and everyone on fire aboard.
wg-ship-admin-repair-part-power = Power
wg-ship-admin-repair-part-power-tooltip = Fills every SMES, substation and APC and switches the breakers on.
wg-ship-admin-repair-part-fuel = Fuel
wg-ship-admin-repair-part-fuel-tooltip = Fills and lights generators, swaps spent AME jars and fuel rods, and trims reactors.
wg-ship-admin-repair-part-bolts = Door bolts
wg-ship-admin-repair-part-bolts-tooltip = Raises the bolts on every door, except doors the owner sealed.
wg-ship-admin-repair-part-consoles = Consoles
wg-ship-admin-repair-part-consoles-tooltip = Clears stuck pilots, reconnects the helm, mounts guns on the hardpoints under them and relinks gunnery at once.
wg-ship-admin-repair-part-trash = Loose trash
wg-ship-admin-repair-part-trash-tooltip = Deletes trash lying loose on the floor: wrappers, cans, butts and the like. Nothing held, worn or in storage.
wg-ship-admin-repair-part-puddles = Puddles
wg-ship-admin-repair-part-puddles-tooltip = Mops up every puddle on the floor.
wg-ship-admin-repair-part-lights = Lights
wg-ship-admin-repair-part-lights-tooltip = Puts a new bulb in every light fitting that is empty, broken or burned out.
wg-ship-admin-repair-quick-heading = Quick fixes
wg-ship-admin-repair-quick-note = Each runs that one part of a fix at once.
wg-ship-admin-repair-quick-air = Fix air
wg-ship-admin-repair-quick-fires = Put out fires
wg-ship-admin-repair-quick-trash = Remove trash
wg-ship-admin-repair-quick-puddles = Mop up puddles
wg-ship-admin-repair-quick-lights = Fix lights
wg-ship-admin-repair-snapshot-heading = Repair snapshot
wg-ship-admin-repair-has-snapshot = This ship has a repair snapshot.
wg-ship-admin-repair-no-snapshot = This ship has no repair snapshot, so missing hull and parts can't be rebuilt.
wg-ship-admin-repair-snapshot-note = The snapshot is what the ship is rebuilt from: taken when it was bought or spawned.
wg-ship-admin-repair-retake = Re-take snapshot
wg-ship-admin-repair-retake-tooltip = Replaces the snapshot with the ship as it stands now. Do this after a player rebuilt it on purpose; the old snapshot is gone for good.
wg-ship-admin-repair-restore = Restore to snapshot
wg-ship-admin-repair-restore-tooltip = Deletes and rebuilds every part that moved since the snapshot and re-lays every hull tile. Player changes are lost.

## Move tab
wg-ship-admin-move-status = { $map } ({ $x }, { $y }) | speed: { $speed } | jump drive: { $ftl }
wg-ship-admin-move-ftl-ready = ready
wg-ship-admin-move-ftl-available = ready
wg-ship-admin-move-ftl-invalid = unknown
wg-ship-admin-move-ftl-starting = spooling up
wg-ship-admin-move-ftl-travelling = in transit
wg-ship-admin-move-ftl-arriving = arriving
wg-ship-admin-move-ftl-cooldown = cooling down
wg-ship-admin-move-heading = Move the ship
wg-ship-admin-move-note = A move is instant. The ship is undocked first, lands in the nearest clear spot and comes to rest. Stations and planets can't be moved from here.
wg-ship-admin-move-to-me = Bring it to me
wg-ship-admin-move-x = X
wg-ship-admin-move-y = Y
wg-ship-admin-move-to-position = Send to this position
wg-ship-admin-move-to-grid-heading = Send it to another grid
wg-ship-admin-move-to-grid-note = The grid list follows the ship filter on the left: Ships and ports, or All grids. Pick a dock to dock at one in particular; a dock another ship holds can't be picked.
wg-ship-admin-move-next-to = Send next to
wg-ship-admin-move-dock-at = Dock at
wg-ship-admin-move-dock-any = Any free dock
wg-ship-admin-move-dock-free = { $dock } (free)
wg-ship-admin-move-dock-used = { $dock } (in use: { $with })
wg-ship-admin-dock-unnamed = Dock { $number }
wg-ship-admin-move-unstick = Stop Knotting
wg-ship-admin-move-unstick-tooltip = Frees a ship caught on another grid or wedged in debris: undocks it, cuts ropes and tractor beams on it, and sets it down at rest in the nearest clear space.
wg-ship-admin-move-orbit = Return to orbit
wg-ship-admin-move-orbit-tooltip = Lifts a ship that is down on a planet, or flying in its air, up to the planet's orbit, with the ships docked to it.
wg-ship-admin-move-spot = a spot on { $map }
wg-ship-admin-move-free-heading = Free a stuck ship
wg-ship-admin-move-undock = Undock
wg-ship-admin-move-undock-tooltip = Releases every dock on the ship.
wg-ship-admin-move-halt = Halt
wg-ship-admin-move-halt-tooltip = Stops a ship that is drifting or spinning.
wg-ship-admin-move-clear-pilots = Clear pilots
wg-ship-admin-move-clear-pilots-tooltip = Takes everyone off the helms, including pilots who are stuck on one.
wg-ship-admin-move-reset-ftl = Reset jump cooldown
wg-ship-admin-move-reset-ftl-tooltip = Ends the wait after a jump so the ship can jump again at once.
wg-ship-admin-move-ftl-lock-on = Travel with docked ships: turn on
wg-ship-admin-move-ftl-lock-off = Travel with docked ships: turn off
wg-ship-admin-move-ftl-lock-tooltip = While on, the ship is carried along when a ship it is docked to jumps.

## Systems tab
wg-ship-admin-systems-radar = Radar signature
wg-ship-admin-systems-colour-hint = #RRGGBB
wg-ship-admin-systems-set-colour = Set colour
wg-ship-admin-systems-hidden = Hidden from radar
wg-ship-admin-systems-hidden-tooltip = Other ships' radars don't show it at all.
wg-ship-admin-systems-label-hidden = Label hidden
wg-ship-admin-systems-label-hidden-tooltip = Radars show the hull but not its name.
wg-ship-admin-systems-shields = Shields
wg-ship-admin-systems-shields-status = Shield strength: { $percent }%
wg-ship-admin-systems-shields-none = This ship has no shield generator.
wg-ship-admin-systems-shields-on = Raise shields
wg-ship-admin-systems-shields-off = Drop shields
wg-ship-admin-systems-shields-restore = Restore to full
wg-ship-admin-systems-shields-restore-tooltip = Puts every shield generator back at full strength and ends a recharge or overload, so the shields come straight back up if they are powered and raised.
wg-ship-admin-systems-shields-strength-hint = 0 to 100
wg-ship-admin-systems-shields-set = Set strength %
wg-ship-admin-systems-shields-set-tooltip = Sets how much strength the shields have left. Below a hundred it wears off as the generators heal.
wg-ship-admin-systems-power = Power
wg-ship-admin-systems-power-on = Power on
wg-ship-admin-systems-power-off = Power off
wg-ship-admin-systems-power-off-tooltip = Drains every battery aboard and switches the breakers off. The crew gets it back by flipping breakers.
wg-ship-admin-systems-protect = Protections
wg-ship-admin-systems-protect-blastproof = Blast-proof
wg-ship-admin-systems-protect-blastproof-tooltip = Explosions and EMPs don't harm the ship and blasts don't break its floors.
wg-ship-admin-systems-protect-pacified = Pacified
wg-ship-admin-systems-protect-pacified-tooltip = Nobody aboard can fight.
wg-ship-admin-systems-protect-cleanupimmune = Safe from cleanup
wg-ship-admin-systems-protect-cleanupimmune-tooltip = The cleanup of abandoned grids never deletes it.
wg-ship-admin-systems-protect-gunsdisabled = Ship guns disabled
wg-ship-admin-systems-protect-gunsdisabled-tooltip = Its ship guns don't fire.
wg-ship-admin-systems-protect-ftlunrestricted = No jump limits
wg-ship-admin-systems-protect-ftlunrestricted-tooltip = It jumps whatever its mass, with other ships close by, and into no-jump zones.

## Upgrades tab
wg-ship-admin-upgrades-refits = Refits
wg-ship-admin-upgrades-refits-note = Brings a refit line up to a tier at no cost. The ship then owns that tier, as if it had bought it at a drydock.
wg-ship-admin-upgrades-tier = tier { $tier } of { $max }
wg-ship-admin-upgrades-to-tier = To tier { $tier }
wg-ship-admin-upgrades-parts = Machine parts
wg-ship-admin-upgrades-fit = Fit the best parts
wg-ship-admin-upgrades-fit-tooltip = Swaps the parts in every machine aboard for the best of their kind.
wg-ship-admin-upgrades-modifiers = Hull modifiers
wg-ship-admin-upgrades-modifiers-note = A hull modifier swaps parts of the ship for others. It can't be taken back.
wg-ship-admin-upgrades-modify = Apply modifier

## Danger tab
wg-ship-admin-danger-aboard = Players aboard: { $count }
wg-ship-admin-danger-event-heading = Aim an event at this ship
wg-ship-admin-danger-event-note = An event aboard picks this ship and a spot on it, or fails rather than going elsewhere. Arriving ships are set down beside it. It starts after the event's own warning delay, usually 10 to 20 seconds, and is in the admin log.
wg-ship-admin-danger-event-start = Start event
wg-ship-admin-danger-event-aboard = Aboard: { $name }
wg-ship-admin-danger-event-arrival = Arrival: { $name }
wg-ship-admin-danger-eject-heading = Empty the ship
wg-ship-admin-danger-eject-note = Brings every player aboard, and whatever they are pulling, to where you stand.
wg-ship-admin-danger-eject = Bring everyone aboard to me
wg-ship-admin-danger-strip-heading = Strip to a bare hull
wg-ship-admin-danger-strip-note = Removes the owner, every deed, the access setup, crew slots and the ship's late-join station, leaving an unclaimed hull like one on the used-ship lot. What is aboard stays.
wg-ship-admin-danger-strip = Strip the ship
wg-ship-admin-danger-delete-heading = Delete the ship
wg-ship-admin-danger-delete-note = Deletes the ship and everything aboard except players, who are never deleted with it.
wg-ship-admin-danger-bring = Bring players aboard to me first
wg-ship-admin-danger-bring-tooltip = Left unticked, players are left floating where the ship was.
wg-ship-admin-danger-delete = Delete the ship

## Chat lines to players
wg-ship-admin-notice-lost = Staff have taken { $ship } out of your ownership.
wg-ship-admin-notice-gained = Staff have made you an owner of { $ship }.
wg-ship-admin-notice-access = Staff have changed who can board { $ship }.

## Results
wg-ship-admin-result-no-permission = You don't have the permission for that.
wg-ship-admin-result-gone = That ship no longer exists.
wg-ship-admin-result-unknown = The panel can't do that yet.
wg-ship-admin-result-scanned = Appraised { $ship }.
wg-ship-admin-result-not-ship = That grid is a station or a planet's surface, so it can't have an owner.
wg-ship-admin-result-no-access = That grid has no ship access. Give it an owner first.
wg-ship-admin-result-offline = That player isn't connected. A ship can only be handed to someone who is.
wg-ship-admin-result-hangar = That is a company hangar ship. Tick "Detach from the company hangar" to hand it over.
wg-ship-admin-result-transfer-failed = The deed could not be assigned.
wg-ship-admin-result-transferred = Transferred { $ship } from { $from } to { $to }.
wg-ship-admin-result-already-owner = { $name } already owns it.
wg-ship-admin-result-owner-added = { $name } now co-owns { $ship }.
wg-ship-admin-result-not-owner = That owner is no longer on the ship.
wg-ship-admin-result-last-owner = That is the last owner. Release the ship instead.
wg-ship-admin-result-owner-removed = Removed { $name } as an owner of { $ship }.
wg-ship-admin-result-no-owner = That ship has no owner to release it from.
wg-ship-admin-result-released = Released { $ship }. It has no owner now.
wg-ship-admin-result-lock-refused = No owner's card can open it, so locking would shut them out. Give it an owner with an ID card first.
wg-ship-admin-result-locked = Locked { $ship }.
wg-ship-admin-result-unlocked = Unlocked { $ship }.
wg-ship-admin-result-mode-unavailable = { $mode } mode isn't available: the ship has no linked crew, company or hull faction for it.
wg-ship-admin-result-mode = Set { $ship } to { $mode } mode.
wg-ship-admin-result-allow-no-body = That player has no living body to carry an ID card.
wg-ship-admin-result-allow-no-card = { $name } carries no ID card.
wg-ship-admin-result-allow-no-record = { $name }'s ID card has no crew record, so no door can be keyed to it.
wg-ship-admin-result-allow-already = { $name } is already on the list, or their card holds the deed.
wg-ship-admin-result-allow-added = Added { $name } to the allow list of { $ship }.
wg-ship-admin-result-allow-missing = That entry is no longer on the list.
wg-ship-admin-result-allow-removed = Removed { $name } from the allow list of { $ship }.
wg-ship-admin-result-allow-cleared = Cleared { $count } from the allow list of { $ship }.
wg-ship-admin-result-doors-reset = Reset { $count } doors on { $ship } and cleared its code.
wg-ship-admin-result-fix-nothing = Nothing is ticked to fix.
wg-ship-admin-result-fix-running = A fix is already running on that ship.
wg-ship-admin-result-fix-started = Fixing { $ship }: { $count } hull and damage items queued.
wg-ship-admin-result-fixed = Fixed { $ship }: { $tiles } hull tiles, { $parts } parts rebuilt, { $healed } parts healed.
wg-ship-admin-result-snapshot = Re-took the repair snapshot of { $ship }.
wg-ship-admin-result-no-snapshot = That ship has no repair snapshot to restore.
wg-ship-admin-result-restored = Restored { $ship } to its repair snapshot.
wg-ship-admin-result-move-station = That grid is a station or a planet's surface. The panel only moves ships.
wg-ship-admin-result-move-no-body = You have no body on a map to bring it to.
wg-ship-admin-result-move-bad-position = That position isn't valid.
wg-ship-admin-result-move-bad-target = Pick another grid to send it to.
wg-ship-admin-result-move-failed = No clear spot was found there.
wg-ship-admin-result-moved = Moved { $ship } to { $where }.
wg-ship-admin-result-docked = Docked { $ship } at { $where }.
wg-ship-admin-result-undocked = Undocked { $ship }.
wg-ship-admin-result-halted = Halted { $ship }.
wg-ship-admin-result-pilots-cleared = Cleared the pilots of { $ship }.
wg-ship-admin-result-ftl-lock-on = { $ship } now travels with ships it is docked to.
wg-ship-admin-result-ftl-lock-off = { $ship } no longer travels with ships it is docked to.
wg-ship-admin-result-ftl-ready = Its jump drive is ready already.
wg-ship-admin-result-ftl-jumping = It is in the middle of a jump. Wait for it to arrive.
wg-ship-admin-result-ftl-reset = Reset the jump cooldown of { $ship }.
wg-ship-admin-result-bad-name = A ship's name needs 1 to { $max } characters.
wg-ship-admin-result-rename-failed = The ship could not be renamed.
wg-ship-admin-result-renamed = Renamed { $old } to { $ship }.
wg-ship-admin-result-bad-colour = That isn't a colour. Use hex, like #FFC000.
wg-ship-admin-result-iff = Changed the radar signature of { $ship }.
wg-ship-admin-result-no-shields = That ship has no shields to switch.
wg-ship-admin-result-shields-on = Raised the shields of { $ship }.
wg-ship-admin-result-shields-off = Dropped the shields of { $ship }.
wg-ship-admin-result-bad-strength = Shield strength is a number from 0 to 100.
wg-ship-admin-result-shields-restored = Restored the shields of { $ship } to full strength.
wg-ship-admin-result-shields-strength = Set the shields of { $ship } to { $percent }% strength.
wg-ship-admin-result-no-power = That ship has no batteries or APCs.
wg-ship-admin-result-power-on = Powered { $ship }.
wg-ship-admin-result-power-off = Unpowered { $ship }.
wg-ship-admin-result-protect-on = Switched { $protect } on for { $ship }.
wg-ship-admin-result-protect-off = Switched { $protect } off for { $ship }.
wg-ship-admin-result-bad-refit = That refit or tier doesn't exist.
wg-ship-admin-result-refit-no-parts = The ship has no parts the { $refit } line covers.
wg-ship-admin-result-refit-started = Refitting { $ship }: { $refit }, { $count } parts queued.
wg-ship-admin-result-refitted = Refitted { $ship }: { $refit } is now tier { $tier }.
wg-ship-admin-result-fitted = Fitted the best parts to { $count } machines on { $ship }.
wg-ship-admin-result-bad-modifier = That hull modifier doesn't exist.
wg-ship-admin-result-modified = Applied { $modifier } to { $ship }.
wg-ship-admin-result-ejected = Brought { $count } from { $ship } to you.
wg-ship-admin-result-stripped = Stripped { $ship } to a bare unclaimed hull.
wg-ship-admin-result-deleted = Deleted { $ship }.
wg-ship-admin-result-bad-faction = That faction doesn't exist.
wg-ship-admin-result-faction = { $ship } now flies for { $faction }.
wg-ship-admin-result-faction-company-ship = { $ship } flies for a player company, so its hull faction is set aside: it flies for { $faction } once it leaves the company.
wg-ship-admin-result-bad-company = That company doesn't exist any more.
wg-ship-admin-result-company = { $ship } now flies for the company { $company }.
wg-ship-admin-result-company-cleared = { $ship } no longer flies for a player company.
wg-ship-admin-result-jump-failed = { $ship } has no open floor to put you on. Ghost to jump there.
wg-ship-admin-result-jumped = Jumped to { $ship }.
wg-ship-admin-result-spawned = Spawned { $ship }. It is selected on the Ships page.
wg-ship-admin-result-hopping = It is hopping to or from a planet. Wait for it to land.
wg-ship-admin-result-unstuck =Freed { $ship } and set it down in clear space.
wg-ship-admin-result-not-on-planet = { $ship } isn't down on a planet.
wg-ship-admin-result-orbit-refused = { $ship } can't lift off: { $reason }
wg-ship-admin-result-orbit = Sending { $ship } up to orbit.
wg-ship-admin-result-dock-occupied = { $dock } is in use by { $with }.
wg-ship-admin-result-dock-no-fit = None of the docks of { $ship } fits { $dock }.
wg-ship-admin-result-docked-at = Docked { $ship } at { $dock } on { $where }.
wg-ship-admin-result-bad-event = That event can't be aimed at a ship.
wg-ship-admin-result-event-no-station = { $ship } belongs to no station, so events can't find it. Only arriving ships can be sent to it.
wg-ship-admin-result-event = Started { $event } aboard { $ship }.
wg-ship-admin-result-event-queued = Queued { $event } aimed at { $ship }. It starts after its warning delay; you're told here if it finds nothing aboard.
wg-ship-admin-result-event-arrival = Started { $event }; its ships arrive beside { $ship }.
wg-ship-admin-result-event-arrival-partial = Started { $event }, but only { $arrived } of its { $loaded } ships could be set down beside { $ship }.
wg-ship-admin-result-event-shared-station = { $ship } shares its station, { $station }, with other grids, and an event aboard would reach them too. Only arriving ships can be sent to it.
wg-ship-admin-result-event-no-map ={ $ship } isn't on a map, so no event can find it.
wg-ship-admin-result-event-refused = { $event } refused to start; its ships may have failed to load.
wg-ship-admin-result-event-missed = { $event } found nothing it could use aboard { $ship }, so nothing happened there.
wg-ship-admin-gone-ship = the ship, which is gone
wg-ship-admin-result-cleaned = Removed { $trash } trash, mopped { $puddles } puddles and fixed { $lights } lights.
wg-ship-admin-note-no-snapshot =It has no repair snapshot, so missing hull and parts were left out.
wg-ship-admin-note-account-only = Registered to their account only: { $reason }
wg-ship-admin-note-crew = It is the ship of crew "{ $crew }", which keeps spawning aboard.
wg-ship-admin-note-unlocked = It was unlocked, as no owner's card could open it yet; it locks itself once one can.
wg-ship-admin-note-untold = Not connected, so not told: { $names }.
