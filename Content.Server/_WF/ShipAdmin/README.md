# ShipAdmin

Shipmaster, the admin ship panel: one window where staff see every ship and change one. It opens from the
Shipmaster button on the Wolfgate admin tab, with `shipmaster [grid|here]`, from the Shipmaster verb (Admin
category) on a grid or anything standing on one, or from the Shipmaster action staff hold while their body is
over a ship. The verb and the action open it on that ship; opened with no ship named, it starts on the ship the
admin is over.

The left side lists ships: grids with a deed, an owner, ship access or a vessel design, or every grid with the
All grids filter (the first 500). It is sorted on the client, nearest first by default: the ship the viewer is on,
then ships on their map by distance, then the rest; name, owner and most aboard are the other sorts. Each row has
an arrow and a distance to its ship, as does the selected ship's header. They are worked out on the client four
times a second from where it sees the ships (or where the list last had them) and send nothing. The right side
shows the selected ship in tabs:

- **Overview**: design, position, what it is docked to, every owner (deed cards, vouchers and registered
  accounts, with who is online), access mode and lock, who is aboard. Scan appraises it and counts its damage.
- **Ownership**: transfer to a connected player, add a co-owner by account, remove one owner, release.
- **Access**: lock, mode (Public included, which only staff set), allow list, reset door rules and the ship code.
- **Repair**: a fix of ticked parts, and the repair snapshot (re-take, restore).
- **Move**: bring it to the admin, send it to a position or another grid, dock it, undock, halt, clear pilots,
  FTL lock and jump cooldown.
- **Systems**: rename, radar colour and visibility, shields (raise, drop, restore to full, set strength), power,
  protections.
- **Upgrades**: refit tiers, best machine parts, hull modifiers, all free.
- **Danger**: bring everyone aboard to the admin, strip to a bare hull, delete.

The Admin flag opens the panel and runs every action; the ones that are hard to undo ask for a second press.
`WFShipAdminPermissions` is the one table: the server checks it on every message and the window greys what the
viewer may not do. Every action is written to the admin log as `WFShipAdmin`. Ownership and access actions carry
a "tell the owners" tick, on by default, that sends each connected owner a chat line.

Every dropdown is a `WFShipAdminDropdown`: the engine's option button with its search box on, and a click on it
while it is open closes it instead of opening it again. Text that could run off the panel is a `WFShipAdminText`,
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

A fix never deletes what players moved or built. It uses drydock's own list (`WFDrydockSystem.BuildFreeRepairItems`):
hull tiles and parts come back only where the repair snapshot says something is missing, and damaged parts are
healed. A hull tile counts as missing when it is gone or down to one of its base turfs (pried up or blown to
plating or lattice); a different floor laid in its place stays, also when it is laid while the fix is still
queued. The list is worked through a budget of items a tick (`FixItemsPerTick`), then air, fires, power, fuel,
door bolts and consoles are done in one pass. Restore to snapshot is Mono's `repairgrid`: it also deletes and
rebuilds every part that moved. Free refits run through the same queue and record the tier on the ship when done.

## Admins at any ship's console

`WFShipAdminManagerSystem` answers `WFShipManagerEvent` for an active admin, on both sides, so staff use the
console's own Access tab (door map, per-door rules, codes) and lock verbs on any ship. It leaves grids with
`WFShipCommandComponent` to that module's own switch and ignores grids that aren't ships. The helm lock upstream
only opens to a deed card, so `ShuttleConsoleLockSystem.WFShipAdmin.cs` lets anyone who manages the ship toggle
it: an admin, or an owner by account who holds no deed card. The console's mode selector won't bind a ship to
the crew or company of someone who manages it without owning it.

