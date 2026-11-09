wf-outpost-default-name = {$owner}'s outpost

wf-outpost-found-done = The outpost console unfolds and locks itself to the ground.
wf-outpost-found-disabled = Outposts can't be founded on this server yet.
wf-outpost-found-no-owner = Only a player can found an outpost.
wf-outpost-found-already-own = You already have an outpost. Sell it, or lose its console, before you can found another.
wf-outpost-found-not-ground = An outpost can only be founded on a planet's ground.
wf-outpost-found-not-natural = The outpost needs open, natural ground under every tile of it.
wf-outpost-found-blocked = Something is in the way. Clear the ground first.
wf-outpost-found-occupied = Something already stands there.
wf-outpost-found-reserved = That ground is spoken for: a road, a site or a cave mouth.
wf-outpost-found-claimed = That ground is claimed by another outpost.
wf-outpost-found-too-close = Too close to a road, a site or a cave mouth. An outpost needs clear ground around it.
wf-outpost-claimed = This ground is claimed by {$outpost}.

wf-outpost-roof-not-outpost = Roof panels only fit an outpost's frame.
wf-outpost-roof-not-owner = Only the outpost's owner and its builders can roof it.
wf-outpost-roof-no-floor = There's no floor there to roof over.
wf-outpost-roof-already = That tile is already roofed.

wf-outpost-console-title = Outpost console
wf-outpost-console-tab-overview = Overview
wf-outpost-console-name = Name
wf-outpost-console-rename = Rename
wf-outpost-console-owner = Owner
wf-outpost-console-tiles = Floor tiles
wf-outpost-console-entities = Objects
wf-outpost-console-count = {$count} / {$max}
wf-outpost-console-raids = Raids
wf-outpost-console-raids-active = Possible now; safe again in {$minutes} min
wf-outpost-console-raids-quiet = Safe; possible in {$minutes} min
wf-outpost-console-raids-always = Possible at any hour
wf-outpost-console-raids-never = None on this world

wf-outpost-plate-not-outpost = Foundation plates extend an outpost of your own; you don't have one.
wf-outpost-plate-wrong-ground = Your outpost isn't on this ground.
wf-outpost-plate-outside-claim = A foundation plate has to go inside your outpost's claim.
wf-outpost-plate-too-wide = Your outpost can't spread any wider than that.
wf-outpost-plate-taken = There's already floor there.
wf-outpost-plate-full = Your outpost is as big as an outpost can be.
wf-outpost-plate-claimed = That ground is claimed by another outpost.

wf-outpost-hatch-done = You open a hatch in the floor and sink a ladder into the room dug below.
wf-outpost-hatch-not-outpost = Use the hatch kit on the floor of an outpost.
wf-outpost-hatch-not-builder = You aren't allowed to dig under this outpost.
wf-outpost-hatch-no-cavern = There is no cavern under this world to dig into.
wf-outpost-hatch-no-floor = There's no floor there to open.
wf-outpost-hatch-edge = A hatch needs floor all round it; move it in from the edge.
wf-outpost-hatch-blocked = Clear the tile first.
wf-outpost-hatch-blocked-below = Something stands in the basement right under that tile.
wf-outpost-hatch-claim = That would dig out past the outpost's claim.
wf-outpost-basement-name = {$outpost} (basement)

wf-outpost-console-tab-saves = Saves
wf-outpost-console-saves-reading = Reading your saves...
wf-outpost-console-saves-hint = Save your outpost to bring it back in a later round. A save costs what its parts are worth, and half again, to load onto a freshly founded outpost; its console lands on this one's tile.
wf-outpost-console-slot = Slot {$slot}
wf-outpost-console-slot-empty = Empty
wf-outpost-console-slot-details = {$tiles} tiles, {$entities} objects, saved {$saved}
wf-outpost-console-slot-price = Loads for {$price}
wf-outpost-console-slot-in-use = Loadable next round
wf-outpost-console-save = Save here
wf-outpost-console-overwrite = Overwrite
wf-outpost-console-load = Load
wf-outpost-console-tab-access = Access
wf-outpost-console-not-yours = Only this outpost's owner can use its console.
wf-outpost-console-preview = Preview
wf-outpost-preview-title = Preview: {$name}
wf-outpost-preview-close = Close
wf-outpost-preview-wait = Wait a moment before previewing again.
wf-outpost-preview-empty = That slot is empty.
wf-outpost-preview-too-big = This save is too big to preview.
wf-outpost-preview-failed = Failed to load this save.
wf-outpost-console-delete = Delete
wf-outpost-console-pending = {$name} lands in {$seconds} s ({$price} paid).
wf-outpost-console-cancel-load = Call it off

