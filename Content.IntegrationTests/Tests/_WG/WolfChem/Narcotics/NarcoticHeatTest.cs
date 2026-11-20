#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.Radar;
using Content.Server._NF.SectorServices;
using Content.Server._WG.CargoSales;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.Cargo.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Contraband;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Selling narcotics heats the seller alone: a little pays in full and tips security with no detail, a stockpile sold
/// at once pays a fraction, gets them described but never named in a tip, and puts a warrant in their name on the
/// bounty board. Where full price ends differs from seller to seller, inside the fuzz. A ship's stockpile shows on radar, and a narcotic hands in for what it holds.
/// </summary>
[TestFixture]
[TestOf(typeof(WGNarcoticHeatSystem))]
public sealed class NarcoticHeatTest
{
    [Test]
    public async Task HeatCutsThePayoutAndSharpensTheTip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var heat = server.System<WGNarcoticHeatSystem>();
            var goods = server.System<WGNarcoticGoodsSystem>();
            var pricing = server.System<PricingSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var probe = server.System<RadioProbeSystem>();
            var grid = map.Grid.Owner;
            // The bounty board lives on the sector service; a test map hosts one if the server has none.
            if (!entMan.EntityExists(server.System<SectorServiceSystem>().GetServiceEntity()))
                entMan.AddComponent<StationSectorServiceHostComponent>(map.MapUid);
            var pad = new EntityCoordinates(grid, 0.5f, 0.5f);
            var pallet = entMan.SpawnEntity("CargoPalletSell", pad);
            var xform = entMan.GetComponent<TransformComponent>(pallet);
            if (!xform.Anchored)
                server.System<SharedTransformSystem>().AnchorEntity((pallet, xform));

            var console = entMan.SpawnEntity("ComputerPalletConsole", pad);
            var seller = entMan.SpawnEntity("MobHuman", pad);
            var bystander = entMan.SpawnEntity("MobHuman", pad);

