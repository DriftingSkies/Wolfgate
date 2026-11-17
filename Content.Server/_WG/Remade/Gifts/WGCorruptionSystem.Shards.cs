using System.Linq;
using Content.Server._WG.Remade.Counterplay;
using Content.Server._WG.Remade.Gifts;
using Content.Server._WG.Remade.Monolith;
using Content.Server._WG.Remade.Structures;
using Content.Shared._WG.Remade;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Remade.Corruption;

/// <summary>Shards as small nodes: a rooted shard feeds a handful of tiles for its Scion, however far from it the shard is carried.</summary>
public sealed partial class WGCorruptionSystem
{
    /// <summary>Whether a node is a rooted shard whose Scion has reached Growth.</summary>
    private bool ShardFeeds(EntityUid node)
    {
        return TryComp<WGMonolithShardComponent>(node, out var shard)
               && shard.Deployed
               && TryComp<WGMonolithComponent>(shard.Parent, out var parent)
               && parent.Stage >= WGMonolithStage.Growth;
    }

    /// <summary>Roots a shard: hands its node to the Scion and puts the first tile under it. False on a sanctuary or off a grid.</summary>
    public bool SeedShard(EntityUid shard, Entity<WGCorruptionNodeComponent> node, Entity<WGMonolithComponent> parent)
    {
        var xform = Transform(shard);
        if (xform.GridUid is not { } gridUid || IsWarded(gridUid) || GridFull(gridUid)
            || !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return false;
        }

        AttachNode(node, parent);
        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var attempt = new WGCorruptionGrowAttemptEvent(tile);
        RaiseLocalEvent(gridUid, ref attempt);
        if (!attempt.Cancelled && !IsCorrupted((gridUid, grid), tile))
            Grow(parent, _map.GridTileToLocal(gridUid, grid, tile), node);

        return true;
    }

    /// <summary>Takes a shard's node away from its Scion. The tiles it was feeding starve.</summary>
    public void DetachNode(Entity<WGCorruptionNodeComponent> node)
    {
        if (node.Comp.Source is { } source && _nodes.TryGetValue(source, out var set))
        {
            set.Remove(node);
            if (set.Count == 0)
                _nodes.Remove(source);
        }

        node.Comp.Source = null;
        var now = _timing.CurTime;
        foreach (var uid in node.Comp.Tiles.ToList())
        {
            if (TryComp<WGCorruptionComponent>(uid, out var tile) && tile.DecayAt == null)
                SetDecay(uid, tile, now + TimeSpan.FromSeconds(_random.NextFloat(0f, NodeStarveSeconds)));
        }
    }
}
