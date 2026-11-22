using System.Collections.Generic;
using Content.Server._Mono.Store;
using Content.Shared.FixedPoint;
using Content.Shared.Store.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Operations;

/// <summary>A faction payout lands once, at one uplink, however many uplinks the faction has.</summary>
[TestFixture]
public sealed class WGInjectCurrencyOnceTest
{
    private const string Currency = "FederationMilitaryCredit";

    [Test]
    public async Task PaysOneUplinkOnly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var currency = entMan.System<CurrencyInjectionSystem>();
            var first = entMan.SpawnEntity("VendingMachineTSFMCUplink", map.GridCoords);
            var second = entMan.SpawnEntity("VendingMachineTSFMCUplink", map.GridCoords);
            FixedPoint2 Balance(EntityUid uid) =>
                entMan.GetComponent<StoreComponent>(uid).Balance.GetValueOrDefault(Currency);
            var before = Balance(first) + Balance(second);
            var secondBefore = Balance(second);

            var paid = currency.InjectCurrencyOnce("TSF", new Dictionary<string, FixedPoint2> { [Currency] = 50 }, second);
            Assert.That(paid, Is.EqualTo(second), "the preferred uplink wasn't paid.");
            Assert.That(Balance(second) - secondBefore, Is.EqualTo(FixedPoint2.New(50)));
            Assert.That(Balance(first) + Balance(second) - before, Is.EqualTo(FixedPoint2.New(50)), "the payout landed twice.");

            currency.InjectCurrencyOnce("TSF", new Dictionary<string, FixedPoint2> { [Currency] = 10 });
            Assert.That(Balance(first) + Balance(second) - before, Is.EqualTo(FixedPoint2.New(60)), "a payout with no uplink named landed twice.");
        });

        await pair.CleanReturnAsync();
    }
}
