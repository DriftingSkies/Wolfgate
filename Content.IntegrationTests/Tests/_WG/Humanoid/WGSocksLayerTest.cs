#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Humanoid;

/// <summary>Every species a socks marking allows has a socks layer, over its feet and under its uniform, on its body and its creator doll.</summary>
[TestFixture]
public sealed class WGSocksLayerTest
{
    [Test]
    public async Task EverySockWearerHasTheLayer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entities = client.ResolveDependency<IEntityManager>();
        var protos = client.ResolveDependency<IPrototypeManager>();
        var markings = client.ResolveDependency<MarkingManager>();

        await client.WaitAssertion(() =>
        {
            var sprites = entities.System<SpriteSystem>();
            var species = markings.Markings.Values
                .Where(m => m.MarkingCategory == MarkingCategories.UndergarmentSocks)
                .SelectMany(m => m.SpeciesRestrictions ?? new List<string>())
                .ToHashSet();
            Assert.That(species, Is.Not.Empty, "No socks marking names a species.");

            var failures = new List<string>();
            foreach (var id in species.Order())
            {
                if (!protos.TryIndex<SpeciesPrototype>(id, out var speciesProto))
                {
                    failures.Add($"{id}: no such species");
                    continue;
                }

                foreach (var mob in new[] { speciesProto.Prototype, speciesProto.DollPrototype }.Distinct())
                {
                    var uid = entities.SpawnEntity(mob, MapCoordinates.Nullspace);
                    var sprite = entities.GetComponent<SpriteComponent>(uid);
                    var ent = (uid, sprite);
                    if (!sprites.LayerMapTryGet(ent, HumanoidVisualLayers.UndergarmentSocks, out var socks, false))
                        failures.Add($"{id} ({mob}): no socks layer");
                    else
                    {
                        if (sprites.LayerMapTryGet(ent, HumanoidVisualLayers.LFoot, out var left, false)
                            && sprites.LayerMapTryGet(ent, HumanoidVisualLayers.RFoot, out var right, false)
                            && socks < System.Math.Max(left, right))
                            failures.Add($"{id} ({mob}): socks draw under the feet");

                        if (sprites.LayerMapTryGet(ent, "jumpsuit", out var uniform, false) && socks > uniform)
                            failures.Add($"{id} ({mob}): socks draw over the uniform");
                    }

                    entities.DeleteEntity(uid);
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        });

        await pair.CleanReturnAsync();
    }
}
