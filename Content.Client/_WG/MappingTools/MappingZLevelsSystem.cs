using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WG.MappingTools;
using Robust.Client.Player;

namespace Content.Client._WG.MappingTools;

/// <summary>
/// Reads the z-stack the local player is on and sends the mapping tools' level requests.
/// </summary>
public sealed partial class MappingZLevelsSystem : EntitySystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;

    /// <summary>
    /// The depths of the player's z-stack, lowest first, and the player's own; a map outside any stack is level 0
    /// of a stack of one.
    /// </summary>
    public void GetStack(List<int> depths, out int current)
    {
        depths.Clear();
        current = 0;

        if (_player.LocalEntity is not { } player || Transform(player).MapUid is not { } map)
            return;

        if (!TryComp<CEZMapComponent>(map, out var zMap) || !_zLevels.TryGetMapNetwork(map, out var network))
        {
            depths.Add(0);
            return;
        }

        current = zMap.Depth;
        foreach (var (depth, level) in network.Comp.ZLevels)
        {
            if (level != null)
                depths.Add(depth);
        }

        depths.Sort();
    }

    public void Move(int offset)
    {
        if (offset != 0)
            RaiseNetworkEvent(new MappingZLevelsMoveEvent(offset));
    }

    public void Add(bool above)
    {
        RaiseNetworkEvent(new MappingZLevelsAddEvent(above));
    }

    public void Remove()
    {
        RaiseNetworkEvent(new MappingZLevelsRemoveEvent());
    }
}
