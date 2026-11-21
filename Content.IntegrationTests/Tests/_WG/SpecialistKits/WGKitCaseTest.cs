using System.Collections.Generic;
using System.Linq;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Content.Shared.Prototypes;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.SpecialistKits;

/// <summary>Every kit case holds all of its gear, takes nothing back, and is gone once emptied; the racks offer every kit.</summary>
[TestFixture]
public sealed class WGKitCaseTest
{
    [Test]
    public async Task CasesHoldTheirGearAndTakeNothingBack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var entMan = server.EntMan;
        var factory = server.ResolveDependency<IComponentFactory>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var storage = entMan.System<SharedStorageSystem>();
            var containers = entMan.System<SharedContainerSystem>();
            var coords = new MapCoordinates(0, 0, map.MapId);
            var kits = 0;
            foreach (var kit in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (kit.Abstract
                    || !kit.HasComponent<WGSpecialistKitComponent>(factory)
                    || !kit.TryComp<StorageFillComponent>(out var fill, factory))
                    continue;

                kits++;
                var expected = fill.Contents.Sum(e => e.Amount);
                var uid = entMan.SpawnEntity(kit.ID, coords);
                var comp = entMan.GetComponent<StorageComponent>(uid);
                Assert.That(comp.Container.ContainedEntities, Has.Count.EqualTo(expected),
                    $"{kit.ID} holds {comp.Container.ContainedEntities.Count} of its {expected} items; widen its grid.");
                Assert.That(entMan.GetComponent<WGTakeOnlyStorageComponent>(uid).Sealed, Is.True, $"{kit.ID} never sealed.");

                var spare = entMan.SpawnEntity("Wirecutter", coords);
                Assert.That(storage.CanInsert(uid, spare, out _), Is.False, $"{kit.ID} took an item back in.");
                entMan.DeleteEntity(spare);

                foreach (var item in comp.Container.ContainedEntities.ToList())
                {
                    containers.Remove(item, comp.Container);
                    entMan.DeleteEntity(item);
                }
            }

            Assert.That(kits, Is.EqualTo(26), "Expected 13 kits per faction.");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var left = 0;
            var query = entMan.AllEntityQueryEnumerator<WGTakeOnlyStorageComponent>();
            while (query.MoveNext(out _, out _))
            {
                left++;
            }
            Assert.That(left, Is.Zero, "An emptied kit case wasn't deleted.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RacksOfferEveryKit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var offered = new HashSet<string>();
            foreach (var rack in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!rack.HasComponent<WGSpecialistVendorComponent>(factory)
                    || !rack.TryComp<CMAutomatedVendorComponent>(out var vendor, factory))
                    continue;

                foreach (var entry in vendor.Sections.SelectMany(s => s.Entries))
                {
                    Assert.That(offered.Add(entry.Id), Is.True, $"{entry.Id} is on more than one rack.");
                }
            }

            foreach (var kit in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!kit.Abstract && kit.HasComponent<WGSpecialistKitComponent>(factory))
                    Assert.That(offered, Does.Contain(kit.ID), $"{kit.ID} is on no specialist rack.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
