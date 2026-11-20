#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipAdmin;
using Content.IntegrationTests.Tests._WG.Drydock;
using Content.Server._Mono.FireControl;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Server.Light.EntitySystems;
using Content.Server.StationEvents.Components;
using Content.Server.Station.Systems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Fluids.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipAdmin;

/// <summary>
/// The staff requests on Shipmaster: a hardpoint laid under a gun links it and gunnery relinks at once, the quick
/// clean-ups, the hull faction, jumping to a ship, Stop Knotting and an event aimed at one ship.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipAdminSystem))]
public sealed class ShipAdminRescueTest
{
    private const string VesselProto = "Guppy";

    /// <summary>
    /// A gun built before its hardpoint never linked, so no refresh put it on the ship's gunnery. Now the hardpoint
    /// takes it, and the relink a fix ends with puts it on the server without the minute's wait.
    /// </summary>
    [Test]
    public async Task HardpointLaidUnderGunLinksAndRelinks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid gun = default, serverUid = default;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            serverUid = em.SpawnEntity(null, map.GridCoords);
            var fireServer = em.AddComponent<FireControlServerComponent>(serverUid);
            fireServer.ConnectedGrid = grid;
            fireServer.ProcessingPower = 100;
            em.EnsureComponent<FireControlGridComponent>(grid).ControllingServer = serverUid;

            gun = DrydockTest.SpawnAnchored(pair, grid, "WeaponTurretSunder", new Vector2i(2, 2));
            Assert.That(em.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.Null, "Precondition: no hardpoint yet.");

            var hardpoint = DrydockTest.SpawnAnchored(pair, grid, "HardpointEnergyHeavy", new Vector2i(2, 2));
            Assert.Multiple(() =>
            {
                Assert.That(em.GetComponent<HardpointComponent>(hardpoint).anchoring, Is.EqualTo(gun), "The hardpoint did not take the gun above it.");
                Assert.That(em.GetComponent<HardpointAnchorableOnlyComponent>(gun).anchoredTo, Is.EqualTo(hardpoint));
            });

            Assert.That(em.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.Null,
                "Precondition: linking alone doesn't put the gun on gunnery.");

