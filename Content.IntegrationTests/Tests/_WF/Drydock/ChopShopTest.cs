using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Drydock;
using Content.Server._WF.Encounters.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Station.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Cargo.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests the Chop-Shop's consoles: sheets and cash for a break, no repairs, and what counts as lawful property.</summary>
[TestFixture]
public sealed class ChopShopTest
{
    private const int HullSize = 4;

    /// <summary>Builds a mobile crane yard worked by a chop-shop console, with a 4x4 steel-walled hull in its berth.</summary>
    private static (EntityUid Console, EntityUid Crane, EntityUid Ship) SetUpChopShop(TestPair pair, MapId map)
    {
        var em = pair.Server.EntMan;
        var yard = MobileDrydockTest.SpawnMobileYard(pair, map);
        var at = em.GetComponent<TransformComponent>(yard.Console).Coordinates;
        em.DeleteEntity(yard.Console);
        var console = em.SpawnEntity("WFComputerChopShop", at);
        DrydockTest.LinkCrane(pair, console, yard.Crane);

        var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
        Assert.That(pair.Server.System<WFDrydockSystem>().TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

        var ship = DrydockTest.CreateShip(pair, map);
        DrydockTest.SetFloor(pair, ship, HullSize);
        DrydockTest.SpawnAnchored(pair, ship, "WallReinforced", new Vector2i(0, 0));
        DrydockTest.SpawnAnchored(pair, ship, "Autolathe", new Vector2i(2, 2));
        DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
        return (console, yard.Crane, ship);
    }

    /// <summary>Counts the thalers lying on a grid.</summary>
    private static int ThalersOn(TestPair pair, EntityUid grid, out int stacks)
    {
        var em = pair.Server.EntMan;
        var total = 0;
        stacks = 0;
        var query = em.EntityQueryEnumerator<CashComponent, StackComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var stack, out var xform))
        {
            if (xform.GridUid != grid)
                continue;

            total += stack.Count;
            stacks++;
        }

