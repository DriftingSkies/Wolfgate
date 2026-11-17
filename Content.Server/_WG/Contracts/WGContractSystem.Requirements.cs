using System.Linq;
using Content.Shared._WG.Contracts;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Contracts;

/// <summary>What a contract needs of its taker beyond standing and crew: items carried, and the round's game mode.</summary>
public sealed partial class WGContractSystem
{
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>Whether a template may be posted in this round's game preset.</summary>
    private bool PresetAllows(WGContractPrototype template)
    {
        return template.ExcludePresets.Count == 0 || _ticker.CurrentPreset?.ID is not { } preset
               || !template.ExcludePresets.Contains(preset);
    }

    /// <summary>The required items a mob is not carrying, anywhere on its person or in what it holds.</summary>
    public List<WGContractRequirement> Missing(EntityUid mob, WGContractPrototype template)
    {
        if (template.Requires.Count == 0)
            return new List<WGContractRequirement>();

        var carried = new List<EntityUid>();
        Collect(mob, carried, 0);
        return template.Requires.Where(r => !carried.Any(item => Satisfies(item, r))).ToList();
    }

    private bool Satisfies(EntityUid item, WGContractRequirement requirement)
    {
        if (requirement.Tag is { } tag && _tags.HasTag(item, tag))
            return true;

        if (requirement.Entity is not { } entity || MetaData(item).EntityPrototype is not { } proto)
            return false;

        return proto.ID == entity.Id || _proto.EnumerateParents<EntityPrototype>(proto.ID).Any(p => p.ID == entity.Id);
    }

    /// <summary>Everything inside an entity's containers, a few levels deep.</summary>
    private void Collect(EntityUid uid, List<EntityUid> into, int depth)
    {
        if (depth > 4 || !TryComp<ContainerManagerComponent>(uid, out var manager))
            return;

        foreach (var container in _containers.GetAllContainers(uid, manager))
        {
            foreach (var item in container.ContainedEntities)
            {
                into.Add(item);
                Collect(item, into, depth + 1);
            }
        }
    }
}
