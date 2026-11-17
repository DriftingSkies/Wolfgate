# SectorControl

Faction territory on the sector map. The sector is a grid of flat-top hexes (`wg.sector.cell_size`, 3000 m) around
the origin; a `wgSectorFaction` holds cells, and the shuttle console's MAP tab, the nav radar and the sector map
window show them in the faction's colour. The module only keeps and shows territory: who claims what, and why, is up to a driver such as the
Reaver campaign. Every cell has a callsign (`WGSectorHex.Callsign`, such as `N-13` for the origin) for
announcements and the map. The sector is the cells whose centre lies within `WGSectorHex.SectorRadius` (30 km)
of the origin, the grid the map outlines; a cell past it can't be claimed or contested.

Players see held cells, borders and contested cells with their countdown on the map, and a legend line per faction
that the driver writes. A crewed player ship that has been inside a faction's space for twenty seconds is told "WARNING:
entering ... controlled space" (at most once every ten minutes), and one back outside for a minute that it has
left, in chat to everyone aboard and over its PA when it has a speaker; a faction may set its own lines
(`enteredNotice`, `leftNotice`). Flying a
cell is what keeps it: every minute each crewed player ship that moved 100 m counts as activity in its cell (parked,
docked and idle ships count for nothing; stations never do), and a contest is only claimed if the cell stayed quiet.
A cell with a faction ship and a crewed player ship in it is disputed (`SetPresence`, `IsDisputed`,
`WGSectorCellDisputedEvent`): the map hatches it and its contest deadline holds until one side leaves or dies.
A held cell that a crewed player ship has sat in for three minutes (`wg.sector.retake_minutes`, 0 turns it off) with none
of the holder's ships there is retaken: the map pulses it in a neutral colour with the countdown, and at zero it is freed
(`WGSectorRetakeAttemptEvent` lets a driver veto a cell, `WGSectorCellRetakenEvent` tells it and names the crewed player
ships that held the cell through the whole countdown). The holder's ships reappearing, the player leaving or a contest on the cell reset the countdown.
A cell that was held and is freed (a retake, the last source gone, `Free`, `Release` or `Clear`) is clearing for
`wg.sector.clearing_minutes` (10, 0 turns it off): `IsHeldOrClearing` keeps the drone hook out of it as for a held cell,
`WGSectorCellClearingEvent` (with the faction that held it) fires when it starts and `WGSectorCellClearedEvent` when it
ends, and the map outlines it dim with "Clearing m:ss" and a hover line saying when drones return. Claiming the cell
again ends the state with no cleared event. Wiping territory (a switch or a resize) starts no clearing.
A station in a cell held by a faction with `blockades` drops out of the storyteller's station list, so NPC freight and
traders stop calling there.
A faction can leave some parties alone (`neutralCompanies`, `neutralStations`): `WGSectorActivitySystem.IsNeutralTo`
is true for a grid of a neutral company, docked to or being a neutral station, or crewed only by people of a neutral
company. Such ships are left out of the faction's disputes and retakes and get no entering or leaving notice, and a
neutral station is not cut off by a blockade. `CrewedShips(map, faction)` lists the crewed ships without them.

The sector map window shows the sector and nothing a shuttle console's MAP scan would not: the whole hex grid out to
30 km with every cell's callsign, held and contested cells, the grids the MAP tab names (none hidden by IFF, and no
label-hidden ones, since the map has no console to detect them) as blips in their IFF colour (a ringed dot for a station,
a diamond for a ship), one glyph per encounter and the player's own ship, from 1.5 km to 60 km either side of centre.
A blip's name shows only while the cursor is on it, at any zoom. The server sends only territory;
the client names the grids it knows. The wheel zooms about the cursor, right-drag pans, a double-click centres on your
ship, and resting the cursor on a blip shows its card (name, kind, range and bearing from you, its cell and who holds
it), or on empty space the cell, its distance from the centre and who holds it. Everyone, ghosts included, opens it from the top-bar button beside the
guidebook or its key (Shift+N, rebindable), "Sector map" in the game menu or the `wg_sectormap` command (`WGSectorMapControl`,
`WGSectorMapWindow`, `WGSectorMapUIController` in `Content.Client/_WG/SectorControl`).

