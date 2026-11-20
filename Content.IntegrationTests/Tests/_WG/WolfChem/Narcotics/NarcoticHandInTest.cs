#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Shared._NF.Contraband.Events;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// A contraband pallet pays for the street value of all the narcotics on it together: packing a batch small earns
/// nothing more and nothing less, and every package is taken.
/// </summary>
[TestFixture]
[TestOf(typeof(WGNarcoticGoodsSystem))]
public sealed class NarcoticHandInTest
{
    // A cocaine baggie is 10,000 of street value and a point is 15,000.
    [TestCase(1, 0, TestName = "OneBaggiePaysNothingAndIsTaken")]
    [TestCase(3, 2, TestName = "ThreeBaggiesPayTwoPoints")]
    [TestCase(9, 6, TestName = "NineBaggiesPaySixPoints")]
    public async Task BaggiesHandInTogether(int baggies, int points)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var handedIn = new List<EntityUid>();
        var console = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var pallet = entMan.SpawnEntity("ContrabandPallet", map.GridCoords);
            Assert.That(entMan.GetComponent<TransformComponent>(pallet).Anchored, "The pallet needs to be anchored to sell from.");
            for (var i = 0; i < baggies; i++)
            {
                handedIn.Add(entMan.SpawnEntity("WGBaggieCocaine", map.GridCoords));
            }

            console = entMan.SpawnEntity("ComputerContrabandPalletConsole", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(console, new ContrabandPalletSellMessage { Actor = console });
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var paid = 0;
            var query = entMan.EntityQueryEnumerator<StackComponent, TransformComponent>();
            while (query.MoveNext(out _, out var stack, out var xform))
            {
                if (xform.MapID == map.MapId && stack.StackTypeId == "FederationMilitaryCredit")
                    paid += stack.Count;
            }

            Assert.Multiple(() =>
            {
                Assert.That(handedIn.All(baggie => entMan.Deleted(baggie)), Is.True, "The pallet left a package it was paid for or counted.");
                Assert.That(paid, Is.EqualTo(points), "The pallet didn't pay for the baggies' street value together.");
            });
        });

        await pair.CleanReturnAsync();
    }
}
