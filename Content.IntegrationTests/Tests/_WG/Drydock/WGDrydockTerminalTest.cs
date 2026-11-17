#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipCommand;
using Content.Server.DeviceLinking.Systems;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Drydock;

/// <summary>
/// The drydock terminal: it works the berth consoles linked to it, each through that console's own handlers; a locked
/// berth refuses walk-in work and bookings but takes the terminal's; off a commanded ship only admins lock; and aboard
/// one the owner locks berths and sets the drydock price as the ship's "drydock" rule.
/// </summary>
[TestFixture]
[TestOf(typeof(WGDrydockSystem))]
public sealed class WGDrydockTerminalTest
{
    private const string Terminal = "WGComputerDrydockTerminal";

    /// <summary>Links a berth console to a terminal, or unlinks it if it was linked, as a multitool does.</summary>
    private static void Link(DeviceLinkSystem links, EntityUid terminal, EntityUid console)
    {
        Assert.That(links.ToggleLink(null, terminal, console, WGDrydockTerminalComponent.BerthPort, WGDrydockTerminalComponent.BerthReceiverPort),
            "The multitool link should toggle.");
    }

    /// <summary>
    /// A terminal linked to a mobile yard's console and a craneless one forwards a repair to the berth it names: none
    /// to the craneless one, a job at the yard's. Locked, the yard's console refuses the same repair at the console.
    /// </summary>
    [Test]
    public async Task TerminalWorksTheBerthItNamesEvenLocked()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();
        var links = server.System<DeviceLinkSystem>();

        EntityUid yard = default, empty = default, terminal = default, crane = default, payer = default, hand = default;

        await server.WaitAssertion(() =>
        {
            EntityUid host;
            (host, crane, yard) = MobileDrydockTest.SpawnMobileYard(pair, map.MapId);
            empty = DrydockTest.SpawnAnchored(pair, host, "WGComputerDrydock", new Vector2i(5, 1));
            terminal = DrydockTest.SpawnAnchored(pair, host, Terminal, new Vector2i(3, 0));
            Link(links, terminal, yard);
            Link(links, terminal, empty);

            var comp = em.GetComponent<WGDrydockConsoleComponent>(yard);
            Assert.That(drydock.TryGetBerth(yard, comp, out var berth), "The crane should make a berth.");
            var ship = DrydockTest.CreateDamagedShip(pair, map.MapId);
            DrydockTest.PlaceOver(pair, ship, host, berth.Area.Center);
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            MobileDrydockTest.Stock(pair, crane);

            var mindSys = server.System<SharedMindSystem>();
            hand = em.SpawnEntity("MobHuman", map.GridCoords);
            var mind = mindSys.CreateMind(null, "Dock Hand");
            mindSys.TransferTo(mind, hand, mind: mind.Comp);
        });

        await MobileDrydockTest.WaitForCranePower(pair, crane);

