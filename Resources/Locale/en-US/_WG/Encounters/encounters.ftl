wg-encounter-ship-name = {$vessel} {$designation}
wg-encounter-name-convoy = Convoy {$designation}
wg-encounter-open-space = open space

wg-encounter-name-patrol = TSF patrol {$designation}
wg-encounter-name-freighter = Independent freighter {$designation}
wg-encounter-name-tsf-transport = TSF transport {$designation}
wg-encounter-cargo-none = no cargo
wg-encounter-cargo-general = general cargo
wg-encounter-cargo-food = foodstuffs
wg-encounter-cargo-medical = medical supplies
wg-encounter-cargo-industrial = industrial materials
wg-encounter-cargo-parts = ship parts
wg-encounter-cargo-munitions = munitions
wg-encounter-cargo-arms = arms and equipment
wg-encounter-cargo-luxury = luxury goods
wg-encounter-cargo-salvage = salvage and scrap
wg-encounter-announce-freight-run = This is {$name}, entering the sector with {$cargo}. We make {$stops} stops, first {$destination}. Any traffic on our lane, please keep clear.
wg-encounter-announce-tsf-transport = All vessels, this is {$name}, a Trans-Solar Federation transport under escort, inbound to {$destination}. Stay outside one kilometre. You will not be warned twice.
# $need is fuel, thrusters or none. A real call gives the ship's registered name as scanners show it, its position and what it lacks.
wg-encounter-distress-adrift = Mayday, mayday. This is {$name}, adrift at {$x}, {$y}. { $need ->
    [fuel] Our generators are dry and our batteries are flat. We need fuel brought aboard to get them started.
    [thrusters] Our thrusters are wrecked. We need someone with a repair device or spare thrusters.
   *[none] We have lost propulsion. Requesting assistance from any vessel.
}
wg-encounter-zone-warn-label = WARNING ZONE
wg-encounter-zone-attack-label = ATTACK ZONE
wg-encounter-zone-warn-1 = {$intruder}, you are {$distance} metres off a restricted vessel. Divert your course and open the range now.
wg-encounter-zone-warn-2 = {$intruder}, you are inside our warning zone. Turn away immediately or you will be fired upon.
wg-encounter-zone-warn-3 = Vessel {$intruder}, this is your warning. Alter course away from us. Do not approach.
wg-encounter-zone-attack = {$intruder}, you were warned. Weapons free.
wg-encounter-zone-unknown = Unidentified vessel
wg-encounter-zone-identify = Unidentified vessel at {$distance} metres, you are in a restricted zone with no IFF showing. Show your IFF or turn away now. Those were warning shots; the next will not be.
wg-encounter-zone-seized = {$intruder}, your codes are dead. Who is flying that ship? Heave to and answer, or we open fire.
wg-encounter-pirate-zone-warn-1 = {$intruder}, cut your engines and stay right where you are. We are coming alongside.
wg-encounter-pirate-zone-warn-2 = You there, {$intruder}. Heave to and open your hold, and nobody has to get hurt.
wg-encounter-pirate-zone-warn-3 = {$intruder}, you are {$distance} metres into our hunting ground. Kill your drive.
wg-encounter-pirate-zone-attack = Too slow, {$intruder}. Light them up.
wg-encounter-name-pirates = Unidentified raiders {$designation}
wg-encounter-name-boarders = Pirate boarders {$designation}
wg-encounter-name-trader = Wandering trader {$designation}
wg-encounter-name-backup = TSF patrol {$designation} under attack
wg-encounter-announce-backup = Any vessel, any vessel, this is {$name}. We are engaged by pirate raiders and outgunned. Requesting immediate backup. The Federation pays its debts.
wg-encounter-kill-bounty-paid = {$amount} thalers paid into your account for killing one of the {$company}.
wg-encounter-reward-capped = You hit the {$cap} thaler hourly payout cap, so your share for {$name} was cut.
wg-encounter-reward-paid = For your help with {$name}, {$amount} { $amount ->
        [one] thaler has
       *[other] thalers have
    } been paid into your account.
wg-encounter-reward-paid-credits = For your help with {$name}, {$amount} { $amount ->
        [one] thaler has
       *[other] thalers have
    } been paid into your account and you have been issued {$credits} military credits.
