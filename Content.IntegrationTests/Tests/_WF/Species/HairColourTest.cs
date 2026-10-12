using System.Collections.Generic;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>No species forces its hair or facial hair to the skin colour: the player's pick is what is drawn.</summary>
[TestFixture]
public sealed class HairColourTest
{
    [Test]
    public async Task HairColourIsThePlayersPickTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var forced = new List<string>();

        await server.WaitPost(() =>
        {
            var protos = server.ResolveDependency<IPrototypeManager>();
            var markings = server.ResolveDependency<MarkingManager>();
            foreach (var species in protos.EnumeratePrototypes<SpeciesPrototype>())
            {
                foreach (var layer in new[] { HumanoidVisualLayers.Hair, HumanoidVisualLayers.FacialHair })
                {
                    if (markings.MustMatchSkin(species.ID, layer, out _, protos))
                        forced.Add($"{species.ID} {layer}");
                }
            }
        });

        Assert.That(forced, Is.Empty, "species whose hair is forced to the skin colour");
        await pair.CleanReturnAsync();
    }
}