        return total;
    }

    /// <summary>
    /// A break at a chop shop stores nothing in its crane and pays 80% of the ship's shipyard price in thalers, printed
    /// by the console as the tiles come off, onto one stack, to the thaler its preview quoted. It sells no repairs.
    /// </summary>
    [Test]
    public async Task BreakPrintsShareOfShipValue()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, actor = default, yard = default;
        var expected = 0;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship) = SetUpChopShop(pair, map.MapId);
            yard = em.GetComponent<TransformComponent>(console).GridUid!.Value;
            em.AddComponent<VesselComponent>(ship).VesselId = "Guppy";
            expected = (int) (server.ProtoMan.Index<VesselPrototype>("Guppy").Price * 0.8);
            actor = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            drydock.UpdateUi(console, force: true);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console,
                WFDrydockConsoleUiKey.Key, out var state), Is.True, "The console should have a state.");
            Assert.Multiple(() =>
            {
                Assert.That(state!.BreakOnly, Is.True, "A chop shop should say it only breaks.");
                Assert.That(state.PaysWithMaterials, Is.False, "A chop shop should list no supplies.");
                Assert.That(state.Upgrades, Is.Empty, "A chop shop should offer no refits.");
                Assert.That(state.BreakCash, Is.EqualTo(expected), "The preview should quote 80% of the shipyard price.");
                Assert.That(state.BreakWarrant, Is.False, "An unregistered hull is anyone's.");
            });

            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartRepair(console, comp, ship, actor), Is.False, "A chop shop should refuse a repair.");
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The break should start.");
        });

        await DrydockTest.RunUntil(pair, 400, 5, () =>
            !em.TryGetComponent(console, out WFDrydockJobComponent job) || job.Done >= HullSize * HullSize / 2);

        await server.WaitAssertion(() =>
        {
            var midway = ThalersOn(pair, yard, out _);
            Assert.That(midway, Is.InRange(1, expected - 1), "Thalers should be printed as the tiles come off.");
        });

        await DrydockTest.RunUntil(pair, 400, 30, () => !em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "The break should end.");
            var printed = ThalersOn(pair, yard, out var stacks);
            Assert.Multiple(() =>
            {
                Assert.That(em.Deleted(ship), Is.True, "The hull should be gone.");
                Assert.That(server.System<SharedMaterialStorageSystem>().GetMaterialAmount(crane, "Steel", localOnly: true), Is.Zero,
                    "A chop shop should store nothing in its crane.");
                Assert.That(printed, Is.EqualTo(expected), "The whole hull should print 80% of the shipyard price.");
                Assert.That(stacks, Is.EqualTo(1), "The bills should pile on one stack while nobody takes it.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A break that is stopped part way and started again pays only what is left: the hull as a whole never prints more
    /// than 80% of its shipyard price.
    /// </summary>
    [Test]
    public async Task RestartedBreakPaysOnlyTheRest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var xforms = server.System<SharedTransformSystem>();

        EntityUid console = default, crane = default, ship = default, actor = default, yard = default;
        var expected = 0;
        var berthed = Vector2.Zero;
        await server.WaitAssertion(() =>
        {
            (console, crane, ship) = SetUpChopShop(pair, map.MapId);
            yard = em.GetComponent<TransformComponent>(console).GridUid!.Value;
            em.AddComponent<VesselComponent>(ship).VesselId = "Guppy";
            expected = (int) (server.ProtoMan.Index<VesselPrototype>("Guppy").Price * 0.8);
            actor = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            berthed = xforms.GetWorldPosition(ship);
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);
        await server.WaitAssertion(() =>
            Assert.That(drydock.TryStartBreak(console, em.GetComponent<WFDrydockConsoleComponent>(console), ship, actor), Is.True));

        await DrydockTest.RunUntil(pair, 400, 5, () =>
            !em.TryGetComponent(console, out WFDrydockJobComponent job) || job.Done >= HullSize * HullSize / 2);

        // Taking the hull out of the berth stops the break.
        var paidFirst = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(ship), Is.False, "Half the hull should still stand.");
            paidFirst = ThalersOn(pair, yard, out _);
            Assert.That(paidFirst, Is.InRange(1, expected - 1), "The first break should have paid part of the hull.");
            xforms.SetWorldPosition(ship, berthed + new Vector2(500f, 0f));
        });
        await DrydockTest.RunUntil(pair, 100, 10, () => !em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), Is.False, "Leaving the berth should stop the break.");
            xforms.SetWorldPosition(ship, berthed);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            drydock.UpdateUi(console, force: true);
            Assert.That(server.System<SharedUserInterfaceSystem>().TryGetUiState<WFDrydockConsoleState>(console,
                WFDrydockConsoleUiKey.Key, out var state), Is.True);
            Assert.That(state!.BreakCash, Is.EqualTo(expected - ThalersOn(pair, yard, out _)), "The preview should quote only what is left.");
            Assert.That(drydock.TryStartBreak(console, comp, ship, actor), Is.True, "The rest of the hull should break.");
        });

        await DrydockTest.RunUntil(pair, 400, 30, () => !em.HasComponent<WFDrydockJobComponent>(console));

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(ship), Is.True, "The hull should be gone.");
            Assert.That(ThalersOn(pair, yard, out _), Is.EqualTo(expected), "Both breaks together should print 80% of the shipyard price.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A deed in someone else's name and a live lawful encounter ship are lawful property; the breaker's own deed, an
    /// outlaw's encounter ship and a derelict are not.
    /// </summary>
    [Test]
    public async Task LawfulPropertyRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var console = em.SpawnEntity("WFComputerChopShop", new MapCoordinates(0, 0, map.MapId));
            var chop = em.GetComponent<WFDrydockChopShopComponent>(console);
            var breaker = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            server.System<MetaDataSystem>().SetEntityName(breaker, "Jo Breaker");

            var deeded = DrydockTest.CreateShip(pair, map.MapId, new Vector2(100f, 0f));
            var deed = em.AddComponent<ShuttleDeedComponent>(deeded);
#pragma warning disable RA0002
            deed.ShuttleOwner = "Someone Else";
#pragma warning restore RA0002

            var pirate = EncounterShip(pair, map.MapId, "WFEncounterPirateRaiders", "raider", derelict: false);
            var navy = EncounterShip(pair, map.MapId, "WFEncounterTsfPatrol", "lead", derelict: false);
            var wreck = EncounterShip(pair, map.MapId, "WFEncounterTsfPatrol", "lead", derelict: true);

            Assert.Multiple(() =>
            {
                Assert.That(drydock.IsLawfulProperty(chop, deeded, breaker), Is.True, "Someone else's ship is lawful property.");
                Assert.That(drydock.IsLawfulProperty(chop, pirate, breaker), Is.False, "A pirate's ship is anyone's.");
                Assert.That(drydock.IsLawfulProperty(chop, navy, breaker), Is.True, "A navy ship is lawful property.");
                Assert.That(drydock.IsLawfulProperty(chop, wreck, breaker), Is.False, "A derelict is anyone's.");
            });

#pragma warning disable RA0002
            deed.ShuttleOwner = "Jo Breaker";
#pragma warning restore RA0002
            Assert.That(drydock.IsLawfulProperty(chop, deeded, breaker), Is.False, "The breaker's own ship is theirs to break.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Makes a grid one ship of a stand-in encounter of the given prototype.</summary>
    private static EntityUid EncounterShip(TestPair pair, MapId map, string prototype, string key, bool derelict)
    {
        var em = pair.Server.EntMan;
        var ship = DrydockTest.CreateShip(pair, map, new Vector2(-100f, 0f));
        var encounter = em.SpawnEntity(null, new MapCoordinates(0, 0, map));
        var comp = em.AddComponent<WFEncounterComponent>(encounter);
        comp.Prototype = prototype;
        comp.Ships[key] = new WFEncounterShipState { Grid = ship, Derelict = derelict };
        var member = em.AddComponent<WFEncounterGridComponent>(ship);
        member.Encounter = encounter;
        member.Key = key;
        return ship;
    }

    /// <summary>
    /// A vessel spawned by an admin with no owner gets a station of its own but no deed; it is a ship, not an outpost,
    /// so it can be broken. Refused hulls say why on the console.
    /// </summary>
    [Test]
    public async Task OwnerlessSpawnedVesselIsBreakable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var vessel = server.ProtoMan.Index<VesselPrototype>("Guppy");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(800f, 0f), null,
                out var spawned, createStation: true), Is.True, "The Guppy should spawn.");
            var ship = spawned!.Value;
            Assert.That(em.HasComponent<StationMemberComponent>(ship), "The spawned vessel should be a station member.");

            var console = em.SpawnEntity("WFComputerChopShop", new MapCoordinates(0, 0, map.MapId));
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.BreakRefusal(console, comp, ship), Is.Null, "An ownerless spawned vessel should be breakable.");

            var outpost = DrydockTest.CreateShip(pair, map.MapId, new Vector2(-800f, 0f));
            DrydockTest.SetFloor(pair, outpost, 2);
            em.AddComponent<StationMemberComponent>(outpost);
            Assert.That(drydock.BreakRefusal(console, comp, outpost), Is.Not.Null, "An outpost should still be refused.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Chop-Shop map loads with four chop-shop berths, each with its cranes, and a fence who books nothing.</summary>
    [Test]
    public async Task ChopShopMapLoads()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath("/Maps/_WF/Drydock/chopshop.yml"),
                out var grid, offset: new Vector2(500f, 500f)), "chopshop.yml should load.");
            var station = grid!.Value.Owner;

            var consoles = 0;
            var query = em.EntityQueryEnumerator<WFDrydockConsoleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var comp, out var xform))
            {
                if (xform.GridUid != station)
                    continue;

                consoles++;
                Assert.That(em.HasComponent<WFDrydockChopShopComponent>(uid), $"The {comp.Berth} console should be a chop shop.");
                Assert.That(drydock.TryGetBerth(uid, comp, out _), $"The {comp.Berth} berth should have cranes.");
            }

            Assert.Multiple(() =>
            {
                Assert.That(consoles, Is.EqualTo(4), "The Chop-Shop should have four berths.");
                Assert.That(DrydockTest.HailContactsOn(pair, station), Has.Count.EqualTo(1), "The fence should answer hails.");
                Assert.That(em.EntityQuery<WFDrydockOperatorComponent>().Any(), Is.False, "Nobody at the Chop-Shop books berths.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
