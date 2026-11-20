using Content.Server.LandMines;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Content.Shared.Item;
using Content.Shared.Prototypes;
using Content.Shared.Storage.Components;
using Content.Shared.Store;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.SpecialistKits;

/// <summary>Kits and store listings hand out only carryable items, so never a live mine that arms under the player.</summary>
[TestFixture]
public sealed class WGKitMineTest
{
    [Test]
    public async Task NoLiveMinesHandedOut()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            bool IsMine(EntProtoId id) => protos.Index(id).HasComponent<LandMineComponent>(factory);

            foreach (var kit in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!kit.HasComponent<WGSpecialistKitComponent>(factory)
                    || !kit.TryComp<CMVendorBundleComponent>(out var bundle, factory))
                    continue;

                foreach (var id in bundle.Bundle)
                {
                    Assert.That(protos.Index(id).HasComponent<ItemComponent>(factory), Is.True,
                        $"{kit.ID} hands out {id}, which can't be picked up.");
                }
            }

            foreach (var listing in protos.EnumeratePrototypes<ListingPrototype>())
            {
                if (listing.ProductEntity is not { } product)
                    continue;

                Assert.That(IsMine(product), Is.False, $"{listing.ID} sells the live mine {product}.");

                if (!protos.Index(product).TryComp<StorageFillComponent>(out var fill, factory))
                    continue;

                foreach (var entry in fill.Contents)
                {
                    if (entry.PrototypeId is { } content)
                        Assert.That(IsMine(content), Is.False, $"{listing.ID} sells {product}, which holds the live mine {content}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
