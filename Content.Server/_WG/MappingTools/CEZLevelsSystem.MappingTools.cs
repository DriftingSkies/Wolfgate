using System.Linq;
using Content.Shared._CE.ZLevels.Core.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>
    /// Replaces the z-networks of <paramref name="maps"/>, listed bottom-up, with one new network that puts the map
    /// at index <paramref name="ground"/> on depth 0. The old networks' shared components and name carry over.
    /// </summary>
    public Entity<CEZMapNetworkComponent> WgRestack(List<EntityUid> maps, int ground)
    {
        ComponentRegistry? components = null;
        string? name = null;

        foreach (var map in maps)
        {
            if (!TryGetMapNetwork(map, out var old))
                continue;

            components ??= old.Comp.Components;
            name ??= MetaData(old).EntityName;

            foreach (var level in old.Comp.ZLevels.Values)
            {
                if (level is { } uid)
                    RemComp<CEZMapComponent>(uid);
            }

            Del(old);
        }

        var network = CreateMapNetwork(components);
        _meta.SetEntityName(network, name ?? "Mapping z-network");

        // Depth 0 goes in first and the rest outward from it: QuickApiCache indexes past its list otherwise.
        foreach (var index in Enumerable.Range(0, maps.Count).OrderBy(i => Math.Abs(i - ground)))
        {
            TryAddMapsIntoNetwork(network, new Dictionary<EntityUid, int> { { maps[index], index - ground } });
        }

        QueueAllViewerUpdates();
        return network;
    }
}
