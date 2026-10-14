#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.VehicleShop;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.Server._NF.Bank;
using Content.Server._WF.VehicleShop;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.VehicleShop;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.VehicleShop;

/// <summary>
/// The expeditionary terminal sells a vehicle onto its pad from the buyer's bank, won't deliver onto a busy pad, and buys
/// the vehicle back for its share of the value.
/// </summary>
[TestFixture]
[TestOf(typeof(WFVehicleShopSystem))]
public sealed class VehicleShopTest
{
    private const string Terminal = "WFComputerVehicleShopExpeditionary";
    private const string Catalog = "WFVehicleCatalogExpeditionary";

    [Test]
    public async Task BuyOntoThePadAndSellBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var shop = server.System<WFVehicleShopSystem>();
        EntityUid terminal = default, pad = default, payer = default;
        var catalog = server.ProtoMan.Index<WFVehicleCatalogPrototype>(Catalog);
        var index = catalog.Vehicles.FindIndex(listing => listing.Vehicle == "VehicleBoxVan");
        var price = catalog.Vehicles[index].Price;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            terminal = entMan.SpawnEntity(Terminal, new EntityCoordinates(map.Grid.Owner, 3.5f, 3.5f));
            pad = entMan.SpawnEntity("WFVehicleDeliveryPad", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            server.System<BankSystem>().TryBankDeposit(payer, price * 2, tax: false);
        });
        await server.WaitRunTicks(5);

        // A crate on the pad is in the way, and named; a tool dropped there isn't.
        EntityUid crate = default, tool = default;
        await server.WaitPost(() =>
        {
            crate = entMan.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map.Grid.Owner, 13.5f, 12.5f));
            tool = entMan.SpawnEntity("Wrench", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
        });
        await server.WaitRunTicks(3);
        await server.WaitAssertion(() =>
        {
            var (_, status, thing) = shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
            Assert.That(status, Is.EqualTo(WFVehicleShopPad.Blocked), "A crate on the pad didn't block it.");
            Assert.That(thing, Is.EqualTo(crate), "The pad named the wrong thing in the way.");
            entMan.DeleteEntity(crate);
        });
        await server.WaitRunTicks(2);

        var before = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The box van is not in the expeditionary catalogue.");
            Assert.That(shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal))).Status, Is.EqualTo(WFVehicleShopPad.Clear),
                "A tool lying on the pad blocked it.");
            entMan.DeleteEntity(tool);
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        EntityUid vehicle = default;
        await server.WaitAssertion(() =>
        {
            var (_, status, onPad) = shop.InspectPad((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(status, Is.EqualTo(WFVehicleShopPad.Vehicle), "Nothing was delivered to the pad.");
                Assert.That(onPad is { } delivered && entMan.GetComponent<MetaDataComponent>(delivered).EntityPrototype?.ID == "VehicleBoxVan", Is.True,
                    "The pad holds the wrong vehicle.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - price), "The buyer was not charged the listed price.");
            }

            vehicle = onPad!.Value;
            before = entMan.GetComponent<BankAccountComponent>(payer).Balance;

            // A second order can't land on top of the first.
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        var sellPrice = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before), "A busy pad still took an order.");
            sellPrice = shop.SellPrice((terminal, entMan.GetComponent<WFVehicleShopComponent>(terminal)), vehicle);
            Assert.That(sellPrice, Is.InRange(1, price - 1), "A vehicle should sell back for something, and for less than it cost.");
            entMan.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopSellMessage { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(entMan.Deleted(vehicle), Is.True, "The sold vehicle is still there.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.GreaterThan(before), "The seller was not paid.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(payer).Balance, Is.LessThanOrEqualTo(before + sellPrice));
            }
        });

        // The window previews each vehicle by spawning it on the client.
        var previews = new List<EntityUid>();
        await pair.Client.WaitPost(() =>
        {
            foreach (var listing in catalog.Vehicles)
                previews.Add(pair.Client.EntMan.Spawn(listing.Vehicle));
        });
        await pair.RunTicksSync(5);
        await pair.Client.WaitPost(() =>
        {
            foreach (var preview in previews)
                pair.Client.EntMan.DeleteEntity(preview);
        });

        // The window builds a row per vehicle and offers only what the buyer can afford.
        await pair.Client.WaitAssertion(() =>
        {
            var window = new WFVehicleShopWindow();
            var listings = new List<WFVehicleListing>(catalog.Vehicles);
            var cheapest = listings.Min(listing => listing.Price);
            window.UpdateState(new WFVehicleShopState(listings, cheapest, WFVehicleShopPad.Clear, null, 0, false));
            window.OpenCentered();

            var buttons = window.FindControl<BoxContainer>("Listings").Children
                .SelectMany(row => row.Children.SelectMany(contents => contents.Children))
                .OfType<Button>()
                .ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons, Has.Count.EqualTo(listings.Count), "The window should have a buy button per vehicle.");
                Assert.That(buttons.Count(button => !button.Disabled), Is.EqualTo(listings.Count(listing => listing.Price <= cheapest)),
                    "Only what the buyer can afford should be offered.");
            }

            window.Close();
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every vehicle a catalogue lists exists and costs more than it appraises for, so none can be bought to sell at a profit.</summary>
    [Test]
    public async Task CataloguesNeverSellBelowValue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var pricing = server.System<Content.Server.Cargo.Systems.PricingSystem>();
            using (Assert.EnterMultipleScope())
            {
                foreach (var catalog in server.ProtoMan.EnumeratePrototypes<WFVehicleCatalogPrototype>())
                foreach (var listing in catalog.Vehicles)
                {
                    Assert.That(server.ProtoMan.TryIndex<EntityPrototype>(listing.Vehicle, out var proto), Is.True, $"{catalog.ID} lists {listing.Vehicle}, which doesn't exist.");
                    if (proto == null)
                        continue;

                    Assert.That(listing.Price, Is.GreaterThan(pricing.GetEstimatedPrice(proto)),
                        $"{catalog.ID} sells {listing.Vehicle} for no more than it appraises for.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
