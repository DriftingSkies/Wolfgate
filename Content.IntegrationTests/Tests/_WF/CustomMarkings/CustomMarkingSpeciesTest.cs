using System.Collections.Generic;
using Content.Client._WF.CustomMarkings;
using Content.Client.Humanoid;
using Content.Shared._WF.CustomMarkings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.IntegrationTests.Tests._WF.CustomMarkings;

/// <summary>
/// Every species a player can pick shows a custom marking, including the ones whose body parts are markings
/// themselves, such as the IPC.
/// </summary>
[TestFixture]
[TestOf(typeof(CustomMarkingSystem))]
public sealed class CustomMarkingSpeciesTest
{
    private static readonly string Hash = new('d', CustomMarkingRules.HashLength);

    [Test]
    public async Task EverySpeciesShowsArtTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var bare = new List<string>();
        var count = 0;

        await client.WaitPost(() =>
        {
            var entMan = client.EntMan;
            var sprites = entMan.System<SpriteSystem>();
            var humanoids = entMan.System<HumanoidAppearanceSystem>();
            var resources = CustomMarkingResources.For(client.ResolveDependency<IResourceCache>());

            // Covers every pixel of every facing, so some of it lies on any body.
            var art = new CustomMarkingArt();
            for (var facing = 0; facing < CustomMarkingRules.Facings; facing++)
            {
                for (var y = 0; y < CustomMarkingRules.FrameSize; y++)
                {
                    for (var x = 0; x < CustomMarkingRules.FrameSize; x++)
                    {
                        art.SetPixel(0, facing, x, y, new Rgba32(255, 0, 0, 255));
                    }
                }
            }

            resources.Store(Hash, art.ToPng());

            foreach (var species in client.ResolveDependency<IPrototypeManager>().EnumeratePrototypes<SpeciesPrototype>())
            {
                if (!species.RoundStart)
                    continue;

                count++;
                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID)
                    .WithCustomMarkings(new List<CustomMarking> { new(Hash, CustomMarkingPlacement.Skin) });
                var doll = entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
                humanoids.LoadProfile(doll, profile);
                var sprite = entMan.GetComponent<SpriteComponent>(doll);
                if (!sprites.LayerMapTryGet((doll, sprite), CustomMarkingSystem.LayerKey(0), out _, false))
                    bare.Add(species.ID);

                entMan.DeleteEntity(doll);
            }
        });

        Assert.That(count, Is.GreaterThan(0));
        Assert.That(bare, Is.Empty, "species that show nothing of a custom marking");
        await pair.CleanReturnAsync();
    }
}