wg-encounter-reward-thanks-tsf = All vessels that answered our call: the Federation thanks you. Payment has been made.
wg-encounter-reward-thanks-hauler = Whoever that was, thank you. We've sent what we can spare.
wg-encounter-announce-trader = {$name} is passing through the sector, open for business to anyone who comes alongside. Small stock, low prices, no refunds.
wg-encounter-name-ambush = Hauler {$designation} under attack
wg-encounter-name-station-blockade = Raider blockade {$designation}
wg-encounter-announce-station-blockade = All traffic: raiders are holding the approaches to {$origin}. Supply lines are cut and freighters are turning back. Any armed vessel able to drive them off will be paid for it.
wg-encounter-blockade-zone-warn-1 = {$intruder}, you are {$distance} metres off our line. Nothing goes in or out of this station. Turn back.
wg-encounter-blockade-zone-warn-2 = {$intruder}, this lane is closed. Come about now.
wg-encounter-blockade-zone-warn-3 = Last warning, {$intruder}. Nobody runs this blockade.
wg-encounter-blockade-zone-attack = {$intruder}, you wanted to run it. Open fire.
wg-encounter-sender-traffic = Sector traffic control
wg-encounter-announce-freighter = {$name} is entering the sector at {$origin}, inbound to {$destination} with general cargo.
wg-encounter-announce-ambush = Mayday, mayday, mayday. {$name}. Raiders have jumped us and we can't outrun them. Any vessel in range, we need guns out here. Please hurry.

wg-encounter-preset-quiet = Quiet sector
wg-encounter-preset-standard = Standard sector
wg-encounter-preset-dangerous = Dangerous sector
wg-encounter-vote-title = How dangerous is the sector this round?
wg-encounter-vote-initiator = Sector traffic control
wg-encounter-vote-result = This round: {$preset}.

wg-encounter-resolution-expired = Expired
wg-encounter-resolution-completed = Completed
wg-encounter-resolution-decided = Decided
wg-encounter-resolution-destroyed = Destroyed
wg-encounter-resolution-ended = Ended