            // Through the panel's own entry point: a fix of the consoles relinks gunnery a tick later.
            Assert.That(em.System<WGShipAdminSystem>().StartFix(session, grid, WGShipAdminFixParts.Consoles, out var result), Is.True, result);
        });

        await pair.RunTicksSync(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.EqualTo(serverUid),
                "The fix did not put the gun on gunnery.");

            // And the helper on its own, for a gun dropped from the server since.
            em.GetComponent<FireControllableComponent>(gun).ControllingServer = null;
            Assert.That(server.System<FireControlSystem>().WgRelinkGrid(grid), Is.True, "The ship's server was not reported up.");
            Assert.That(em.GetComponent<FireControllableComponent>(gun).ControllingServer, Is.EqualTo(serverUid),
                "The relink did not put the gun on gunnery.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The old Spawn Vessel window is Shipmaster's spawn page: <c>shipmaster spawn</c> opens the panel on it, and a
    /// ship the admin spawns is selected in their panel.
    /// </summary>
    [Test]
    public async Task SpawnPageOpensAndSelectsTheSpawnedShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ships = em.System<WGShipAdminSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        WGShipAdminEuiState state = default!;
        EntityUid body = default;
        var wasAdmin = false;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            body = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(-60, 0), map.MapId));
            server.PlayerMan.SetAttachedEntity(session, body);
            server.ConsoleHost.ExecuteCommand(session, $"{WGShipAdminCommands.Panel} {WGShipAdminCommands.SpawnArg}");
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(ships.Panels, Has.Count.EqualTo(1), "shipmaster spawn did not open the panel.");
            var panel = ships.Panels.Single();
            Assert.That(((WGShipAdminEuiState) panel.GetNewState()).SpawnPageSeq, Is.GreaterThan(0), "The panel was not opened on its spawn page.");

            server.ConsoleHost.ExecuteCommand(session, $"spawnvessel {VesselProto}");
            Assert.That(panel.Selected, Is.Not.Null, "The spawned ship was not selected.");
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<MapGridComponent>(panel.Selected!.Value), Is.True);
                Assert.That(panel.Selected, Is.Not.EqualTo(map.Grid.Owner), "The test map's own grid was selected, not the new ship.");
            });
            state = (WGShipAdminEuiState) panel.GetNewState();
        });

        await pair.Client.WaitAssertion(() =>
        {
            var window = new WGShipAdminWindow();
            window.OpenCentered();
            Assert.That(window.SpawnPage.Visible, Is.False, "The panel opens on its ships page by default.");
            window.UpdateState(state);
            Assert.Multiple(() =>
            {
                Assert.That(window.SpawnPage.Visible, Is.True, "The spawn page was not brought to the front.");
                Assert.That(window.SpawnPageButton.Pressed, Is.True);
            });

            window.ShowPage(false);
            Assert.That(window.SpawnPage.Visible, Is.False);
            window.Close();
        });

        await server.WaitAssertion(() =>
        {
            if (ships.Panels.FirstOrDefault()?.Selected is { } spawned)
                em.DeleteEntity(spawned);

            em.DeleteEntity(body);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The quick fixes delete loose trash and puddles, leave held trash, and put a new bulb in a broken fitting.</summary>
    [Test]
    public async Task QuickFixesCleanUp()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ships = em.System<WGShipAdminSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid trash = default, puddle = default, light = default, brokenBulb = default;

        await server.WaitAssertion(() =>
        {
            DrydockTest.SetFloor(pair, grid, 5);
            trash = em.SpawnEntity("FoodPacketChipsTrash", new EntityCoordinates(grid, 1.5f, 1.5f));
            Assert.That(server.System<TagSystem>().HasTag(trash, "Trash"), "Precondition: the wrapper is trash.");
            puddle = em.SpawnEntity("Puddle", new EntityCoordinates(grid, 2.5f, 1.5f));
            light = DrydockTest.SpawnAnchored(pair, grid, "Poweredlight", new Vector2i(3, 3));
            var lights = server.System<PoweredLightSystem>();
            brokenBulb = lights.GetBulb(light)!.Value;
            server.System<SharedLightBulbSystem>().SetState(brokenBulb, LightBulbState.Broken);

            var parts = WGShipAdminFixParts.Trash | WGShipAdminFixParts.Puddles | WGShipAdminFixParts.Lights;
            Assert.That(ships.StartFix(session, grid, parts, out var result), Is.True, result);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var bulb = server.System<PoweredLightSystem>().GetBulb(light);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(trash), Is.True, "Loose trash was left.");
                Assert.That(em.Deleted(puddle), Is.True, "The puddle was left.");
                Assert.That(bulb, Is.Not.Null.And.Not.EqualTo(brokenBulb), "The broken bulb was not replaced.");
                Assert.That(em.GetComponent<LightBulbComponent>(bulb!.Value).State, Is.EqualTo(LightBulbState.Normal));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The hull faction is set and cleared; jumping puts the admin's body on open floor aboard; Stop Knotting moves the
    /// ship clear and at rest; an event aimed at the ship picks it and its station.
    /// </summary>
    [Test]
    public async Task FactionJumpUnstickAndEvent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var ships = em.System<WGShipAdminSystem>();
        var xforms = em.System<SharedTransformSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        EntityUid grid = default;
        Vector2 before = default;
        var wasAdmin = false;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(em.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned), Is.True);
            grid = spawned!.Value;

            Assert.That(ships.SetHullFaction(session, grid, "NoSuchFaction", out var result), Is.False, "An unknown faction is refused.");
            Assert.That(ships.SetHullFaction(session, grid, "WGAsf", out result), Is.True, result);
            Assert.That(em.GetComponent<CompanyComponent>(grid).CompanyName.Id, Is.EqualTo("WGAsf"));
            Assert.That(ships.BuildDetail(grid)!.Faction, Is.EqualTo("WGAsf"));
            Assert.That(ships.SetHullFaction(session, grid, null, out result), Is.True, result);
            Assert.That(ships.BuildDetail(grid)!.Faction, Is.Empty, "The faction was not taken off.");

            var body = em.SpawnEntity("MobHuman", new MapCoordinates(new Vector2(-40, 0), map.MapId));
            server.PlayerMan.SetAttachedEntity(session, body);
            Assert.That(ships.JumpTo(session, grid, out result), Is.True, result);
            var bodyXform = em.GetComponent<TransformComponent>(body);
            Assert.That(bodyXform.GridUid, Is.EqualTo(grid), "The jump did not land aboard.");
            var gridComp = em.GetComponent<MapGridComponent>(grid);
            var tile = em.System<SharedMapSystem>().GetTileRef(grid, gridComp, bodyXform.Coordinates);
            var turfs = em.System<TurfSystem>();
            Assert.Multiple(() =>
            {
                Assert.That(turfs.IsSpace(tile), Is.False, "The jump landed off the floor.");
                Assert.That(turfs.IsTileBlocked(tile, Content.Shared.Physics.CollisionGroup.Impassable | Content.Shared.Physics.CollisionGroup.MidImpassable),
                    Is.False, "The jump landed in a wall.");
            });
            em.DeleteEntity(body);

            before = xforms.GetWorldPosition(grid);
            Assert.That(ships.Unstick(session, grid, out result), Is.True, result);
            Assert.That((xforms.GetWorldPosition(grid) - before).Length(), Is.GreaterThan(1f), "Stop Knotting did not move the ship clear.");

            // An admin-spawned vessel has no station of its own; a bought one does.
            var stations = em.System<StationSystem>();
            if (stations.GetOwningStation(grid) == null)
            {
                var frontier = server.ProtoMan.Index<Content.Shared.Maps.GameMapPrototype>("Frontier");
                stations.InitializeNewStation(frontier.Stations.Values.First(), new[] { grid });
            }

            Assert.That(stations.GetOwningStation(grid), Is.Not.Null, "Precondition: the vessel has a station.");
            Assert.That(ships.StartEvent(session, grid, "NoSuchEvent", out result), Is.False, "An event not on the list is refused.");
            Assert.That(ships.StartEvent(session, grid, "GasLeak", out result), Is.True, result);

            // The test pool's ticker is a dummy that never starts a queued rule, so its warning delay is cut short here.
            var ticker = server.System<Content.Server.GameTicking.GameTicker>();
            var queued = em.EntityQueryEnumerator<Content.Shared.GameTicking.Components.DelayedStartRuleComponent, WGEventTargetComponent>();
            while (queued.MoveNext(out var rule, out var delay, out _))
            {
                delay.RuleStartTime = server.Timing.CurTime;
                ticker.StartGameRule(rule);
            }
        });

        await server.WaitAssertion(() =>
        {
            var station = em.System<StationSystem>().GetOwningStation(grid);
            var found = false;
            var leaks = em.EntityQueryEnumerator<GasLeakRuleComponent, WGEventTargetComponent>();
            while (leaks.MoveNext(out var rule, out var leak, out _))
            {
                found = true;
                var active = server.System<Content.Server.GameTicking.GameTicker>().IsGameRuleActive(rule);
                var atmos = server.System<Content.Server.Atmos.EntitySystems.AtmosphereSystem>();
                var open = em.System<SharedMapSystem>().GetAllTiles(grid, em.GetComponent<MapGridComponent>(grid))
                    .Count(t => !atmos.IsTileSpace(grid, em.GetComponent<TransformComponent>(grid).MapUid, t.GridIndices)
                                && !atmos.IsTileAirBlockedCached(grid, t.GridIndices));
                Assert.That(leak.FoundTile, $"No tile found: rule active {active}, open tiles aboard {open}.");
                Assert.Multiple(() =>
                {
                    Assert.That(leak.TargetStation, Is.EqualTo(station), "The event picked another station.");
                    Assert.That(leak.TargetGrid, Is.EqualTo(grid), "The event picked another grid.");
                });
            }

            Assert.That(found, "The gas leak did not start.");
            em.DeleteEntity(grid);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }
}
