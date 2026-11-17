#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.Drydock;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Drydock;
using Content.Server._WG.ShipCommand;
using Content.Server._WG.Traders;
using Content.Server._WG.VehicleShop;
using Content.Server.Cargo.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.Traders;
using Content.Shared._WG.VehicleShop;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// What a commanded ship's services sell below the list price resells for no more than was paid: a tank cannon and a
/// humvee from the Lupercal's vehicle terminal and goods from its apothecary, all served free to the owner, sell for
/// nothing anywhere, parts and all; a ship bought at a bargain breaks at a chop shop for its share of what was paid; and a
/// used-lot copy loses the mark.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipServiceSystem))]
public sealed class WGBargainTest
{
    private const string Terminal = "WGComputerVehicleShopLupercal";
    private const string Part = "VehicleTankLTBCannon";
    private const string Vehicle = "WGMilitaryChassisHumvee";
    private const string Apothecary = "WGLupercalApothecary";

    [Test]
    public async Task FreeGoodsResellForNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var grid = map.Grid.Owner;
        var pricing = server.System<PricingSystem>();
        var shop = server.System<WGVehicleShopSystem>();
        var counter = server.System<TraderShopSystem>();
        EntityUid terminal = default, payer = default, trader = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            // A grid with no deed counts the buyer as its owner, who is served free.
            var command = em.EnsureComponent<WGShipCommandComponent>(grid);
            command.OwnerServedFree = command.AdminsServedFree = true;
            terminal = em.SpawnEntity(Terminal, new EntityCoordinates(grid, 3.5f, 3.5f));
            em.SpawnEntity("WGVehicleDeliveryPad", new EntityCoordinates(grid, 12.5f, 12.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            var licence = em.SpawnEntity("WGHeavyVehicleLicence", em.GetComponent<TransformComponent>(payer).Coordinates);
            server.System<SharedHandsSystem>().PickupOrDrop(payer, licence);
            trader = em.SpawnEntity(Apothecary, map.GridCoords.Offset(new Vector2(1f, 0f)));
        });
        await server.WaitRunTicks(5);

        var before = 0;
        await server.WaitAssertion(() =>
        {
            var listings = shop.Listings((terminal, em.GetComponent<WGVehicleShopComponent>(terminal)));
            var part = listings.FindIndex(listing => listing.Vehicle == Part);
            var vehicle = listings.FindIndex(listing => listing.Vehicle == Vehicle);
            Assert.That(part, Is.GreaterThanOrEqualTo(0), $"The Lupercal's terminal doesn't sell the {Part}.");
            Assert.That(vehicle, Is.GreaterThanOrEqualTo(0), $"The Lupercal's terminal doesn't sell the {Vehicle}.");
            Assert.That(shop.IsCarried(listings[part].Vehicle), Is.True, "The cannon is handed over the counter.");

            before = em.GetComponent<BankAccountComponent>(payer).Balance;
            em.EventBus.RaiseLocalEvent(terminal, new WGVehicleShopBuyMessage(part) { Actor = payer });
            em.EventBus.RaiseLocalEvent(terminal, new WGVehicleShopBuyMessage(vehicle) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        EntityUid humvee = default;
        await server.WaitAssertion(() =>
        {
            var ent = (terminal, em.GetComponent<WGVehicleShopComponent>(terminal));
            var (_, status, onPad) = shop.InspectPad(ent);
            Assert.That(status, Is.EqualTo(WGVehicleShopPad.Vehicle), "The humvee was delivered.");
            humvee = onPad!.Value;
            Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before), "The owner was served free.");

            using (Assert.EnterMultipleScope())
            {
                ResellsForNothing(em, pricing, grid, OfProto(em, map.MapUid, Part).Single(), "The cannon");
                ResellsForNothing(em, pricing, grid, humvee, "The humvee");
                var tree = Tree(em, humvee).ToHashSet();
                Assert.That(pricing.AppraiseGrid(grid, tree.Contains), Is.EqualTo(0).Within(0.01), "The humvee adds nothing to its ship's appraisal.");
                Assert.That(shop.SellPrice(ent, humvee), Is.Zero, "The terminal buys the humvee back for nothing.");
            }

            em.EventBus.RaiseLocalEvent(terminal, new WGVehicleShopSellMessage { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(humvee), Is.True, "The humvee was sold back.");
            Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before), "Selling it back paid nothing.");

            // The apothecary, aboard the same ship, serves the owner free too: her dearest line, two of it.
            var shopEnt = (trader, em.GetComponent<TraderShopComponent>(trader));
            var priced = counter.GetStock(shopEnt).Where(entry => entry.Price > 0).ToList();
            Assert.That(priced, Is.Not.Empty, "The apothecary sells nothing at a price.");
            var line = priced.MaxBy(entry => pricing.GetEstimatedPrice(server.ProtoMan.Index<EntityPrototype>(entry.Item)));

            var existing = OfProto(em, map.MapUid, line.Item).ToHashSet();
            Assert.That(counter.TryCheckout(shopEnt, (trader, em.GetComponent<TraderComponent>(trader)), payer,
                new Dictionary<string, int> { [line.Item] = 2 }), Is.True, "The free checkout went through.");

            var goods = OfProto(em, map.MapUid, line.Item).Where(uid => !existing.Contains(uid)).ToList();
            Assert.That(goods, Has.Count.EqualTo(2), "Both were handed over.");
            Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before), "The owner paid nothing at the counter.");