# Admin encounter window
wg-encounter-admin-title = Encounters
wg-encounter-admin-scheduler = Scheduler
wg-encounter-admin-enabled = Start encounters automatically
wg-encounter-admin-paused = Paused
wg-encounter-admin-next = Next attempt in {$time}
wg-encounter-admin-next-none = Not running
wg-encounter-admin-interval = Every (seconds)
wg-encounter-admin-to = to
wg-encounter-admin-cap = At most running
wg-encounter-admin-apply = Apply
wg-encounter-admin-schedule-now = Pick one now
wg-encounter-admin-start = Start an encounter
wg-encounter-admin-distance = Metres north of you
wg-encounter-admin-spawn = Start
wg-encounter-admin-prototype-manual = {$id} (manual only)
wg-encounter-admin-search = Search
wg-encounter-admin-keep = Keep until ended
wg-encounter-admin-tier-sector = Sector threat tier
wg-encounter-admin-tier = Tier {$tier}
wg-encounter-admin-search-hint = name, prototype, ship or state
wg-encounter-admin-none = No encounters.
wg-encounter-admin-none-match = Nothing matches the search.
wg-encounter-admin-bad-input = Enter whole seconds, a cap and a distance as plain numbers.
wg-encounter-admin-bad-settings = Intervals must be 30 to 86400 seconds with the longest not below the shortest, and the cap 0 to 10.
wg-encounter-admin-gone = That encounter or ship no longer exists.
wg-encounter-admin-expires = Expires in {$time}
wg-encounter-admin-ship-gone = {$key}: gone, side {$side}
wg-encounter-admin-teleport = Go to
wg-encounter-admin-resolve = Resolve
wg-encounter-admin-end = End and remove
wg-encounter-admin-crew = Crew
wg-encounter-admin-preset = Preset
wg-encounter-admin-budget = Budget in use: {$cost} of {$budget}
wg-encounter-admin-start-round = Place round-start encounters
wg-encounter-admin-reveal = Reveal
wg-encounter-admin-tab-encounters = Running
wg-encounter-admin-tab-start = Start
wg-encounter-admin-tab-scheduler = Scheduler
wg-encounter-admin-show-resolved = Show finished encounters
wg-encounter-admin-pick = Pick an encounter on the left.
wg-encounter-admin-start-search-hint = prototype id
wg-encounter-admin-prototype = Encounter
wg-encounter-admin-tier-label = Threat
wg-encounter-admin-prototype-running = {$label} (running)
wg-encounter-admin-list-entry = {$rank}{$name} · {$ships} {$ships ->
    [one] ship
   *[other] ships
}{$simulated ->
    [0] {""}
   *[other] , {$simulated} simulated
}{$resolved ->
    [1] {" "}(finished)
   *[other] {""}
}
wg-encounter-admin-detail-prototype = Prototype: {$prototype}
wg-encounter-admin-detail-state = State: {$state}
wg-encounter-admin-detail-age = Running {$age}
wg-encounter-admin-detail-pinned = Kept until ended
wg-encounter-admin-detail-threat = Threat {$threat}
wg-encounter-admin-ships = Ships
wg-encounter-admin-ship-line = {$key}: {$name} · {$side} · {$status} · {$crew} crew
wg-encounter-admin-ship-status-gone = gone
wg-encounter-admin-ship-status-building = spawning
wg-encounter-admin-ship-status-simulated = simulated
wg-encounter-admin-ship-status-disabled = out of the fight
wg-encounter-admin-ship-status-real = spawned
wg-encounter-admin-order = Give an order
wg-encounter-admin-order-ship = Ship
wg-encounter-admin-order-do = Order
wg-encounter-admin-order-target = Target
wg-encounter-admin-order-go = Do it
wg-encounter-admin-target = {$name} ({$players}), {$distance}
wg-encounter-admin-target-distance = {$km} km
wg-encounter-admin-target-elsewhere = another map
wg-encounter-admin-target-none = No player ships
wg-encounter-admin-order-extort = Extort
wg-encounter-admin-order-board = Board
wg-encounter-admin-order-attack = Attack
wg-encounter-admin-order-follow = Follow
wg-encounter-admin-order-cometome = Come to me
wg-encounter-admin-order-hold = Hold position
wg-encounter-admin-order-resume = Resume its orders
wg-encounter-admin-order-spawnnow = Spawn now
wg-encounter-admin-order-sendaway = Send away
wg-encounter-admin-order-extort-hint = Goes after the target for its toll: closes in, hails and boards a ship that won't pay.
wg-encounter-admin-order-board-hint = Runs the target down, docks and raids it.
wg-encounter-admin-order-attack-hint = Engages the target, then holds where it ends up.
wg-encounter-admin-order-follow-hint = Keeps station on the target, then holds.
wg-encounter-admin-order-cometome-hint = Flies to where you are and holds there.
wg-encounter-admin-order-hold-hint = Stops where it is.
wg-encounter-admin-order-resume-hint = Takes up the orders it was spawned with.
wg-encounter-admin-order-spawnnow-hint = Builds the simulated ship's hull and crew now, wherever it is.
wg-encounter-admin-order-sendaway-hint = Wraps the encounter up: its ships jump out once nobody is aboard.
wg-encounter-admin-order-no-target = Pick a player ship to aim the order at.
wg-encounter-admin-order-done = {$key}: {$order}.
wg-encounter-admin-order-resolved = That encounter is already over.
wg-encounter-admin-order-building = That ship is spawning; try again in a moment.
wg-encounter-admin-order-failed = The ship could not take that order.
wg-encounter-admin-order-not-extorter = Only an extorter can extort.
wg-encounter-admin-order-boarding = It is boarding a ship already.
wg-encounter-admin-order-not-hunter = Only a boarding ship can board.
wg-encounter-admin-order-not-here = You need to be on the ship's map.
wg-encounter-admin-order-not-simulated = That ship is already spawned.

