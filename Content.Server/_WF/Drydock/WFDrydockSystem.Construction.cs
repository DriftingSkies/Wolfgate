using Content.Server.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

public sealed partial class WFDrydockSystem
{
    /// <summary>Construction materials by entity prototype, in material units.</summary>
    private readonly Dictionary<string, Dictionary<string, int>> _constructionMaterials = new();

    /// <summary>
    /// Materials to build an entity by hand: every material step on its construction graph from the start node to its
    /// node. Empty without a graph.
    /// </summary>
    private Dictionary<string, int> ConstructionMaterials(EntityPrototype proto)
    {
        if (_constructionMaterials.TryGetValue(proto.ID, out var cached))
            return cached;

        var materials = new Dictionary<string, int>();
        _constructionMaterials[proto.ID] = materials;
        if (!proto.TryGetComponent<ConstructionComponent>(out var construction, Factory) ||
            !_prototype.TryIndex<ConstructionGraphPrototype>(construction.Graph, out var graph) ||
            graph.Start is not { } start ||
            !graph.TryPath(start, construction.Node, out var path))
        {
            return materials;
        }

        var current = graph.Nodes[start];
        foreach (var node in path)
        {
            var edge = current.GetEdge(node.Name);
            current = node;
            if (edge == null)
                continue;

            foreach (var step in edge.Steps)
            {
                if (step is not MaterialConstructionGraphStep materialStep ||
                    !_prototype.TryIndex<StackPrototype>(materialStep.MaterialPrototypeId, out var stack) ||
                    !_prototype.TryIndex(stack.Spawn, out var sheet) ||
                    !sheet.TryGetComponent<PhysicalCompositionComponent>(out var composition, Factory))
                {
                    continue;
                }

                foreach (var (material, amount) in composition.MaterialComposition)
                {
                    materials[material] = materials.GetValueOrDefault(material) + amount * materialStep.Amount;
                }
            }
        }

        return materials;
    }

    /// <summary>Market value of a set of materials, from each material's price per unit.</summary>
    private double MaterialValue(Dictionary<string, int> materials)
    {
        var value = 0d;
        foreach (var (id, amount) in materials)
        {
            if (_prototype.TryIndex<MaterialPrototype>(id, out var material))
                value += material.Price * amount;
        }

        return value;
    }
}
