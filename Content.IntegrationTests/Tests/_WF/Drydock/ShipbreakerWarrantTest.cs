using System.Linq;
using System.Numerics;
using System;
using Content.IntegrationTests.Pair;
using Content.Server._NF.Bank;
using Content.Server._WF.Drydock;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.Drydock;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.Drydock;

/// <summary>Tests shipbreaking warrants and who a break is blamed on.</summary>
[TestFixture]
public sealed class ShipbreakerWarrantTest
{
    private const int Reward = 15000;

    /// <summary>Hurts a mob by an origin, then kills it.</summary>
    private static void Kill(TestPair pair, EntityUid target, EntityUid? origin)
    {
        var blunt = pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt");
        pair.Server.System<DamageableSystem>().TryChangeDamage(target, new DamageSpecifier(blunt, 10),
            ignoreResistances: true, origin: origin);
        pair.Server.System<MobStateSystem>().ChangeMobState(target, MobState.Dead, origin: origin);
    }

    /// <summary>
    /// A second break raises the warrant; when the breaker dies at a player's hand, that player is paid all of it and
    /// the warrant is gone.
    /// </summary>
    [Test]
    public async Task WarrantStacksAndPaysTheKiller()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var warrants = server.System<WFShipbreakerWarrantSystem>();
        var bank = server.System<BankSystem>();

