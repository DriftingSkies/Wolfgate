# Hideouts

Turns a player's outpost (module Outposts) into a home that is bought up level by level: modules with prices, build
times and prerequisites, and a hangar that keeps one ship of the owner's between rounds, with insurance on it. The
design, the numbers and the company outpost plan are in `Docs/_WF/Hideouts/DESIGN.md`; the prefabs are drawn in
`Docs/_WF/Hideouts/prefabs.png`.

Players use the outpost console's Hideout tab (`WFHideoutPanel`, off with `wf.hideouts.enabled`). Admins have
`wfhideout finish <player>` and `wfhideout setlevel <player> <module> <level>`.

Modules (`WFHideoutSystem`, `wfHideoutModule` in `Resources/Prototypes/_WF/Hideouts/modules.yml`): every outpost
carries a `WFHideoutComponent` with the level of each module, saved with the outpost. The owner starts a module's next
level (`TryBuild`) if the modules it needs stand and their bank covers its price; one level builds at a time, and its
build time only runs while the outpost stands in a round. A finished level (`Complete`) leaves its grants at the
console and adds to the outpost's caps: floor tiles and objects through `WFOutpostCapsEvent`, cryopods through
`WFOutpostPodLimitEvent`. A module marked `company` is only built on an outpost founded under a company.

