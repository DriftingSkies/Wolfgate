## The contract board, its messages and the Contracts PDA app.

wg-contract-board-title = Contract Manager
wg-contract-board-name = the contract board
wg-contract-program-name = Contracts
wg-contract-station-unknown = an unknown station
wg-contract-designation-unknown = (unconfirmed)
wg-contract-thalers = {$amount} {$amount ->
        [one] thaler
       *[other] thalers
    }

wg-contract-rank-hostile = Hostile
wg-contract-rank-distrusted = Distrusted
wg-contract-rank-applicant = Applicant
wg-contract-rank-junior = Junior Contractor
wg-contract-rank-member = Contractor
wg-contract-rank-experienced = Experienced Contractor
wg-contract-rank-senior = Senior Contractor

wg-contract-category-all = All offers
wg-contract-category-delivery = Delivery
wg-contract-category-hauling = Hauling
wg-contract-category-supply = Supply
wg-contract-category-mining = Mining
wg-contract-category-bounty = Bounty Hunting
wg-contract-category-salvage = Salvage
wg-contract-category-escort = Escort
wg-contract-category-science = Science
wg-contract-category-planetary = Planetary

wg-contract-tier-short = T{$tier}
wg-contract-tier = Risk tier {$tier}

wg-contract-status-offered = Offered
wg-contract-status-active = In progress
wg-contract-status-completed = Completed
wg-contract-status-failed = Failed
wg-contract-status-abandoned = Abandoned
wg-contract-status-void = Called off

wg-contract-time-left = {$time} left
wg-contract-time-allowed = {$minutes} min allowed

## Board window

wg-contract-tab-offers = Offers ({$count})
wg-contract-tab-accepted = Accepted ({$count}/{$max})
wg-contract-tab-history = History
wg-contract-tab-standing = Standing
wg-contract-rail-entry = {$name} ({$count})
wg-contract-board-slots = {$crew ->
    [1] Solo: {$slots} contract slot. A crew of {$next} would hold more.
   *[other] Crew of {$crew}: {$slots} contract slots, shared. A crew of {$next} would hold one more.
} Track them in the Contracts app on your PDA.
wg-contract-board-no-character = Contracts are held by a character. Ghosts may only browse.
wg-contract-board-owed = {$pay} {$pay ->
        [one] thaler
       *[other] thalers
    } owed to you will be paid when your account can take it.

wg-contract-empty-offers = Nothing posted in this category right now. Contractors post again every few minutes.
wg-contract-empty-planetary = Planetary operations open once surface survey licences are issued. Check back later.
wg-contract-empty-accepted = You hold no contracts. Take one from any contract board.
wg-contract-empty-history = Nothing finished yet this shift.

