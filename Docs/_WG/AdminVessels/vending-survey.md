# Lupercal vending survey

What the sector's stations sell from vending machines, what the Lupercal sells, and what was done to close the gap.
Surveyed from `main` on 2026-11-24: every `vendingMachineInventory` and vending machine prototype under
`Resources/Prototypes`, every point of interest's grid and the central station's map (`Resources/Maps`), and the
Lupercal's map (`Resources/SharedMaps/_WG/AdminVessels/lupercal.yml`).

## Counts

- 154 vending machine prototypes (not abstract) and 137 stock packs across the layers (upstream, `_NF`, `_Mono`, `_DV`,
  `_WG` and the rest). Many machines share a pack: the POI variants (`VendingMachineEngivendPOI`, `...YouToolPOI`,
  `...SalvagePOI`, `...TankDispenserEVAPOI`, `...VendomatPOI`, `...AstroVendPOI`) take cash and can't be unanchored,
  and their packs add one or two lines to the plain machine's at most (unlimited amounts aside).
- 33 station and POI grids carry vending machines; the central station, Caelestinus Central, carries 80 machines of
  48 kinds.
- The Lupercal carries 52 machines of 34 kinds, nine of them hot drinks machines.

## Stations

Machine ids without the `VendingMachine` prefix; the count after each station is its number of machines.

### Central station

| Station | Machines |
|---|---|
| CaelestinusCentral (80) | FlatpackVendPremium x5, NFCart, VendingBarDrobe, AmmoPOIMercenary, AstroVendPOI x4, AtmosDrobe, AutoTuneVend, Barista, BoozePOI, BountyVendPunk, CargoDrobe, ChefDrobe, Chefvend, Chemicals, Cigs, CigsPOI, CiviMedPlus, Clothing, Coffee, ColaRed, CuddlyCritterVend, Dinnerware, EngivendPOI x4, FuelVend x7, JaniDrobe, MailDrobe, MailVend x2, MediDrobe, Medical, Nutri, PMCclothingsPOI x2, PottedPlantVend, Pride, Robotics, SalvagePOI x5, Sec, SecDrobe, Seeds, SovietSoda, Sustenance, TankDispenserEVA x2, TankDispenserEVAPOI x5, Theater, TraderDepot x2, TraderTradeMall x2, VendomatPOI, YouToolPOI x4, WGLuvvits |

### Service stations (always spawned)

| Station | Machines |
|---|---|
| WGDrydock (15) | CiviMedPlus, Coffee, EngivendPOI, FlatpackVend, FuelVend x3, JaniDrobe, PwrGame, SalvagePOI, Snack, TankDispenserEVAPOI x3, YouToolPOI |
| WGChopShop (13) | Coffee x2, Cola x2, Discount, Engivend, Snack x2, WallMedical x4, YouTool |
| WGAsfLantern (20) | Chefvend, Cigs, CiviMedPlus, Coffee x3, Cola, Dinnerware, Discount, Engivend, FlatpackVend, FuelVend, Nutri, Seeds, Snack x2, Sustenance, WGAsfDrobe x2, WGAsfGrantKiosk |
| Edison (9) | EngiDrobe, EngivendPOI, FlatpackVend, FuelVend x3, TankDispenserEngineering, VendomatPOI, YouToolPOI |
| TSFMCIndustry (11) | AstroVendPOI, BluespaceDrive, CigsPOI, EngivendPOI, FlatpackVend, FuelVend, Medical, PMCclothingsPOI, SalvagePOI, VendomatPOI, YouToolPOI |
| Mining (8) | DrGibb, EngivendPOI, FlatpackVend, SalvagePOI x2, Smite, Sustenance, YouToolPOI |

### Rest stops and leisure (one or more each round)

| Station | Machines |
|---|---|
| Tinnia (14) | VendingBarDrobe, Booze, ChefDrobe, Chefvend, Cigs x2, Coffee, Dinnerware, Games, HappyHonk, Hydrobe, Nutri, PottedPlantVend, SeedsUnlocked |
| Bahama (16) | VendingBarDrobe, AutoTuneVend, Booze, ChefDrobe, Chefvend, Cigs, Clothing, Condiments x2, CuddlyCritterVend, Dinnerware, Games, Nutri, PottedPlantVend, SeedsUnlocked, SovietSoda |
| CaseysCasino (10) | VendingBarDrobe, AutoTuneVend, Booze, Cigs x3, Condiments, CuddlyCritterVend, Games, TankDispenserEVA |
| ThePit (8) | Booze, BoxingDrobe x2, Chefvend, Cigs, CiviMedPlus, CuddlyCritterVend, Dinnerware |
| Grifty (7) | Booze, Cigs x2, Coffee, Condiments, FuelVend x2 |
| McHobo (1) | FuelVend |
| Omnichurch (2) | Chapel, Hydrobe |
| AnomalousLab (6) | Chemicals, Donut, DrGibb, Medical, PwrGame, SpaceUp |
| CargoDepot (4) | TraderDepot x4 |
| CargoDepotAlt (4) | TraderDepotAlt x4 |

