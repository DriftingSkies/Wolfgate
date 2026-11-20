wg-trade-chute-local-goods = This chute only takes goods shipped in from elsewhere.
wg-trade-chute-not-bought = This chute doesn't buy that. Its manifest lists what it takes.
wg-trade-refuse-shunned = The merchant won't buy from someone who hurt them.
wg-trade-refuse-unsealed = The fence only takes sealed freight off the haulers.
wg-trade-refuse-drop = No one here is buying that crate right now. The Underground knows where the drop is.

wg-encounter-name-merchant = Merchant {$designation}
wg-encounter-announce-merchant = {$name} is in the sector buying luxury goods, medicine, reactor fuel, weapon parts and fabric at a premium. Bring cargo alongside before it moves on.

wg-trade-exchange = Sector Exchange

## Live prices

wg-trade-sale = Paying {$amount}.
wg-trade-sale-low = Paying {$amount}. Best now: {$station}, {$best}.
wg-trade-examine-header = [color=#d9a441]Live prices:[/color]
wg-trade-price-line = {$good}: {$amount} {$trend} ({$reason}), {$tonnes}t, {$perTonne}/t
wg-trade-paper-header = [bold]{$station}: live turn-in prices[/bold]
wg-trade-paper-footer = Prices follow how full this station's shelves are: they fall as crates come in and rise as they run low. Sector events raise them. The Market app on your PDA has every station's prices.

wg-trade-reason-steady = steady
wg-trade-reason-glutted = glutted
wg-trade-reason-wanted = in demand
wg-trade-reason-short = short
wg-trade-reason-boom = boom
wg-trade-reason-blockade = blockade
wg-trade-reason-relief = relief
wg-trade-reason-war = war demand
wg-trade-reason-raid = raid alert
wg-trade-reason-rush = rush order
wg-trade-reason-bulk = bulk order
wg-trade-reason-sealed = sealed freight
wg-trade-reason-salvage = salvage bonus
wg-trade-reason-defector = enemy buyer
wg-trade-reason-scarce = scarce
wg-trade-reason-restock = restocking
wg-trade-reason-sealed-only = sealed only
wg-trade-reason-heat = heat on you

wg-trade-goods-medical = medical supplies
wg-trade-goods-supplies = supplies
wg-trade-goods-ammo = ammunition
wg-trade-goods-ship-components = ship components
wg-trade-goods-chemicals = chemicals
wg-trade-goods-fuel = reactor fuel
wg-trade-goods-industrial = industrial goods
wg-trade-goods-materials = construction materials
materials-wg-munitions = ammo crate
materials-wg-reactor-fuel = fission reactor fuel crate
materials-wg-medical-stock = medical supplies crate
materials-wg-gun-parts = weapon parts crate
materials-wg-ship-parts = ship components crate
materials-unit-wg-crate-share = share
wg-trade-goods-agriculture = fertilizer
wg-trade-goods-luxuries = luxury goods
wg-trade-goods-textiles = fabric

## Industries

wg-trade-industry-agriculture = Agriculture
wg-trade-industry-chemicals = Chemicals
wg-trade-industry-pharmaceuticals = Pharmaceuticals
wg-trade-industry-food = Food
wg-trade-industry-munitions = Munitions
wg-trade-industry-refining = Refining
wg-trade-industry-fabrication = Fabrication
wg-trade-industry-ship-works = Ship works
wg-trade-industry-reclamation = Reclamation
wg-trade-industry-luxury-goods = Luxury goods
wg-trade-industry-reactors = Reactors

## Services

wg-trade-service-drydock = Drydock work
wg-trade-service-patrols = Patrols

## Sector Exchange radio

wg-trade-shortage-start = {$station} is short of {$goods}: paying {$factor}x for {$minutes} minutes.
wg-trade-shortage-report = {$station} is short of {$goods}: {$cause}. Paying up to {$factor}x.
wg-trade-shortage-cause-blockade = the blockade has cut it off
wg-trade-shortage-cause-lost = a freighter never arrived
wg-trade-shortage-cause-input = it has no {$input} to make them from
wg-trade-shortage-cause-supply = supply has fallen behind
wg-trade-boom-start = {CAPITALIZE($goods)} boom: every station pays {$percent}% more for {$minutes} minutes.
wg-trade-war-demand = War demand: {$stations} pay {$factor}x for ammunition, weapon parts and medical supplies.
wg-trade-blockade-landed = {$station} is blockaded. It pays up to {$factor}x for essentials while the blockade holds.
wg-trade-blockade-broken = The blockade of {$station} was broken by traffic: {$ships}.
wg-trade-blockade-driven-off = The blockade fleet off {$station} was driven off.
wg-trade-blockade-withdrawn = The raiders off {$station} have moved on.
wg-trade-relief-name = running the blockade of {$station}
wg-trade-rush-start = { $bulk ->
        [true] BULK ORDER
       *[false] RUSH
    }: {$station} needs {$count} crates of {$goods} within {$minutes} minutes, paying {$factor}x.
wg-trade-rush-filled = { $bulk ->
        [true] Bulk order
       *[false] Rush order
    } filled at {$station}: {$fills}.
wg-trade-rush-expired = The { $bulk ->
        [true] bulk order
       *[false] rush order
    } for {$goods} at {$station} ran out.
wg-trade-freight-supplied = {$ship} delivered {$goods} to {$station}.
wg-trade-freight-lost = {$station}'s {$run} never arrived. Its goods are short there.

## War

wg-trade-navy-tsf = the TSF
wg-trade-navy-pdv = the PDV
wg-trade-war-effort = War effort: {$navy} adds {$credits} credits.
wg-trade-defector-warrant = Sold war goods to {$navy} in wartime.

## Contraband

wg-trade-contraband-drop = The drop has moved to {$station}. For the next {$minutes} minutes, smuggling crates sell there and nowhere else.
wg-trade-contraband-drop-leak = Customs tip: smugglers are moving their crates through {$station}.
wg-trade-contraband-tip-goods = smuggling crates

## Priority deliveries

wg-trade-consignment-start = Priority delivery: collect a crate at {$origin} and turn it in at {$station}'s chute within {$minutes} minutes for {$value}.
wg-trade-consignment-taken = {$ship} picked up the priority delivery for {$station}.
wg-trade-consignment-delivered = {$ship} made the priority delivery to {$station} on time.
wg-trade-consignment-examine = [color=#e8c46a]Priority delivery for {$station}.[/color] Turn it in at {$station}'s chute within {$minutes} min for {$value}, less if late. Any other chute pays {$elsewhere}.
wg-trade-consignment-lapsed = The priority delivery for {$station} ran out of time and was recalled.
wg-trade-consignment-cancelled = The priority delivery's destination is gone; it was recalled.
wg-trade-unknown-station = an unknown station
wg-trade-rush-fill = {$seller} {$count}

## Freight

wg-trade-seal-examine = Manifest seal: {$run} freight run, bound for {$station}. Lawful chutes pay {$percent}%; deliver it to {$station} for a salvage bonus.
wg-trade-thermal-examine = [color=#9aa7b1]Warm: it shows on thermal sensors.[/color]

## Market app

wg-market-program-name = Market
wg-market-title = Sector Exchange
wg-market-status = {$stations} markets open
wg-market-tab-prices = Prices
wg-market-tab-alerts = Alerts
wg-market-tab-materials = Materials
wg-market-tab-sector = Sector
wg-market-sector-stock = {$stock} crates in the sector, {$owed} waiting on freight
wg-market-sector-flow = {$made} made and {$used} used an hour
wg-market-sector-short = Short at {$stations}
wg-market-no-sector = No station is reporting its stocks.
wg-market-shelf-tooltip = Shelf {$percent}% of target
wg-market-material-note-sell = Where to sell what you mine or salvage, and where to buy stock to build with.
wg-market-material-note-spread = Stations charge more than they pay, so buying to resell loses money.
wg-market-material-pays = Mined some? {$station} pays most: {$amount} each
wg-market-material-sells = Need some? {$station} is cheapest: {$amount} each, {$stock} in stock
wg-market-material-no-buyer = No pallet is paying for this.
wg-market-material-no-stock = No station has this to sell.
wg-market-no-materials = No station is trading materials.
wg-market-tab-alerts-count = Alerts ({$count})
wg-market-filter = Filter goods...
wg-market-price-amount = {$amount}
wg-market-price-stock = {$stock} left
wg-market-price-weight = {$tonnes}t · {$perTonne}/t
wg-market-station-best = ★ best for {$count}
wg-market-sell-heading = SELL HERE
wg-market-buy-heading = BUY HERE
wg-market-no-stations = No stations are buying right now.
wg-market-no-match = No market buys that.
wg-market-best-sale = Best price for {$item}: {$station}, {$amount}.
wg-market-best-sale-none = No market would buy {$item} from you right now.
wg-market-best-sale-drop = Only one station buys {$item} right now, and only the Underground knows which.
wg-market-tab-underground = Underground
wg-market-underground-title = UNDERGROUND UPLINK
wg-market-underground-subtitle = Unlisted buyers, cash only.
wg-market-faction-caelestinus = Caelestinus
wg-market-faction-tsf = Trans-Solarian Federation
wg-market-faction-ussp = USSP
wg-market-faction-pdv = Phaethon Dynasty
wg-market-faction-underground = The Underground
wg-market-faction-independent = Independent
wg-market-faction-drop = Smuggling drop, moves in {$minutes} min
wg-market-no-alerts = Nothing happening in the sector right now.
wg-market-minutes-left = {$minutes} min left
wg-market-alert-kind-deal = DEAL
wg-market-alert-kind-traffic = TRAFFIC
wg-market-alert-kind-warning = WARNING
wg-market-alert-kind-personal = FOR YOU
wg-market-alert-rush = { $bulk ->
        [true] BULK ORDER
       *[false] RUSH
    } {$station}: {$left} of {$count} crates of {$goods} wanted at {$factor}x
wg-market-alert-consignment = Priority delivery: collect a crate at {$origin} and take it to {$station}'s chute. Pays {$value} on time.
wg-market-alert-consignment-carried = Priority delivery to {$station} is aboard {$ship}. Pays {$value} on time.
wg-market-alert-relief = {$station} pays relief rates ({$factor}x) while the blockade is contested; ships that break it split {$bounty}
wg-market-alert-raid = Raid on {$station}: ship components and medical supplies pay {$factor}x
wg-market-alert-blockade = {$station} is blockaded: essentials pay up to {$factor}x
wg-market-alert-shortage = {$station} is short of {$goods}: {$factor}x
wg-market-alert-shortage-report = {$station} is short of {$goods}: {$factor}x ({$cause})
wg-market-alert-waiting = {$station} is waiting on {$goods}
wg-market-alert-patrols = Patrols are at {$percent}% strength: the sector is short of ammunition
wg-market-alert-service = {$service} at {$station} costs {$factor}x: short of {$goods}
wg-market-alert-boom = {CAPITALIZE($goods)} boom: +{$percent}% everywhere
wg-market-alert-war = War demand: the navies pay {$factor}x for munitions and medicine
wg-market-alert-freight = Freighter {$ship} inbound to {$station} with {$goods}
wg-market-alert-merchant = Merchant {$ship} buying near {$cell} until it moves on
wg-market-alert-drop = Smuggling drop at {$station}: smuggling crates sell there and nowhere else until it moves
wg-market-alert-shunned = {$station} won't trade with you: you hurt its people
wg-market-alert-war-effort = War effort: {$navy} pays you {$credits} faction credits a crate for munitions and ship parts
wg-market-alert-defector = {$navy} pays you {$factor}x for war goods
wg-market-alert-defector-warrant = {$navy} pays you {$factor}x for war goods, but {$own} posts a {$warrant} warrant on anyone who sells

## wg_trade command

cmd-wg_trade-desc = Shows the sector economy and starts trade events: shortages, rush orders and consignments.
cmd-wg_trade-help = Usage: {$command} status | ledger [station] | shortage [prototype] | rush [prototype] | consignment | clear
cmd-wg_trade-hint-sub = <subcommand>
cmd-wg_trade-hint-shortage = [shortage prototype, random if left out]
cmd-wg_trade-hint-rush = [rush prototype, random if left out]
cmd-wg_trade-ledger-good = {$good}: {$stock} crates, {$owed} owed, {$made} made and {$used} used an hour, short at {$short} stations.
cmd-wg_trade-ledger-station = {$station} ({$role}):
cmd-wg_trade-ledger-row =   {$good}: {$stock} crates, level {$level}, priced at {$priced}, {$owed} owed, {$made} made and {$used} used an hour.
cmd-wg_trade-status-empty = No shortages, rush orders or consignments right now.
cmd-wg_trade-status-sector = whole sector
cmd-wg_trade-status-shortage = Shortage: {$goods} at {$station}, {$factor}x, {$seconds}s left.
cmd-wg_trade-status-rush = Rush: {$left}/{$count} {$goods} at {$station}, {$factor}x, {$seconds}s left.
cmd-wg_trade-status-consignment = Consignment for {$station}, carrier {$ship}, {$seconds}s to deadline.
cmd-wg_trade-status-supply = Supply at {$station}: {$goods} goods, {$soldOut} sold out, lowest stock {$lowest}, {$stops} freighter stops, blockaded {$blockaded}, short {$short}.
cmd-wg_trade-bad-shortage = {$arg} is not a shortage prototype.
cmd-wg_trade-bad-rush = {$arg} is not a rush prototype.
cmd-wg_trade-shortage = Started a {$goods} shortage at {$station}.
cmd-wg_trade-shortage-failed = No station buys those goods, or each one that does already has that shortage.
cmd-wg_trade-rush = Posted a rush order for {$goods} at {$station}.
cmd-wg_trade-rush-failed = No unblockaded station without an open rush buys those goods.
cmd-wg_trade-consignment = Posted a consignment as {$uid}.
cmd-wg_trade-consignment-failed = Couldn't post a consignment. It needs two unblockaded chute stations and one that isn't already a destination.
cmd-wg_trade-cleared = Ended {$shortages} shortages and booms and {$rushes} rush orders.

wg-trade-chain-radiation-examine = [color=yellow]Its radiation sets off nearby crates like it. Keep them apart, or a pile of them will explode.[/color]
