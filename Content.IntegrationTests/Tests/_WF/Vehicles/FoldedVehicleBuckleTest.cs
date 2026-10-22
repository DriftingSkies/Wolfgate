using System.Linq;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Foldable;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Vehicles;

/// <summary>A refused buckle onto a folded rideable vehicle leaves the rider's hands free.</summary>
[TestFixture]
public sealed class FoldedVehicleBuckleTest
{
    [Test]
    public async Task FoldedUnicycleLeavesNoVirtualItem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        EntityUid rider = default;
        EntityUid unicycle = default;
        int Blocking() => em.System<SharedHandsSystem>().EnumerateHeld(rider)
            .Count(held => em.TryGetComponent<VirtualItemComponent>(held, out var virt) && virt.BlockingEntity == unicycle);

        await pair.Server.WaitAssertion(() =>
        {
            var buckle = em.System<SharedBuckleSystem>();
            rider = em.SpawnEntity("MobHuman", map.GridCoords);
            unicycle = em.SpawnEntity("VehicleUnicycleFolded", map.GridCoords);

            Assert.That(buckle.TryBuckle(rider, rider, unicycle), Is.False, "A folded unicycle can't be ridden.");
            Assert.That(Blocking(), Is.Zero, "The refused buckle left a virtual item in hand.");

            em.System<FoldableSystem>().SetFolded(unicycle, em.GetComponent<FoldableComponent>(unicycle), false);
            Assert.That(buckle.TryBuckle(rider, rider, unicycle), Is.True, "An unfolded unicycle can be ridden.");
            Assert.That(Blocking(), Is.EqualTo(1), "Riding takes one hand.");

            buckle.Unbuckle((rider, em.GetComponent<BuckleComponent>(rider)), rider);
        });

        // The virtual item is queued for deletion, so look a tick later.
        await pair.RunTicksSync(1);
        await pair.Server.WaitAssertion(() => Assert.That(Blocking(), Is.Zero, "Getting off frees the hand."));

        await pair.CleanReturnAsync();
    }
}
