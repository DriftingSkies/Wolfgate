using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;

namespace Content.Server._CE.Power;

/// <summary>
/// Grids that change level or z-network are refloaded together at the end of the tick, in one scan of the node
/// containers, instead of one whole-world scan per grid.
/// </summary>
public sealed partial class CEZCableVerticalSystem
{
    private readonly HashSet<EntityUid> _wfRefloodGrids = new();

    /// <summary>Queues a grid's vertical cables for the batched reflood.</summary>
    private void WFQueueReflood(EntityUid gridUid)
    {
        _wfRefloodGrids.Add(gridUid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_wfRefloodGrids.Count == 0)
            return;

        var enumerator = EntityQueryEnumerator<NodeContainerComponent, TransformComponent>();
        while (enumerator.MoveNext(out _, out var nc, out var xform))
        {
            if (xform.GridUid is not { } grid || !_wfRefloodGrids.Contains(grid))
                continue;

            foreach (var node in nc.Nodes.Values)
            {
                if (node is CECableVerticalNode)
                    _nodeGroup.QueueReflood(node);
            }
        }

        _wfRefloodGrids.Clear();
    }
}