### Faction bases

| Station | Machines |
|---|---|
| TSFMCHalcyon (43) | NFCartNfsd, AmmoPOIMercenary, AstroVendPOI x3, BluespaceDrive x3, Booze, ChefDrobe, Chefvend x2, Chemicals, CigsPOI, CiviMedPlus, Condiments, Dinnerware, Engivend, FlatpackVend x3, FuelVend x4, NfsdDrobe, NfsdTech, Nutri x2, SalvagePOI, SciDrobe, SeedsUnlocked x2, Sustenance, TSFMCUplink, TankDispenserEVA x3, TankDispenserEVAPOI, TankDispenserEngineering, YouTool, WGTraderHalcyon, WGTsfOutfitter |
| USSPCamelot (45) | AmmoPOIMercenary, AstroVendPOI, BluespaceDrive, BoozePOI x3, BountyVendPOI, Chefvend, Chemicals, CigsPOI x4, Clothing, CuddlyCritterVend, Dinnerware, Discount x2, Donut, EngivendPOI, ExpeditionaryFlatpackVend, FlatpackVend, FuelVend x5, Medical, Nutri, PottedPlantVendPOI, SalvagePOI, SeedsUnlocked, SovietSoda x4, Sustenance, TankDispenserEVAPOI x3, TraderBaikal x3, USSP, YouToolPOI |
| HeliosFortress (35) | AmmoPOI, BoozePOI, BountyVendPOI, Chapel, Chefvend, Chemicals, CiviMed, Coffee, Dinnerware, EngivendPOI, FlatpackVend, FuelVend x5, Medical, Nutri, PDVUplink, Robotics, Salvage, Seeds, SovietSoda, TankDispenserEVAPOI x8, TankDispenserEngineering, YouToolPOI, WGPdvOutfitter x2 |
| Hokkaido (45) | MonoCartMieyo, AmmoPOI, AstroVendPOI x2, Chefvend, Chemicals, CigsPOI, CiviMedPlus, Dinnerware, EngivendPOI x3, FlatpackVend x2, FuelVend x10, MMCUplink, Nutri, SalvagePOI, SeedsUnlocked, TankDispenserEVAPOI x10, VendomatPOI x3, YouToolPOI x3, WGTraderHokkaido |
| Underground (20) | AmmoPOIMercenary x2, AstroVendPOI x2, Booze, BountyVendPOI, Chefvend, Chemicals, CiviMedPlus, Dinnerware, Engivend, FuelVend x2, Nutri, SeedsUnlocked, SyndieContrabandPOI, SyndieDrobe, TankDispenserEVAPOI, YouTool, WGFence |
| Zvezda (13) | AstroVendPOI, BoozePOI, BoxingDrobe, Chefvend, Dinnerware, FlatpackVend, FuelVend, MediDrobePOI, Medical, SalvagePOI, TankDispenserEVAPOI x2, WGTraderZvezda |

### Hyperwar preset only

| Station | Machines |
|---|---|
| TSFMCHalcyonHyperwar (40) | As TSFMCHalcyon, with a Barista, one AstroVendPOI fewer and without the AmmoPOIMercenary, CigsPOI and SalvagePOI |
| HeliosFortressHyperwar (33) | As HeliosFortress, with AmmoPOIMercenary, AstroVendPOI, CiviMedPlus, ColaBlack and Donut, without AmmoPOI, BountyVendPOI, Chapel, CiviMed, Robotics, Salvage and SovietSoda |
| Chengdu (10) | AstroVendPOI, Medical, TSFMCUplink, TankDispenserEVAPOI x6, YouToolPOI |
| Jupiter_HW (40) | AmmoPOIMercenary, AstroVendPOI, BoozePOI, Chefvend, Chemicals, Cigs, CigsPOI x2, CiviMedPlus, Coffee, Dinnerware, DrGibb, EngivendPOI, FlatpackVend, FuelVend x7, GamesPOI, Medical, PottedPlantVendPOI, PwrGame, Seeds x2, Snack, SnackTeal x2, Sustenance x2, TankDispenserEVAPOI x7, YouToolPOI |

### Derelicts

| Station | Machines |
|---|---|
| Jupiter (8) | Booze, Chemicals, FuelVend x2, Seeds, Sustenance x2, TankDispenserEVA |
| Lancelot (7) | AstroVend, Chemicals, Salvage, TankDispenserEVA x3, Vendomat |
| Polaris (1) | Chemicals |

## The Lupercal

