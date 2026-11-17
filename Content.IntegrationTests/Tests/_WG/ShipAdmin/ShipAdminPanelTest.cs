#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipAdmin;
using Content.Client.UserInterface.Controls;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Administration;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// The admin ship panel lists a deeded ship with its owner, shows its detail, runs a scan and drops it and any
/// pending confirmation when another ship is selected, keeps one panel per admin and closes when the admin flag goes.
/// Staff over a ship hold the Shipmaster action, and a panel opened with no ship named starts on that ship.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminPanelTest
{
    private const string VesselProto = "Guppy";
    private const string IdProto = "PassengerIDCard";
    private const string HumanProto = "MobHuman";

    [Test]
    public async Task PanelListsShowsAndCloses()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var shipyard = entMan.System<ShipyardSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid grid = default, card = default, host = default;
        var wasAdmin = false;
        WGShipAdminEuiState state = default!;
        WGShipAdminDetail other = default!;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            // Deeds write a sector shuttle record; the host component gives it somewhere to go.
            host = entMan.Spawn();
            entMan.EnsureComponent<StationSectorServiceHostComponent>(host);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned), Is.True);
            grid = spawned!.Value;
            card = entMan.SpawnEntity(IdProto, map.GridCoords);
            Assert.That(shipyard.TryAssignDeed(grid, card, session, vessel), Is.True);

            var rows = ships.BuildList(WGShipAdminFilter.PlayerShips, out var truncated);
            var row = rows.SingleOrDefault(r => r.Grid == entMan.GetNetEntity(grid));
            Assert.That(row, Is.Not.Null, "A deeded ship should be listed as a player ship.");
            Assert.Multiple(() =>
            {
                Assert.That(truncated, Is.False);
                Assert.That(row!.Owner, Is.Not.Empty);
                Assert.That(row.OwnerState, Is.EqualTo(WGShipAdminOwnerState.Online));
                Assert.That(row.Design, Is.Not.Empty);
                Assert.That(row.Flags.HasFlag(WGShipAdminRowFlags.Snapshot), Is.True);
            });

            var all = ships.BuildList(WGShipAdminFilter.AllGrids, out _);
            Assert.That(all, Has.Count.GreaterThan(rows.Count), "All grids should add the test map's own grid.");

            ships.Open(session, grid);
            ships.Open(session, null);
            Assert.That(ships.Panels, Has.Count.EqualTo(1), "A second open should reuse the admin's panel.");

            var panel = ships.Panels.Single();
            Assert.That(panel.Selected, Is.EqualTo(grid));
            Assert.That(panel.TryTarget(NetEntity.Invalid, WGShipAdminAction.Scan, out _), Is.False, "A missing ship is refused.");

            panel.HandleMessage(new WGShipAdminActionMessage(entMan.GetNetEntity(grid), WGShipAdminAction.Scan));
            state = (WGShipAdminEuiState) panel.GetNewState();
            Assert.That(state.Selected, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(state.Selected!.Value, Is.Not.Null, "A scan fills the value.");
                Assert.That(state.Selected.Owners.Any(o => o.Kind == WGShipAdminOwnerKind.DeedCard), Is.True);
                Assert.That(state.Selected.Registered, Is.Not.Empty);
                Assert.That(state.ViewerFlags.HasFlag(AdminFlags.Admin), Is.True);
            });

            panel.Select(map.Grid.Owner);
            panel.Select(grid);
            Assert.That(((WGShipAdminEuiState) panel.GetNewState()).Selected?.Value, Is.Null, "A scan is dropped once another ship is selected.");

            state.Ships = rows;
            other = ships.BuildDetail(map.Grid.Owner)!;
        });

        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            var window = new WGShipAdminWindow();
            window.OpenCentered();
            window.UpdateState(state);
            Assert.Multiple(() =>
            {
                Assert.That(window.ShipList.ChildCount, Is.EqualTo(state.Ships!.Count));
                Assert.That(window.ShipNameLabel.Text, Is.EqualTo(state.Selected!.Name));
                Assert.That(window.Overview.LineCount, Is.GreaterThan(5));
                Assert.That(window.ResultLabel.Text, Is.EqualTo(state.Result));
            });

            // A confirmation asked for one ship is kept while it stays selected, and dropped when another is.
            var confirm = Confirms(window.Danger).First();
            confirm.IsConfirming = true;
            window.UpdateState(state);
            Assert.That(confirm.IsConfirming, Is.True);
            state.Selected = other;
            window.UpdateState(state);
            Assert.That(confirm.IsConfirming, Is.False, "A second press must not act on the newly selected ship.");
            window.Close();
        });

        await server.WaitPost(() => admins.DeAdmin(session));
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(ships.Panels, Is.Empty, "Losing the admin flag should close the panel.");

            if (wasAdmin)
                admins.ReAdmin(session);

            entMan.DeleteEntity(card);
            entMan.DeleteEntity(grid);
            entMan.DeleteEntity(host);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StaffOverAShipGetTheActionAndStartOnIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WGShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var xforms = entMan.System<SharedTransformSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid grid = default, body = default;
        var wasAdmin = false;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned), Is.True);
            grid = spawned!.Value;
            var floor = entMan.System<SharedMapSystem>().GetAllTiles(grid, entMan.GetComponent<MapGridComponent>(grid)).First().GridIndices;
            body = entMan.SpawnEntity(HumanProto, new EntityCoordinates(grid, new Vector2(floor.X + 0.5f, floor.Y + 0.5f)));
            server.PlayerMan.SetAttachedEntity(session, body);
            Assert.That(ships.ShipUnder(session), Is.EqualTo(grid));
            Assert.That(ships.HasOpenAction(session), Is.False, "Precondition: the action is given on the next check.");
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(ships.HasOpenAction(session), Is.True, "Staff over a ship get the Shipmaster action.");

            ships.Open(session, null);
            var panel = ships.Panels.Single();
            Assert.That(panel.Selected, Is.EqualTo(grid), "A panel opened with no ship named starts on the one the admin is over.");
            panel.Close();

            // Out in open space there is no ship to act on.
            xforms.SetCoordinates(body, new EntityCoordinates(map.MapUid, new Vector2(600, 600)));
            Assert.That(ships.ShipUnder(session), Is.Null);
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(ships.HasOpenAction(session), Is.False, "The action goes once the admin has left the ship.");

            server.PlayerMan.SetAttachedEntity(session, null);
            entMan.DeleteEntity(body);
            entMan.DeleteEntity(grid);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<ConfirmButton> Confirms(Control parent)
    {
        return parent.Children.SelectMany(child => child is ConfirmButton confirm ? new[] { confirm } : Confirms(child));
    }
}