        EntityUid hunter = default, breaker = default;
        var before = 0;
        await server.WaitAssertion(() =>
        {
            hunter = DrydockTest.CreatePayer(pair, map.MapId);
            breaker = em.SpawnEntity("MobHuman", new MapCoordinates(2, 0, map.MapId));
            warrants.Issue(breaker, "Alpha", Reward, "first", "office");
            warrants.Issue(breaker, "Beta", Reward, "second", "office");
            Assert.That(em.GetComponent<WFShipbreakerWarrantComponent>(breaker).Reward, Is.EqualTo(2 * Reward),
                "A second break should raise the warrant.");
            Assert.That(bank.TryGetBalance(hunter, out before), "The hunter should have an account.");

            Kill(pair, breaker, hunter);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(bank.TryGetBalance(hunter, out var after), "The hunter should still have an account.");
            Assert.Multiple(() =>
            {
                Assert.That(server.System<MobStateSystem>().IsDead(breaker), Is.True, "The breaker should be dead.");
                Assert.That(after - before, Is.EqualTo(2 * Reward), "The killer should be paid the whole warrant.");
                Assert.That(em.HasComponent<WFShipbreakerWarrantComponent>(breaker), Is.False, "A paid warrant should be gone.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A breaker who dies with no player's hand in it keeps the warrant.</summary>
    [Test]
    public async Task UnclaimedDeathKeepsWarrant()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid breaker = default;
        await server.WaitAssertion(() =>
        {
            breaker = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            server.System<WFShipbreakerWarrantSystem>().Issue(breaker, "Alpha", Reward, "first", "office");
            Kill(pair, breaker, null);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MobStateSystem>().IsDead(breaker), Is.True, "The breaker should be dead.");
            Assert.That(em.HasComponent<WFShipbreakerWarrantComponent>(breaker), Is.True, "Nobody earned the warrant, so it stands.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A chop-shop break is blamed on the ship on the breaker's deed, not the yard, and the victim's crew turn
    /// hostile to the breaker in person.
    /// </summary>
    [Test]
    public async Task BreakBlamesTheBreakerAndTheirShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();

        EntityUid console = default, crane = default, ship = default, breaker = default, own = default, crewman = default;
        await server.WaitAssertion(() =>
        {
            var yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var at = em.GetComponent<TransformComponent>(yard.Console).Coordinates;
            em.DeleteEntity(yard.Console);
            console = em.SpawnEntity("WFComputerChopShop", at);
            crane = yard.Crane;
            DrydockTest.LinkCrane(pair, console, crane);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

            ship = DrydockTest.CreateShip(pair, map.MapId);
            DrydockTest.SetFloor(pair, ship, 4);
            DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);
            crewman = em.SpawnEntity("MobHuman", new EntityCoordinates(ship, 1.5f, 1.5f));
            em.EnsureComponent<WFCrewComponent>(crewman);
            em.EnsureComponent<NpcFactionMemberComponent>(crewman);

            own = DrydockTest.CreateShip(pair, map.MapId, new Vector2(400f, 0f));
            DrydockTest.SetFloor(pair, own, 2);
            breaker = em.SpawnEntity("MobHuman", new MapCoordinates(0, 0, map.MapId));
            Assert.That(server.System<InventorySystem>().SpawnItemInSlot(breaker, "jumpsuit", "ClothingUniformJumpsuitColorBlack", true)
                && server.System<InventorySystem>().SpawnItemInSlot(breaker, "id", "PassengerIDCard", true), "The breaker needs an ID.");
            Assert.That(server.System<InventorySystem>().TryGetSlotEntity(breaker, "id", out var card), "The breaker's ID should be worn.");
            var deed = em.AddComponent<ShuttleDeedComponent>(card!.Value);
#pragma warning disable RA0002
            deed.ShuttleUid = own;
#pragma warning restore RA0002
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartBreak(console, comp, ship, breaker), Is.True, "The break should start.");
            var job = em.GetComponent<WFDrydockJobComponent>(console);
            Assert.Multiple(() =>
            {
                Assert.That(job.Attacker, Is.EqualTo(own), "The break should be blamed on the breaker's own ship.");
                Assert.That(job.Breaker, Is.EqualTo(breaker), "The break should be blamed on the breaker.");
                Assert.That(server.System<NpcFactionSystem>().GetHostiles(crewman).Contains(breaker), Is.True,
                    "The victim's crew should turn on the breaker.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A registered ship is its player's whatever their character is called, a break is blamed on the breaker's
    /// registered ship, and a break whose console is destroyed still pays what it recovered.
    /// </summary>
    [Test]
    public async Task RegisteredOwnerAndDestroyedConsole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WFDrydockSystem>();
        var bank = server.System<BankSystem>();

        EntityUid console = default, crane = default, ship = default, breaker = default, own = default;
        var before = 0;
        await server.WaitAssertion(() =>
        {
            var yard = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            var at = em.GetComponent<TransformComponent>(yard.Console).Coordinates;
            em.DeleteEntity(yard.Console);
            console = em.SpawnEntity("WFComputerChopShop", at);
            crane = yard.Crane;
            DrydockTest.LinkCrane(pair, console, crane);
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryGetBerth(console, comp, out var berth), "The crane should make a berth.");

            ship = DrydockTest.CreateShip(pair, map.MapId);
            DrydockTest.SetFloor(pair, ship, 4);
            DrydockTest.SpawnAnchored(pair, ship, "Autolathe", new Vector2i(1, 1));
            DrydockTest.SpawnAnchored(pair, ship, "Autolathe", new Vector2i(2, 2));
            DrydockTest.PlaceOver(pair, ship, yard.Grid, berth.Area.Center);

            breaker = DrydockTest.CreatePayer(pair, map.MapId);
            var user = pair.Client.Session!.UserId;
            own = DrydockTest.CreateShip(pair, map.MapId, new Vector2(400f, 0f));
            DrydockTest.SetFloor(pair, own, 2);
            em.AddComponent<ShuttleDeedComponent>(own);
            var ownership = em.AddComponent<ShipOwnershipComponent>(own);
            ownership.OwnerUserId = user;

            var chop = em.GetComponent<WFDrydockChopShopComponent>(console);
            Assert.That(drydock.IsLawfulProperty(chop, own, breaker), Is.False, "A ship registered to the breaker's player is theirs.");
            ownership.OwnerUserId = new NetUserId(Guid.NewGuid());
            Assert.That(drydock.IsLawfulProperty(chop, own, breaker), Is.True, "A ship registered to another player is not.");
            ownership.OwnerUserId = user;
            Assert.That(bank.TryGetBalance(breaker, out before), "The breaker should have an account.");
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WFDrydockConsoleComponent>(console);
            Assert.That(drydock.TryStartBreak(console, comp, ship, breaker), Is.True, "The break should start.");
            Assert.That(em.GetComponent<WFDrydockJobComponent>(console).Attacker, Is.EqualTo(own),
                "The break should be blamed on the breaker's registered ship.");
        });

        await DrydockTest.RunUntil(pair, 400, 10, () => !em.TryGetComponent(console, out WFDrydockJobComponent job) || job.Done >= 4);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<WFDrydockJobComponent>(console), "The break should still be running.");
            var job = em.GetComponent<WFDrydockJobComponent>(console);
            var printed = job.CashPaid;
            var bills = job.CashStack;
            Assert.That(printed, Is.GreaterThan(0), "The broken tiles should have printed thalers.");
            em.DeleteEntity(console);
            Assert.That(em.GetComponent<StackComponent>(bills!.Value).Count, Is.EqualTo(printed),
                "Thalers printed before the console was destroyed should stay.");
        });

        await pair.CleanReturnAsync();
    }
}
