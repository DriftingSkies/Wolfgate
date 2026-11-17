#nullable enable
using Content.Server._WG.ShipCommand;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>A stack bought free keeps its discount when merged into a stack bought at full price, either way round.</summary>
[TestFixture]
[TestOf(typeof(WGShipServiceSystem))]
public sealed class WGBargainStackTest
{
    private const string Sheet = "SheetSteel";

    [TestCase(true)]
    [TestCase(false)]
    public async Task MergingKeepsTheDiscount(bool bargainGives)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var pricing = server.System<PricingSystem>();
        var stacks = server.System<StackSystem>();

        await server.WaitAssertion(() =>
        {
            var user = em.SpawnEntity("MobHuman", map.GridCoords);
            var full = em.SpawnEntity(Sheet, map.GridCoords);
            var free = em.SpawnEntity(Sheet, map.GridCoords);
            stacks.SetCount(full, 10);
            stacks.SetCount(free, 10);
            var paid = pricing.GetPrice(full);
            server.System<WGShipServiceSystem>().MarkBargainGoods(free, pricing.GetPrice(free), 0);
            Assert.That(pricing.GetPrice(free), Is.EqualTo(0).Within(0.01), "A free stack resells for nothing.");

            // The clicked stack gives its sheets to the one in hand.
            var (donor, recipient) = bargainGives ? (free, full) : (full, free);
            em.EventBus.RaiseLocalEvent(donor, new InteractUsingEvent(user, recipient, donor, map.GridCoords));
            Assert.That(em.GetComponent<StackComponent>(recipient).Count, Is.EqualTo(20), "The stacks merged.");

            var total = pricing.GetPrice(recipient) + (em.Deleted(donor) ? 0 : pricing.GetPrice(donor));
            Assert.That(total, Is.EqualTo(paid).Within(0.01), "Merging changed what the sheets resell for.");

            em.DeleteEntity(user);
            em.DeleteEntity(recipient);
            if (!em.Deleted(donor))
                em.DeleteEntity(donor);
        });

        await pair.CleanReturnAsync();
    }
}
