# TradeGoods

The trade-crate economy, modelled on Starsector: stations use and make goods, surplus is owed to whoever is short, freighters
deliver it, and prices at every chute and vendor follow how full a station's shelf is, with sector events on top and a reason
players can see. [`DESIGN.md`](../../../Docs/_WG/TradeGoods/DESIGN.md) sets out the model and its numbers. A chute won't take crates bought from its own station's vendors; anything else,
printed crates included, sells wherever it was made, while war-effort credits, the freight destination bonus and
counting as station traffic need a haul of about 3 km. The Market PDA app, chute examine and the chute's empty-hand paper show live prices,
each with its weight and price per tonne. The app's Alerts tab lists every event below with its countdown, plus alerts
for the holder alone: traders who won't deal with them and what the war pays their faction; soonest deadline first. The app holds no ship information: what a ship carries is the
helm's business (CargoLoad). Every PDA gets the app: `WGMarketCartridgeSystem` appends it to each PDA's preinstalled
programs at startup, since most PDA prototypes replace the base list. Only open copies refresh, every 5 s. Hitting a
trade crate with the app open names the station paying most for that crate now (`BestSale`), skipping chutes that
would refuse it; a hidden market is named only to an underground company's members. Its Prices
tab groups stations by faction (`marketFactions`, matched by chute). Hidden markets (`hiddenChutes`: PDV Helios and the
Underground fence) go to a separate Underground tab, and only holders of an `undergroundCompanies` company (PDV and the
Underground's godfathers and fugitives) are sent them, along with alerts naming them. UI state reaches every client near a PDA, so for those holders the networked state is a public stand-in and the
full state goes to the viewers alone (`WGMarketPrivateStateEvent`, kept by the client `WGMarketSystem`). Radio lines
naming a hidden market go only to the underground companies (`WGTradeMarketSystem.Announce`), and a low sale's "best now"
hint never names one.
A chute offered something it doesn't buy says so instead of only buzzing.
`stations.yml` adds the chutes and vendors Monolith's maps lack: chutes at MMC Hokkaido and Anvilhold Drydock, and
vendors at TSFMC Halcyon, Zvezda and MMC Hokkaido that sell what their station's chute doesn't buy.

The sector economy (`WGTradeEconomySystem`, `WGTradeMarketSystem.Ledger`, `economy.yml`): each station keeps a ledger, a row per
good with its stock, the level prices see and the crates it's owed. Roles (`wgTradeStation`) name a station's industries
(`wgTradeIndustry`), population and what it uses, by POI or game map id through `WGTradeStationComponent`. Every
`economyInterval` a station uses goods, its industries make theirs at a share of the sector rate scaled by their input, and stock
over target goes into a sector pool that's owed out to stations under target. Only a freighter turns what's owed into stock. A
station's vendors sell from its shelf (`vendorStock` at most), a sold-out good drops off the Market app and contraband never runs
out. The round opens by running `openingHours` of this with assumed freight. A grid with no role stands outside it all: it trades,
and its stock settles back to target. `wg_trade ledger [station]` prints the ledgers, and `wg_trade status` each station's supply,
which the server also logs hourly (`WGTradeSupplySystem`).

Cargo markets open with `supplyMaterials` at the target. A freighter stop unloads what the station is owed of what its hold carries,
up to its share of the hold along the route, at `freightCrateStock` owed crates per crate aboard, brings `freightRestock` of the
target of its manifest's `materials`, and ends the station's shortages of those goods. Cargo stolen off a crewed hull never arrives,
one stripped bare brings nothing, and a lost run writes off what its stops were owed. Runs lean toward stations owed the most and
manifests carrying what their stops are owed. Every good a station vendor sells has to be on a manifest that runs at any player
count (`EveryVendorGoodComesByFreightAtAnyPlayerCount`). Plasma, uranium, lithium and precious metals only come from miners, and
industries with `materials` listed make half as much again while their station's cargo market holds them.

Some crates go into lathes like sheets (`Resources/Prototypes/_WG/TradeGoods/materials.yml`): ammo becomes munitions for
researched ship ammo, fission fuel becomes reactor fuel for uranium fuel rods, medical supplies become medical stock for blank
medipens, ship components become ship parts for ship vouchers and weapon parts become gun parts for turrets. A crate is 10000
of its material and is used up going in. A lathe only takes one it has a recipe for, researched or not. Roundstart ship ammo
stays free so a new crew never has to buy crates to fire. These materials cost nothing, since the crate carries the value, and
have no stack, so they can't be taken back out. A lathe names each by the crate it comes from. `CrateMaterialTest` covers this.

The two kinds of cargo depot sell different goods (medical supplies only at the alt depot), so
`PointOfInterestSystem.GenerateDepots` spawns one of each before any repeats, counting a planet hub as the standard
kind (`DepotKindsTest`).

Players fill shelves too. Every crate a chute takes goes on the station's shelf. Once a good is scarce, the station's chutes take it
even if they don't list it, at the best rate any open chute lists, and rush and bulk orders go to the station furthest under target
for their goods. A chute still refuses a crate bought on its own station, so this can't be looped.

Stopping the freighters is one blockade: a station they don't reach runs down, and a lost run leaves its stops short. Players do it
by intercepting freighters, outside the 256 m that Conflict Rule 4 protects. Freighters also stay away from a station under a faction
holding its cell (SectorControl) or a `blockades` encounter (`WGEncounterStationBlockade`), raiders on its approaches for as long as
they stay; destroying the raiders lifts it, announces it and pays whoever did. A blockaded station's imports go off its shelves toward
`blockadeFloor`, most of the way in half an hour, and stay owed to it, so the first ship through brings relief.

Upkeep (`wgTradeService`): drydock work at a station with a role costs the curve at its Ship Components level
(`WGTradeMarketSystem.ServicePrice`), and patrols are scheduled less the less ammunition the sector holds
(`WGTradeEconomySystem.ServiceState`). The Market app warns when either has fallen by a fifth.

Prices read one curve (`WGTradeMarketSystem.LevelFactor`): near normal for small dips, steep near empty and falling through a glut. A
chute pays its table price times `chuteTableRate` times the curve, stopping at `floorPrices`, and by the higher of the shelf and the
level prices see, so a sale cuts its pay at once and a big haul walks down the curve. Trade vendors (`WGTradeVendorComponent`, on
`VendingMachineTraderBaikal` and its children) charge their base price times the curve at the level prices see, a few minutes behind
the shelf (`priceSmoothing`), never below base, so the sell floors still cover a bought crate. Shortages, booms, blockades and raids
raise a vendor's price further (`WGTradeBuyQuoteEvent`). The server keeps each vendor's live prices on the networked component,
refreshed every 5 s and on each purchase, so the vending menu shows what `AuthorizedVend` charges; the Market app lists them as each
station card's Buy here rows, cheapest starred, with a bar for each shelf. Its Sector tab shows each good's stock, what's waiting on
freight, what's made and used, and who is short. NPC trader shops and console trading crates keep their fixed prices.

What raises prices (`WGTradeQuoteEvent` subscribers, capped together at `modifierCap`, with the whole price capped at `priceCap`):
- Sector-wide booms (`WGTradeShortageSystem`, `wgTradeShortage`). Shortages at one station are reports of real gaps: the worst good
  scarce for `shortageDelay` is announced with its cause, adds nothing to a price that already shows it, and ends when the shelf
  recovers or a freighter brings the goods. An admin can force one with no gap behind it, which raises its group for its duration.
- Blockades, relief rates while a blockade is contested, war demand at the navy chutes and raid alerts
  (`WGTradeSectorSystem`, reading SectorControl, the war level and the Reaver campaign).
- Rush orders: a station wants a few crates fast (`WGTradeRushSystem`, `wgTradeRush`). Bulk orders (`bulk: true`, on their
  own `bulkMinutes` schedule) are the big-hold version: an open station wants 25 to 45 crates of a group within 45 to 60
  minutes, at 1.15x to 1.25x and a flat price that ignores the shelf (`WGTradeQuote.NoGlut`), so the last pays what the
  first did. Anyone can deliver toward one, but only a big hold can fill it in a run.
- NPC freight (`WGTradeFreightSystem`): a run's crates carry a manifest seal and pay 60% at lawful chutes, full price at
  a fence (the Underground fence chute, PDV Helios) and a bonus at their destination. A run lost to attack leaves its remaining stops short. The Underground fence takes
  sealed freight only (`sealedOnlyChutes`), so it is where pirates unload what they take off haulers. While the Reaver campaign runs,
  freight fenced there funds the Reavers (ReaverCampaign `.Funding`).

Materials (`MarketSystem.Materials`, `materialGoods`) trade through each station's cargo market stock. A pallet pays by
how much of a material the station holds, from 1.2x with none through full value at
`materialStockTarget` to 0.5x at a glut, and a sale walks down that curve, so one big haul doesn't all fetch the top
rate. The market console charges by the same stock, from 1.5x near empty to 0.8x at a glut, before its own modifier.
Stations use up what they hold, halving it every `materialHalfLife`, and industries work it into crates, so a glut clears and a
station nobody supplies runs down. A sale lands in the stock at once, and otherwise stock only leaves by being bought, so every station with a pallet needs a cargo
market and a market console (`EveryPalletStopSellsBackWhatItBuys`). Materials in a console's cart stay part of the stock:
they still count as supply and follow the live price until they are bought. Everything else the console sells works as it always did. The curves
keep selling and buying back at a loss on the tightest pallet and console pair on any station, which
`SellingAndBuyingBackNeverProfits` checks. Console prices follow a purchase or sale after a delay, and
pallet pay rises after it, but a sale cuts pallet pay at once: a delayed glut would let a split sale fetch the top rate and
be bought back at a profit once the console caught up. Freight moves prices at once. The Market app's Materials tab names each material's best sale and cheapest
stock. Materials aboard a ship count toward its cargo value (`WGTradeMaterialCargoSystem`), so a full mining hold draws
raiders. Crates built from materials use about 90% of their vendor price in them; crates built from reagents, parts
or cloth only have a heavier steel shell, and fissile fuel is unchanged.

Contraband (`WGTradeContrabandSystem`, `contrabandGoods`): the smuggling crate is sold by the Underground's fence vendor
(`WGVendingMachineFence`) and bought well by every open chute, after the usual
haul, at a fixed street price: sales never glut it, no event raises what chutes pay for it and buying it never makes
the vendor dearer. The margin is small (15,000 to buy, 20,000 to sell); each sale also drops `contrabandTelecrystals`
telecrystals at the chute, the black market's steady source of them. It shows on radar from other grids like the PDV supply crates. It is Frontier contraband, so security can seize it at their contraband pallet for
faction credits. Each sale at an open chute has a `contrabandWarrantChance` of customs flagging the seller: each flag
adds `contrabandWarrant` to one criminal warrant on the bounty board in their name, up to `contrabandWarrantCap`, so a
bigger run is a bigger target. A warrant taken off the board starts over. The warrant is a notice only: nothing pays
it out automatically. Security hears who sold it where, and the warrant's total, on `contrabandAlertChannel`, at most
once per `contrabandAlertCooldown` for each seller. Hidden markets don't buy it. While the Reaver campaign runs, every few crates sold at open chutes fund the Reavers (ReaverCampaign `.Funding`).

Only the drop buys it (`WGTradeContrabandSystem.Drop`): one open station whose chute lists contraband, never a
wandering merchant, hidden market or blockaded station. Every other open chute refuses a smuggling crate with a popup.
The drop moves every `contrabandDropInterval` (30 minutes, and never to the same station twice running when there is
another), or at once if its station is gone. The underground companies are told where it went in chat and see it, with
its countdown, in the Market app's alerts and as its own card at the top of the Underground tab
(`WGMarketCartridgeSystem.DropCard`); a Market app hint on a smuggling crate names the drop only to them. The open
station cards on the Prices tab leave contraband out while there is a drop, so lawful players see no price for it.
Security is tipped off on `contrabandAlertChannel` `contrabandDropLeak` (10 minutes) after each move. A
`contrabandDropInterval` of zero lets every open chute buy contraband, and the Prices tab lists it again. The drop only moves in round; `Rotate`, `Leak` and `TickDrop` are public for tests.

Priority deliveries (consignments in code, `WGTradeSpecialCrateSystem`): a crate appears at one station's chute, bound
for another station at least a haul away, never a wandering merchant or a hidden market, and pays well at that station's chute on time
and less when late. One left at its origin past its deadline is withdrawn;
one carried off is recalled a window after its deadline, or as soon as its destination is gone.

Traffic and rewards: a blockade attempt that traffic breaks is announced with the ships that ran it, who split a relief
bounty; real deliveries stamp the station's cell as traffic. There is no escort bonus: traders who want protection pay
escorts themselves. Wandering merchants (`WGEncounterWanderingMerchant`) carry a chute that buys a few goods at a
premium; a merchant won't buy from someone who hurt them.

Cargo-console trading crates (Frontier's `TradeCrate`, normal and express) sell at their destination's pallet, not a
chute. `WGTradeCrateMarketSystem` puts them in the same markets: their pallet price is scaled by the selling grid's
demand for one shared good (`WGTradeCrate`), each sale adds `tradeCratePerSale` there, the price never falls below
`tradeCrateFloor`, and they count toward a ship's cargo value for raids and extortion. A pallet sale prices its whole
batch in one tick before any of it adds pressure, so each crate priced in a tick is priced as if the ones before it had
sold. Cargo value is worked out once per tick for every grid, however many ships a raider weighs.

Entry points: `WGTradeMarketSystem` (quotes, the ledger, sales, payout, provenance, the Sector Exchange's radio),
`WGTradeEconomySystem` (the sector tick), the partial `DispenserSystem.TradeGoods` hooked into the Crescent chute, `WGMarketCartridgeSystem` and the client `MarketUi`.
All tuning is in `WGTradeMarketDefault` (`Resources/Prototypes/_WG/TradeGoods/market.yml`). Market state lasts one round.

Provenance (`WGTradeOriginComponent`, not networked or saved): a crate is stamped where it first appears on a market
map (the sector map, or a map with `WGTradeMarketMapComponent`): at map init, on `FTLCompletedEvent`, by a 5 s sweep
that catches shipyard docking and z-level moves. An unknown origin counts as local, so a
crate printed in hyperspace, orbit or on an expedition is local to where the ship returns, and a ship bought or
reloaded at the shipyard is local to the station it is delivered to. A chute that moves (Jupiter, a merchant) and flies
to the crates has made the haul itself, but one that carried them docked has not: a stamp records the grids docked to
the crate's ship (`WGTradeOriginComponent.Docked`). The origin only gates rewards for a real haul (`WasHauled`: freight
bonus, war-effort credits, station traffic): a crate stamped off the chute's grid and its docked ships, at least `minHaulDistance` from
the chute's current position. A chute refuses a crate bought on its grid (`WasPurchasedOnGrid`), and a sealed-only
chute one without a seal (`CheckSeal`). A crate already
taken by a chute and waiting for deletion is refused, so one crate can't pay at two chutes in a tick.

Faucet bound: a station's demand for a good is what it uses and exports, a few crates an hour, so past that a hauler walks down the
curve to the good's floor and has to move on. A sale dents a price by about 1% a crate above target. Fertilizer's floor of 1000 is
still well over the roughly 260 in materials it costs to print.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/TradeGoods/CargoSystem.Materials.cs`](CargoSystem.Materials.cs)
- [`Content.Server/_WG/TradeGoods/Commands/WGTradeCommand.cs`](Commands/WGTradeCommand.cs)
- [`Content.Server/_WG/TradeGoods/Components/WGSpecialCrateComponents.cs`](Components/WGSpecialCrateComponents.cs)
- [`Content.Server/_WG/TradeGoods/Components/WGTradeMarketComponent.cs`](Components/WGTradeMarketComponent.cs)
- [`Content.Server/_WG/TradeGoods/DispenserSystem.TradeGoods.cs`](DispenserSystem.TradeGoods.cs)
- [`Content.Server/_WG/TradeGoods/MarketSystem.Materials.cs`](MarketSystem.Materials.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGChainRadiationExamineSystem.cs`](Systems/WGChainRadiationExamineSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGCrateMaterialStorageSystem.cs`](Systems/WGCrateMaterialStorageSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGMarketCartridgeSystem.cs`](Systems/WGMarketCartridgeSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeContrabandSystem.cs`](Systems/WGTradeContrabandSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeContrabandSystem.Drop.cs`](Systems/WGTradeContrabandSystem.Drop.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeCrateMarketSystem.cs`](Systems/WGTradeCrateMarketSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeEconomySystem.cs`](Systems/WGTradeEconomySystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeFreightSystem.cs`](Systems/WGTradeFreightSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMarketSystem.Announce.cs`](Systems/WGTradeMarketSystem.Announce.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMarketSystem.Buy.cs`](Systems/WGTradeMarketSystem.Buy.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMarketSystem.cs`](Systems/WGTradeMarketSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMarketSystem.Ledger.cs`](Systems/WGTradeMarketSystem.Ledger.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMarketSystem.Origin.cs`](Systems/WGTradeMarketSystem.Origin.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeMaterialCargoSystem.cs`](Systems/WGTradeMaterialCargoSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeRushSystem.cs`](Systems/WGTradeRushSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeSectorSystem.cs`](Systems/WGTradeSectorSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeShortageSystem.cs`](Systems/WGTradeShortageSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeSpecialCrateSystem.cs`](Systems/WGTradeSpecialCrateSystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeSupplySystem.cs`](Systems/WGTradeSupplySystem.cs)
- [`Content.Server/_WG/TradeGoods/Systems/WGTradeWarSystem.cs`](Systems/WGTradeWarSystem.cs)
- [`Content.Server/_WG/TradeGoods/VendingMachineSystem.TradeGoods.cs`](VendingMachineSystem.TradeGoods.cs)
- [`Content.Server/_WG/TradeGoods/WGTradeEvents.cs`](WGTradeEvents.cs)

### Shared

- [`Content.Shared/_WG/TradeGoods/WGMarketUiState.cs`](../../../Content.Shared/_WG/TradeGoods/WGMarketUiState.cs)
- [`Content.Shared/_WG/TradeGoods/WGTradeCrateMarketComponent.cs`](../../../Content.Shared/_WG/TradeGoods/WGTradeCrateMarketComponent.cs)
- [`Content.Shared/_WG/TradeGoods/WGTradeMarketPrototype.cs`](../../../Content.Shared/_WG/TradeGoods/WGTradeMarketPrototype.cs)
- [`Content.Shared/_WG/TradeGoods/WGTradeOriginComponent.cs`](../../../Content.Shared/_WG/TradeGoods/WGTradeOriginComponent.cs)
- [`Content.Shared/_WG/TradeGoods/WGTradeVendorComponent.cs`](../../../Content.Shared/_WG/TradeGoods/WGTradeVendorComponent.cs)

### Client

- [`Content.Client/_WG/TradeGoods/MarketUi.cs`](../../../Content.Client/_WG/TradeGoods/MarketUi.cs)
- [`Content.Client/_WG/TradeGoods/VendingMachineMenu.TradeGoods.cs`](../../../Content.Client/_WG/TradeGoods/VendingMachineMenu.TradeGoods.cs)
- [`Content.Client/_WG/TradeGoods/WGMarketSystem.cs`](../../../Content.Client/_WG/TradeGoods/WGMarketSystem.cs)
- [`Content.Client/_WG/TradeGoods/WGTradeVendorSystem.cs`](../../../Content.Client/_WG/TradeGoods/WGTradeVendorSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/TradeGoods/CrateMaterialTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/CrateMaterialTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/DepotKindsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/DepotKindsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/EconomyLedgerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/EconomyLedgerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/MarketAppTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/MarketAppTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/MarketBestSaleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/MarketBestSaleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/MarketPdaTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/MarketPdaTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/MarketUndergroundTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/MarketUndergroundTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/MaterialMarketTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/MaterialMarketTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/StationSupplyTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/StationSupplyTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeBuyPriceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeBuyPriceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeCargoTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeCargoTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeChuteTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeChuteTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeCommandTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeCommandTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeConsignmentTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeConsignmentTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeContrabandDropTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeContrabandDropTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeContrabandTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeContrabandTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeFreightTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeFreightTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeMarketTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeMarketTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradePricingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradePricingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeProvenanceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeProvenanceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeRewardsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeRewardsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeTestWorld.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeTestWorld.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeVendorTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeVendorTest.cs)
- [`Content.IntegrationTests/Tests/_WG/TradeGoods/TradeWarTest.cs`](../../../Content.IntegrationTests/Tests/_WG/TradeGoods/TradeWarTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/TradeGoods/economy.yml`](../../../Resources/Prototypes/_WG/TradeGoods/economy.yml)
- [`Resources/Prototypes/_WG/TradeGoods/entities.yml`](../../../Resources/Prototypes/_WG/TradeGoods/entities.yml)
- [`Resources/Prototypes/_WG/TradeGoods/market.yml`](../../../Resources/Prototypes/_WG/TradeGoods/market.yml)
- [`Resources/Prototypes/_WG/TradeGoods/materials.yml`](../../../Resources/Prototypes/_WG/TradeGoods/materials.yml)
- [`Resources/Prototypes/_WG/TradeGoods/merchant.yml`](../../../Resources/Prototypes/_WG/TradeGoods/merchant.yml)
- [`Resources/Prototypes/_WG/TradeGoods/stations.yml`](../../../Resources/Prototypes/_WG/TradeGoods/stations.yml)

### Localization

- [`Resources/Locale/en-US/_WG/TradeGoods/trade-goods.ftl`](../../../Resources/Locale/en-US/_WG/TradeGoods/trade-goods.ftl)

### Textures

- [`Resources/Textures/_WG/TradeGoods/market.rsi/`](../../../Resources/Textures/_WG/TradeGoods/market.rsi/)
- [`Resources/Textures/_WG/TradeGoods/smuggling.rsi/`](../../../Resources/Textures/_WG/TradeGoods/smuggling.rsi/)

### Docs

- [`Docs/_WG/TradeGoods/DESIGN.md`](../../../Docs/_WG/TradeGoods/DESIGN.md)

## Non-modular edits

- [`Content.Client/VendingMachines/UI/VendingMachineMenu.xaml.cs`](../../../Content.Client/VendingMachines/UI/VendingMachineMenu.xaml.cs): trade vendors show the station's live price
- [`Content.Client/VendingMachines/VendingMachineBoundUserInterface.cs`](../../../Content.Client/VendingMachines/VendingMachineBoundUserInterface.cs): the menu reads the vendor's live trade-good prices
- [`Content.Server/_Crescent/Dispenser/DispenserSystem.cs`](../../_Crescent/Dispenser/DispenserSystem.cs)
  - chutes refuse goods not hauled away from where they were made, and quote the rest live
  - chutes also take consignments they don't list
  - say the chute doesn't buy it instead of only buzzing
  - pay the live quote, print live prices
- [`Content.Server/_NF/GameRule/PointOfInterestSystem.cs`](../../_NF/GameRule/PointOfInterestSystem.cs)
  - the depot kinds not spawned yet this round
  - a planet hub is a standard depot, so the space slots owe the other kind
  - every kind of depot spawns once before any repeats, as each sells goods the other doesn't
  - stations take their economy role from their POI
- [`Content.Server/Cargo/Systems/CargoSystem.Shuttle.cs`](../../Cargo/Systems/CargoSystem.Shuttle.cs): materials pay what the station's stock makes them worth
- [`Content.Server/VendingMachines/VendingMachineSystem.cs`](../../VendingMachines/VendingMachineSystem.cs): trade vendors charge the station's live price
- [`Resources/Locale/en-US/paper/paper-misc.ftl`](../../../Resources/Locale/en-US/paper/paper-misc.ftl)
  - the alt depot manifest lists the chemical supplies it makes medical supplies from
  - the CC manifest lists the merged Caelestinus chute table
- [`Resources/Maps/_Mono/Outpost/caelestinus_central.yml`](../../../Resources/Maps/_Mono/Outpost/caelestinus_central.yml): Both Exped chutes become CC chutes, so every chute on the station pays from one table.
- [`Resources/Maps/_Mono/POI/Dungeons/zvezda.yml`](../../../Resources/Maps/_Mono/POI/Dungeons/zvezda.yml): A trade-goods vendor in the cargo bay, beside the turn-in chute.
- [`Resources/Maps/_Mono/POI/hokkaido.yml`](../../../Resources/Maps/_Mono/POI/hokkaido.yml): A turn-in chute and a trade goods vendor in the north cargo room, so the station is a market.
- [`Resources/Maps/_Mono/POI/tsfmchalcyon.yml`](../../../Resources/Maps/_Mono/POI/tsfmchalcyon.yml): A trade-goods vendor in the reception lobby, by the turn-in chute.
- [`Resources/Maps/_Mono/POI/tsfmchalcyon_hw.yml`](../../../Resources/Maps/_Mono/POI/tsfmchalcyon_hw.yml): A trade-goods vendor in the reception lobby, by the turn-in chute.
- [`Resources/Maps/_Mono/POI/underground.yml`](../../../Resources/Maps/_Mono/POI/underground.yml): The fence chute and the smuggling-crate vendor in the cargo bay, beside the sell pallets.
- [`Resources/Maps/_NF/POI/cargodepot.yml`](../../../Resources/Maps/_NF/POI/cargodepot.yml): A market console and crate machine, so what a depot buys can be bought back there.
- [`Resources/Maps/_NF/POI/cargodepotalt.yml`](../../../Resources/Maps/_NF/POI/cargodepotalt.yml): A market console and crate machine, so what a depot buys can be bought back there.
- [`Resources/Prototypes/_Crescent/Entities/Objects/Misc/trade.yml`](../../../Resources/Prototypes/_Crescent/Entities/Objects/Misc/trade.yml): chutes check where a crate was made
- [`Resources/Prototypes/_FarHorizons/Recipes/Lathes/reactor_parts.yml`](../../../Resources/Prototypes/_FarHorizons/Recipes/Lathes/reactor_parts.yml): fuel rods use reactor fuel from fission crates
- [`Resources/Prototypes/_Mono/Entities/Objects/Misc/trade.yml`](../../../Resources/Prototypes/_Mono/Entities/Objects/Misc/trade.yml): goes into a lathe like a sheet
- [`Resources/Prototypes/_Mono/Entities/Structures/dispensers.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/dispensers.yml)
  - open stations pay well for contraband
  - what the depot makes medical supplies from
  - every Caelestinus chute is this one, so it pays the Exped chute's higher medical price
  - match the CC manifest, with metals at 5000 and the goods only the Exped chute bought
  - match the Halcyon manifest, which buys fertilizer and no components
- [`Resources/Prototypes/_Mono/Entities/Structures/Machines/vending_machines.yml`](../../../Resources/Prototypes/_Mono/Entities/Structures/Machines/vending_machines.yml)
  - no free crates from the eject wire or wrecking the vendor
  - trade goods cost the station's live price
- [`Resources/Prototypes/_Mono/Outpost/caelestinus.yml`](../../../Resources/Prototypes/_Mono/Outpost/caelestinus.yml): names the station for its role in the sector economy
- [`Resources/Prototypes/_Mono/Recipes/Lathes/equipment.yml`](../../../Resources/Prototypes/_Mono/Recipes/Lathes/equipment.yml): turrets use gun parts from weapon part crates
- [`Resources/Prototypes/_Mono/Recipes/Lathes/Factions/shipyard.yml`](../../../Resources/Prototypes/_Mono/Recipes/Lathes/Factions/shipyard.yml): ship vouchers use ship parts from ship component crates
- [`Resources/Prototypes/_Mono/Recipes/Lathes/ShipgunAmmo/base_shipgun_ammo.yml`](../../../Resources/Prototypes/_Mono/Recipes/Lathes/ShipgunAmmo/base_shipgun_ammo.yml): researched ship ammo uses bulk munitions from ammo crates
- [`Resources/Prototypes/_Mono/Recipes/Lathes/ShipgunAmmo/research.yml`](../../../Resources/Prototypes/_Mono/Recipes/Lathes/ShipgunAmmo/research.yml): researched ship ammo uses bulk munitions from ammo crates
- [`Resources/Prototypes/_Mono/Recipes/Lathes/trade_crates.yml`](../../../Resources/Prototypes/_Mono/Recipes/Lathes/trade_crates.yml)
  - was 1000; crates cost real material
  - was 200; crates cost real material
  - was 500; crates cost real material
  - was 10; crates cost real material
  - was 20; crates cost real material
  - was 1200; crates cost real material
  - was 600; crates cost real material
  - was 50; crates cost real material
  - was 800; crates cost real material
- [`Resources/Prototypes/_NF/Entities/Stations/base.yml`](../../../Resources/Prototypes/_NF/Entities/Stations/base.yml): trade crates are materials to lathes, not console stock
- [`Resources/Prototypes/_NF/Entities/Structures/Specific/TradingCrates/crates.yml`](../../../Resources/Prototypes/_NF/Entities/Structures/Specific/TradingCrates/crates.yml): the pallet price follows the selling station's demand
- [`Resources/Prototypes/_NF/PointsOfInterest/depots.yml`](../../../Resources/Prototypes/_NF/PointsOfInterest/depots.yml): was StandardFrontierOutpost; pallets price materials by station stock
- [`Resources/Prototypes/_NF/Recipes/Lathes/chemistry.yml`](../../../Resources/Prototypes/_NF/Recipes/Lathes/chemistry.yml): medipens use medical stock from medical crates

<!-- WOLFGATE-GENERATED END -->
