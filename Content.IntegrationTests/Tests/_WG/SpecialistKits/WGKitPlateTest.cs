using System.Linq;
using Content.Shared._Mono.ArmorPlate;
using Content.Shared._RMC14.Vendors;
using Content.Shared._WG.SpecialistKits;
using Content.Shared.Prototypes;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.SpecialistKits;

/// <summary>Every specialist kit that hands out an armor plate also hands out armor the plate fits into.</summary>
[TestFixture]
public sealed class WGKitPlateTest
{
    [Test]
    public async Task KitPlatesFitKitArmor()
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
            var coords = new MapCoordinates(0, 0, map.MapId);
            var plated = 0;
            foreach (var kit in protos.EnumeratePrototypes<EntityPrototype>())
            {
                if (!kit.HasComponent<WGSpecialistKitComponent>(factory)
                    || !kit.TryGetComponent<CMVendorBundleComponent>(out var bundle, factory))
                    continue;

                var plates = bundle.Bundle.Where(id => protos.Index(id).HasComponent<ArmorPlateItemComponent>(factory)).ToList();
                if (plates.Count == 0)
                    continue;

                var holder = bundle.Bundle.FirstOrDefault(id => protos.Index(id).HasComponent<ArmorPlateHolderComponent>(factory));
                Assert.That(holder.Id, Is.Not.Null, $"{kit.ID} hands out a plate but no armor that holds one.");

                foreach (var plateId in plates)
                {
                    var armor = entMan.SpawnEntity(holder, coords);
                    var plate = entMan.SpawnEntity(plateId, coords);
                    Assert.That(storage.CanInsert(armor, plate, out var reason), Is.True,
                        $"{kit.ID}: {plateId} doesn't fit {holder} ({reason}).");
                    entMan.DeleteEntity(armor);
                    entMan.DeleteEntity(plate);
                }
                plated++;
            }

            // The four front-line pairs: Breacher/Boarder, Combat Engineer/Artificer, Heavy Gunner/Heavy, Pyrotechnician/Burner.
            Assert.That(plated, Is.EqualTo(8));
        });

        await pair.CleanReturnAsync();
    }
}