# wg_encounter command
cmd-wg_encounter-desc = Lists, spawns and ends encounters, and runs or pauses the encounter scheduler.
cmd-wg_encounter-help = Usage: {$command} list | spawn <prototype> [distance] [keep|jump] [tier=N] | end <encounter> | schedule | pause | resume
cmd-wg_encounter-hint-sub = <subcommand>
cmd-wg_encounter-hint-prototype = <encounter prototype>
cmd-wg_encounter-hint-distance = [metres north of you] [keep|jump]
cmd-wg_encounter-hint-keep = [keep until ended | jump out as usual]
cmd-wg_encounter-hint-tier = [tier=0..5, the sector threat's if left out]
cmd-wg_encounter-hint-uid = <encounter entity>
cmd-wg_encounter-list-line = {$uid} {$prototype} "{$name}" {$state}, {$ships} ships, origin {$x}, {$y}
cmd-wg_encounter-list-footer = {$count} encounters.
cmd-wg_encounter-state-active = Active
cmd-wg_encounter-no-player = You need a body or a ghost on a map to spawn an encounter.
cmd-wg_encounter-unknown = {$prototype} is not an encounter.
cmd-wg_encounter-bad-distance = {$arg} is not a distance between 0 and 20000.
cmd-wg_encounter-bad-tier = {$arg} is not a threat tier from tier=0 to tier={$max}.
cmd-wg_encounter-spawned = Started "{$name}" as {$uid}.
cmd-wg_encounter-spawn-failed = {$prototype} could not be started. Station encounters need a station (routes need two) on this map; otherwise the server log says which ship failed.
cmd-wg_encounter-not-found = {$arg} is not an encounter.
cmd-wg_encounter-ended = Ended {$uid} and removed its ships.
cmd-wg_encounter-scheduled = The scheduler started {$uid}.
cmd-wg_encounter-not-scheduled = The scheduler started nothing; the server log says why.
cmd-wg_encounter-paused = Encounter scheduler paused.
cmd-wg_encounter-resumed = Encounter scheduler resumed.
wg-encounter-distress-attacked = Mayday, mayday! This is {$name}. We are under attack at {$x}, {$y}. Any vessel, please, we need help!
wg-encounter-map-object = Contact: {$name}
wg-encounter-map-object-ranked = Contact: {$name} · Threat {$threat}
wg-encounter-threat-rank = Threat {$threat ->
    [0] none
    [1] one chevron
    [2] two chevrons
    [3] three chevrons
    [4] one star
    [5] two stars
   *[other] three stars
}
wg-encounter-threat-retrofit = Retrofitted civilian hulls
wg-encounter-threat-gunship = Pirate gunships
wg-encounter-name-trader-surplus = Surplus dealer {$designation}
wg-encounter-announce-trader-surplus = {$name} is in the sector with decommissioned military stock. Arms, kit and ammunition, priced to move. Come alongside with your hands where we can see them.
wg-encounter-name-trader-curios = Curio dealer {$designation}
wg-encounter-announce-trader-curios = {$name} is passing through with antiques and curiosities from estates across the frontier. One of each, and none of it will be here tomorrow.
wg-encounter-name-trader-medic = Medical supplier {$designation}
wg-encounter-announce-trader-medic = {$name} is in the sector with medical stock: dressings, pens, pills and clinic equipment below station prices while they last.
wg-encounter-name-trader-shipwright = Shipwright {$designation}
wg-encounter-announce-trader-shipwright = {$name} is in the sector with ship fittings off the slip: thrusters, gyros, drives, airlocks and the boards to run them. Come alongside and bring your refit list.
wg-encounter-name-trader-fence = Pirate fence {$designation}
wg-encounter-announce-trader-fence = {$name} is moving goods through the sector, no questions asked. Arms, kit and cheap contraband, and anyone who comes alongside peacefully is welcome to look.

# Larger ambush
wg-encounter-name-ambush-large = Freighter {$designation} under attack
wg-encounter-announce-ambush-large = Mayday, mayday, mayday. {$name}. Two raiders have us boxed in and our guns can't hold them off. Any vessel in range, we need help now. We will pay what we can.

# Stranded freighters. The adrift call is wg-encounter-distress-adrift above.
wg-encounter-name-stranded = Disabled freighter {$designation}
wg-encounter-reward-thanks-stranded = That's thrust under us again. Whoever you are, thank you. We've sent what we can spare.

# False mayday. It is made at once, on the common channel, by the ship that baits the trap. Unlike a real call it gives no
# registered name and no position, says only that the drive is gone, and names a hull class that is not the one on the scanner.
wg-encounter-announce-false-mayday = Mayday, mayday. This is a Brute-class hauler, adrift and losing power. We have no drive left. Come alongside and dock, any vessel. We can't hold out much longer.
# Dweller hulks
wg-encounter-name-hulk = Derelict hull {$designation}
wg-encounter-announce-hulk = Traffic control to all vessels: {$name}, adrift and unregistered, no power and no answer on any channel. Scans show movement aboard. The hull is unclaimed; whoever clears it and files its papers may keep it.
wg-salvage-claim-gone = The ship these papers name is gone.
wg-salvage-claim-taken = That ship already has an owner.
wg-salvage-claim-not-aboard = You have to be aboard the ship to claim it.
wg-salvage-claim-no-card = You need an ID card that holds no ship deed to register the ship to.
wg-salvage-claim-failed = The registry won't take the claim.
wg-salvage-claim-done = {$ship} is registered to you.
# Encounter ship deeds
wg-encounter-deed-name = deed to {$ship}
wg-encounter-deed-gone = The ship this deed names is gone.
wg-encounter-deed-already = {$ship} is already permanent.
wg-encounter-deed-done = {$ship} is now permanent. It will never despawn.
wg-encounter-ambush-sprung-1 = Kind of you to come. There never was a mayday. Cut your engines and open your holds, our friends are already on their way in.
wg-encounter-ambush-sprung-2 = That's close enough. Drive's fine, by the way. Shut down and stand by to be boarded, or the ships behind you open fire.
wg-encounter-ambush-sprung-3 = Thanks for answering. Nobody here needs rescuing but you. Kill your engines, we're taking the ship.
wg-encounter-announce-false-mayday-large = Mayday, mayday. This is an Olympus-class freighter, adrift with the lights failing. We have no drive left. Come alongside and dock, please. We can't hold out much longer.

# Skirmishes
wg-encounter-name-skirmish-patrol = Patrol engaging raiders {$designation}
wg-encounter-announce-skirmish-patrol = Traffic control to all vessels: {$name}. A TSF patrol has run pirate raiders to ground and the two are about to trade fire. Neither is taking calls. Keep clear, unless you mean to take a side.
wg-encounter-name-skirmish-bands = Rival raiders {$designation}
wg-encounter-announce-skirmish-bands = Traffic control to all vessels: {$name}. Two raider bands have found each other in open space and are settling an old score. Neither is taking calls. Stay clear of both, or pick one.
wg-encounter-name-skirmish-corsairs = Patrol engaging corsairs {$designation}
wg-encounter-announce-skirmish-corsairs = Traffic control to all vessels: {$name}. A TSF patrol has cornered a RedSail corsair crew in open space. Expect heavy fire. Anything that gets between them is on its own.
wg-encounter-name-skirmish-hostile = Running battle {$designation}
wg-encounter-announce-skirmish-hostile = Traffic control to all vessels: {$name}. Two armed parties are fighting it out in open space, and both are firing on anything that comes near. Do not approach either one.
wg-encounter-reward-thanks-skirmish-tsf = TSF patrol to the vessels that stood with us: your help is noted, and it has been paid for.
wg-encounter-reward-thanks-skirmish-pirates = You fight well for a stranger. Here is a cut of what we took off them.
wg-encounter-reward-thanks-skirmish-corsairs = RedSail remembers a favour. Take your share and fly on.

# Black market transport. Only a rumour goes out, once someone has fought it.
wg-encounter-name-blackmarket = Unregistered transport {$designation}
wg-encounter-announce-blackmarket = Sector traffic control has unconfirmed reports of weapons fire around {$name}, a vessel on no manifest. Whatever it carries, it isn't licensed. Keep your distance.
wg-encounter-blackmarket-zone-warn-1 = {$intruder}, you are {$distance} metres off a private vessel. Turn around. We won't ask twice.
wg-encounter-blackmarket-zone-warn-2 = {$intruder}, this is private cargo, and whatever you think you saw, you didn't. Open the range.
wg-encounter-blackmarket-zone-warn-3 = You are drifting into something that is none of your business, {$intruder}. Fly on.
wg-encounter-blackmarket-zone-attack = {$intruder}, that was your last chance. Open fire.

# Phaethon Dynasty navy. Its zone lines name nobody, because the hostile skirmishes use them on every ship.
wg-encounter-name-pdv-patrol = Dynasty patrol {$designation}
wg-encounter-name-pdv-transport = Dynasty supply convoy {$designation}
wg-encounter-announce-pdv-transport = All vessels, this is {$name}, a Phaethon Dynasty supply convoy under Vanguard escort, inbound to {$destination}. Federation vessels, stay outside one kilometre. You will not be warned twice.
wg-encounter-pdv-zone-warn-1 = {$intruder}, you are {$distance} metres off a Vanguard vessel. Open the range now, in the Sultan's name.
wg-encounter-pdv-zone-warn-2 = {$intruder}, you are inside the Vanguard's warning zone. Turn away at once or be fired upon.
wg-encounter-pdv-zone-warn-3 = Vessel {$intruder}, this is the Vanguard. Alter course and stay clear of us. This is your last courtesy.
wg-encounter-pdv-zone-attack = {$intruder}, you were warned. The Sultan's guns are loose.
wg-encounter-name-pdv-backup = Dynasty patrol {$designation} under attack
wg-encounter-announce-pdv-backup = Any vessel, any vessel, this is {$name}. Federation fighters have jumped us and we are outgunned. Requesting immediate backup. The Vanguard repays its debts, and with interest.
wg-encounter-reward-thanks-pdv = All vessels that answered our call: the Vanguard thanks you, and the Sultan remembers. Payment has been made.
wg-encounter-name-skirmish-navies = Rival patrols {$designation}
wg-encounter-announce-skirmish-navies = Traffic control to all vessels: {$name}. A TSF patrol and a Dynasty patrol have met in open space and are about to trade fire. Neither is taking calls. Keep clear, unless you mean to take a side.
wg-encounter-name-skirmish-pdv-corsairs = Dynasty patrol engaging corsairs {$designation}
wg-encounter-announce-skirmish-pdv-corsairs = Traffic control to all vessels: {$name}. A Dynasty patrol has caught a RedSail corsair crew in open space and means to finish it. Expect heavy fire. Anything that gets between them is on its own.
wg-encounter-reward-thanks-skirmish-pdv = Vanguard patrol to the vessels that stood with us: your help is noted, and the Sultan's purse has paid for it.

# Frigates under attack
wg-encounter-name-frigate = TSF frigate {$designation} under attack
wg-encounter-announce-frigate = Mayday, mayday, all vessels, this is {$name}. A pirate pack has us surrounded, raiders and fighters on every quarter, and our guns can't hold them all. Any armed vessel, engage the pirates. The Federation will pay, and pay well.
wg-encounter-name-pdv-frigate = Dynasty frigate {$designation} under attack
wg-encounter-announce-pdv-frigate = All vessels, all vessels, this is {$name}. A Federation strike group is on us from every side and we are taking hits faster than we can answer them. Any armed vessel, engage the Federation fighters. The Vanguard repays its debts, and with interest.

# Heavier and stranger patrols
wg-encounter-name-patrol-heavy = TSF heavy patrol {$designation}
wg-encounter-announce-patrol-heavy = Traffic control to all vessels: {$name}. A Federation gunboat and escort are holding station off {$destination} for the next while. Dynasty traffic is advised to keep its distance.
wg-encounter-name-pdv-patrol-heavy = Dynasty heavy patrol {$designation}
wg-encounter-announce-pdv-patrol-heavy = Traffic control to all vessels: {$name}. A Vanguard gunboat and escort are holding station off {$destination} for the next while. Federation traffic is advised to keep its distance.
wg-encounter-name-ussp-patrol = Union remnant {$designation}
wg-encounter-announce-ussp-patrol = Traffic control to all vessels: {$name}. Union warships are holding a position in open space and answering nobody's hails. All traffic is advised to give them a wide berth.
wg-encounter-ussp-zone-warn-1 = {$intruder}, you are {$distance} metres inside Union space. There is no Union space on your charts. There is on ours. Turn around.
wg-encounter-ussp-zone-warn-2 = Vessel {$intruder}, this is a warship of the Union. Your company means nothing here. Alter course away from us.
wg-encounter-ussp-zone-warn-3 = {$intruder}, we have not surrendered and we are not negotiating. Open the range.
wg-encounter-ussp-zone-attack = {$intruder}, for the Union. Fire.

# Capital fleet battle
wg-encounter-name-fleet-battle = Capital fleet action {$designation}
wg-encounter-announce-fleet-battle = GAMMA ALERT. Traffic control to all vessels: {$name}. A Federation capital ship and its frigate screen have met a Dynasty battle group in open space and are engaging. All civilian traffic is to stay well clear. Both navies are calling for any armed vessel that will fight for them.

# Capitals under attack
wg-encounter-name-capital = TSF capital ship {$designation} under attack
wg-encounter-announce-capital = Mayday, mayday, all vessels, this is {$name}. A pirate flotilla has us surrounded, raiders and fighters from every quarter, and they are boarding our wounded sections. Any armed vessel in the sector, engage the pirates. The Federation will remember who came.
wg-encounter-name-pdv-capital = Dynasty capital ship {$designation} under attack
wg-encounter-announce-pdv-capital = All vessels, all vessels, this is {$name}. A Federation strike wing has thrown itself at us and our screens are failing. Any armed vessel, engage the Federation. The Sultan does not forget a debt.

# Supercapitals under attack
wg-encounter-name-supercapital = TSF supercapital {$designation} under attack
wg-encounter-announce-supercapital = Mayday, mayday, all vessels, this is {$name}. The whole pirate flotilla has fallen on the Chengdu: heavies, raiders and fighters from every quarter, and our screens are going. This is the Federation's flagship. Every armed vessel in the sector, engage the pirates. The Federation will not forget who came.
wg-encounter-name-pdv-supercapital = Dynasty supercapital {$designation} under attack
wg-encounter-announce-pdv-supercapital = All vessels, all vessels, this is {$name}. A Federation strike force has fallen on the Jupiter with everything it has and our screens are failing. This is the Vanguard's flagship. Every armed vessel in the sector, engage the Federation. The Sultan does not forget a debt, and he will not forget this.

# Supercapital fleet battle
wg-encounter-name-fleet-battle-large = Grand fleet action {$designation}
wg-encounter-announce-fleet-battle-large = EPSILON ALERT. Traffic control to all vessels: {$name}. The Federation and the Dynasty have committed their supercapitals, with their capital ships and fighter wings. A full fleet action is under way in open space. All civilian traffic is to clear the sector lanes at once. Both navies are calling for every armed vessel that will fight for them.
wg-encounter-name-trader-exotic = Exotic dealers {$designation}
wg-encounter-announce-trader-exotic = {$name} is in the sector under Blackhawk escort with ship guns, hardpoints and systems that don't reach any catalogue. Serious buyers may come alongside. Anyone else is advised that the escort is not for show.

# Frigate, capital and supercapital patrols
wg-encounter-name-frigate-patrol = TSF frigate patrol {$designation}
wg-encounter-announce-frigate-patrol = Traffic control to all vessels: {$name}. A Federation frigate and fighter wing are holding station off {$destination} for the next while. Dynasty traffic is advised to keep well clear.
wg-encounter-name-pdv-frigate-patrol = Dynasty frigate patrol {$designation}
wg-encounter-announce-pdv-frigate-patrol = Traffic control to all vessels: {$name}. A Vanguard frigate and fighter wing are holding station off {$destination} for the next while. Federation traffic is advised to keep well clear.
wg-encounter-name-capital-patrol = TSF capital patrol {$designation}
wg-encounter-announce-capital-patrol = Traffic control to all vessels: {$name}. The Federation capital ship Flyssa and her fighter wing are holding station off {$destination}. Dynasty traffic is advised to keep well clear.
wg-encounter-name-pdv-capital-patrol = Dynasty capital patrol {$designation}
wg-encounter-announce-pdv-capital-patrol = Traffic control to all vessels: {$name}. The Vanguard capital ship Saturn and her fighter wing are holding station off {$destination}. Federation traffic is advised to keep well clear.
wg-encounter-name-supercapital-patrol = TSF supercapital patrol {$designation}
wg-encounter-announce-supercapital-patrol = Traffic control to all vessels: {$name}. The Federation supercapital Chengdu and her fighter wing are holding station off {$destination}. Dynasty traffic is advised to keep well clear.
wg-encounter-name-pdv-supercapital-patrol = Dynasty supercapital patrol {$designation}
wg-encounter-announce-pdv-supercapital-patrol = Traffic control to all vessels: {$name}. The Vanguard supercapital Jupiter and her fighter wing are holding station off {$destination}. Federation traffic is advised to keep well clear.

# Traders answering a hail; the caller can ask them to hold position so they can dock.
wg-encounter-trader-hail-greet = Trader here, under way. What can we do for you?
wg-encounter-trader-hail-holding = Trader here. We're holding position, come alongside when you're ready.
wg-encounter-trader-hail-stop = Hold position, we're coming to dock.
wg-encounter-trader-hail-leave = Never mind.
wg-encounter-trader-hail-stopping = Understood. Holding position for {$minutes} minutes. Dock when ready.
wg-encounter-trader-hail-cannot = Sorry, we can't stop here.
wg-encounter-trader-hail-farewell = Safe travels.
wg-encounter-admin-target-anchor = reality anchor