wf-outpost-editor-tab = Outposts
wf-outpost-editor-hint = Your outposts' saves, shared by all your characters. Save an outpost at its console; load a save onto a freshly founded outpost.
wf-outpost-editor-none = You have no saved outposts.

wf-outpost-save-done = Outpost saved.
wf-outpost-save-failed = The outpost couldn't be saved.
wf-outpost-save-not-owner = Only the outpost's owner can save it.
wf-outpost-save-busy = The console is still busy with your saves.
wf-outpost-save-loading = Not while a save is on its way down.
wf-outpost-save-too-big = The outpost holds {$count} objects; a save holds {$max}. Most of it: {$big}.

wf-outpost-load-not-owner = Only the outpost's owner can load a save onto it.
wf-outpost-load-empty = That slot is empty.
wf-outpost-load-in-use = That save was saved or loaded this round. It can be loaded next round.
wf-outpost-load-not-fresh = A save only loads onto a freshly founded outpost: no plates laid, nothing built on it.
wf-outpost-load-failed = The save couldn't be loaded.
wf-outpost-load-lost = The outpost the save was meant for is gone. You were refunded.
wf-outpost-load-claimed = The save would land on ground another outpost claims.
wf-outpost-load-too-close = The save would land too close to a road, a site or a cave mouth.
wf-outpost-load-cannot-afford = You can't afford to load that save.
wf-outpost-load-started = The save is on its way down. Its outline is marked on the ground.
wf-outpost-load-cancelled = The load is called off. You were refunded.
wf-outpost-load-landed = The saved outpost locks into place.
wf-outpost-load-blocked = Something got in the way of the save's outline, so it couldn't land. You were refunded.

cmd-wfoutpost-desc = Lists, restores, refunds or deletes a player's outpost saves.
cmd-wfoutpost-help = Usage: wfoutpost list <player> | wfoutpost restore|refund|delete <player> <slot>. Restore makes a save loadable again this round; refund pays the player what loading it costs.
cmd-wfoutpost-invalid-args = Expected list, restore, refund or delete, then a player.
cmd-wfoutpost-no-player = No player called {$player}.
cmd-wfoutpost-none = {$player} has no saved outposts.
cmd-wfoutpost-entry = Slot {$slot}: {$name}, {$tiles} tiles, {$entities} objects, appraised {$appraisal}, saved in round {$saved}, last loaded in round {$loaded}, lineage {$lineage}
cmd-wfoutpost-bad-slot = The slot is a number from 1 to 3.
cmd-wfoutpost-empty = That slot is empty.
cmd-wfoutpost-restored = {$name} can be loaded again this round.
cmd-wfoutpost-refund-failed = The player has to be in the round, in a body with a bank account, to be refunded.
cmd-wfoutpost-refunded = Paid {$price} back to {$player}.
cmd-wfoutpost-deleted = Deleted {$name}.
cmd-wfoutpost-hint-action = list, restore, refund or delete
cmd-wfoutpost-hint-player = player
cmd-wfoutpost-hint-slot = slot (1 to 3)

wf-outpost-deploy-no-floor = Face a free tile of an outpost's floor to set this down.
wf-outpost-deploy-not-yours = Only the outpost's owner and its builders can set things down there.
wf-outpost-deploy-blocked = Something is in the way.
wf-outpost-repack-verb = Pack up
wf-outpost-repack-not-yours = Only the outpost's owner and its builders can pack this up.
wf-outpost-repack-failed = It won't go back into its pack.
wf-outpost-repack-console-built = Clear the outpost back to its founding square before packing up its console.

## Fabricator

wf-lathe-category-outpost-building = Outpost building
wf-lathe-category-outpost-rations = Rations

## Trade