            // Takes the cash a sale paid out off the grid, so the next sale doesn't sell it on.
            int TakeCash()
            {
                var total = 0;
                var paid = new List<EntityUid>();
                var query = entMan.EntityQueryEnumerator<CashComponent, StackComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var stack, out var cashXform))
                {
                    if (cashXform.GridUid != grid)
                        continue;

                    total += stack.Count;
                    paid.Add(uid);
                }

                foreach (var uid in paid)
                {
                    entMan.DeleteEntity(uid);
                }

                return total;
            }

            // A tip is one line on each of the law's channels.
            var channels = WGNarcoticHeatSystem.TipChannels.Select(channel => channel.Id).ToList();
            int Tips() => probe.Sent.Count(sent => channels.Contains(sent.Channel));

            // A couple of doses: full price, a cold seller, a tip with nothing in it.
            var rock = entMan.SpawnEntity("WGCrackRock", pad);
            var baggie = entMan.SpawnEntity("WGBaggieHeroin", pad);
            var small = goods.StreetValue(rock) + goods.StreetValue(baggie);
            var smallPrice = pricing.GetPrice(rock) + pricing.GetPrice(baggie);
            TakeCash();
            probe.Sent.Clear();
            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });

            var paid = TakeCash();
            Assert.Multiple(() =>
            {
                Assert.That(small, Is.GreaterThan(0));
                Assert.That(entMan.EntityExists(rock) || entMan.EntityExists(baggie), Is.False, "The drugs should have sold.");
                Assert.That(paid, Is.EqualTo(smallPrice).Within(1), "A cold seller is paid in full.");
                Assert.That(heat.Heat(seller), Is.EqualTo(small).Within(1));
                Assert.That(heat.Tier(seller), Is.EqualTo(WGNarcoticHeatTier.Cold));
                Assert.That(heat.Heat(bystander), Is.Zero, "Heat is the seller's alone.");
                Assert.That(heat.Warrant(seller), Is.Null, "A seller under the line has no warrant.");
                Assert.That(channels, Is.EquivalentTo(new[] { "Security", "Nfsd", "Freelance" }));
                Assert.That(Tips(), Is.EqualTo(channels.Count), "The sale should be radioed to Caelestinus Security, the TSF and the PDV.");
            });

            // Where full price ends is each seller's own, somewhere inside the fuzz.
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var low = cfg.GetCVar(WGNarcoticsCVars.HeatFree) * (1 - cfg.GetCVar(WGNarcoticsCVars.HeatFuzz));
            var high = cfg.GetCVar(WGNarcoticsCVars.HeatFree) * (1 + cfg.GetCVar(WGNarcoticsCVars.HeatFuzz)) + 10_000;
            Assert.Multiple(() =>
            {
                Assert.That(heat.Payout(bystander, low), Is.EqualTo(low).Within(1), "Under the fuzz everyone is paid in full.");
                Assert.That(heat.Payout(bystander, high), Is.LessThan(high - 1), "Past the fuzz nobody is.");
                Assert.That(heat.Payout(bystander, high), Is.EqualTo(heat.Payout(bystander, high)), "A look doesn't move the line.");
            });

            // A tank of cocaine: most of it goes cheap, and the console says so before it is sold.
            var tank = entMan.SpawnEntity("WaterTank", pad);
            Assert.That(solutions.TryGetSolution(tank, "tank", out var soln, out _));
            solutions.TryAddReagent(soln!.Value, "WGCocaine", FixedPoint2.New(1500));
            var value = goods.StreetValue(tank);
            var cut = value - heat.Payout(seller, value);
            var full = pricing.GetPrice(tank);

            var appraisal = new WGPalletAppraisalEvent(new HashSet<EntityUid> { tank }, grid, console, seller, 1);
            entMan.EventBus.RaiseEvent(EventSource.Local, ref appraisal);

            probe.Sent.Clear();
            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            paid = TakeCash();

            var name = entMan.GetComponent<MetaDataComponent>(seller).EntityName;
            var warrant = heat.Warrant(seller);
            var cold = heat.TipLine(seller, console, grid, WGNarcoticHeatTier.Cold);
            var hot = heat.TipLine(seller, console, grid, WGNarcoticHeatTier.Hot);
            Assert.Multiple(() =>
            {
                Assert.That(value, Is.EqualTo(1_500_000).Within(1));
                Assert.That(cut, Is.GreaterThan(value * 0.4), "A stockpile sold at once should lose much of its worth.");
                Assert.That(appraisal.Cut, Is.EqualTo(cut).Within(1), "The console shows what the sale will pay.");
                Assert.That(entMan.EntityExists(tank), Is.False, "The tank should have sold.");
                Assert.That(paid, Is.EqualTo(full - cut).Within(2));
                Assert.That(heat.Tier(seller), Is.EqualTo(WGNarcoticHeatTier.Hot));
                Assert.That(heat.Heat(bystander), Is.Zero);
                Assert.That(Tips(), Is.EqualTo(channels.Count), "A hotter seller is told on again inside the cooldown.");
                Assert.That(hot, Is.Not.EqualTo(cold));
                Assert.That(hot, Does.Not.Contain(name), "A tip never names the seller.");
                Assert.That(cold, Does.Not.Contain(name));
                Assert.That(warrant, Is.Not.Null, "Selling past the hot line should post a warrant.");
                Assert.That(warrant?.Name, Is.EqualTo(name), "The warrant is in the seller's name.");
                Assert.That(warrant?.Reward, Is.EqualTo((int) cfg.GetCVar(WGNarcoticsCVars.WarrantCap)),
                    "A dump this size fills the warrant.");
                Assert.That(heat.Warrant(bystander), Is.Null);
            });

            // A third sale straight after adds no tip: security has heard, and the seller is no hotter.
            entMan.SpawnEntity("WGCrackRock", pad);
            probe.Sent.Clear();
            entMan.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage { Actor = seller });
            Assert.Multiple(() =>
            {
                Assert.That(Tips(), Is.Zero);
                Assert.That(heat.Warrant(seller)?.ContractId, Is.EqualTo(warrant?.ContractId), "A further sale adds to the one warrant.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AStockpileShowsOnRadarAndHandsInForItsWorth()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var stock = server.System<WGNarcoticStockSystem>();
            var goods = server.System<WGNarcoticGoodsSystem>();
            var solutions = server.System<SharedSolutionContainerSystem>();
            var ship = map.Grid.Owner;
            entMan.EnsureComponent<ShuttleDeedComponent>(ship);

            var jug = entMan.SpawnEntity("Jug", map.GridCoords);
            Assert.That(solutions.TryGetSolution(jug, "beaker", out var jugSoln, out _));
            solutions.TryAddReagent(jugSoln!.Value, "WGCocaine", FixedPoint2.New(100));
            stock.Scan();
            Assert.Multiple(() =>
            {
                Assert.That(stock.Stock(ship), Is.EqualTo(100_000).Within(1));
                Assert.That(entMan.HasComponent<WGNarcoticSignatureComponent>(jug), Is.False, "A small stash stays off the radar.");
            });

            var tank = entMan.SpawnEntity("WaterTank", map.GridCoords);
            Assert.That(solutions.TryGetSolution(tank, "tank", out var tankSoln, out _));
            solutions.TryAddReagent(tankSoln!.Value, "WGCocaine", FixedPoint2.New(300));
            var flat = entMan.GetComponent<ContrabandComponent>(tank).TurnInValues["FederationMilitaryCredit"];
            var pool = 0.0;
            var taken = new HashSet<EntityUid>();
            var points = goods.TurnIn(tank, flat, ref pool, taken);
            stock.Scan();
            Assert.Multiple(() =>
            {
                Assert.That(stock.Stock(ship), Is.EqualTo(400_000).Within(1));
                Assert.That(entMan.HasComponent<WGNarcoticSignatureComponent>(tank), Is.True, "The largest lot shows the stockpile.");
                Assert.That(entMan.HasComponent<RadarBlipComponent>(tank), Is.True);
                Assert.That(entMan.HasComponent<WGNarcoticSignatureComponent>(jug), Is.False);
                Assert.That(flat, Is.Zero, "A narcotic has a flat hand-in reward, so a batch split into doses pays per dose.");
                Assert.That(points, Is.EqualTo(20), "300,000 of cocaine hands in for 20 points.");
                Assert.That(taken, Does.Contain(tank), "The pallet doesn't take the tank it paid for.");
            });

            solutions.RemoveAllSolution(tankSoln.Value);
            stock.Scan();
            taken.Clear();
            var clean = goods.TurnIn(tank, 5, ref pool, taken);
            Assert.Multiple(() =>
            {
                Assert.That(stock.Stock(ship), Is.EqualTo(100_000).Within(1));
                Assert.That(entMan.HasComponent<WGNarcoticSignatureComponent>(tank), Is.False, "Poured out, the signature goes.");
                Assert.That(entMan.HasComponent<RadarBlipComponent>(tank), Is.False);
                Assert.That(clean, Is.EqualTo(5), "A clean tank hands in for more than its own reward.");
                Assert.That(taken, Is.Empty, "The narcotics hand-in takes a clean tank.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
