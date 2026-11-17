#nullable enable
using Content.Shared._Mono.Store;
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.Cargo;
using Content.Server._Mono.Store.Components;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._WG.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.TradeGoods;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.CargoSales;

/// <summary>
/// A pallet over a MaxPerSale limit is refused whole, appraising a pallet changes nothing on the goods, and taxed
/// goods pay their tax inside a container.
/// </summary>
[TestFixture]
[TestOf(typeof(CargoSystem))]
public sealed class SaleLimitTest
{
    private const string Limited = "FentanylSolidified"; // maxPerSale: 100
    private const int Limit = 100;

    private sealed record Setup(EntityUid Console, EntityUid Grid, EntityCoordinates Pad);

    private static Setup Build(IEntityManager entMan, TestMapData map)
    {
        var pad = new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f);
        var pallet = entMan.SpawnEntity("CargoPalletSell", pad);
        var xform = entMan.GetComponent<TransformComponent>(pallet);
        if (!xform.Anchored)
            entMan.System<SharedTransformSystem>().AnchorEntity((pallet, xform));

        var console = entMan.SpawnEntity("ComputerPalletConsole", pad);
        return new Setup(console, map.Grid.Owner, pad);
    }

    private static void Sell(IEntityManager entMan, Setup setup)
    {
        entMan.EventBus.RaiseLocalEvent(setup.Console, new CargoPalletSellMessage());
    }

    private static int Stacks(IEntityManager entMan)
    {
        return entMan.Count<StackComponent>();
    }

    [Test]
    public async Task OverLimitPalletIsRefusedWhole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var market = entMan.System<WGTradeMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var goods = Enumerable.Range(0, Limit + 1).Select(_ => entMan.SpawnEntity(Limited, setup.Pad)).ToList();
            var other = entMan.SpawnEntity("CrateTradeSecureHigh", setup.Pad);
            var good = entMan.GetComponent<WGTradeCrateMarketComponent>(other).Good;
            var pressure = market.Pressure(setup.Grid, good);
            var stacks = Stacks(entMan);

            Sell(entMan, setup);

            Assert.Multiple(() =>
            {
                Assert.That(goods.All(entMan.EntityExists), "A refused sale must keep the goods.");
                Assert.That(entMan.EntityExists(other), "A refused sale must keep the other cargo too.");
                Assert.That(Stacks(entMan), Is.EqualTo(stacks), "A refused sale must not pay.");
                Assert.That(market.Pressure(setup.Grid, good), Is.EqualTo(pressure), "A refused sale must not count as sold.");
            });

            // At the limit it goes through.
            entMan.DeleteEntity(goods[^1]);
            goods.RemoveAt(goods.Count - 1);
            Sell(entMan, setup);

            Assert.Multiple(() =>
            {
                Assert.That(goods.Any(entMan.EntityExists), Is.False, "A sale at the limit must go through.");
                Assert.That(Stacks(entMan), Is.GreaterThan(stacks), "A sale at the limit must pay.");
                Assert.That(market.Pressure(setup.Grid, good), Is.GreaterThan(pressure), "A sale must count as sold.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GoodsInsideContainersCount()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var storage = entMan.System<SharedEntityStorageSystem>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var crate = entMan.SpawnEntity("CrateGenericSteel", setup.Pad);
            for (var i = 0; i <= Limit; i++)
            {
                var item = entMan.SpawnEntity(Limited, setup.Pad);
                Assert.That(storage.Insert(item, crate), "The crate should take the crystal.");
            }

            Sell(entMan, setup);
            Assert.That(entMan.EntityExists(crate), "A crate over the limit must be refused.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A taxed good pays the same tax inside an untaxed crate as it does loose.</summary>
    [Test]
    public async Task TaxedGoodsInsideContainersPayTax()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var storage = entMan.System<SharedEntityStorageSystem>();
        var bank = entMan.System<BankSystem>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            // The sector bank only exists once a station hosts the sector services.
            var host = entMan.SpawnEntity(null, setup.Pad.Offset(new System.Numerics.Vector2(5f, 5f)));
            entMan.AddComponent<StationSectorServiceHostComponent>(host);

            int BlackMarket()
            {
                Assert.That(bank.TryGetBalance(SectorBankAccount.BlackMarket, out var balance));
                return balance;
            }

            var start = BlackMarket();
            entMan.SpawnEntity(Limited, setup.Pad);
            Sell(entMan, setup);
            var loose = BlackMarket() - start;
            Assert.That(loose, Is.GreaterThan(0), "A loose crystal pays the black market its share.");

            var crate = entMan.SpawnEntity("CrateGenericSteel", setup.Pad);
            Assert.That(storage.Insert(entMan.SpawnEntity(Limited, setup.Pad), crate));
            Sell(entMan, setup);
            Assert.That(entMan.EntityExists(crate), Is.False);
            Assert.That(BlackMarket() - start - loose, Is.EqualTo(loose), "A crated one pays the same.");
            entMan.DeleteEntity(host);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleShowsNoPayoutForARefusedPallet()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        var ui = entMan.System<SharedUserInterfaceSystem>();

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var goods = Enumerable.Range(0, Limit + 1).Select(_ => entMan.SpawnEntity(Limited, setup.Pad)).ToList();

            entMan.EventBus.RaiseLocalEvent(setup.Console, new CargoPalletAppraiseMessage());
            Assert.That(ui.TryGetUiState<CargoPalletConsoleInterfaceState>(setup.Console, CargoPalletConsoleUiKey.Sale, out var refused));
            Assert.That(refused!.Appraisal, Is.Zero);

            entMan.DeleteEntity(goods[^1]);
            entMan.EventBus.RaiseLocalEvent(setup.Console, new CargoPalletAppraiseMessage());
            Assert.That(ui.TryGetUiState<CargoPalletConsoleInterfaceState>(setup.Console, CargoPalletConsoleUiKey.Sale, out var allowed));
            Assert.That(allowed!.Appraisal, Is.GreaterThan(0));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Whichever entity comes first, a group uses its lowest limit.</summary>
    [Test]
    public async Task GroupUsesItsLowestLimit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var goods = new List<EntityUid>();
            foreach (var limit in new[] { 0, 1, 0, 5 })
            {
                var item = entMan.SpawnEntity(Limited, setup.Pad);
                var alert = entMan.GetComponent<SoldAlertComponent>(item);
                alert.GroupId = "WGTestGroup";
                alert.MaxPerSale = limit;
                goods.Add(item);
            }

            Sell(entMan, setup);
            Assert.That(goods.All(entMan.EntityExists), "Four items are over the group's lowest limit of 1.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Pricing a pallet used to add the other items' injections to the first item's own amounts.</summary>
    [Test]
    public async Task AppraisingLeavesCurrencyInjectionsAlone()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var setup = Build(entMan, map);
            var comps = new List<CurrencyInjectionOnSellComponent>();
            for (var i = 0; i < 3; i++)
            {
                var item = entMan.SpawnEntity(Limited, setup.Pad);
                var comp = entMan.AddComponent<CurrencyInjectionOnSellComponent>(item);
                comp.Injection = new CurrencyInjection
                {
                    Company = "PDV",
                    Amount = new Dictionary<string, FixedPoint2> { ["Doubloon"] = 10 },
                };
                comps.Add(comp);
            }

            for (var i = 0; i < 3; i++)
            {
                entMan.EventBus.RaiseLocalEvent(setup.Console, new CargoPalletAppraiseMessage());
            }

            foreach (var comp in comps)
            {
                Assert.That(comp.Injection.Amount["Doubloon"], Is.EqualTo(FixedPoint2.New(10)));
            }
        });

        await pair.CleanReturnAsync();
    }
}
