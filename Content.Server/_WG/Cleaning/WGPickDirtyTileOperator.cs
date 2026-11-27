using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._WG.Wolfmed.Gore;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Cleaning;

/// <summary>
/// Picks a nearby tile with cleanable decals on the NPC's grid and puts its centre into the target key.
/// </summary>
public sealed partial class WGPickDirtyTileOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private WGDecalCleaningSystem _cleaning = default!;

    /// <summary>How far to look for dirt, in tiles.</summary>
    [DataField]
    public float Range = 10f;

    /// <summary>How many of the nearest dirty tiles the pick is made from.</summary>
    [DataField]
    public int Choices = 4;

    [DataField]
    public string TargetCoordinates = "TargetCoordinates";

    /// <summary>Whether tiles with a Wolfmed blood splat (on a wall too) count as dirty, for a hand that can scrub them.</summary>
    [DataField]
    public bool Stains;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _cleaning = sysManager.GetEntitySystem<WGDecalCleaningSystem>();
    }

    /// <summary>The tiles of a grid holding a cleanable Wolfmed splat.</summary>
    private List<Vector2i> StainTiles(EntityUid grid)
    {
        var tiles = new List<Vector2i>();
        if (!_entManager.TryGetComponent<MapGridComponent>(grid, out var map))
            return tiles;
        var maps = _entManager.System<SharedMapSystem>();
        var query = _entManager.EntityQueryEnumerator<WolfmedCleanableComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid)
                tiles.Add(maps.TileIndicesFor(grid, map, xform.Coordinates));
        }
        return tiles;
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var xform = _entManager.GetComponent<TransformComponent>(owner);

        if (xform.GridUid is not { } gridUid || xform.ParentUid != gridUid ||
            !_cleaning.TryPickDirtyTile(gridUid, xform.LocalPosition, Range, Choices, out var tile, Taken(owner, gridUid),
                Stains ? StainTiles(gridUid) : null))
        {
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        }

        var target = new EntityCoordinates(gridUid, (Vector2) tile + new Vector2(0.5f, 0.5f));
        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { TargetCoordinates, target },
        }));
    }

    /// <summary>The tiles other cleaners on the grid are already headed for, so two don't wash the same one.</summary>
    private HashSet<Vector2i> Taken(EntityUid owner, EntityUid grid)
    {
        var taken = new HashSet<Vector2i>();
        var query = _entManager.EntityQueryEnumerator<HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var htn, out var xform))
        {
            if (uid == owner || xform.GridUid != grid
                || !htn.Blackboard.TryGetValue<EntityCoordinates>(TargetCoordinates, out var other, _entManager)
                || other.EntityId != grid)
                continue;
            taken.Add(new Vector2i((int) MathF.Floor(other.Position.X), (int) MathF.Floor(other.Position.Y)));
        }
        return taken;
    }
}
