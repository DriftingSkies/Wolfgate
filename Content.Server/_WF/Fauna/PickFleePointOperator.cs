using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Picks a reachable point away from the target key and puts it into the target coordinates, for mobs that flee.
/// </summary>
public sealed partial class PickFleePointOperator : HTNOperator
{
    private static readonly float[] Angles = { 0f, 45f, -45f, 90f, -90f };

    [Dependency] private IEntityManager _entManager = default!;
    private PathfindingSystem _pathfinding = default!;
    private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Blackboard key of the entity to run from.
    /// </summary>
    [DataField]
    public string Key = "Target";

    /// <summary>
    /// Blackboard key the picked point is written to.
    /// </summary>
    [DataField]
    public string TargetCoordinates = "TargetCoordinates";

    /// <summary>
    /// Blackboard key the path to the picked point is written to.
    /// </summary>
    [DataField]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    /// <summary>
    /// How far to run, in tiles.
    /// </summary>
    [DataField]
    public float Distance = 8f;

    /// <summary>
    /// Blackboard key of a float that replaces <see cref="Distance"/> when set.
    /// </summary>
    [DataField]
    public string? DistanceKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!blackboard.TryGetValue<EntityUid>(Key, out var threat, _entManager) ||
            !_entManager.TryGetComponent<TransformComponent>(owner, out var xform) ||
            !_entManager.TryGetComponent<TransformComponent>(threat, out var threatXform))
            return (false, null);

        var ownerPos = _transform.GetWorldPosition(xform);
        var away = ownerPos - _transform.GetWorldPosition(threatXform);
        away = away.LengthSquared() < 0.01f ? Vector2.UnitX : Vector2.Normalize(away);
        var distance = DistanceKey != null && blackboard.TryGetValue<float>(DistanceKey, out var keyed, _entManager)
            ? keyed
            : Distance;

        foreach (var angle in Angles)
        {
            var dir = new Angle(MathHelper.DegreesToRadians(angle)).RotateVec(away);
            var end = _transform.ToCoordinates(new MapCoordinates(ownerPos + dir * distance, xform.MapID));
            var path = await _pathfinding.GetPath(owner, xform.Coordinates, end, 1.5f, cancelToken,
                _pathfinding.GetFlags(blackboard));

            if (path.Result != PathResult.Path)
                continue;

            return (true, new Dictionary<string, object>
            {
                { TargetCoordinates, end },
                { PathfindKey, path },
            });
        }

        return (false, null);
    }
}
