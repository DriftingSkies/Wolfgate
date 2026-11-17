using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Construction;

/// <summary>
/// Recipes the construction menu can start: it only gathers materials and parts for the first edge, so a tool step
/// there drops the materials and reports them missing.
/// </summary>
[TestFixture]
public sealed class ConstructionStartStepsTest
{
    [Test]
    public async Task FirstEdgeTakesOnlyMaterialsAndParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var recipe in protos.EnumeratePrototypes<ConstructionPrototype>())
                {
                    var graph = protos.Index<ConstructionGraphPrototype>(recipe.Graph);
                    if (!graph.Nodes.TryGetValue(recipe.StartNode, out var start) ||
                        graph.Path(recipe.StartNode, recipe.TargetNode) is not { Length: > 0 } path ||
                        start.GetEdge(path[0].Name) is not { } edge)
                        continue;

                    foreach (var step in edge.Steps)
                    {
                        Assert.That(step is MaterialConstructionGraphStep or ArbitraryInsertConstructionGraphStep,
                            $"Recipe {recipe.ID} starts with a {step.GetType().Name} on graph {graph.ID}; the first " +
                            "edge may only take materials and parts.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
