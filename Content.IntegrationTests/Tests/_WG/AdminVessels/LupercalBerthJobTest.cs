#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Drydock;
using Content.Server._Mono.ShipRepair;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipAccess;
using Content.Server._WG.ShipCommand;
using Content.Server.Administration.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.AdminVessels;

/// <summary>
/// A full drydock job at a Lupercal berth: a real ship docked at each arm lies in that lobby's berth, and a broken ship
/// docked there is refused to a barred customer, then repaired end to end for one who pays at the berth console.
/// </summary>
[TestFixture]
[TestOf(typeof(WGDrydockSystem))]
public sealed class LupercalBerthJobTest
{
    private static readonly ResPath ShipPath = new("/SharedMaps/_WG/OldVessels/vaquita.yml");

    [Test]
    public async Task DockedShipIsRepairedAtABerth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var drydock = server.System<WGDrydockSystem>();
        var docking = server.System<DockingSystem>();
        var shuttles = server.System<ShuttleSystem>();
        var maps = server.System<SharedMapSystem>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default, ship = default, console = default, payer = default;
        Vector2i hole = default;
        EntityUid wall = default;
        var thrusters = 0;
        var dockedAt = new Dictionary<EntityUid, EntityUid>();

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WGLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, ShipPath, out var loaded, offset: new Vector2(120, 260)), Is.True);
            ship = loaded!.Value.Owner;
        });

        // The cranes need half their power to start, which takes the ship's network a while to reach.
        await pair.RunTicksSync(ticks * 30);

        await server.WaitAssertion(() =>
        {
            var berths = new List<EntityUid>();
            var consoles = em.EntityQueryEnumerator<WGDrydockConsoleComponent, TransformComponent>();
            while (consoles.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    berths.Add(uid);
            }

            Assert.That(berths, Has.Count.EqualTo(4), "A berth in each dock lobby.");

            // Dock the ship at each of the Lupercal's docks in turn and see which berth, if any, then holds it.
            var shipDock = docking.GetDocks(ship).Single();
            var xforms = em.GetComponent<TransformComponent>(ship);
            var report = new List<string>();
            foreach (var dock in docking.GetDocks(grid))
            {
                if (docking.GetDockingConfig(ship, grid, shipDock, shipDock.Comp, dock, dock.Comp) is not { } config)
                    continue;

                shuttles.FTLDock((ship, xforms), config);
                var holder = berths.FirstOrDefault(b => drydock.GetBerthedShip(b, em.GetComponent<WGDrydockConsoleComponent>(b)) == ship);
                var at = em.GetComponent<TransformComponent>(dock).LocalPosition;
                if (!holder.IsValid())
                {
                    report.Add($"dock at {at}: no berth");
                    docking.UndockDocks(ship);
                    continue;
                }

                // Every tile of the docked ship lies within reach of one of the berth's cranes.
                var berthComp = em.GetComponent<WGDrydockConsoleComponent>(holder);
                var cranes = drydock.LinkedCranes(holder);
                var shipTiles = maps.GetAllTiles(ship, em.GetComponent<MapGridComponent>(ship));
                var total = 0;
                var outOfReach = 0;
                while (shipTiles.MoveNext(out var shipTile))
                {
                    total++;
                    var centre = shipTile!.Value.GridIndices + new Vector2(0.5f, 0.5f);
                    if (!cranes.Any(c => drydock.CanReach(berthComp, ship, c, centre)))
                        outOfReach++;
                }

                report.Add($"dock at {at}: {berthComp.Berth}, {cranes.Count} cranes, {outOfReach} of {total} tiles out of reach");
                if (outOfReach == 0)
                    dockedAt.TryAdd(holder, dock);

                docking.UndockDocks(ship);
            }

            TestContext.Out.WriteLine(string.Join("\n", report));
            Assert.That(dockedAt.Keys, Is.EquivalentTo(berths), "Every berth's cranes reach all of a ship docked at its arm:\n" + string.Join("\n", report));

            // Dock for the job at the first berth found, and break the ship: a thruster lost, a wall dented, a hole.
            console = dockedAt.Keys.First();
            var target = dockedAt[console];
            shuttles.FTLDock((ship, xforms), docking.GetDockingConfig(ship, grid, shipDock, shipDock.Comp, target, em.GetComponent<DockingComponent>(target))!);
            server.System<ShipRepairSystem>().GenerateRepairData(ship);

            var thrusterList = OnShip<ThrusterComponent>(em, ship);
            thrusters = thrusterList.Count;
            em.DeleteEntity(thrusterList[0]);
            wall = OnShip<DamageableComponent>(em, ship).First(e => em.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID.StartsWith("Wall") == true);
            var blunt = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(20));
            server.System<DamageableSystem>().TryChangeDamage(wall, blunt, ignoreResistances: true);

            // A bare floor tile inside the hull, so nothing on it drifts off when it goes.
            var shipGrid = em.GetComponent<MapGridComponent>(ship);
            var lookup = server.System<EntityLookupSystem>();
            var sides = new[] { Vector2i.Up, Vector2i.Down, Vector2i.Left, Vector2i.Right };
            var tiles = maps.GetAllTiles(ship, shipGrid);
            Vector2i? bare = null;
            while (bare == null && tiles.MoveNext(out var tileRef))
            {
                var tile = tileRef!.Value.GridIndices;
                if (lookup.GetLocalEntitiesIntersecting(ship, tile).Count == 0
                    && sides.All(d => !maps.GetTileRef(ship, shipGrid, tile + d).Tile.IsEmpty))
                    bare = tile;
            }

            Assert.That(bare, Is.Not.Null, "The ship has a bare floor tile inside.");
            hole = bare!.Value;
            maps.SetTile(ship, shipGrid, hole, Tile.Empty);

            var items = drydock.BuildRepairItems(ship, em.GetComponent<WGDrydockConsoleComponent>(console));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(items.Count(i => i.Kind == WGDrydockWorkKind.Tile), Is.GreaterThanOrEqualTo(1), "The hole needs a floor.");
                Assert.That(items.Count(i => i.Kind == WGDrydockWorkKind.Entity), Is.GreaterThanOrEqualTo(1), "The thruster needs rebuilding.");
                Assert.That(items.Count(i => i.Kind == WGDrydockWorkKind.Heal), Is.GreaterThanOrEqualTo(1), "The wall needs patching.");
            }

            // Owned by someone else, and not free for the owner or admins, so the job is charged.
            server.System<WGShipAccessServerSystem>().SetupRegisteredShip(grid, "Ada Vance", new List<NetUserId>());
            var command = em.GetComponent<WGShipCommandComponent>(grid);
            command.OwnerServedFree = command.AdminsServedFree = false;
            payer = DrydockTest.CreatePayer(pair, map.MapId);
        });

        // The test player is an admin, and admins outrank the barred list.
        var admins = server.ResolveDependency<IAdminManager>();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        await server.WaitPost(() => admins.DeAdmin(session));

        // A barred customer is turned away at the berth console, money and all.
        await server.WaitAssertion(() =>
        {
            var command = server.System<WGShipCommandSystem>();
            var user = session.UserId;
            Entity<WGShipCommandComponent> lupercal = (grid, em.GetComponent<WGShipCommandComponent>(grid));
            Assert.That(command.AddPerson(lupercal, WGShipCommandList.Barred, null, user, "Test Payer"), Is.True);
            Assert.That(command.GetAuthority(payer, grid), Is.EqualTo(WGShipAuthority.Barred));

            var balance = em.GetComponent<BankAccountComponent>(payer).Balance;
            DrydockTest.RaiseUiMessage(pair, console, WGDrydockConsoleUiKey.Key, new WGDrydockRepairMessage(), payer);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.False, "No job for the barred.");
                Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(balance), "Nothing charged.");
            }

            Assert.That(command.RemovePerson(lupercal, WGShipCommandList.Barred, null, user), Is.True);
            Assert.That(command.GetAuthority(payer, grid), Is.EqualTo(WGShipAuthority.Guest));
        });

        // The same customer, no longer barred, pays at the console and the job runs to the end.
        var before = 0;
        var skipped = -1;
        var trace = new List<string>();
        await server.WaitAssertion(() =>
        {
            before = em.GetComponent<BankAccountComponent>(payer).Balance;
            DrydockTest.RaiseUiMessage(pair, console, WGDrydockConsoleUiKey.Key, new WGDrydockRepairMessage(), payer);
            Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.True, "The repair starts.");

            // Where each item lies in each crane's arm frame, for a failure to explain itself.
            var xf = server.System<SharedTransformSystem>();
            var job = em.GetComponent<WGDrydockJobComponent>(console);
            var geometry = new List<string> { $"berth {em.GetComponent<WGDrydockConsoleComponent>(console).Berth}" };
            foreach (var craneUid in drydock.LinkedCranes(console))
            {
                var crane = em.GetComponent<WGDrydockCraneComponent>(craneUid);
                var cx = em.GetComponent<TransformComponent>(craneUid);
                geometry.Add($"crane at {cx.LocalPosition} rot {cx.LocalRotation.Degrees:0}: gap {crane.BerthGap} arm max {crane.MaxArm} boom max {crane.MaxExtension} blocker {crane.Blocker}");
                foreach (var item in job.Pending.Concat(job.Slots.Where(s => s.Item != null).Select(s => s.Item!)))
                {
                    var world = xf.ToMapCoordinates(new EntityCoordinates(ship, item.LocalPos)).Position;
                    var arm = crane.LocalToArm(Vector2.Transform(world, xf.GetInvWorldMatrix(craneUid)));
                    geometry.Add($"  {item.Kind} at ship {item.LocalPos}: arm frame across {arm.X:0.0} along {arm.Y:0.0}, reach {drydock.CanReach(em.GetComponent<WGDrydockConsoleComponent>(console), ship, craneUid, item.LocalPos)}");
                }
            }

            trace.AddRange(geometry);
        });

        var second = 0;
        await DrydockTest.RunUntil(pair, 300, ticks, () =>
        {
            if (em.TryGetComponent(console, out WGDrydockJobComponent? job))
            {
                skipped = job.Skipped;
                var held = drydock.GetBerthedShip(console, em.GetComponent<WGDrydockConsoleComponent>(console));
                var cranes = string.Join(" ", drydock.LinkedCranes(console).Select(c =>
                    $"{em.GetComponent<Content.Server.Power.Components.PowerConsumerComponent>(c).ReceivedPower:0}/{em.GetComponent<Content.Server.Power.Components.PowerConsumerComponent>(c).DrawRate:0}"));
                trace.Add($"{second++}s: power {job.Power:0.00} unpowered {job.Unpowered:0.0} started {job.Started} done {job.Done} pending {job.Pending.Count} berthed {(held == ship ? "ship" : held?.ToString() ?? "none")} cranes {cranes}");
            }

            return !em.HasComponent<WGDrydockJobComponent>(console);
        });
        TestContext.Out.WriteLine(string.Join("\n", trace));

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGDrydockConsoleComponent>(console);
            var ledger = em.GetComponent<WGShipServiceLedgerComponent>(grid).Entries;
            var charged = before - em.GetComponent<BankAccountComponent>(payer).Balance;
            var shipGrid = em.GetComponent<MapGridComponent>(ship);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.HasComponent<WGDrydockJobComponent>(console), Is.False, "The job finishes.");
                Assert.That(skipped, Is.Zero, "The cranes reach everything on a ship docked at the arm.");
                Assert.That(drydock.BuildRepairItems(ship, comp), Is.Empty, "Nothing is left to repair. Job trace:\n" + string.Join("\n", trace.Take(12).Concat(trace.TakeLast(4))));
                Assert.That(maps.GetTileRef(ship, shipGrid, hole).Tile.IsEmpty, Is.False, "The hole is floored.");
                Assert.That(OnShip<ThrusterComponent>(em, ship), Has.Count.EqualTo(thrusters), "The thruster is rebuilt.");
                Assert.That(em.GetComponent<DamageableComponent>(wall).TotalDamage, Is.EqualTo(FixedPoint2.Zero), "The wall is patched.");
                Assert.That(charged, Is.GreaterThan(0), "The repair was charged.");
                Assert.That(ledger, Has.Some.Matches<WGShipServiceEntry>(e => e.Kind == WGShipServiceKind.Drydock && e.Charged == charged),
                    "The service log records what was paid.");
                foreach (var crane in drydock.LinkedCranes(console))
                    Assert.That(em.GetComponent<WGDrydockCraneComponent>(crane).Active, Is.False, "The cranes stand down.");
            }
        });

        await server.WaitPost(() => admins.ReAdmin(session));
        await pair.CleanReturnAsync();
    }

    /// <summary>Entities with a component, on a grid.</summary>
    private static List<EntityUid> OnShip<T>(IEntityManager em, EntityUid grid) where T : IComponent
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<T, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                found.Add(uid);
        }

        return found;
    }
}