Entry points: `WFShipAdminSystem` (`Open`, `BuildList`, `BuildDetail`, and one public method per action),
`WFShipAdminEui`, `WFShipAdminCommand`, `WFShipAdminWindow`.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/ShipAdmin/ShipShieldsSystem.WFShipAdmin.cs`](ShipShieldsSystem.WFShipAdmin.cs)
- [`Content.Server/_WF/ShipAdmin/ShuttleConsoleLockSystem.WFShipAdmin.cs`](ShuttleConsoleLockSystem.WFShipAdmin.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminCommand.cs`](WFShipAdminCommand.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminEui.cs`](WFShipAdminEui.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Access.cs`](WFShipAdminSystem.Access.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Action.cs`](WFShipAdminSystem.Action.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.cs`](WFShipAdminSystem.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Danger.cs`](WFShipAdminSystem.Danger.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Move.cs`](WFShipAdminSystem.Move.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Ownership.cs`](WFShipAdminSystem.Ownership.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Query.cs`](WFShipAdminSystem.Query.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Repair.cs`](WFShipAdminSystem.Repair.cs)
- [`Content.Server/_WF/ShipAdmin/WFShipAdminSystem.Systems.cs`](WFShipAdminSystem.Systems.cs)

### Shared

- [`Content.Shared/_WF/ShipAdmin/WFShipAdminEuiTypes.cs`](../../../Content.Shared/_WF/ShipAdmin/WFShipAdminEuiTypes.cs)
- [`Content.Shared/_WF/ShipAdmin/WFShipAdminManagerSystem.cs`](../../../Content.Shared/_WF/ShipAdmin/WFShipAdminManagerSystem.cs)
- [`Content.Shared/_WF/ShipAdmin/WFShipAdminPermissions.cs`](../../../Content.Shared/_WF/ShipAdmin/WFShipAdminPermissions.cs)

### Client

- [`Content.Client/_WF/ShipAdmin/ConfirmButton.WFShipAdmin.cs`](../../../Content.Client/_WF/ShipAdmin/ConfirmButton.WFShipAdmin.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminAccessTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminAccessTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminDangerTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminDangerTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminMoveTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminMoveTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminOverviewTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminOverviewTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminOwnershipTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminOwnershipTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminRepairTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminRepairTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminSystemsTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminSystemsTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminTab.cs)
- [`Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminUpgradesTab.cs`](../../../Content.Client/_WF/ShipAdmin/Tabs/WFShipAdminUpgradesTab.cs)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminCompass.cs`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminCompass.cs)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminDropdown.cs`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminDropdown.cs)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminEui.cs`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminEui.cs)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminText.cs`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminText.cs)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminWindow.xaml`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminWindow.xaml)
- [`Content.Client/_WF/ShipAdmin/WFShipAdminWindow.xaml.cs`](../../../Content.Client/_WF/ShipAdmin/WFShipAdminWindow.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminAccessTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminAccessTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminDangerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminDangerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminMoveTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminMoveTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminOwnershipTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminOwnershipTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminPanelTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminPanelTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminRepairTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminRepairTest.cs)
- [`Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminSystemsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/ShipAdmin/ShipAdminSystemsTest.cs)

### Unit tests

- [`Content.Tests/_WF/ShipAdmin/ShipAdminPermissionsTest.cs`](../../../Content.Tests/_WF/ShipAdmin/ShipAdminPermissionsTest.cs)
- [`Content.Tests/_WF/ShipAdmin/ShipAdminSortTest.cs`](../../../Content.Tests/_WF/ShipAdmin/ShipAdminSortTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/ShipAdmin/actions.yml`](../../../Resources/Prototypes/_WF/ShipAdmin/actions.yml)

### Localization

- [`Resources/Locale/en-US/_WF/ShipAdmin/ship-admin.ftl`](../../../Resources/Locale/en-US/_WF/ShipAdmin/ship-admin.ftl)

## Non-modular edits

- [`Content.Server/Shuttles/Systems/ShuttleConsoleLockSystem.cs`](../../Shuttles/Systems/ShuttleConsoleLockSystem.cs)
  - account owners and admins get the lock verbs with empty hands
  - a manager with no deed card toggles the helm lock, and is not made a guest
- [`Content.Shared.Database/LogType.cs`](../../../Content.Shared.Database/LogType.cs): admin ship panel actions

<!-- WOLFGATE-GENERATED END -->