BarDrobe, AtmosDrobe, Booze, CargoDrobe, Chang, Chefvend, ChemDrobe, Chemicals, Cigs x2, Clothing, Coffee x9, Cola x4,
Dinnerware, Discount, Donut x2, DrGibb, EngiDrobe, Engivend, Hydrobe, JaniDrobe, MailVend, MediDrobe x2, Medical x2,
Nutri, RoboDrobe, Robotics, Salvage, SciDrobe, Seeds, Snack x3, TankDispenserEVA x2, Vendomat, Winter, YouTool.

## What the Lupercal lacked

The most common station kinds, by how many grids above carry one (any variant), against the Lupercal:

| Kind | Grids | Lupercal | Done |
|---|---|---|---|
| FuelVend | 17 | none | map: add one (below) |
| Chefvend, Dinnerware | 14 | has | extras |
| Chemicals | 13 | has | extras |
| FlatpackVend | 13 | none | map: add one; its pack gets the premium vendor's civilian lines |
| TankDispenserEVA (POI) | 12 | has the plain one | parity line (water vapour double tank), extras |
| YouTool, Engivend, Salvage, Vendomat (POI) | 4 to 11 | has the plain ones | parity lines (inflatables, salvage radar), extras |
| AstroVend (POI) | 11 | none | map: add one |
| Nutri | 11 | has | extras |
| CiviMedPlus | 10 | none | folded into the NanoMed Plus (medipens, hyposprays, body bags, pens, hydraulic fluid) |
| Medical, Coffee, Booze, Cigs, Seeds | 6 to 9 | has | extras; Barista's tea and biscuits folded into the hot drinks machine |
| Sustenance | 8 | none | folded into the Getmore snack machine (MREs, nutribricks) |
| AmmoPOIMercenary | 6 | none | not added: a gun and ammo vendor; the Lupercal arms its own crew |
| TankDispenserEngineering | 5 | none | plasma tank folded into the EVA dispenser |
| ChefDrobe | 5 | none | map: add one |
| Condiments | 5 | none | folded into Dinnerware |
| CuddlyCritterVend | 5 | none | map: add one |
| SovietSoda, PwrGame, SpaceUp, Smite | 1 to 4 | none | folded into Cola and Dr. Gibb (one soda set) |
| BluespaceDrive | 4 | none | its FTL drive flatpack goes in the flatpack vendor's extras |
| AutoTuneVend, Games, PottedPlantVend | 3 | none | map: add one each |
| BountyVendPOI, Sec, SecDrobe, PMCclothings | 1 to 3 | none | not added: security and mercenary gear |
| MailDrobe, Theater, Pride, NFCart (PTech), WGLuvvits, FlatpackVendPremium | 1 (central) | none | MailDrobe, Theater and PTech: map. Pride and boxing drobe lines folded into ClothesMate; Luvvits' pies into the snack machine; the premium flatpacks' civilian lines into the flatpack vendor's extras |
| Chapel, BoxingDrobe, HappyHonk, Barista | 1 or 2 | none | not added as machines (Barista and boxing folded in) |

Faction machines (uplinks, outfitters, Syndicate and NFSD drobes, trader and fence vendors, the PDV, TSF and MMC
cartridge vendors) were left out: they serve a faction or the trade economy, not an admin station.

## Map additions

These kinds have no Lupercal machine to fold into; they need a machine placed by the generator. Each becomes unlimited
on its own once aboard (the sweep below), and the flatpack vendor also takes `WGLupercalFlatpackExtras`.

| Prototype | Room |
|---|---|
| `VendingMachineFuelVend` | Hardware store (or the Exchange, beside the quartermaster) |
| `VendingMachineFlatpackVend` | Hardware store |
| `VendingMachineAstroVend` | EVA storage |
| `VendingMachineChefDrobe` | Kitchen |
| `VendingMachineMailDrobe` | Mail room |
| `VendingMachineTheater` | Lounge (beside the ClothesMate and Winter drobe) |
| `VendingMachineCuddlyCritterVend` | Lounge |
| `VendingMachineGames` | Lounge |
| `VendingMachineAutoTuneVend` | Bar or lounge |
| `VendingMachinePottedPlantVend` | Hydroponics |
| `NFVendingMachineCart` | Cargo office or the General dock lobby (N) |

## How it is done

`WGShipFitting` on the Lupercal (`Resources/Prototypes/_WG/AdminVessels/lupercal.yml`) sets `unlimitedVending` and
`vendingExtras`. The fitting pass (ShipCommand) adds each machine's extras pack to its regular stock and sets every
regular line to `uint.MaxValue`, Frontier's unlimited amount, which a vend leaves alone and a restock skips. Every five
seconds the same system stocks any machine aboard that isn't, so a machine placed later, or a line knocked loose by a
forced eject, is unlimited again. The extras are in `Resources/Prototypes/_WG/AdminVessels/vending.yml`. Prices are
unchanged; the ship's price rules (ShipCommand) still decide what each customer pays.
