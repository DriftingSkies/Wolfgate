# ShipCommand

Owner control for large admin vessels such as the Lupercal: who may go where, how the ship's NPC crew and counters
behave, what services cost and for whom, a log of service use, restaffing, and personal presets. The design is
`.agent_docs/wolfgate/shipwright/lupercal/2026-11-09-ship-command-code-plan.md`; this module is built in phases.

## Authority

`WGShipCommandComponent` on the grid holds the command lists and the admin powers. `GetAuthority` ranks anyone
against the ship: Owner (ShipAccess's owner: the deed, or a player an admin tool registered the ship to), Admin
(an active admin, or event staff while `EventStaffAreAdmins`), Officer, Staff, Guest or Barred. People are listed
by their ID card's record key, their player or both, and either matches. Each person has one standing, so adding
them to a list takes them off the other two; the barred list wins over the staff and officer lists, never over an
admin or the owner. `CanConfigure` is the owner, and admins while `AdminsAtConsole`; `CanOperate` adds officers.
ShipAccess asks this module through `WGShipManagerEvent` before refusing a non-owner its access tab, lock verbs or
console locks, and whoever may configure the ship, admins included, is let through; the client's
`WGShipCommandClientSystem` answers the same event from its own admin flags, so the access tab shows admins its
controls. Admins share the owner's powers
by default, each a switch on the component: zones, the console, never targeted
by the crew, served free. Where the owner turns one off, an admin stands where the lists put them
(`GetAuthority(user, grid, out listed)`): an admin on the officers list still operates the ship and is still the
crew's Boss. The lists are round state.

## Zones

`WGShipZonesComponent` on the grid lists the zones (id, Fluent name, mode, tile areas), saved with the map; a door
belongs to a zone through ShipAccess's `WGDoorAccessRuleComponent.Zone`. `SetZoneMode` switches one: Public and
Sealed give its doors those ShipAccess rules, Staff and Locked give them the Zone rule, whose reader keys this
module names through `WGDoorZoneKeysEvent`: Staff admits officers, the staff list and admins; Locked officers and
admins; the owner is always in. Admins are matched by the ID card they wear. The keys are recomputed when a list
or switch changes and every two seconds, so a new card or an arriving admin takes effect; a spawned ship's doors
take their zones' saved modes on map init. Each switch is admin-logged and raises `WGShipZoneModeChangedEvent`
on the grid. The command console's Zones tab switches them; admins can also use
`wg_shipzone list|set <grid|here> [<zone|all> <mode>]`.

## Wires

With `StaffOnlyWires` set (the Lupercal sets it in its vessel prototype), a maintenance panel aboard opens and
closes for the staff list and up alone (`CanTouchWires`), so nobody else hacks the ship's doors and machines or pries
out their boards. It cancels SharedWires' `AttemptChangePanelEvent` on the server, with a popup to whoever tried.

## Crew

The ship's NpcCrew judge visitors through `WGCrewAuthorizeEvent`, which `WGCrewSecuritySystem` raises on a
crewman's home grid before its company and faction rules; that one hook covers boarding, the gunners' consoles and
friendly fire. `Welcome` answers: anyone who attacked the crew (its retaliation memory or shared alert) is turned
away as hostile; the barred are turned away with `BarredResponse`; the owner, and admins while
`AdminsNeverTargeted`, are always welcome; an admin without that pass counts as an officer while `AdminsInZones`,
else by the lists; elsewhere the zone under the visitor decides (Public and the open deck:
everyone; Staff: the staff list or better; Locked and Sealed: officers or better), and someone turned away gets
that mode's response (Warn by default, Hostile in a Sealed zone). Warn is NpcCrew's: a warning, then the fighters
after 30 seconds, forgotten once the visitor is somewhere they may be, since the crew's one-second sweep asks again.
There is no escort out yet. The crew take a blow, a shove or a pickpocketing from the Boss (`IsBoss`: the owner,
officers, and admins as `Welcome` counts them) without a fight, through NpcCrew's `WGCrewForgiveEvent`; CrewBarks has
them thank him for it. Docking ships are welcome while `DocksWelcome`. The crew's own cards get Staff and
Locked keys, so they walk those zones.

## Prices and the service log

`WGShipServiceSystem.Quote(service, customer, kind, basePrice)` is what a customer pays for a service aboard a
commanded ship, decided by the service's grid: the owner and admins pay nothing while `OwnerServedFree` and
`AdminsServedFree` are on; otherwise the lowest multiplier among the enabled `PriceRules` that cover the customer
(everyone, the staff list or better, officers or better, one person, one company) and the service kind applies, or
the usual price if none does. `Record` writes one `WGShipService` admin log line, linked to the customer, and one
entry in the grid's round ledger (`WGShipServiceLedgerComponent`, the last 500). Off a commanded ship both do
nothing, so hooks are safe everywhere. Hooked so far: vending machines (marked lines into
`VendingMachineSystem.ShipCommand.cs`; the client still shows the usual price), the trader shop (each catalogue line
quoted, and the basket charged the sum of the lines shown), the drydock (repairs, refits, drift cleaning, bookings, each
viewer shown their own price; refunds scale with the share paid), the vehicle terminal (sales and repairs, each
customer sent their own prices), safety deposit boxes and the shipyard consoles (`ShipyardSystem.ShipCommand.cs`; the
listing shows the buyer's price).

Anything sold below its list price carries a `WGBargainComponent` so it can't be resold at a profit. A ship's grid holds
the discount (list price less what was paid), which `ResaleValue` takes off its appraisal wherever it is sold (the
shipyard, the used-ship trader, the chop shop), never below zero, so goods added later keep their value; the used lot
strips the mark from its copy. Goods and vehicles (`MarkBargainGoods`) spread the discount, or what they appraise at over
the price paid if that is more, over themselves and everything inside them, and `PricingSystem` (`PricingSystem.ShipCommand.cs`,
marked lines in `GetPrice` and the pallet's `GetPriceWithVendingDiscount`) takes each part's share off its own price, so
the cargo pallet, ship appraisals, traders taking goods in trade and the vehicle terminal's buy-back all pay at most what
was paid. A split stack takes its share; a bargain flatpack or vehicle beacon passes it to what it opens into. Repairs
and value caps go by `GetUndiscountedPrice`.
Medical stays free and is only logged. `Refuses(service, customer, out reason)` turns the barred away from every
service aboard: vendors, trader counters (at the start of a conversation and at each choice, checkout and used-ship
purchase), the drydock berths and bookings, the vehicle terminal, the deposit box console (no new box, and one they own
is neither taken out, reclaimed nor stored while they are barred), the shipyard's purchases and sales, cargo order
approval, the sale pallets and the gas sale console. Rule edits are logged as `WGShipServiceRules`. The command console's Prices tab edits the rules; admins can also
use `wg_shipprice`. A drydock terminal aboard sets one rule of its own, `drydock` (everyone, drydock work), from its
price row (Drydock README).

## The ship's shield

A generator with `WGShipShieldStrengthControl` opens only for the staff list and up aboard a commanded ship
(`WGShipShieldControlSystem`). They set its strength in the generator's window, to any value up to a billion. The
field keeps its share of health and its time to refill, and power at full load stays the same, so a very strong
field can't brown the ship out.

## Crew roster

`WGShipRosterComponent` on the grid staffs two kinds of post marker (`posts.yml`): crew posts (`WGShipCrewPost*`),
with NpcCrew crewmen of one group and mission (boarding Warn, docking Ignore, pilot holding), and counter posts
(`WGShipCounterPost*`), with the station trader the marker names, standing on it and facing the way it faces, at
the counter in front. A post marker spawns nothing on its own: the roster reads them on map init and staffs them
where they stand (`StaffOnSpawn`). Each crewman is spawned alone (`WGCrewSystem.SpawnCrewman`, then
`WGCrewSetupSystem.ApplyMission`), so restaffing one post never resets the rest of the crew or the grid's faction.
A post is lost when its occupant dies or is deleted (`WGShipRosterMemberComponent`); bodies stay. Restaffing is by
hand (the command console's Crew tab, or `wg_shipcrew staff`) unless `AutoRestaff` is switched on, when a lost post is filled after `RestaffDelay`, at
most `RestaffsPerHour` an hour; with a crew bay marker (`WGShipCrewBay`) restaffed crew come aboard there and walk
to their posts. A post can be stood down (`wg_shipcrew down|up`), which relieves its occupant and leaves it empty,
or held by a player who took its job (below). A relieved crewman says a word, drops his duty and walks to the crew
bay, where he leaves the ship (or after `ReliefTimeout`); a relieved trader closes his counter and goes at once.
Each staffing raises `WGShipPostStaffedEvent` (broadcast, with whether he came aboard at the crew bay), and each relief
`WGShipPostRelievedEvent` on the crewman, which a system that has him say his own word marks as spoken (CrewBarks).
Staffing refreshes the zone keys at once, so a newcomer's card opens his zones without waiting for the sweep. The
roster also sets its crew's `Skill` and `InfiniteAmmo` (NpcCrew), and with `AutoRepair` gives a crew with nothing
queued a Repair objective every `RepairInterval`, which its deckhands and engineers work.

Sentries (`WGShipSentryComponent`, `WGShipSentrySystem`) are turrets on a faction hostile to nobody: when the crew turn
on someone (NpcCrew's `WGCrewAttackAnsweredEvent` on the grid) every sentry aboard aggroes on him, and each lets go of
targets that are down or off the ship. A target raised only by a boarding sighting is also let go once no crewman
aboard holds him hostile any more (`WGCrewSecuritySystem.OwnsHostile`), as after a policy edit clears him; an attacker
is kept.

## Player jobs

`WGShipJobsComponent` (added by the vessel prototype) lists the ship's jobs, each with the crew role or counter it
takes over and the command list (officers or staff) its holder stands on. Every job starts at 0 slots in the
vessel's gameMap, and a commanded ship with jobs is on the join list only while one has an open slot, as a
late-join station showing its open jobs (a marked block in `StationJobsSystem.GenerateJobsAvailableEvent`);
`WGRoundSetupSystem.CanSpawnOnStation` lets it take ordinary joins although it is a vessel station. The owner opens
and closes slots on the command console's Jobs tab (admins can also use `wg_shipjobs`, which checks
`CanConfigure`). On `PlayerSpawnCompleteEvent` for one of its jobs, the player gets
`WGShipJobHolderComponent`, goes on the job's list and takes the matching post, one whose NPC is on duty first, so
that NPC is relieved. Cryosleep, a deleted body or `wg_shipjobs release` hands the post back to the roster
(restaffed by its usual rule), takes them off the list the job put them on and reopens the slot, which Frontier's
cryosleep does only for ForceAnchor stations.

## Presets

`WGShipPresetSystem` keeps a commanded ship's settings between rounds in the `wolfgate_ship_command_preset` table
(migration `WolfgateShipCommandPresets`), per player, vessel design and name, as JSON (`WGShipPresetData`): zone
modes, the command lists by player (people added by card alone are this round's and are left out), the admin
powers and crew responses, price rules, restaffing, stood-down posts, the callsign, the open job slots and the recovery pads' access. Each
player lists, saves, loads and deletes only their own, on the command console's Presets tab (admins can also use
`wg_shippreset`, which checks `CanConfigure`); loading re-applies everything and is admin-logged. The admin
Spawn Vessel command raises `WGAdminVesselSpawnedEvent` (Administration) once the vessel is set up: a named
`preset:<name>` loads the spawning admin's preset of that name; without one the owner's `default` applies, else the
admin's. Listed players are matched to whatever card they wear this round, as the zones' keys already follow them.

## Command console

`WGShipConsoleComponent` on the bridge console (`WGComputerShipCommand`, `console.yml`) and `WGShipConsoleSystem`
run the ship the console stands on; the window is `WGShipConsoleWindow`. Its state is built per viewer
(`State(console, viewer)`): a guest sees only that they are not cleared, officers (`CanOperate`) see every tab with
its buttons off, whoever configures the ship (`CanConfigure`) changes it, and the service log is for the owner and
admins. Tabs: Zones (each zone's mode or all at once, the crew's answer per mode and to the barred, `DocksWelcome`);
People (the three lists, everyone aboard with a mind with a button per list, by the card they carry and their
player, and the admins' powers); Prices (the owner's and admins' free service, the rules, a form for a new one,
checked: a name that isn't "owner" or "admins", some services, a multiplier from 0 to 10, the person or company it
needs; only the services that charge through `Quote` are offered, vending, counters and drydock work); Crew
(`AutoRestaff`, `RestaffDelay`, `RestaffsPerHour`, staff one post or every empty one, stand a post down or up); Orders
(the callsign and the crew's standing orders, `WGShipBehaviourSystem`: marines patrol the deck, walk the marine posts
in turn or stand at their posts; gunners are weapons free on ships marked hostile, defend the docks or fire only when
fired on; the pilot evades, runs, holds station or holds and faces the attacker. The roster keeps them, a preset
carries them, and whoever is staffed later gets them); Jobs (open slots per job, up to 10, and a release per holder); Presets (the viewer's own, read from the
database when the window opens: save, load, delete); Recovery pads (each pad's `Access`); Service log (the newest 200,
filtered on the client). Every change goes through the same call the `wg_ship*` commands use, so it is logged the
same way; open windows refresh every five seconds and after any change.

A helm with `WGOfficerHelmComponent` opens only for officers and up of the commanded ship it stands on, for a helm
somewhere public such as the Lupercal's dockmaster's desk.

## Medical staff

The ship's NPC medics are NpcCrew's (`WGCrewParamedic`, `WGCrewSurgeon`) on roster posts (`WGShipCrewPostParamedic`
in the ward, `WGShipCrewPostSurgeon` in Surgery). `WGCrewMayTreatEvent` is answered here: the barred and anyone who
attacked the crew are refused. Each treatment, autodoc run, cloning consent and clone goes in the service log as a
Medical service, free (the v0.8 review). A relieved medic stops treating (his `WGCrewMedicComponent` is removed with his
other duties).

The Lupercal's medical machines (`AdminVessels/medical.yml`): `WGLupercalAutodoc` knows every autodoc program and
comes with the defibrillator and autofix modules, an opiate and an antibiotic bottle and blood; `WGLupercalCloningPod`
never runs out of biomass (`WGBottomlessMaterials`). The cloning set stands on adjacent wall cells, its consoles
device-linked to the scanner and the pod.

## Recovery pad

`WGRecoveryPadComponent` on the pad and `WGRecoveryPadConsoleComponent` on its console (`WGRecoveryPadSystem`; the
window is `WGRecoveryPadWindow`). The console works the nearest pad on its grid and lists everyone with a mind on the
pad's map, the dead first, then the critical, then the living, the worst hurt first within each and then the nearest,
with how they are (alive, critical, dead) and where. A recall charges the pad
(`ChargeTime`, 5 s), then takes the person out of whatever holds them (a container however deep, a buckle, a pull)
and puts them on the pad; then the pad cools down (`Cooldown`, 30 s). One recall at a time. Someone alive and awake is only recalled
when the operator confirms (the window asks for a second press), and they see a countdown before they go. Admins only
by default (`Access`); whoever configures the ship can open it to officers or staff from the console, and the owner
always may use it. On a grid with no command lists only admins count. Every recall goes in the admin log and in the
service log as a free Medical service. The pad's reach is its map, or `Range` when set.

## Fitting out

`WGShipFittingComponent` on the grid runs one pass the tick after the ship's machines build their stock parts (map
init, or when added to a running grid): every machine's parts become the best of their kind (quadratic capacitors,
femto manipulators, bluespace matter bins) and its stats are refreshed, and every ore silo client aboard that isn't
bottomless is linked to the ship's silo through the Lathe module's `SharedOreSiloSystem.TryLinkClient`, with the
silo's range widened to cover the hull. `WGBottomlessMaterialsComponent` on a storage tops each material back up
every five seconds: the admin fabricators use it, everyone else draws from the silo. That stock is for printing only:
ejecting it is refused through Remade's `WGMatterEjectAttemptEvent`, it is emptied before a deconstructed or destroyed
machine drops its contents, it adds nothing to the machine's price, and every print gets `WGBottomlessPrintComponent`,
which sells for nothing. A print merged into a stack already lying there loses the mark, and a recycler still takes a
print apart for its materials. The same pass optionally hardens the ship: `IndestructibleArms` puts godmode on every
ship gun, hardpoint and sentry turret, `UnpryableDoors` clears `CanPry` on every door, `GunChargeScale` and
`GunRechargeScale` multiply each gun's battery and recharge (filled at once), and `CraneSpeed` sets the speed of
berth cranes that take no part upgrades.

## Unrestricted FTL

A grid with `WGFtlUnrestrictedComponent` (a vessel adds it through its prototype's
`addComponents`) jumps with other ships' helms close by, past the server's FTL mass limit and into exclusion zones,
each switchable on the component. The checks are marked one-line guards in `ShuttleConsoleSystem.FTL.cs`,
`ShuttleSystem.FasterThanLight.cs` and `SharedShuttleSystem.cs`, calling the helpers in
`SharedShuttleSystem.ShipCommand.cs`. `WGFtlDriveUnrestricted` is a heavy bluespace core with a range past the
sector, no mass penalty and short timings.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ShipCommand/CargoSystem.ShipCommand.cs`](CargoSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/GasDepositSystem.ShipCommand.cs`](GasDepositSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/PricingSystem.ShipCommand.cs`](PricingSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/ServerDbManager.ShipCommand.cs`](ServerDbManager.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/ShipyardSystem.ShipCommand.cs`](ShipyardSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/ShuttleConsoleSystem.ShipCommand.cs`](ShuttleConsoleSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/VendingMachineSystem.ShipCommand.cs`](VendingMachineSystem.ShipCommand.cs)
- [`Content.Server/_WG/ShipCommand/WGBargainComponent.cs`](WGBargainComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGOfficerHelmComponent.cs`](WGOfficerHelmComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGRecoveryPadComponent.cs`](WGRecoveryPadComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGRecoveryPadSystem.cs`](WGRecoveryPadSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipBehaviourSystem.cs`](WGShipBehaviourSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCommandConsole.cs`](WGShipCommandConsole.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCommandSystem.Crew.cs`](WGShipCommandSystem.Crew.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCommandSystem.cs`](WGShipCommandSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCommandSystem.Wires.cs`](WGShipCommandSystem.Wires.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCommandSystem.Zones.cs`](WGShipCommandSystem.Zones.cs)
- [`Content.Server/_WG/ShipCommand/WGShipConsoleComponent.cs`](WGShipConsoleComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipConsoleSystem.cs`](WGShipConsoleSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipCrewCommand.cs`](WGShipCrewCommand.cs)
- [`Content.Server/_WG/ShipCommand/WGShipFittingComponent.cs`](WGShipFittingComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipFittingSystem.cs`](WGShipFittingSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipJobsCommand.cs`](WGShipJobsCommand.cs)
- [`Content.Server/_WG/ShipCommand/WGShipJobsComponent.cs`](WGShipJobsComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipJobsSystem.cs`](WGShipJobsSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipPresetCommand.cs`](WGShipPresetCommand.cs)
- [`Content.Server/_WG/ShipCommand/WGShipPresetData.cs`](WGShipPresetData.cs)
- [`Content.Server/_WG/ShipCommand/WGShipPresetSystem.cs`](WGShipPresetSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipPriceCommand.cs`](WGShipPriceCommand.cs)
- [`Content.Server/_WG/ShipCommand/WGShipRosterComponent.cs`](WGShipRosterComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipRosterEvents.cs`](WGShipRosterEvents.cs)
- [`Content.Server/_WG/ShipCommand/WGShipRosterSystem.cs`](WGShipRosterSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipSentryComponent.cs`](WGShipSentryComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipSentrySystem.cs`](WGShipSentrySystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipServiceLedgerComponent.cs`](WGShipServiceLedgerComponent.cs)
- [`Content.Server/_WG/ShipCommand/WGShipServiceSystem.cs`](WGShipServiceSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipShieldControlSystem.cs`](WGShipShieldControlSystem.cs)
- [`Content.Server/_WG/ShipCommand/WGShipZoneCommand.cs`](WGShipZoneCommand.cs)

### Shared

- [`Content.Shared/_WG/ShipCommand/SharedShuttleSystem.ShipCommand.cs`](../../../Content.Shared/_WG/ShipCommand/SharedShuttleSystem.ShipCommand.cs)
- [`Content.Shared/_WG/ShipCommand/WGEmagAttemptEvent.cs`](../../../Content.Shared/_WG/ShipCommand/WGEmagAttemptEvent.cs)
- [`Content.Shared/_WG/ShipCommand/WGFtlUnrestrictedComponent.cs`](../../../Content.Shared/_WG/ShipCommand/WGFtlUnrestrictedComponent.cs)
- [`Content.Shared/_WG/ShipCommand/WGRecoveryPadUi.cs`](../../../Content.Shared/_WG/ShipCommand/WGRecoveryPadUi.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipBehaviour.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipBehaviour.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipCommandComponent.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipCommandComponent.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipConsoleUi.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipConsoleUi.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipEmagPredictionSystem.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipEmagPredictionSystem.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipPricing.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipPricing.cs)
- [`Content.Shared/_WG/ShipCommand/WGShipZonesComponent.cs`](../../../Content.Shared/_WG/ShipCommand/WGShipZonesComponent.cs)
- [`Content.Shared/_WG/ShipCommand/WGStackMergeEvent.cs`](../../../Content.Shared/_WG/ShipCommand/WGStackMergeEvent.cs)

### Client

- [`Content.Client/_WG/ShipCommand/WGRecoveryPadBoundUserInterface.cs`](../../../Content.Client/_WG/ShipCommand/WGRecoveryPadBoundUserInterface.cs)
- [`Content.Client/_WG/ShipCommand/WGRecoveryPadWindow.cs`](../../../Content.Client/_WG/ShipCommand/WGRecoveryPadWindow.cs)
- [`Content.Client/_WG/ShipCommand/WGShipCommandClientSystem.cs`](../../../Content.Client/_WG/ShipCommand/WGShipCommandClientSystem.cs)
- [`Content.Client/_WG/ShipCommand/WGShipConsoleBoundUserInterface.cs`](../../../Content.Client/_WG/ShipCommand/WGShipConsoleBoundUserInterface.cs)
- [`Content.Client/_WG/ShipCommand/WGShipConsoleWindow.cs`](../../../Content.Client/_WG/ShipCommand/WGShipConsoleWindow.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGBargainStackTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGBargainStackTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGBargainTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGBargainTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGFtlUnrestrictedTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGFtlUnrestrictedTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGRecoveryPadTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGRecoveryPadTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipBehaviourTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipBehaviourTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipConsoleTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipConsoleTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipCrewTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipCrewTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipFittingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipFittingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipJobsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipJobsTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipPresetTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipPresetTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipRosterTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipRosterTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipServiceTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipServiceTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipZoneTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipCommand/WGShipZoneTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/ShipCommand/console.yml`](../../../Resources/Prototypes/_WG/ShipCommand/console.yml)
- [`Resources/Prototypes/_WG/ShipCommand/ftl.yml`](../../../Resources/Prototypes/_WG/ShipCommand/ftl.yml)
- [`Resources/Prototypes/_WG/ShipCommand/posts.yml`](../../../Resources/Prototypes/_WG/ShipCommand/posts.yml)
- [`Resources/Prototypes/_WG/ShipCommand/recovery_pad.yml`](../../../Resources/Prototypes/_WG/ShipCommand/recovery_pad.yml)

### Localization

- [`Resources/Locale/en-US/_WG/ShipCommand/recovery-pad.ftl`](../../../Resources/Locale/en-US/_WG/ShipCommand/recovery-pad.ftl)
- [`Resources/Locale/en-US/_WG/ShipCommand/ship-command.ftl`](../../../Resources/Locale/en-US/_WG/ShipCommand/ship-command.ftl)
- [`Resources/Locale/en-US/_WG/ShipCommand/ship-console.ftl`](../../../Resources/Locale/en-US/_WG/ShipCommand/ship-console.ftl)

## Non-modular edits

- [`Content.Client/_NF/Shipyard/UI/ShipyardConsoleMenu.xaml.cs`](../../../Content.Client/_NF/Shipyard/UI/ShipyardConsoleMenu.xaml.cs): a commanded ship's price rules
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs)
  - players' ship presets
  - one preset per player, vessel design and name
  - a player's saved settings for a commanded ship of one vessel design
- [`Content.Server/_NF/Atmos/Systems/GasDepositSystem.cs`](../../_NF/Atmos/Systems/GasDepositSystem.cs): a commanded ship's gas sale doesn't buy from the barred
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.Consoles.cs)
  - a commanded ship's shipyards turn the barred away
  - a commanded ship's price rules
  - what this buyer is charged
  - a bargain resells less its discount
  - the price actually paid
  - shows what a commanded ship's rules charge
- [`Content.Server/_NF/Shipyard/Systems/ShipyardSystem.cs`](../../_NF/Shipyard/Systems/ShipyardSystem.cs)
  - appraised before a bargain's discount
  - a bargain resells less its discount
  - the used lot lists a bargain at its full value
- [`Content.Server/Cargo/Systems/CargoSystem.Orders.cs`](../../Cargo/Systems/CargoSystem.Orders.cs): a commanded ship's order consoles turn the barred away
- [`Content.Server/Cargo/Systems/CargoSystem.Shuttle.cs`](../../Cargo/Systems/CargoSystem.Shuttle.cs): a commanded ship's sale pallets don't buy from the barred
- [`Content.Server/Cargo/Systems/PricingSystem.cs`](../../Cargo/Systems/PricingSystem.cs): a bargain resells for no more than was paid
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.cs): a helm kept for officers refuses anyone else.
- [`Content.Server/Shuttles/Systems/ShuttleConsoleSystem.FTL.cs`](../../Shuttles/Systems/ShuttleConsoleSystem.FTL.cs): an unrestricted grid jumps with other ships' helms close by
- [`Content.Server/Shuttles/Systems/ShuttleSystem.FasterThanLight.cs`](../../Shuttles/Systems/ShuttleSystem.FasterThanLight.cs): an unrestricted grid has no mass limit
- [`Content.Server/Station/Systems/StationJobsSystem.cs`](../../Station/Systems/StationJobsSystem.cs): an admin vessel with player jobs is listed, with its open jobs, only while one is open
- [`Content.Server/VendingMachines/VendingMachineSystem.cs`](../../VendingMachines/VendingMachineSystem.cs)
  - a commanded ship's vendors turn the barred away
  - a commanded ship's price rules
  - logged as service use aboard a commanded ship, a bargain held for the item
  - a vend sold below its usual price resells for no more than was paid
- [`Content.Shared.Database/LogType.cs`](../../../Content.Shared.Database/LogType.cs): service use and price rules aboard a commanded ship
- [`Content.Shared/_NF/Shipyard/BUI/ShipyardConsoleInterfaceState.cs`](../../../Content.Shared/_NF/Shipyard/BUI/ShipyardConsoleInterfaceState.cs): what a commanded ship's price rules make of a list price
- [`Content.Shared/Emag/Systems/EmagSystem.cs`](../../../Content.Shared/Emag/Systems/EmagSystem.cs): a ship with staff-only wires refuses an emag from anyone below its staff.
- [`Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs`](../../../Content.Shared/Shuttles/Systems/SharedShuttleSystem.cs): an unrestricted grid jumps into no-jump zones
- [`Content.Shared/Stacks/SharedStackSystem.cs`](../../../Content.Shared/Stacks/SharedStackSystem.cs): a bargain's discount moves with the items it covers

<!-- WOLFGATE-GENERATED END -->
