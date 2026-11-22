using Content.Server._WG.ShipTiers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipTiers;

/// <summary>A capital admitted but not yet deeded holds its faction's slot, so no second one slips in meanwhile.</summary>
[TestFixture]
public sealed class WGCapitalApprovalTest
{
    [Test]
    public async Task PendingCapitalHoldsTheSlot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var tiers = entMan.System<WGShipTierSystem>();
            var first = entMan.SpawnEntity(null, MapCoordinates.Nullspace);
            var second = entMan.SpawnEntity(null, MapCoordinates.Nullspace);

            // With nobody online the faction has one slot.
            Assert.That(tiers.TryApproveCapital(first, "Luda", "TSF", true, out _), Is.True);
            Assert.That(tiers.TryApproveCapital(second, "Luda", "TSF", true, out var reason), Is.False,
                "a second capital was admitted while the first still held the slot.");
            Assert.That(reason, Is.Not.Empty);

            Assert.That(tiers.TryApproveCapital(second, "Luda", "PDV", true, out _), Is.True, "one faction's capital took another's slot.");
            tiers.ReleaseCapital(second);
            Assert.That(tiers.TryApproveCapital(second, "WGNotACapital", "TSF", true, out _), Is.True, "a non-capital was capped.");
            Assert.That(tiers.TryApproveCapital(second, "Luda", null, true, out _), Is.True, "a buyer outside the cap was capped.");

            tiers.ReleaseCapital(first);
            Assert.That(tiers.TryApproveCapital(second, "Luda", "TSF", true, out _), Is.True, "releasing the first didn't free its slot.");

            entMan.DeleteEntity(first);
            entMan.DeleteEntity(second);
        });

        await pair.CleanReturnAsync();
    }
}