Prefabs: a level with a `prefab` leaves a survey stake (`WFHideoutSurveyStake`) at the console. Used on open ground in
the outpost's claim (`TryUseStake`), it lays the prefab's grid onto the outpost with `WFOutpostSystem.TryStampPrefab`:
the prefab's bottom middle tile lands on the tile pointed at and it extends the way the user faces, with its roofs and
everything on it. The prefab grids are written by `Tools/_WF/Hideouts/build_prefabs.py` from the layouts in it.
A level may list `layouts`, other grids its stake can be set to from the same menu. A stake set to a theme (`wfHideoutTheme` in `themes.yml`, picked from the stake's alt-click menu) passes a
`WFPrefabSkin` that swaps the prefab's walls, windows, doors and floors as they are laid, for the theme's price.

The blueprint (`WFHideoutSystem.Blueprint`, the console's Blueprint tab, `WFBlueprintPanel` and `WFBlueprintCanvas`):
a builder paints brushes (`wfBlueprintBrush` in `blueprint.yml`) onto a plan of the outpost and has it built at once
for its price (`TryApplyBlueprint`). Floor goes through `WFOutpostSystem.PaintFloor`, roof through `PaintRoof`.
The palette is every `wfBlueprintBrush` and every prototype on a `wfBlueprintCatalogue` shelf (`WFBlueprintCatalog`).
`ApplyPlan` builds a plan on any grid; the shipwright consoles use it for a hull's interior (`OnInteriorApply`).

Saving: a hideout answers `WFOutpostWantsSlotEvent`, so its outpost is given a save slot and autosaved without a
manifest, over ticks (`WFOutpostSystem.SlicedSave`). `LeaveTradeHub` leaves a packed `WFHideoutTradeHub` at founding.

The hangar (`WFHideoutSystem.Hangar`): the Hangar module's prefab is an apron with a hangar beacon
(`WFHideoutHangarPad`, a landing pad that needs no power), whose widest size follows the module's level. The owner
registers the hull parked there (`TryRegister`; it has to be theirs by deed or by account), one per account. A
registered hull is serialised with `ShipyardSystem.TrySaveShipDeferred` every time it lands on its owner's hangar pad
(`WFPadDockedEvent`) and once more at round end if it is home, into `wolfgate_hideout_ship`
(`ServerDbManager.Hideouts`). When the owner's outpost next lands (`WFOutpostSettledEvent`), the ship is loaded,
stripped like any loaded save and set on the pad (`TryPlaceStored`); `TryRecall` does the same from the console. It is
the owner's through ship access by account and carries no deed, so it can't be sold at a shipyard; a hull registered
while it still had a deed is forgotten if it is sold.

Two kinds of hangar: the Hangar module's pad (a faction hangar) only registers a stock shipyard hull
(`IsFactionHull`: a `VesselComponent`, and the owner's company if its listing names companies). The Shipwright module's
bay (`WFShipwrightBayComponent` on its pad) registers custom ships. `TryGetHangar` takes which kind to look for. A
system that refuses a save sets `WFHideoutShipSaveAttemptEvent.Refusal`.

Shipwright (`Shipwright/`, `WFShipwrightSystem`): a hull on a refit point budget (`Survey`). Guns take points by the
hardpoint size they need, systems (`wfShipwrightSystem`, matched by prototype or component) their own; the budget is
the frame's points, or the hand-built hull's size, plus the bay's levels (`shipwrightPoints`). Only parts marked
`WFShipwrightFittedComponent` count as fitted: a gun that isn't won't fire on a shipwright ship (`ShotAttemptedEvent`
on `HardpointAnchorableOnlyComponent`), and a hull that doesn't pass isn't saved. The console
(`WFShipwrightSystem.Console`, client `WFShipwrightConsoleWindow`) is a drydock console whose cranes, linked when its
window first opens (`LinkCranes`), do the work as drydock jobs: `TryBuildFrame` loads a `wfShipwrightFrame`'s grid onto
the pad, strips it to a keel tile (`StripToKeel`) and queues every tile and part back on; `TryRefit` removes and
installs guns and systems; `TryCertify` inspects hand-built parts. The drydock raises `WFDrydockPartInstalledEvent`
and `WFDrydockPartInspectedEvent` on the hull, which mark parts fitted, and `WFDrydockJobEndedEvent` on the console,
which registers a finished frame or saves a refitted ship. The numbers are `wfShipwrightRules` in `shipwright.yml`.

Hardpoints (`TryRefit`'s other overload): swaps (`hardpoints`), removals and placements on free edge tiles
(`FreeEdgeTiles`), priced and pointed by `mountPoints` and `mountPrices`; a frame's hardpoints carry
`WFShipwrightStockComponent` and take no points. `Ceiling` is the largest frame's points; `customMaxTiles` its floor.

Cargo: `Cargo` lists what a hangar ship doesn't keep (loose items, crates, storage contents); `StripCargo` takes it off a
restored ship and `Appraise` leaves it out. A strongbox (`WFShipStashComponent`, `WFShipStrongbox`) is kept, its
contents stamped `WFShipSaveOriginComponent` by `StampStash`; outposts' `IsKept` leaves stamped items out and
`IsKeptAnywhere` keeps them for the ship.

Admins have `wfhideouttest` (`WFHideoutTestCommand`), which builds a planetside test world with a ready outpost.

Insurance: `TryInsure` stores the parked ship as it stands for a share of its value. A registered hull deleted during a
round is lost (`Lose`) and no longer comes back. `TryClaim` writes off whatever hull is out (no one may be aboard),
makes the insured state the stored ship and sets it on the pad, for a share of the insured value; the policy is used
up. The shares are the lowest `insurancePremium` and `insuranceDeductible` among the built levels.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Hideouts/ServerDbManager.Hideouts.cs`](ServerDbManager.Hideouts.cs)
- [`Content.Server/_WF/Hideouts/Shipwright/WFShipwrightComponents.cs`](Shipwright/WFShipwrightComponents.cs)
- [`Content.Server/_WF/Hideouts/Shipwright/WFShipwrightSystem.Console.cs`](Shipwright/WFShipwrightSystem.Console.cs)
- [`Content.Server/_WF/Hideouts/Shipwright/WFShipwrightSystem.cs`](Shipwright/WFShipwrightSystem.cs)
- [`Content.Server/_WF/Hideouts/Shipwright/WFShipwrightSystem.Faction.cs`](Shipwright/WFShipwrightSystem.Faction.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutCommand.cs`](WFHideoutCommand.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutComponents.cs`](WFHideoutComponents.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutRegistrySystem.cs`](WFHideoutRegistrySystem.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutSystem.Blueprint.cs`](WFHideoutSystem.Blueprint.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutSystem.cs`](WFHideoutSystem.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutSystem.Hangar.cs`](WFHideoutSystem.Hangar.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutSystem.Home.cs`](WFHideoutSystem.Home.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutSystem.Materials.cs`](WFHideoutSystem.Materials.cs)
- [`Content.Server/_WF/Hideouts/WFHideoutTestCommand.cs`](WFHideoutTestCommand.cs)

### Shared

- [`Content.Shared/_WF/Hideouts/Blueprint/WFBlueprintCatalog.cs`](../../../Content.Shared/_WF/Hideouts/Blueprint/WFBlueprintCatalog.cs)
- [`Content.Shared/_WF/Hideouts/Blueprint/WFBlueprintUi.cs`](../../../Content.Shared/_WF/Hideouts/Blueprint/WFBlueprintUi.cs)
- [`Content.Shared/_WF/Hideouts/HideoutCVars.cs`](../../../Content.Shared/_WF/Hideouts/HideoutCVars.cs)
- [`Content.Shared/_WF/Hideouts/Shipwright/WFShipwrightPrototypes.cs`](../../../Content.Shared/_WF/Hideouts/Shipwright/WFShipwrightPrototypes.cs)
- [`Content.Shared/_WF/Hideouts/Shipwright/WFShipwrightUi.cs`](../../../Content.Shared/_WF/Hideouts/Shipwright/WFShipwrightUi.cs)
- [`Content.Shared/_WF/Hideouts/WFHideoutModulePrototype.cs`](../../../Content.Shared/_WF/Hideouts/WFHideoutModulePrototype.cs)
- [`Content.Shared/_WF/Hideouts/WFHideoutRegistryUi.cs`](../../../Content.Shared/_WF/Hideouts/WFHideoutRegistryUi.cs)
- [`Content.Shared/_WF/Hideouts/WFHideoutRulesPrototype.cs`](../../../Content.Shared/_WF/Hideouts/WFHideoutRulesPrototype.cs)
- [`Content.Shared/_WF/Hideouts/WFHideoutThemePrototype.cs`](../../../Content.Shared/_WF/Hideouts/WFHideoutThemePrototype.cs)
- [`Content.Shared/_WF/Hideouts/WFHideoutUi.cs`](../../../Content.Shared/_WF/Hideouts/WFHideoutUi.cs)
- [`Content.Shared/_WF/Hideouts/WFShipSaveOriginComponent.cs`](../../../Content.Shared/_WF/Hideouts/WFShipSaveOriginComponent.cs)

### Client

- [`Content.Client/_WF/Hideouts/Blueprint/WFBlueprintCanvas.cs`](../../../Content.Client/_WF/Hideouts/Blueprint/WFBlueprintCanvas.cs)
- [`Content.Client/_WF/Hideouts/Blueprint/WFBlueprintIcons.cs`](../../../Content.Client/_WF/Hideouts/Blueprint/WFBlueprintIcons.cs)
- [`Content.Client/_WF/Hideouts/Blueprint/WFBlueprintPanel.cs`](../../../Content.Client/_WF/Hideouts/Blueprint/WFBlueprintPanel.cs)
- [`Content.Client/_WF/Hideouts/Shipwright/WFShipwrightConsoleBoundUserInterface.cs`](../../../Content.Client/_WF/Hideouts/Shipwright/WFShipwrightConsoleBoundUserInterface.cs)
- [`Content.Client/_WF/Hideouts/Shipwright/WFShipwrightConsoleWindow.cs`](../../../Content.Client/_WF/Hideouts/Shipwright/WFShipwrightConsoleWindow.cs)
- [`Content.Client/_WF/Hideouts/Shipwright/WFShipwrightHullMap.cs`](../../../Content.Client/_WF/Hideouts/Shipwright/WFShipwrightHullMap.cs)
- [`Content.Client/_WF/Hideouts/WFHideoutPanel.cs`](../../../Content.Client/_WF/Hideouts/WFHideoutPanel.cs)
- [`Content.Client/_WF/Hideouts/WFHideoutRegistryBoundUserInterface.cs`](../../../Content.Client/_WF/Hideouts/WFHideoutRegistryBoundUserInterface.cs)
- [`Content.Client/_WF/Hideouts/WFHideoutRegistryWindow.cs`](../../../Content.Client/_WF/Hideouts/WFHideoutRegistryWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Hideouts/BlueprintTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Hideouts/BlueprintTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Hideouts/HideoutTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Hideouts/HideoutTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Hideouts/RegistryTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Hideouts/RegistryTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Hideouts/ShipwrightTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Hideouts/ShipwrightTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Hideouts/blueprint.yml`](../../../Resources/Prototypes/_WF/Hideouts/blueprint.yml)
- [`Resources/Prototypes/_WF/Hideouts/blueprint_catalogue.yml`](../../../Resources/Prototypes/_WF/Hideouts/blueprint_catalogue.yml)
- [`Resources/Prototypes/_WF/Hideouts/entities.yml`](../../../Resources/Prototypes/_WF/Hideouts/entities.yml)
- [`Resources/Prototypes/_WF/Hideouts/modules.yml`](../../../Resources/Prototypes/_WF/Hideouts/modules.yml)
- [`Resources/Prototypes/_WF/Hideouts/shipwright.yml`](../../../Resources/Prototypes/_WF/Hideouts/shipwright.yml)
- [`Resources/Prototypes/_WF/Hideouts/themes.yml`](../../../Resources/Prototypes/_WF/Hideouts/themes.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Hideouts/hideouts.ftl`](../../../Resources/Locale/en-US/_WF/Hideouts/hideouts.ftl)

### Shared maps

- [`Resources/SharedMaps/_WF/Hideouts/canteen.yml`](../../../Resources/SharedMaps/_WF/Hideouts/canteen.yml)
- [`Resources/SharedMaps/_WF/Hideouts/canteen_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/canteen_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/company_wing.yml`](../../../Resources/SharedMaps/_WF/Hideouts/company_wing.yml)
- [`Resources/SharedMaps/_WF/Hideouts/company_wing_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/company_wing_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/farm.yml`](../../../Resources/SharedMaps/_WF/Hideouts/farm.yml)
- [`Resources/SharedMaps/_WF/Hideouts/farm_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/farm_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/garden.yml`](../../../Resources/SharedMaps/_WF/Hideouts/garden.yml)
- [`Resources/SharedMaps/_WF/Hideouts/garden_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/garden_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/hangar.yml`](../../../Resources/SharedMaps/_WF/Hideouts/hangar.yml)
- [`Resources/SharedMaps/_WF/Hideouts/library.yml`](../../../Resources/SharedMaps/_WF/Hideouts/library.yml)
- [`Resources/SharedMaps/_WF/Hideouts/library_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/library_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/lounge.yml`](../../../Resources/SharedMaps/_WF/Hideouts/lounge.yml)
- [`Resources/SharedMaps/_WF/Hideouts/lounge_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/lounge_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/medbay.yml`](../../../Resources/SharedMaps/_WF/Hideouts/medbay.yml)
- [`Resources/SharedMaps/_WF/Hideouts/medbay_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/medbay_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/powerhouse.yml`](../../../Resources/SharedMaps/_WF/Hideouts/powerhouse.yml)
- [`Resources/SharedMaps/_WF/Hideouts/powerhouse_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/powerhouse_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/quarters.yml`](../../../Resources/SharedMaps/_WF/Hideouts/quarters.yml)
- [`Resources/SharedMaps/_WF/Hideouts/quarters_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/quarters_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/quarters_c.yml`](../../../Resources/SharedMaps/_WF/Hideouts/quarters_c.yml)
- [`Resources/SharedMaps/_WF/Hideouts/shipwright_bay.yml`](../../../Resources/SharedMaps/_WF/Hideouts/shipwright_bay.yml)
- [`Resources/SharedMaps/_WF/Hideouts/vault.yml`](../../../Resources/SharedMaps/_WF/Hideouts/vault.yml)
- [`Resources/SharedMaps/_WF/Hideouts/vault_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/vault_b.yml)
- [`Resources/SharedMaps/_WF/Hideouts/workshop.yml`](../../../Resources/SharedMaps/_WF/Hideouts/workshop.yml)
- [`Resources/SharedMaps/_WF/Hideouts/workshop_b.yml`](../../../Resources/SharedMaps/_WF/Hideouts/workshop_b.yml)

### Tools

- [`Tools/_WF/Hideouts/build_prefabs.py`](../../../Tools/_WF/Hideouts/build_prefabs.py)

### Docs

- [`Docs/_WF/Hideouts/DESIGN.md`](../../../Docs/_WF/Hideouts/DESIGN.md)
- [`Docs/_WF/Hideouts/prefabs.png`](../../../Docs/_WF/Hideouts/prefabs.png)

## Non-modular edits

- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - the ship an account keeps in its outpost's hangar
  - one hangar ship per account
  - the ship an account keeps in its outpost's hangar, as it last landed and as it was insured
- [`Content.Server/_Mono/Persistence/PersistentProfileSystem.Items.cs`](../../_Mono/Persistence/PersistentProfileSystem.Items.cs): something saved in a hangar ship's strongbox comes back with the ship
- [`Resources/Maps/_Mono/Outpost/caelestinus_central.yml`](../../../Resources/Maps/_Mono/Outpost/caelestinus_central.yml): Mapped the outpost registry console on the public side of the shipyard trader's counter.

<!-- WOLFGATE-GENERATED END -->