wg-contract-details-none = Select a contract.
wg-contract-details-title = [font size=14][bold]{$title}[/bold][/font]
wg-contract-details-objectives = OBJECTIVES
wg-contract-details-objective = {$done ->
    [true] [color=#7fd18b]+[/color] [color=#8b98a5]{$text}[/color]
   *[false] [color=#5fc3e4]>[/color] {$text}
}
wg-contract-details-location = LOCATION
wg-contract-details-coordinates = Coordinates: {$x}, {$y} (enter them on your helm's waypoint panel)
wg-contract-details-terms = TERMS
wg-contract-details-reward = Reward
wg-contract-details-deposit = Deposit
wg-contract-details-deposit-none = None
wg-contract-details-time = Time limit
wg-contract-details-deadline = Time left
wg-contract-details-offer-ends = Offer ends in

wg-contract-action-accept = {$deposit ->
    [0] Accept contract
   *[other] Accept contract (pay {$deposit} deposit)
}
wg-contract-action-abandon = Abandon contract
wg-contract-action-abandon-confirm = {$deposit ->
    [0] Press again to abandon
   *[other] Press again to abandon and lose the {$deposit} deposit
}

wg-contract-standing-help = Standing is kept between shifts. It rises with each contract you finish, and falls when one fails, is abandoned, works against a contractor's interests, or when you fire on a target someone else holds. Each shift it drifts a little back toward neutral. Tier 2 needs Applicant, tier 3 and up need more; Distrusted gets tier 1 only, and Hostile contractors won't deal with you.
wg-contract-standing-absent = {$description} (not in the sector this shift)
wg-contract-standing-progress = {$points} / {$next} to the next rank
wg-contract-standing-max = {$points}: top rank

wg-contract-pda-hint = {$offers} offers on the boards. {$crew ->
    [1] Solo: {$slots} slot.
   *[other] Crew of {$crew}: {$slots} shared slots.
}

## Why an offer cannot be taken

wg-contract-locked-no-character = You need to be playing a character to take contracts.
wg-contract-locked-rank = Needs the rank of {$rank} with this contractor.
wg-contract-locked-full = Your crew already holds {$max ->
    [one] its one contract. A bigger crew can hold more.
   *[other] all {$max} of its contracts. A bigger crew can hold more.
}
wg-contract-locked-requires = You need to be carrying: {$items}.
wg-contract-locked-crew = Needs a crew of {$needed} in the sector; yours has {$size}. Form a crew in Crew Setup.
wg-contract-locked-company = {$contractor} only deals with its own people.
wg-contract-locked-hostile = {$contractor} won't deal with you. Your standing with them is Hostile.
wg-contract-error-gone = Someone else took that contract.
wg-contract-error-deposit = Your account can't cover the {$deposit} {$deposit ->
        [one] thaler
       *[other] thalers
    } deposit.
wg-contract-error-start = The contract fell through before it started. Your deposit was returned.

## Messages to the holder

wg-contract-message-wrap = [color=#9fd3e0][bold]Contracts:[/bold] {$message}[/color]
wg-contract-accepted = Accepted "{$title}". You have {$deadline} minutes. Track it in the Contracts app on your PDA.
wg-contract-abandoned = Abandoned "{$title}". Any deposit is forfeit.
wg-contract-completed = "{$title}" complete. {$pay} {$pay ->
        [one] thaler
       *[other] thalers
    } paid to your account{$crew ->
        [1] .
       *[other] , your share of the crew's pay.
    }{$cut ->
        [0] {""}
       *[other] {" "}Your company took {$cut}.
    }
wg-contract-completed-owed = "{$title}" complete. {$pay} {$pay ->
        [one] thaler is
       *[other] thalers are
    } owed to you and will be paid when your account can take it.
wg-contract-failed = "{$title}" failed: {$reason} Any deposit is forfeit.
wg-contract-void = "{$title}" was called off: {$reason} {$deposit ->
    [0] {""}
   *[other] Your {$deposit} {$deposit ->
        [one] thaler
       *[other] thalers
    } deposit was returned.
}
wg-contract-owed-paid = {$pay} {$pay ->
        [one] thaler owed from finished contracts was
       *[other] thalers owed from finished contracts were
    } paid to your account.
wg-contract-package-delivered = "{$title}": {$done} of {$count} delivered.
wg-contract-anomaly-surveyed = "{$title}": {$done} of {$count} anomalies surveyed.

wg-contract-reason-expired = the deadline passed.
wg-contract-reason-lost = the cargo was destroyed or lost.
wg-contract-reason-escaped = the target got away.
wg-contract-reason-target-gone = the target is no longer out there.
wg-contract-reason-escort-lost = the ship you were escorting was lost.
wg-contract-reason-escort-surrendered = the ship you were escorting surrendered.
wg-contract-reason-anomaly-collapsed = an anomaly collapsed before it was surveyed.
wg-contract-reason-hull-lost = the hull was destroyed.

wg-contract-interfered = You fired on a target held by {$claimant} under a {$contractor} contract. Your standing with {$contractor} fell by {$points}.

## Contract cargo

wg-contract-package-name = {$name} for {$destination}
wg-contract-crate-name = contract crate #{$id} for {$destination}
wg-contract-crate-void-name = empty contract crate
wg-contract-wreck-name = Wreck (contract #{$id})
wg-contract-item-examine = [color=#9fd3e0]Contract #{$id}, "{$title}", held by {$holder}. Deliver to {$destination}.[/color]
wg-contract-crate-line = {$done ->
    [true] [color=#7fd18b]{$name}: {$have}/{$count}[/color]
   *[false] {$name}: {$have}/{$count}
}
wg-contract-crate-missing-line = {$count} {$name}
wg-contract-crate-short = The manifest is short: {$missing}.

## Where to go, and what to do

wg-contract-location-courier = Pick up at {$origin}, deliver to {$destination} ({$km} km).
wg-contract-location-supply = Collect the manifest crate at the board; deliver it closed to {$destination} ({$km} km).
wg-contract-location-bounty = {$active ->
    [true] Target last reported about {$km} km out from {$home}. It shows on the sector radar.
   *[false] Target reported about {$km} km out from {$home}. Exact position on accepting.
}
wg-contract-location-recovery = {$active ->
    [true] Wreck about {$km} km out from {$home}. Bring the find to {$destination}.
   *[false] Wreck reported about {$km} km out from {$home}. Exact position on accepting; bring the find to {$destination}.
}

wg-contract-objective-courier = Deliver the cargo to {$destination} ({$done}/{$count})
wg-contract-objective-goods = {$active ->
    [true] Pack {$name} ({$have}/{$count})
   *[false] Pack {$count} {$name}
}
wg-contract-objective-crate = Close the crate aboard {$destination}
wg-contract-objective-bounty = Destroy {$target}
wg-contract-objective-recover = Recover the {$item} from the {$planet ->
    [true] site
   *[false] wreck
}
wg-contract-objective-recover-many = Bring all {$count} {$item}s from the {$planet ->
    [true] site
   *[false] wreck
} to {$destination} ({$done}/{$count})
wg-contract-objective-return = Bring the {$item} to {$destination}
wg-contract-location-survey = {$active ->
    [true] Derelict about {$km} km out from {$home}. Expect hostiles aboard.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wg-contract-location-tow = {$active ->
    [true] Derelict about {$km} km out from {$home}. Tow it to within {$range} m of {$home}.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wg-contract-objective-tow = {$gap ->
    [-1] Tow the derelict to within {$range} m of {$destination}
   *[other] Tow the derelict to within {$range} m of {$destination} ({$gap} m away)
}
wg-contract-objective-survey = Scan every anomaly {$planet ->
    [true] at the site
   *[false] aboard
} with an anomaly scanner ({$done}/{$count})
wg-contract-objective-escort-meet = Meet {$target} at the rendezvous
wg-contract-objective-escort = See {$target} safely to {$destination}
wg-contract-location-escort = {$active ->
    [true] Rendezvous about 1–2 km out from {$home}; escort to {$destination} ({$km} km). It waits for you there.
   *[false] Rendezvous near {$home}, escort to {$destination} ({$km} km). Exact position on accepting.
}

wg-contract-claimed-name = {$name} [claimed: {$claimant}]
wg-contract-marker-name = Contract #{$id}
wg-contract-row-holder = held by {$holder}
wg-contract-harms = -{$points} with {$contractor}
wg-contract-credits = {$amount} {$credit}
wg-contract-details-bonus = Bonus
wg-contract-details-holder = Held by
wg-contract-details-harms = Standing
wg-contract-employer = Working for {$contractor}, on behalf of {$employer}.
wg-contract-details-requires = Bring
wg-contract-requires-rope = a coil of load-bearing rope or tow cable
wg-contract-requires-tether-installer = a tether installer
wg-contract-row-crew = crew of {$crew}+
wg-contract-details-crew = Needs
wg-contract-details-crew-bonus = Crew bonus
wg-contract-details-share = Crew pay
wg-contract-crew-bonus = +{$percent}% for each crewmate past {$past}, up to {$most} more
wg-contract-share = {$pool} thalers for a crew of {$size}, {$each} each

wg-contract-goods-entry = {$count} {$name}

## Admin commands

cmd-contractpost-desc = Posts an offer on the contract board from a contract template.
cmd-contractpost-help = Usage: contractpost <wgContract id>
cmd-contractpost-hint = wgContract id
cmd-contractpost-invalid = Expected one wgContract prototype id.
cmd-contractpost-failed = Could not post {$template}: no board, its contractor's station is not in the round, or it has no valid destination.
cmd-contractpost-done = Posted #{$id} "{$title}" for {$reward} {$reward ->
        [one] thaler
       *[other] thalers
    }.
cmd-contractlist-desc = Lists every offer and live contract on the contract board.
cmd-contractlist-help = Usage: contractlist
cmd-contractlist-none = Nothing on the contract board.
cmd-contractlist-entry = #{$id} [{$status}] {$title}: {$reward} {$reward ->
        [one] thaler
       *[other] thalers
    }, held by {$holder}

## Guidebook

guide-entry-wg-contracts = Contracts

## The second set of objectives.

wg-contract-location-mail = Collect the parcels at the board; deliver each to its own station ({$count} stops, about {$km} km).
wg-contract-objective-mail = Deliver the parcel for {$destination}
wg-contract-location-sweep = {$active ->
    [true] {$count} points out from {$home}, up to about {$km} km. The next one is on your waypoints.
   *[false] {$count} points out from {$home}, up to about {$km} km. Positions on accepting.
}
wg-contract-objective-sweep-count = Log {$count} points in open space
wg-contract-objective-sweep = Log the point at {$x}, {$y}
wg-contract-location-derelict = {$active ->
    [true] Derelict about {$km} km out from {$home}.
   *[false] Derelict reported about {$km} km out from {$home}. Exact position on accepting.
}
wg-contract-location-deploy = {$active ->
    [true] {$count} points out from {$home}, up to about {$km} km. The next one is on your waypoints.
   *[false] {$count} points out from {$home}, up to about {$km} km. Collect the buoys at the board; positions on accepting.
}
wg-contract-objective-deploy-count = Set {$count} {$item}s adrift in open space
wg-contract-objective-deploy = Set a buoy adrift within {$range} m of {$x}, {$y}
wg-contract-location-picket = {$active ->
    [true] Picket point about {$km} km out from {$home}.
   *[false] Picket point about {$km} km out from {$home}. Exact position on accepting.
}
wg-contract-objective-picket = Hold within {$range} m of the point ({$percent}%, {$minutes} min in all), or destroy the raiders
wg-contract-objective-clear = Destroy every {$planet ->
    [true] den at the site
   *[false] nest aboard
} ({$done}/{$count})
wg-contract-objective-repair-weld = Weld the beacon whole
wg-contract-objective-repair-cell = Fit a power cell
wg-contract-objective-download = Stay by the terminal until the download completes ({$percent}%, {$minutes} min in all)
wg-contract-objective-inspect = Find the suspect cargo crate and inspect it

wg-contract-point-logged = "{$title}": {$done} of {$count} points logged.
wg-contract-buoy-set = "{$title}": {$done} of {$count} buoys set.
wg-contract-picket-started = "{$title}": you're on station. Hold the point for {$minutes} minutes; raiders are inbound.
wg-contract-recovered-one = "{$title}": {$done} of {$count} brought in.
wg-contract-download-started = "{$title}": download started. Stay by the terminal; it stops when no one is near.
wg-contract-follow-up = A new lead: "{$title}" is on the board for your crew for the next {$minutes} minutes.
wg-contract-inspect-verb = Inspect cargo
wg-contract-inspect-done = You go through the crate against its manifest and log what's really inside.
wg-contract-reason-device-lost = the objective aboard the derelict was destroyed.

## Planetside contracts.

wg-contract-location-planet = {$active ->
    [true] {$site} on {$world}. Its signal is marked in orbit; land and find it.
   *[false] A site on {$world}. Exact site on accepting.
}
wg-contract-world-unknown = an unknown world
wg-contract-site-unknown = a site
wg-contract-objective-construct = Set every kit down on the site, where it unfolds ({$done}/{$count})
wg-contract-kit-unfolded = "{$title}": {$done} of {$count} kits set up.
wg-contract-receiving-locked = This receiving crate is sealed for the station's own faction.
