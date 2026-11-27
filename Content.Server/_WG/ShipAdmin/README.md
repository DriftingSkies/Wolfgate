# ShipAdmin

Shipmaster, the admin ship panel: one window where staff see every ship and change one. It opens from the
Shipmaster button on the Wolfgate admin tab, with `shipmaster [grid|here]`, from the Shipmaster verb (Admin
category) on a grid or anything standing on one, or from the Shipmaster action staff hold while their body is
over a ship. The verb and the action open it on that ship; opened with no ship named, it starts on the ship the
admin is over.

Two big page buttons along the bottom of the window switch between the ships page and the spawn page. The spawn
page replaces the old Spawn Vessel window and its admin tab button (`Spawn/WGShipAdminSpawnPanel`): vessels by
category with a preview, an owner and a name, spawned through `spawnvessel`. `shipmaster spawn` opens the panel
on it, and a ship the admin spawns is selected on the ships page. Spawning needs the Spawn flag as before.

The left side of the ships page lists ships. Ships and ports lists grids with a deed, an owner, ship access or a
vessel design, then the ports and bases they dock at (`IsPort`: trade stations and outposts, planet cargo hubs, station grids that
aren't a ship's own). All grids lists every grid (the first 500). A note under the filter says so, since the Move
tab's lists follow it. The list is sorted on the client, nearest first by default: the ship the viewer is on,
then ships on their map by distance, then the rest; name, owner and most aboard are the other sorts. Each row has
an arrow and a distance to its ship, as does the selected ship's header. They are worked out on the client four
times a second from where it sees the ships (or where the list last had them) and send nothing. A click on an
arrow or distance jumps the admin there: a ghost to the ship's middle, a body to the open floor nearest it. The
right side shows the selected ship in tabs:

- **Overview**: design, position, what it is docked to, every owner (deed cards, vouchers and registered
  accounts, with who is online), access mode and lock, who is aboard. Scan appraises it and counts its damage.
- **Ownership**: rename, transfer to a connected player, add a co-owner by account, remove one owner, release, and set
  the hull faction or the player company it flies for without changing its owner.
- **Access**: lock, mode (Public included, which only staff set), allow list, reset door rules and the ship code.
- **Repair**: a fix of ticked parts, quick fixes that run one part at once (air, fires, trash, puddles, lights),
  and the repair snapshot (re-take, restore). Puddles takes the stains with them: footprints, blood and dirt decals
  that cleaning would take (a mapper's paint stays) and Wolfmed's blood splats on the walls (`RemoveStains`).
- **Move**: bring it to the admin, send it to a position or another grid, dock it at any free dock or one picked
  from the target's docks (those in use are shown and can't be picked), Stop Knotting, return to orbit, undock,
  halt, clear pilots, FTL lock and jump cooldown.
- **Systems**: radar colour and visibility, shields (raise, drop, restore to full, set strength), power,
  protections.
- **Upgrades**: refit tiers, best machine parts, hull modifiers, all free.
- **Danger**: aim an event at the ship, bring everyone aboard to the admin, strip to a bare hull, delete.

Stop Knotting undocks the ship, breaks ropes and harpoons on it, lets go of tractor beams on it or from it, and
sets it down at rest in the nearest clear space (`ShuttleSystem.TryFTLProximity` from where it is). Return to
orbit is for a ship on a planet's ground or air layer: the planet hop takes it and the ships docked to it up to the
orbit layer, as the planet's pull does.

## Events aimed at a ship

The event list is the station events that happen aboard and pick their target through the game rule pickers
(`AboardEvents`), plus every station event that loads ships. An aboard event gets `WGEventTargetComponent` before
it starts; `GameRuleSystem.WGEventTarget.cs` marks the rule whose start is running, and `TryGetRandomStation(s)`
and `TryFindRandomTile` then return that station and a tile on that grid, or nothing: an aimed event never falls
back to another station. It needs the ship to have a station, which bought ships do; the aim is that station, which
for a ship is the ship alone, and a grid whose station has other grids is refused, since vents, critters and
sentience pick across the whole station. Most station events wait out a warning delay, so the panel says the event is queued,
and if it then finds nothing aboard the pickers raise `WGEventTargetMissedEvent` and the admin is told. An arrival
event's ships load when it is added and are moved beside the target before it starts.

## Gunnery after a rebuild

