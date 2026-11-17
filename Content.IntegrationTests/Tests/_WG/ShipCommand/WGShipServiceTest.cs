#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Pair;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.ShipCommand;
using Content.Server._WG.Traders;
using Content.Server._WG.VehicleShop;
using Content.Server.Power.Components;
using Content.Server.VendingMachines;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.Traders;
using Content.Shared._WG.VehicleShop;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// Price rules and the service log: the owner and admins free by default, each a switch; the lowest multiplier of the
/// rules that cover a customer wins; switched-off rules and other services don't count; services off a commanded ship
/// are untouched; each recorded use lands in the ship's ledger.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipServiceSystem))]
public sealed class WGShipServiceTest
{
    [Test]
    public async Task RulesPriceEachCustomer()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var services = entMan.System<WGShipServiceSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var keys = entMan.System<WGShipAccessSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var vendor = entMan.SpawnEntity(null, map.GridCoords);
            var offShip = entMan.SpawnEntity(null, new MapCoordinates(new Vector2(500, 500), map.MapId));
            var (owner, deed) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(entMan, deed, grid);
            var (staffer, staffCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Cy Lund", 3);
            var (stranger, _) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Random Stranger", 4);
            entMan.EnsureComponent<WGShipAccessComponent>(grid);
            var cmd = entMan.EnsureComponent<WGShipCommandComponent>(grid);
            Entity<WGShipCommandComponent> ship = (grid, cmd);
            keys.TryGetKey(staffCard, out var staffKey);
            command.AddPerson(ship, WGShipCommandList.Staff, staffKey, null, "Cy Lund");

            int Price(EntityUid customer, WGShipServiceKind kind = WGShipServiceKind.Vending) =>
                services.Quote(vendor, customer, kind, 100);

            Assert.Multiple(() =>
            {
                Assert.That(Price(owner), Is.EqualTo(0), "The owner is served free by default.");
                Assert.That(Price(stranger), Is.EqualTo(100), "Without rules a guest pays the usual price.");
                Assert.That(services.Quote(offShip, owner, WGShipServiceKind.Vending, 100), Is.EqualTo(100), "Off a commanded ship nothing changes.");
            });

            services.SetRule(ship, new WGShipPriceRule { Name = "dear", Audience = WGShipPriceAudience.Everyone, Multiplier = 2f }, null);
            services.SetRule(ship, new WGShipPriceRule { Name = "staff half", Audience = WGShipPriceAudience.Staff, Services = WGShipServiceKind.Vending, Multiplier = 0.5f }, null);
            Assert.Multiple(() =>
            {
                Assert.That(Price(stranger), Is.EqualTo(200), "An everyone rule above 1 makes it dearer.");
                Assert.That(Price(staffer), Is.EqualTo(50), "The lowest multiplier that covers the customer wins.");
                Assert.That(Price(staffer, WGShipServiceKind.Drydock), Is.EqualTo(200), "A rule covers only its services.");
            });

            Assert.That(services.ToggleRule(ship, "staff half", null, out var on), Is.True);
            Assert.That(on, Is.False);
            Assert.That(Price(staffer), Is.EqualTo(200), "A switched-off rule doesn't apply.");

            Assert.That(services.ToggleRule(ship, "owner", null, out var ownerFree), Is.True);
            Assert.That(ownerFree, Is.False);
            Assert.That(Price(owner), Is.EqualTo(200), "With owner service switched off, the owner pays like anyone the rules cover.");

            Assert.That(services.RemoveRule(ship, "dear", null), Is.True);
            Assert.That(services.RemoveRule(ship, "dear", null), Is.False);
            Assert.That(Price(stranger), Is.EqualTo(100));

            var (barred, barredCard) = ShipAccessTest.SpawnPersonWithCard(entMan, hands, map, "Bo Reyes", 5);
            keys.TryGetKey(barredCard, out var barredKey);
            command.AddPerson(ship, WGShipCommandList.Barred, barredKey, null, "Bo Reyes");
            services.SetRule(ship, new WGShipPriceRule { Name = "free day", Audience = WGShipPriceAudience.Everyone, Multiplier = 0f }, null);
            Assert.Multiple(() =>
            {
                Assert.That(Price(stranger), Is.EqualTo(0), "An everyone rule at 0 is free for a guest.");
                Assert.That(Price(barred), Is.EqualTo(100), "The barred get no discount.");
            });
            services.RemoveRule(ship, "free day", null);

            services.MarkBargain(staffer, 1000, 1000);
            Assert.That(services.ResaleValue(staffer, 900), Is.EqualTo(900), "Bought at the list price, it resells as usual.");
            services.MarkBargain(stranger, 1000, 250);
            Assert.That(services.ResaleValue(stranger, 900), Is.EqualTo(150), "A bargain resells less its discount.");
            Assert.That(services.ResaleValue(stranger, 1400), Is.EqualTo(650), "What was added since keeps its full value.");
            services.MarkBargain(barred, 1000, 0);
            Assert.That(services.ResaleValue(barred, 900), Is.EqualTo(0), "Something free resells for nothing, never less.");

            var price = services.Quote(vendor, staffer, WGShipServiceKind.Vending, 100, out var rule);
            services.Record(vendor, staffer, WGShipServiceKind.Vending, "a soda", 100, price, rule);
            services.Record(offShip, staffer, WGShipServiceKind.Vending, "elsewhere", 100, 100, null);
            var ledger = entMan.GetComponent<WGShipServiceLedgerComponent>(grid).Entries;
            Assert.That(ledger, Has.Count.EqualTo(1), "Only use aboard the ship is recorded.");
            Assert.That(ledger[0].What, Is.EqualTo("a soda"));
            Assert.That(ledger[0].Customer, Is.EqualTo("Cy Lund"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The barred are turned away by every service aboard even on a free day: a vending machine, the vehicle terminal and
    /// a trader's counter, which also ends a conversation with someone barred partway through. A stranger is served.
    /// </summary>
    [Test]
    public async Task BarredAreTurnedAway()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var services = em.System<WGShipServiceSystem>();
        var command = em.System<WGShipCommandSystem>();
        var keys = em.System<WGShipAccessSystem>();
        var hands = em.System<SharedHandsSystem>();
        var vending = em.System<VendingMachineSystem>();
        var shop = em.System<WGVehicleShopSystem>();
        var traders = em.System<TraderSystem>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var catalog = server.ProtoMan.Index<WGVehicleCatalogPrototype>("WGVehicleCatalogExpeditionary");
        var van = catalog.Vehicles.FindIndex(listing => listing.Vehicle == "VehicleBoxVan");
        EntityUid barred = default, stranger = default, strangerCard = default, vendor = default, terminal = default, trader = default;
        Entity<WGShipCommandComponent> ship = default;

        await server.WaitPost(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            // A deeded grid, so the others aren't counted as its owner.
            var (_, deed) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Ada Vance", 1);
            ShipAccessTest.GiveDeed(em, deed, grid);
            (stranger, strangerCard) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Random Stranger", 2);
            (barred, var barredCard) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Bo Reyes", 3);
            em.EnsureComponent<WGShipAccessComponent>(grid);
            ship = (grid, em.EnsureComponent<WGShipCommandComponent>(grid));
            Assert.That(keys.TryGetKey(barredCard, out var barredKey), Is.True);
            command.AddPerson(ship, WGShipCommandList.Barred, barredKey, null, "Bo Reyes");

            // Free for everyone, so only the refusal can stop the barred.
            services.SetRule(ship, new WGShipPriceRule { Name = "free day", Audience = WGShipPriceAudience.Everyone, Multiplier = 0f }, null);

            vendor = em.SpawnEntity("VendingMachineCola", new EntityCoordinates(grid, 20.5f, 2.5f));
            var power = em.GetComponent<ApcPowerReceiverComponent>(vendor);
            power.NeedsPower = false;
            power.Powered = true;
            terminal = em.SpawnEntity("WGComputerVehicleShopExpeditionary", new EntityCoordinates(grid, 3.5f, 3.5f));
            em.SpawnEntity("WGVehicleDeliveryPad", new EntityCoordinates(grid, 12.5f, 12.5f));
            // Beside the customers, as its window only opens in reach.
            trader = em.SpawnEntity("WGLupercalQuartermaster", map.GridCoords.Offset(new Vector2(1f, 0f)));
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var offShip = em.SpawnEntity(null, new MapCoordinates(new Vector2(500, 500), map.MapId));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(services.Refuses(vendor, barred, out var reason), Is.True, "The barred are refused aboard.");
                Assert.That(reason, Does.Contain(em.GetComponent<MetaDataComponent>(grid).EntityName), "The refusal names the ship.");
                Assert.That(services.Refuses(vendor, stranger, out _), Is.False, "A stranger is not.");
                Assert.That(services.Refuses(offShip, barred, out _), Is.False, "Nobody is refused off a commanded ship.");
            }

            // Vending: the barred get nothing, even free; a stranger gets a can.
            var comp = em.GetComponent<VendingMachineComponent>(vendor);
            var entry = vending.GetAvailableInventory(vendor, comp).First();
            var stock = entry.Amount;
            uint Stock() => vending.GetAllInventory(vendor, comp).First(e => e.ID == entry.ID && e.Type == entry.Type).Amount;
            vending.AuthorizedVend(vendor, barred, entry.Type, entry.ID, comp);
            Assert.That(Stock(), Is.EqualTo(stock), "Nothing vended for the barred.");
            vending.AuthorizedVend(vendor, stranger, entry.Type, entry.ID, comp);
            Assert.That(Stock(), Is.EqualTo(stock - 1), "A stranger is served.");

            // The vehicle terminal delivers nothing to the barred.
            em.EventBus.RaiseLocalEvent(terminal, new WGVehicleShopBuyMessage(van) { Actor = barred });
            var terminalEnt = (terminal, em.GetComponent<WGVehicleShopComponent>(terminal));
            var (_, before, _) = shop.InspectPad(terminalEnt);
            Assert.That(before, Is.EqualTo(WGVehicleShopPad.Clear), "No van for the barred.");
            em.EventBus.RaiseLocalEvent(terminal, new WGVehicleShopBuyMessage(van) { Actor = stranger });
            var (_, after, _) = shop.InspectPad(terminalEnt);
            Assert.That(after, Is.EqualTo(WGVehicleShopPad.Vehicle), "A stranger's van is delivered.");

            // A trader won't talk to the barred, and stops talking to someone barred partway through.
            var dealer = (trader, em.GetComponent<TraderComponent>(trader));
            Assert.That(traders.TryStartConversation(dealer, barred), Is.False, "The barred are turned away at the counter.");
            Assert.That(dealer.Item2.Customer, Is.Null);
            Assert.That(traders.TryStartConversation(dealer, stranger), Is.True, "A stranger is served.");
            Assert.That(keys.TryGetKey(strangerCard, out var strangerKey), Is.True);
            command.AddPerson(ship, WGShipCommandList.Barred, strangerKey, null, "Random Stranger");
            em.EventBus.RaiseLocalEvent(trader, new TraderDialogueSelectMessage(0) { Actor = stranger, UiKey = TraderUiKey.Dialogue });
            Assert.That(dealer.Item2.Customer, Is.Null, "Barring a customer ends the conversation at their next choice.");
        });

        await pair.CleanReturnAsync();
    }
}
