using System.Linq;
using Content.Server._WF.Operations;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Operations;
using Content.Shared._WF.ShipTiers;
using Content.Shared.Store;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Operations;

/// <summary>Operations, their uplink listings and the capital rules agree with each other and with the game's files.</summary>
[TestFixture]
public sealed class WFOperationPrototypeTest
{
    [Test]
    public async Task OperationsAndListingsAgree()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var listings = protos.EnumeratePrototypes<ListingPrototype>()
                .Where(l => l.ProductEvent is WFCallOperationEvent)
                .ToDictionary(l => ((WFCallOperationEvent) l.ProductEvent!).Operation.Id);

            // An abstract flag pushed down from the base once hid every operation; the rest of the test would pass on none.
            Assert.That(protos.EnumeratePrototypes<WFOperationPrototype>().Count(), Is.GreaterThanOrEqualTo(20),
                "The operations didn't load.");

            foreach (var op in protos.EnumeratePrototypes<WFOperationPrototype>())
            {
                Assert.That(op.Grids, Is.Not.Empty, op.ID);
                foreach (var grid in op.Grids.Concat(op.PlanetGrids))
                    Assert.That(resources.ContentFileExists(grid), $"{op.ID}: {grid} is missing");
                Assert.That(op.PlanetChance, Is.InRange(0f, 1f), op.ID);
                Assert.That(op.Reward == 0 || op.FailRefund is >= 0f and <= 1f, op.ID);

                Assert.That(listings.TryGetValue(op.ID, out var listing), $"{op.ID} has no uplink listing");
                var currency = protos.Index<CompanyPrototype>(op.Company).CompanyUplinkCurrency;
                Assert.That(currency, Is.Not.Null, op.ID);
                Assert.That(listing!.Cost.TryGetValue(currency!.Value, out var cost), $"{op.ID} isn't priced in {currency}");
                Assert.That(cost.Int(), Is.EqualTo(op.Stake), $"{op.ID}: the listing must cost the stake");
                Assert.That(listing.Conditions?.OfType<WFOperationReadyCondition>().Any(c => c.Operation == op.ID),
                    Is.True, $"{op.ID}'s listing doesn't check it can be called");
            }

            // The risk ladder: a PvE operation that refunds less on failure pays at least as much for each credit staked.
            var pve = protos.EnumeratePrototypes<WFOperationPrototype>().Where(op => op.Reward > 0 && op.Stake > 0).ToList();
            foreach (var riskier in pve)
            foreach (var safer in pve.Where(op => op.FailRefund > riskier.FailRefund))
            {
                Assert.That((float) (riskier.Stake + riskier.Reward) / riskier.Stake,
                    Is.GreaterThanOrEqualTo((float) (safer.Stake + safer.Reward) / safer.Stake),
                    $"{riskier.ID} refunds less than {safer.ID} but returns less per credit staked");
            }

            var rules = protos.Index<WFShipTiersPrototype>("WFDefault");
            foreach (var capital in rules.Capitals)
                Assert.That(protos.HasIndex<VesselPrototype>(capital), $"capital {capital} isn't a vessel");
        });

        await pair.CleanReturnAsync();
    }
}