A gun only joins gunnery when it is linked to the hardpoint under it, and upstream only a gun looks for its
hardpoint. A hardpoint rebuilt or wrenched down under a gun that was already there now takes it
(`SharedHardpointSystem.WGRelink.cs`), and a gun whose hardpoint died drops the dead link. The fix's Consoles part
and a restore mount every unlinked gun aboard, then relink gunnery a tick later (`FireControlSystem.WgRelinkGrid`,
which does what each gunnery console's Refresh button does) instead of waiting up to a minute for the hardpoint
refresh. A ship whose gunnery server isn't up yet is tried again each second for half a minute.

The Admin flag opens the panel and runs every action; the ones that are hard to undo ask for a second press.
`WGShipAdminPermissions` is the one table: the server checks it on every message and the window greys what the
viewer may not do. Every action is written to the admin log as `WGShipAdmin`. Ownership and access actions carry
a "tell the owners" tick, on by default, that sends each connected owner a chat line.

Every dropdown is a `WGShipAdminDropdown`: the engine's option button with its search box on, and a click on it
while it is open closes it instead of opening it again. Text that could run off the panel is a `WGShipAdminText`,
which wraps.

## Ownership

One ship's owner is recorded in many places: the deed on an ID card or voucher and its copy on the grid,
`ShipOwnershipComponent`, the ship access owner list and buyer key, the shuttle record, the card's owner stamp and
a crew record on the ship's station. `Transfer` moves them together. It takes the deed off every card that holds
it and unregisters every account, then gives the new owner the deed on their ID card when their living body
carries one free of deeds, and registers their account either way; a ghost, a player with no card or one whose
card already holds a deed gets the account alone. The lock, mode, allow list and door rules stay unless an option
changes them. A locked ship whose new owner has no keyed card yet is unlocked and locks itself again once it
can. A ship nobody ever owned goes through `ShipyardSystem.TryAssignDeed` instead, as a purchase would. Either
way the hull's faction follows the option: kept, taken from the ID the new owner carries (deed on it or not), or
cleared. `Release` leaves the doors unlocked and locks the helm, so only staff fly the ship until it is transferred.
Taking a deed away also stops the voucher the ship was bought with from working its helm; the ship stays a voucher
ship for a later sale.

Stations and a planet's own surface can't be given an owner or moved (`CanBeOwned`); a ship's own late-join
station doesn't count. Access actions need a grid that already has a deed or ship access, since setting access
up rewrites every door reader aboard.

## Repair

A fix never deletes what players moved or built. It uses drydock's own list (`WGDrydockSystem.BuildFreeRepairItems`):
hull tiles and parts come back only where the repair snapshot says something is missing, and damaged parts are
healed. A hull tile counts as missing when it is gone or down to one of its base turfs (pried up or blown to
plating or lattice); a different floor laid in its place stays, also when it is laid while the fix is still
queued. The list is worked through a budget of items a tick (`FixItemsPerTick`), then air, fires, power, fuel,
door bolts and consoles are done in one pass. Restore to snapshot is Mono's `repairgrid`: it also deletes and
rebuilds every part that moved. Free refits run through the same queue and record the tier on the ship when done.

## Admins at any ship's console

`WGShipAdminManagerSystem` answers `WGShipManagerEvent` for an active admin, on both sides, so staff use the
console's own Access tab (door map, per-door rules, codes) and lock verbs on any ship. It leaves grids with
`WGShipCommandComponent` to that module's own switch and ignores grids that aren't ships. The helm lock upstream
only opens to a deed card, so `ShuttleConsoleLockSystem.WGShipAdmin.cs` lets anyone who manages the ship toggle
it: an admin, or an owner by account who holds no deed card. The console's mode selector won't bind a ship to
the crew or company of someone who manages it without owning it.

Entry points: `WGShipAdminSystem` (`Open`, `BuildList`, `BuildDetail`, and one public method per action),
`WGShipAdminEui`, `WGShipAdminCommand`, `WGShipAdminWindow`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/ShipAdmin/FireControlSystem.WGRelink.cs`](FireControlSystem.WGRelink.cs)
- [`Content.Server/_WG/ShipAdmin/GameRuleSystem.WGEventTarget.cs`](GameRuleSystem.WGEventTarget.cs)
- [`Content.Server/_WG/ShipAdmin/ShipShieldsSystem.WGShipAdmin.cs`](ShipShieldsSystem.WGShipAdmin.cs)
- [`Content.Server/_WG/ShipAdmin/ShuttleConsoleLockSystem.WGShipAdmin.cs`](ShuttleConsoleLockSystem.WGShipAdmin.cs)
- [`Content.Server/_WG/ShipAdmin/WGEventTargetComponent.cs`](WGEventTargetComponent.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminCommand.cs`](WGShipAdminCommand.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminEui.cs`](WGShipAdminEui.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Access.cs`](WGShipAdminSystem.Access.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Action.cs`](WGShipAdminSystem.Action.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Affiliation.cs`](WGShipAdminSystem.Affiliation.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Clean.cs`](WGShipAdminSystem.Clean.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.cs`](WGShipAdminSystem.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Danger.cs`](WGShipAdminSystem.Danger.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Events.cs`](WGShipAdminSystem.Events.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Jump.cs`](WGShipAdminSystem.Jump.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Move.cs`](WGShipAdminSystem.Move.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Ownership.cs`](WGShipAdminSystem.Ownership.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Query.cs`](WGShipAdminSystem.Query.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Repair.cs`](WGShipAdminSystem.Repair.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Rescue.cs`](WGShipAdminSystem.Rescue.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipAdminSystem.Systems.cs`](WGShipAdminSystem.Systems.cs)
- [`Content.Server/_WG/ShipAdmin/WGShipRefitCommand.cs`](WGShipRefitCommand.cs)

### Shared

- [`Content.Shared/_WG/ShipAdmin/SharedHardpointSystem.WGRelink.cs`](../../../Content.Shared/_WG/ShipAdmin/SharedHardpointSystem.WGRelink.cs)
- [`Content.Shared/_WG/ShipAdmin/WGShipAdminEuiTypes.cs`](../../../Content.Shared/_WG/ShipAdmin/WGShipAdminEuiTypes.cs)
- [`Content.Shared/_WG/ShipAdmin/WGShipAdminManagerSystem.cs`](../../../Content.Shared/_WG/ShipAdmin/WGShipAdminManagerSystem.cs)
- [`Content.Shared/_WG/ShipAdmin/WGShipAdminPermissions.cs`](../../../Content.Shared/_WG/ShipAdmin/WGShipAdminPermissions.cs)

### Client

- [`Content.Client/_WG/ShipAdmin/ConfirmButton.WGShipAdmin.cs`](../../../Content.Client/_WG/ShipAdmin/ConfirmButton.WGShipAdmin.cs)
- [`Content.Client/_WG/ShipAdmin/Spawn/WGShipAdminSpawnPanel.xaml`](../../../Content.Client/_WG/ShipAdmin/Spawn/WGShipAdminSpawnPanel.xaml)
- [`Content.Client/_WG/ShipAdmin/Spawn/WGShipAdminSpawnPanel.xaml.cs`](../../../Content.Client/_WG/ShipAdmin/Spawn/WGShipAdminSpawnPanel.xaml.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminAccessTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminAccessTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminDangerTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminDangerTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminMoveTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminMoveTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminOverviewTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminOverviewTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminOwnershipTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminOwnershipTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminRepairTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminRepairTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminSystemsTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminSystemsTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminTab.cs)
- [`Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminUpgradesTab.cs`](../../../Content.Client/_WG/ShipAdmin/Tabs/WGShipAdminUpgradesTab.cs)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminCompass.cs`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminCompass.cs)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminDropdown.cs`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminDropdown.cs)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminEui.cs`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminEui.cs)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminText.cs`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminText.cs)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminWindow.xaml`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminWindow.xaml)
- [`Content.Client/_WG/ShipAdmin/WGShipAdminWindow.xaml.cs`](../../../Content.Client/_WG/ShipAdmin/WGShipAdminWindow.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminDangerTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminDangerTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminMoveTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminMoveTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminOwnershipTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminOwnershipTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminPanelTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminPanelTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminRepairTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminRepairTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminRescueTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminRescueTest.cs)
- [`Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminSystemsTest.cs`](../../../Content.IntegrationTests/Tests/_WG/ShipAdmin/ShipAdminSystemsTest.cs)