wf-outpost-console-tab-trade = Trade
wf-outpost-trade-hub = Trade hub
wf-outpost-trade-balance = Balance
wf-outpost-trade-credits = {$credits} spesos
wf-outpost-trade-hub-none = None. Deploy a trade hub on the outpost to take orders.
wf-outpost-trade-hub-unpowered = No power
wf-outpost-trade-hub-covered = Roofed over; it needs open sky
wf-outpost-trade-hub-ready = Ready
wf-outpost-trade-orders = On their way ({$count}/{$max})
wf-outpost-trade-order-due = {$name}: drops in {$seconds} s
wf-outpost-trade-order-falling = {$name}: coming down
wf-outpost-trade-search = Search the catalogue
wf-outpost-trade-order = Order ({$cost})
wf-outpost-trade-unknown = That isn't on the outpost catalogue.
wf-outpost-trade-no-hub = Deploy a trade hub on the outpost first.
wf-outpost-trade-unpowered = The trade hub has no power.
wf-outpost-trade-covered = The trade hub is roofed over; drops need open sky.
wf-outpost-trade-full = Wait for an order to land first.
wf-outpost-trade-funds = You can't afford that.
wf-outpost-trade-category-materials = Materials
wf-outpost-trade-category-building = Building
wf-outpost-trade-category-power = Power
wf-outpost-trade-category-supplies = Supplies

## Landing guard

wf-outpost-claimant = {$owner}'s outpost {$outpost}

## Sale

wf-outpost-console-tab-sell = Sell
wf-outpost-sell-hint = Sell the outpost to the game for what its structures and machines are worth, with whatever stands inside them. Loose goods stay behind on the ground, and every save of the outpost is voided. Or offer it to a player standing at the console, who buys it with the console's Buy verb.
wf-outpost-sell-value = Sells for
wf-outpost-sell-button = Sell to the game
wf-outpost-sell-round-end-toggle = Sell when the round ends
wf-outpost-sell-done = {$outpost} sold for {$value} spesos.
wf-outpost-sell-round-end = {$outpost} was sold as the round ended, for {$value} spesos.
wf-outpost-sell-paid-out = You were paid {$value} spesos for {$outpost}, sold as the last round ended.
wf-outpost-sell-loading = Not while a save is on its way down.
wf-outpost-sell-busy = The console is still busy with your saves.
wf-outpost-sell-orders = Wait for your trade orders to land first.
wf-outpost-sell-aboard = Someone else is on the outpost.
wf-outpost-sell-no-bank = You need a bank account to be paid.
wf-outpost-offer-header = Sell to a player
wf-outpost-offer-price = Price
wf-outpost-offer-button = Offer
wf-outpost-offer-refresh = Refresh
wf-outpost-offer-withdraw = Take it back
wf-outpost-offer-no-buyers = No one stands at the console
wf-outpost-offer-standing = Offered to {$buyer} for {$price} spesos.
wf-outpost-offer-not-here = They have to stand at the console.
wf-outpost-offer-price-invalid = That isn't a price.
wf-outpost-offer-received = {$owner} offers you {$outpost} for {$price} spesos. Buy it at its console.
wf-outpost-offer-buy-verb = Buy outpost ({$price})
wf-outpost-offer-none = The outpost isn't on offer to you.
wf-outpost-offer-seller-gone = The seller has to be in the round to sell.
wf-outpost-offer-funds = You can't afford it.
wf-outpost-offer-sold = {$outpost} sold to {$buyer} for {$price} spesos.
wf-outpost-offer-bought = {$outpost} is yours, for {$price} spesos.
wf-outpost-refound-not-yours = That outpost was abandoned by someone else; only they can found it again.
wf-outpost-refound-done = The console unfolds. The outpost is yours again.

## Settlers
wf-job-name-outpost-settler = Outpost Settler
wf-job-description-outpost-settler = Wake up at your own outpost and keep it running.
wf-outpost-settler-station = Outposts
wf-outpost-join-choice = Wake up at {$outpost}
wf-outpost-join-choice-help = Join the round in a free cryopod on your outpost, as its settler, with the loadout and title set on your character's Outposts tab.
wf-outpost-join-in-crew = You're in a crew; leave it to wake up at your outpost.
wf-outpost-join-no-outpost = You have no outpost standing this round.
wf-outpost-join-no-pod = Your outpost has no free cryopod.
wf-outpost-join-failed = You can't wake up at your outpost right now.
wf-outpost-cryopod-full = This outpost already has six cryopods.
wf-outpost-turrets-full = This outpost already has as many turrets as it may.
wf-outpost-editor-settler = Waking up at your outpost: {$job}

