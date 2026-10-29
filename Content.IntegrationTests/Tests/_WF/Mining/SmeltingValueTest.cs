using System.Linq;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Mining;

/// <summary>Smelting ore always adds value, so a smeltery is worth running instead of selling raw ore.</summary>
[TestFixture]
public sealed class SmeltingValueTest
{
    private const double MinimumGain = 1.2;

    /// <summary>Whether a material is mined ore (or coal, which smelting also burns): the raw ores and Mono's copper and lithium.</summary>
    private static bool IsOre(Robust.Shared.Prototypes.ProtoId<MaterialPrototype> material)
        => material.Id.StartsWith("Raw") || material.Id.EndsWith("Ore") || material.Id == "Coal";

    [Test]
    public async Task SmeltedMaterialsAreWorthMoreThanTheirOre()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false });
        var protos = pair.Server.ProtoMan;

        await pair.Server.WaitAssertion(() =>
        {
            double Value(System.Collections.Generic.Dictionary<Robust.Shared.Prototypes.ProtoId<MaterialPrototype>, int> materials)
                => materials.Sum(m => protos.Index(m.Key).Price * m.Value);

            var checkedAny = false;
            Assert.Multiple(() =>
            {
                foreach (var recipe in protos.EnumeratePrototypes<LatheRecipePrototype>())
                {
                    if (recipe.MaterialResult.Count == 0 || recipe.Materials.Count == 0
                        || !recipe.Materials.Keys.All(IsOre)
                        || recipe.MaterialResult.Keys.Any(m => m.Id.StartsWith("Raw")))
                        continue; // only ore smelted into a refined material; salt into iron ore is a conversion

                    checkedAny = true;
                    var input = Value(recipe.Materials);
                    var output = Value(recipe.MaterialResult);
                    Assert.That(output, Is.GreaterThanOrEqualTo(input * MinimumGain),
                        $"{recipe.ID} turns {input:0.##} spesos of ore into only {output:0.##} spesos of material.");
                }
            });

            Assert.That(checkedAny, Is.True, "No smelting recipes were found, so the test proves nothing.");
        });

        await pair.CleanReturnAsync();
    }
}