            using (Assert.EnterMultipleScope())
            {
                foreach (var good in goods)
                    ResellsForNothing(em, pricing, grid, good, line.Item);
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A ship bought at half its list price breaks at a chop shop for its share of the half paid, and for nothing if it
    /// was free; a used-lot copy, stripped for resale, loses the mark.
    /// </summary>
    [Test]
    public async Task BargainShipsBreakForWhatWasPaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var drydock = server.System<WGDrydockSystem>();
        var services = server.System<WGShipServiceSystem>();

        await server.WaitAssertion(() =>
        {
            var ship = map.Grid.Owner;
            em.AddComponent<VesselComponent>(ship).VesselId = "Guppy";
            var list = server.ProtoMan.Index<VesselPrototype>("Guppy").Price;
            var chop = new WGDrydockChopShopComponent();
            Assert.That(drydock.ChopValue(ship), Is.EqualTo(list), "Bought at the list price, a ship breaks for its share of it.");

            services.MarkBargain(ship, list, list / 2);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(drydock.ChopValue(ship), Is.EqualTo(list / 2), "A bargain is worth what was paid for it.");
                Assert.That(drydock.ChopPayout(chop, ship), Is.LessThanOrEqualTo(list / 2), "Breaking a bargain pays no more than it cost.");
            }

            services.MarkBargain(ship, list, 0);
            Assert.That(drydock.ChopPayout(chop, ship), Is.Zero, "Got free, it breaks for nothing.");

            server.System<ShipyardSystem>().StripForResale(ship);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.HasComponent<WGBargainComponent>(ship), Is.False, "A used-lot copy doesn't keep the seller's bargain.");
                Assert.That(drydock.ChopValue(ship), Is.EqualTo(list));
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Asserts something got free is worth something new, yet resells for nothing, nor do its parts taken out.</summary>
    private static void ResellsForNothing(IEntityManager em, PricingSystem pricing, EntityUid grid, EntityUid bought, string what)
    {
        Assert.That(pricing.GetUndiscountedPrice(bought), Is.GreaterThan(0), $"{what} is worth nothing new, so this proves nothing.");
        Assert.That(pricing.GetPrice(bought), Is.EqualTo(0).Within(0.01), $"{what}, got free, resells for something.");
        Assert.That(pricing.GetPriceWithVendingDiscount(bought, grid), Is.EqualTo(0).Within(0.01), $"{what} sells for something at a cargo pallet.");
        Assert.That(Tree(em, bought).Sum(uid => pricing.GetPrice(uid, includeContents: false)), Is.EqualTo(0).Within(0.01),
            $"{what}'s parts, taken out, sell for something.");
    }

    /// <summary>An entity and everything in its containers.</summary>
    private static List<EntityUid> Tree(IEntityManager em, EntityUid root)
    {
        var found = new List<EntityUid> { root };
        for (var i = 0; i < found.Count; i++)
        {
            if (!em.TryGetComponent<ContainerManagerComponent>(found[i], out var containers))
                continue;

            foreach (var container in containers.Containers.Values)
                found.AddRange(container.ContainedEntities);
        }

        return found;
    }

    /// <summary>Everything of one prototype on a map.</summary>
    private static List<EntityUid> OfProto(IEntityManager em, EntityUid mapUid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.MapUid == mapUid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
    }
}