## Drops
wf-outpost-drop-cap = Too many outposts stand on the worlds already.
wf-outpost-drop-no-site = No open ground could be found for the outpost.
wf-outpost-saves-choose = Choose
wf-outpost-saves-chosen = Chosen

## Planet broadcast

wf-outpost-broadcast-title = Planet broadcast
wf-outpost-broadcast-world = Broadcasting to everyone on { $world }.
wf-outpost-broadcast-no-world = This console isn't on a world.
wf-outpost-broadcast-placeholder = Your announcement
wf-outpost-broadcast-send = Broadcast
wf-outpost-broadcast-wait = Recharging: { $seconds } s
wf-outpost-broadcast-limit = Up to { $length } characters.
wf-outpost-broadcast-denied = Only the outpost's builders can broadcast from it.
wf-outpost-broadcast-cooldown = The broadcast array is still recharging.
wf-outpost-broadcast-sender = Outpost { $outpost }
wf-outpost-broadcast-include-position = Include our position
wf-outpost-broadcast-position = { $x }, { $y }
wf-outpost-broadcast-with-position = { $text } (at { $position })
wf-outpost-distress-send = Call for help
wf-outpost-distress-ready = Under raid: a call for help reaches everyone in the sector.
wf-outpost-distress-wait = Under raid: the next call for help in { $seconds } s.
wf-outpost-distress-no-raid = There is no raid to call for help against.
wf-outpost-distress-sender = Distress call
wf-outpost-distress-text = Outpost { $outpost } on { $world } is under attack and calls for help, at { $position }.

## Deposit terminal

wf-outpost-deposit-title = Deposit terminal

## Claiming points of interest

wf-poi-claim-examine-site = This terminal holds the { $site } ({ $type }).
wf-poi-claim-examine-repair = It is [color=orange]damaged[/color]: weld it back together to claim the site.
wf-poi-claim-examine-hack = It is [color=orange]locked[/color]: work a multitool into it to claim the site.
wf-poi-claim-examine-ready = It is [color=green]working[/color]: use it to claim the site as your outpost.
wf-poi-claim-hacked = The terminal unlocks.
wf-poi-claim-done = You claim the site. It is your outpost now: { $outpost }.
wf-poi-claim-not-claimable = This site can't be claimed.
wf-poi-claim-taken = Someone has already claimed this site.
wf-poi-claim-needs-repair = The terminal is too damaged to work. Weld it first.
wf-poi-claim-needs-hack = The terminal is locked. Hack it with a multitool first.
wf-poi-claim-cap = Half of this world's sites are claimed already.
wf-poi-claim-hostiles = Something hostile is still in the site. Clear it out first.

wf-outpost-presets-title = Ready-made outposts
wf-outpost-preset-unknown = That ready-made outpost doesn't exist.
wf-outpost-preset-homestead = Homestead
wf-outpost-preset-homestead-desc = A one-room cabin with a cryopod, a wind turbine and solar panels.
wf-outpost-preset-workshop = Workshop
wf-outpost-preset-workshop-desc = A long hall round a fabricator and its silos, two cryopods and a fuel generator.
wf-outpost-preset-farmstead = Farmstead
wf-outpost-preset-farmstead-desc = A farmhouse and a hydroponic greenhouse with water and nutrient synthesizers, and a feeder for livestock.
wf-outpost-preset-bastion = Bastion
wf-outpost-preset-bastion-desc = A walled compound with two turrets, a warning mast and floodlights round a small barracks.

# Colony floors
tiles-wf-colony-grey = grey colony floor
tiles-wf-colony-grey-texture = ribbed grey colony floor
tiles-wf-colony-grey-bolts = bolted grey colony floor
tiles-wf-colony-white = white colony floor
tiles-wf-colony-white-texture = ribbed white colony floor
tiles-wf-colony-white-bolts = bolted white colony floor