A faction can challenge a cell another holds (`Challenge`): at the deadline it takes the cell, with no clearing, if
its ships are there and the holder's are not, and the challenge is dropped otherwise or when its source dies; the map
reads "held by X, challenged by Y, m:ss".

Drivers use `WGSectorTerritorySystem` (`CellOf`, `Centre`, `Owner`, `TryClaim`, `Contest`, `Challenge`, `SetContestDeadline`, `DropContest`, `Release`, `Free`,
`Held`, `Frontier`, `Sources`, `StationsIn`, `CanClaim`), `WGSectorActivitySystem` (`LastActivity`, `IsQuiet`,
`ActiveShips`) and `WGSectorSyncSystem.SetStatus` for their legend line, and listen for the events in
`WGSectorEvents.cs`. A claim lasts while any of its sources exists. `WGSectorProtectionSystem` vetoes claims on the
origin cell and its ring and on cells holding a station named in `wg.sector.protected_stations` (the Halcyon), for
every driver. The hex maths is in `Content.Shared/_WG/SectorControl/WGSectorHex.cs` so the client draws with it.

Admins use `wg_sector status | claim <faction> <q> <r> | free <q> <r> | contest <faction> <q> <r> <minutes> | challenge
<faction> <q> <r> <minutes> | clear [faction] | callsign <x> <y>`. `wg.sector.enabled` turns it all off and clears the territory. State lives on
`WGSectorTerritoryComponent` on the map entity, where VV shows it, and goes with the round.