### Unit tests

- [`Content.Tests/_WG/ShipAdmin/ShipAdminPermissionsTest.cs`](../../../Content.Tests/_WG/ShipAdmin/ShipAdminPermissionsTest.cs)
- [`Content.Tests/_WG/ShipAdmin/ShipAdminSortTest.cs`](../../../Content.Tests/_WG/ShipAdmin/ShipAdminSortTest.cs)

### Prototypes

- [`Resources/Prototypes/_WG/ShipAdmin/actions.yml`](../../../Resources/Prototypes/_WG/ShipAdmin/actions.yml)

### Localization

- [`Resources/Locale/en-US/_WG/ShipAdmin/ship-admin.ftl`](../../../Resources/Locale/en-US/_WG/ShipAdmin/ship-admin.ftl)

## Non-modular edits

- [`Content.Server/GameTicking/Rules/GameRuleSystem.cs`](../../GameTicking/Rules/GameRuleSystem.cs): mark the starting rule so its target pickers honour where staff aimed it
- [`Content.Server/GameTicking/Rules/GameRuleSystem.Utility.cs`](../../GameTicking/Rules/GameRuleSystem.Utility.cs)
  - an event staff aimed at one station goes there or nowhere
  - an event staff aimed at one ship lands on that ship
- [`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`](../../Shuttles/Systems/ShuttleConsoleLockSystem.cs)
  - account owners and admins get the lock verbs with empty hands
  - a manager with no deed card toggles the helm lock, and is not made a guest
- [`Content.Shared.Database/LogType.cs`](../../../Content.Shared.Database/LogType.cs): admin ship panel actions
- [`Content.Shared/_Crescent/Hardpoints/SharedHardpointSystem.cs`](../../../Content.Shared/_Crescent/Hardpoints/SharedHardpointSystem.cs)
  - partial for the gun relink
  - a rebuilt hardpoint takes the gun above it
  - drop the dead link, so a rebuilt hardpoint can take the gun again
  - a hardpoint wrenched down under a gun that is already in place takes it

<!-- WOLFGATE-GENERATED END -->