        await server.WaitAssertion(() =>
        {
            Assert.That(drydock.TerminalBerths(terminal), Is.EquivalentTo(new[] { yard, empty }), "Both linked consoles are its berths.");

            var seen = drydock.TerminalState(terminal, payer);
            Assert.Multiple(() =>
            {
                Assert.That(seen.Commanded, Is.False);
                Assert.That(seen.CanOperate, Is.True, "Off a commanded ship anyone works the terminal.");
                Assert.That(seen.CanConfigure, Is.True, "The test player is the host, an admin.");
                Assert.That(seen.Berths.Select(b => em.GetEntity(b.Console)), Is.EquivalentTo(new[] { yard, empty }));
                Assert.That(seen.Berths.Single(b => em.GetEntity(b.Console) == yard).State.ShipName, Is.Not.Null,
                    "The yard's row shows its ship.");
            });

            Assert.That(drydock.CanConfigureTerminal(terminal, hand), Is.False, "A player who isn't an admin locks nothing here.");
            Assert.That(drydock.SetBerthLock(terminal, hand, yard, true), Is.False);
            Assert.That(drydock.SetBerthLock(terminal, payer, yard, true), Is.True, "An admin locks the yard's berth.");
            var comp = em.GetComponent<WGDrydockConsoleComponent>(yard);
            Assert.That(comp.Locked, Is.True);

            DrydockTest.RaiseUiMessage(pair, yard, WGDrydockConsoleUiKey.Key, new WGDrydockRepairMessage(), payer);
            Assert.That(em.HasComponent<WGDrydockJobComponent>(yard), Is.False, "The locked berth refuses a walk-in repair.");

            DrydockTest.RaiseUiMessage(pair, terminal, WGDrydockTerminalUiKey.Key,
                new WGDrydockRepairMessage { Berth = em.GetNetEntity(empty) }, payer);
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WGDrydockJobComponent>(empty), Is.False, "The craneless berth has no ship to work.");
                Assert.That(em.HasComponent<WGDrydockJobComponent>(yard), Is.False, "And the repair named it, not the yard.");
            });

            Link(links, terminal, yard);
            DrydockTest.RaiseUiMessage(pair, terminal, WGDrydockTerminalUiKey.Key,
                new WGDrydockRepairMessage { Berth = em.GetNetEntity(yard) }, payer);
            Assert.That(em.HasComponent<WGDrydockJobComponent>(yard), Is.False, "Unlinked, the yard isn't the terminal's to work.");

            Link(links, terminal, yard);
            DrydockTest.RaiseUiMessage(pair, terminal, WGDrydockTerminalUiKey.Key,
                new WGDrydockRepairMessage { Berth = em.GetNetEntity(yard) }, payer);
            Assert.That(em.HasComponent<WGDrydockJobComponent>(yard), Is.True, "Linked again, the terminal works the locked berth.");

            Assert.That(drydock.SetBerthLock(terminal, payer, null, false), Is.True, "Unlock all.");
            Assert.That(comp.Locked, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A dockmaster books a small ship into the next berth when the one it would take is locked, and into none when
    /// every berth is.
    /// </summary>
    [Test]
    public async Task BookingsSkipLockedBerths()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            var (station, consoleA, _) = DrydockTest.LoadDrydock(pair, map.MapId);
            var op = DrydockTest.FindOperator(pair, station);
            var opComp = em.GetComponent<WGDrydockOperatorComponent>(op);
            var ship = DrydockTest.CreateDamagedShip(pair, map.MapId, DrydockTest.OutsideBayPos);
            var payer = DrydockTest.CreatePayer(pair, map.MapId);

            EntityUid? BookedAt()
            {
                var query = em.EntityQueryEnumerator<WGDrydockBookingComponent>();
                while (query.MoveNext(out var uid, out var booking))
                {
                    if (booking.Ship == ship)
                        return uid;
                }

                return null;
            }

            em.GetComponent<WGDrydockConsoleComponent>(consoleA).Locked = true;
            drydock.Book(op, opComp, ship, null, payer, null);
            var booked = BookedAt();
            Assert.That(booked, Is.Not.Null, "The small ship still books a berth.");
            Assert.That(booked, Is.Not.EqualTo(consoleA), "But not berth A, which is locked.");

            em.RemoveComponent<WGDrydockBookingComponent>(booked!.Value);
            var consoles = em.EntityQueryEnumerator<WGDrydockConsoleComponent, TransformComponent>();
            while (consoles.MoveNext(out _, out var comp, out var xform))
            {
                if (xform.GridUid == station)
                    comp.Locked = true;
            }

            drydock.Book(op, opComp, ship, null, payer, null);
            Assert.That(BookedAt(), Is.Null, "With every berth locked, nothing is booked.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Aboard a commanded ship the owner locks berths and sets the drydock price, a rule every customer gets; a guest
    /// does neither and works nothing; out of range or cleared, the price is refused or back to usual.
    /// </summary>
    [Test]
    public async Task OwnerSetsTheDrydockPriceAboard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var drydock = server.System<WGDrydockSystem>();
        var services = server.System<WGShipServiceSystem>();
        var links = server.System<DeviceLinkSystem>();
        var hands = server.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            em.EnsureComponent<WGShipAccessComponent>(grid);
            var command = em.EnsureComponent<WGShipCommandComponent>(grid);
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(em, deed, grid);
            var (guest, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Random Stranger", 3);
            var (officer, officerCard) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Ben Ortiz", 2);
            Assert.That(server.System<WGShipAccessSystem>().TryGetKey(officerCard, out var officerKey), Is.True);
            Assert.That(server.System<WGShipCommandSystem>().AddPerson((grid, command), WGShipCommandList.Officers, officerKey, null, "Ben Ortiz"), Is.True);
            var mindSys = server.System<SharedMindSystem>();
            var mind = mindSys.CreateMind(null, "Random Stranger");
            mindSys.TransferTo(mind, guest, mind: mind.Comp);

            var terminal = em.SpawnEntity(Terminal, map.GridCoords);
            var console = em.SpawnEntity("WGComputerDrydock", map.GridCoords);
            Link(links, terminal, console);

            Assert.Multiple(() =>
            {
                Assert.That(drydock.CanConfigureTerminal(terminal, owner), Is.True);
                Assert.That(drydock.CanOperateTerminal(terminal, guest), Is.False, "A guest works no drydock aboard.");
                Assert.That(drydock.TerminalState(terminal, guest).Commanded, Is.True);
            });

            Assert.That(drydock.SetBerthLock(terminal, guest, console, true), Is.False);
            Assert.That(drydock.SetBerthLock(terminal, owner, console, true), Is.True);
            Assert.That(em.GetComponent<WGDrydockConsoleComponent>(console).Locked, Is.True, "The owner locks the berth.");

            // The v0.11 review: officers lock and unlock too, but the price stays with whoever configures the ship.
            Assert.Multiple(() =>
            {
                Assert.That(drydock.CanLockBerths(terminal, officer), Is.True);
                Assert.That(drydock.CanLockBerths(terminal, guest), Is.False);
                Assert.That(drydock.TerminalState(terminal, officer).CanLock, Is.True);
                Assert.That(drydock.TerminalState(terminal, officer).CanConfigure, Is.False);
                Assert.That(drydock.SetTerminalPrice(terminal, officer, 0f), Is.False, "An officer sets no price.");
            });
            Assert.That(drydock.SetBerthLock(terminal, officer, console, false), Is.True, "An officer unlocks the berth.");
            Assert.That(em.GetComponent<WGDrydockConsoleComponent>(console).Locked, Is.False);
            Assert.That(drydock.SetBerthLock(terminal, officer, console, true), Is.True, "And locks it again.");

            Assert.That(services.Quote(terminal, guest, WGShipServiceKind.Drydock, 1000), Is.EqualTo(1000), "The usual price, to begin.");
            Assert.That(drydock.SetTerminalPrice(terminal, guest, 0f), Is.False, "A guest sets no price.");
            Assert.That(drydock.SetTerminalPrice(terminal, owner, WGDrydockSystem.MaxMultiplier + 1), Is.False, "Out of range.");
            Assert.That(drydock.SetTerminalPrice(terminal, owner, 0f), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(command.PriceRules.Select(r => r.Name), Does.Contain(WGDrydockSystem.PriceRule));
                Assert.That(services.Quote(terminal, guest, WGShipServiceKind.Drydock, 1000), Is.EqualTo(0), "Free drydock work for all.");
                Assert.That(services.Quote(terminal, guest, WGShipServiceKind.Vending, 1000), Is.EqualTo(1000), "And nothing else.");
                Assert.That(drydock.TerminalState(terminal, owner).Multiplier, Is.EqualTo(0f));
            });

            Assert.That(drydock.SetTerminalPrice(terminal, owner, 1.5f), Is.True);
            Assert.That(services.Quote(terminal, guest, WGShipServiceKind.Drydock, 1000), Is.EqualTo(1500), "The rule replaced, not added.");
            Assert.That(command.PriceRules.Count(r => r.Name == WGDrydockSystem.PriceRule), Is.EqualTo(1));

            Assert.That(drydock.SetTerminalPrice(terminal, owner, null), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(services.Quote(terminal, guest, WGShipServiceKind.Drydock, 1000), Is.EqualTo(1000), "Back to the usual price.");
                Assert.That(drydock.TerminalState(terminal, owner).Multiplier, Is.Null);
            });

            em.DeleteEntity(terminal);
            em.DeleteEntity(console);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Lupercal's bridge terminal loads linked to its four dock lobbies' berths, each with its cranes.</summary>
    [Test]
    public async Task LupercalTerminalWorksTheFourDocks()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(map.MapId, new ResPath("/SharedMaps/_WG/AdminVessels/lupercal.yml"),
                out var loaded, offset: new System.Numerics.Vector2(500f, 500f)), "lupercal.yml should load.");
            var grid = loaded!.Value.Owner;

            var terminals = new List<EntityUid>();
            var query = em.EntityQueryEnumerator<WGDrydockTerminalComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    terminals.Add(uid);
            }

            Assert.That(terminals, Has.Count.EqualTo(1), "One drydock terminal, on the bridge.");
            var berths = drydock.TerminalBerths(terminals[0]);
            Assert.That(berths, Has.Count.EqualTo(4), "Linked to the four lobby berths.");
            foreach (var berth in berths)
            {
                Assert.That(drydock.TryGetBerth(berth, em.GetComponent<WGDrydockConsoleComponent>(berth), out _), Is.True,
                    $"{em.ToPrettyString(berth)} should have its cranes.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