`Docs/_WG/SectorControl/design.md` is the design.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WG/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WG/SectorControl/Commands/WGSectorCommand.cs`](Commands/WGSectorCommand.cs)
- [`Content.Server/_WG/SectorControl/Components/WGSectorTerritoryComponent.cs`](Components/WGSectorTerritoryComponent.cs)
- [`Content.Server/_WG/SectorControl/Systems/WGSectorActivitySystem.cs`](Systems/WGSectorActivitySystem.cs)
- [`Content.Server/_WG/SectorControl/Systems/WGSectorNoticeSystem.cs`](Systems/WGSectorNoticeSystem.cs)
- [`Content.Server/_WG/SectorControl/Systems/WGSectorProtectionSystem.cs`](Systems/WGSectorProtectionSystem.cs)
- [`Content.Server/_WG/SectorControl/Systems/WGSectorSyncSystem.cs`](Systems/WGSectorSyncSystem.cs)
- [`Content.Server/_WG/SectorControl/Systems/WGSectorTerritorySystem.cs`](Systems/WGSectorTerritorySystem.cs)
- [`Content.Server/_WG/SectorControl/WGSectorEvents.cs`](WGSectorEvents.cs)

### Shared

- [`Content.Shared/_WG/SectorControl/SectorControlCVars.cs`](../../../Content.Shared/_WG/SectorControl/SectorControlCVars.cs)
- [`Content.Shared/_WG/SectorControl/WGSectorCell.cs`](../../../Content.Shared/_WG/SectorControl/WGSectorCell.cs)
- [`Content.Shared/_WG/SectorControl/WGSectorFactionPrototype.cs`](../../../Content.Shared/_WG/SectorControl/WGSectorFactionPrototype.cs)
- [`Content.Shared/_WG/SectorControl/WGSectorHex.cs`](../../../Content.Shared/_WG/SectorControl/WGSectorHex.cs)
- [`Content.Shared/_WG/SectorControl/WGSectorNetEvents.cs`](../../../Content.Shared/_WG/SectorControl/WGSectorNetEvents.cs)

### Client

- [`Content.Client/_WG/SectorControl/ShuttleMapControl.SectorControl.cs`](../../../Content.Client/_WG/SectorControl/ShuttleMapControl.SectorControl.cs)
- [`Content.Client/_WG/SectorControl/ShuttleNavControl.SectorControl.cs`](../../../Content.Client/_WG/SectorControl/ShuttleNavControl.SectorControl.cs)
- [`Content.Client/_WG/SectorControl/WGSectorClientSystem.cs`](../../../Content.Client/_WG/SectorControl/WGSectorClientSystem.cs)
- [`Content.Client/_WG/SectorControl/WGSectorMapCommand.cs`](../../../Content.Client/_WG/SectorControl/WGSectorMapCommand.cs)
- [`Content.Client/_WG/SectorControl/WGSectorMapControl.cs`](../../../Content.Client/_WG/SectorControl/WGSectorMapControl.cs)
- [`Content.Client/_WG/SectorControl/WGSectorMapPanel.cs`](../../../Content.Client/_WG/SectorControl/WGSectorMapPanel.cs)
- [`Content.Client/_WG/SectorControl/WGSectorMapUIController.cs`](../../../Content.Client/_WG/SectorControl/WGSectorMapUIController.cs)
- [`Content.Client/_WG/SectorControl/WGSectorMapWindow.cs`](../../../Content.Client/_WG/SectorControl/WGSectorMapWindow.cs)
- [`Content.Client/_WG/SectorControl/WGSectorOverlay.cs`](../../../Content.Client/_WG/SectorControl/WGSectorOverlay.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorActivityTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorActivityTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorChallengeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorChallengeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorClearingTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorClearingTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorControlTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorControlTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorDisputeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorDisputeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorEdgeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorEdgeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorHiddenStationTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorHiddenStationTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorMapTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorMapTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorNoticeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorNoticeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorRetakeTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorRetakeTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorSyncTest.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorSyncTest.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorTestBase.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorTestBase.cs)
- [`Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorTestSystem.cs`](../../../Content.IntegrationTests/Tests/_WG/SectorControl/WGSectorTestSystem.cs)

### Unit tests

- [`Content.Tests/_WG/SectorControl/WGSectorHexTest.cs`](../../../Content.Tests/_WG/SectorControl/WGSectorHexTest.cs)

### Localization

- [`Resources/Locale/en-US/_WG/SectorControl/sectorcontrol.ftl`](../../../Resources/Locale/en-US/_WG/SectorControl/sectorcontrol.ftl)

### Textures

- [`Resources/Textures/_WG/SectorControl/menu.rsi/`](../../../Resources/Textures/_WG/SectorControl/menu.rsi/)

### Docs

- [`Docs/_WG/SectorControl/design.md`](../../../Docs/_WG/SectorControl/design.md)

## Non-modular edits

- [`Content.Client/Input/ContentContexts.cs`](../../../Content.Client/Input/ContentContexts.cs): sector map
- [`Content.Client/Options/UI/EscapeMenu.xaml`](../../../Content.Client/Options/UI/EscapeMenu.xaml): sector map entry under the roadmap
- [`Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs`](../../../Content.Client/Options/UI/Tabs/KeyRebindTab.xaml.cs): sector map
- [`Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleMapControl.xaml.cs): faction territory fills, borders and legend on the sector map.
- [`Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs`](../../../Content.Client/Shuttles/UI/ShuttleNavControl.xaml.cs): faction territory borders beneath radar contacts.
- [`Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs`](../../../Content.Client/UserInterface/Systems/EscapeMenu/EscapeUIController.cs): sector map entry in the game menu
- [`Content.Client/UserInterface/Systems/MenuBar/GameTopMenuBarUIController.cs`](../../../Content.Client/UserInterface/Systems/MenuBar/GameTopMenuBarUIController.cs): sector map button
- [`Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml`](../../../Content.Client/UserInterface/Systems/MenuBar/Widgets/GameTopMenuBar.xaml): sector map button beside the guidebook; its icon is set in code from menu.rsi
- [`Content.Shared/Input/ContentKeyFunctions.cs`](../../../Content.Shared/Input/ContentKeyFunctions.cs): sector map
- [`Resources/clientCommandPerms.yml`](../../../Resources/clientCommandPerms.yml): client-only command that opens the sector map window
- [`Resources/keybinds.yml`](../../../Resources/keybinds.yml): sector map key

<!-- WOLFGATE-GENERATED END -->
