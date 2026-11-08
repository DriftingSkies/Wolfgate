using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Outposts.Components;
using Content.Server.Destructible;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server._WF.OutpostRaids;

/// <summary>A breacher's way into an outpost (<see cref="WFRaidBreachSystem"/>).</summary>
[RegisterComponent, Access(typeof(WFRaidBreachSystem))]
public sealed partial class WFRaidBreachComponent : Component
{
    public WFBreachStage Stage;

    /// <summary>The outpost tile it is breaking into.</summary>
    public Vector2i Tile;

    /// <summary>Where it stands to break that tile, in world space.</summary>
    public Vector2 WalkUp;

    /// <summary>The turret it is going for, if any.</summary>
    public EntityUid? Turret;

    /// <summary>Edge tiles it couldn't walk up to, so a restart picks another.</summary>
    public readonly HashSet<Vector2i> Tried = new();
}

public enum WFBreachStage : byte
{
    WalkUp,
    Attack,
    StepIn,
    Enter,
}

/// <summary>
/// Gets a raider into an outpost, in the three stages of spike F0.1: walk the ground to just outside the outpost's edge
/// tile nearest its goal, melee whatever blocks that tile (a wall, then its girder; a door) as a target of its own, then
/// step onto it and walk in. A breacher's goal is the outpost's turrets, one by one, then its middle; walls inside are
/// broken the same way when they stand in the way. The outpost console is never attacked.
/// </summary>
public sealed class WFRaidBreachSystem : EntitySystem
{
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The blackboard key a breacher has while it is to break in.</summary>
    public const string BreachKey = "WFRaidBreach";

    /// <summary>How close to its walk-up spot a breacher counts as there.</summary>
    private const float ArriveRange = 1f;

    /// <summary>How close to the outpost's middle a breacher counts as in.</summary>
    private const float MiddleRange = 2f;

    /// <summary>Within this of a turret a breacher fights it; further off it walks to it. Melee loses a target past 14.</summary>
    private const float TurretRange = 10f;

    private static readonly Vector2i[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>Whether a raid mob has an outpost to break into.</summary>
    public bool CanBreach(EntityUid uid)
    {
        return TryComp<WFRaidMobComponent>(uid, out var mob) && HasComp<MapGridComponent>(mob.Outpost) && !TerminatingOrDeleted(mob.Outpost);
    }

    /// <summary>Picks the breacher's goal and the edge tile to break, and walks it there.</summary>
    public void Start(EntityUid uid)
    {
        if (!TryComp<WFRaidMobComponent>(uid, out var mob) || !TryComp<MapGridComponent>(mob.Outpost, out var grid))
            return;

        var breach = EnsureComp<WFRaidBreachComponent>(uid);
        var at = _transform.GetWorldPosition(uid);
        if (CompOrNull<WFRaidRaiderComponent>(uid)?.Breacher == true && (breach.Turret == null || TerminatingOrDeleted(breach.Turret.Value)))
            breach.Turret = NearestTurret(mob.Outpost, at);

        if (Transform(uid).GridUid == mob.Outpost || !TryPickEdge((mob.Outpost, grid), breach, at, Goal(mob.Outpost, grid, breach)))
        {
            Enter(uid, mob.Outpost, grid, breach);
            return;
        }

        breach.Stage = WFBreachStage.WalkUp;
        SteerAfresh(uid, _transform.ToCoordinates(new MapCoordinates(breach.WalkUp, Transform(uid).MapID)));
    }

    /// <summary>Moves a breacher on a stage when it can; finished once it stands in the outpost's middle.</summary>
    public HTNOperatorStatus Update(EntityUid uid)
    {
        if (!TryComp<WFRaidBreachComponent>(uid, out var breach)
            || !TryComp<WFRaidMobComponent>(uid, out var mob)
            || TerminatingOrDeleted(mob.Outpost)
            || !TryComp<MapGridComponent>(mob.Outpost, out var grid))
            return HTNOperatorStatus.Failed;

        var outpost = mob.Outpost;
        var at = _transform.GetWorldPosition(uid);
        var noPath = CompOrNull<NPCSteeringComponent>(uid)?.Status == SteeringStatus.NoPath;
        switch (breach.Stage)
        {
            case WFBreachStage.WalkUp:
                if (Vector2.Distance(at, breach.WalkUp) < ArriveRange)
                {
                    breach.Stage = WFBreachStage.Attack;
                    return HTNOperatorStatus.Continuing;
                }

                if (!noPath)
                    return HTNOperatorStatus.Continuing;
                if (TryBreakToward(uid, (outpost, grid), breach, breach.WalkUp))
                    return HTNOperatorStatus.Continuing;
                breach.Tried.Add(breach.Tile);
                return HTNOperatorStatus.Failed;

            case WFBreachStage.Attack:
                if (Blocker((outpost, grid), breach.Tile) is { } blocker)
                {
                    EnsureComp<NPCMeleeCombatComponent>(uid).Target = blocker;
                    // Steering's own smash gives up between hits and leaves no path, which makes melee call the target
                    // unreachable; start it afresh.
                    if (noPath)
                        _steering.Unregister(uid);
                    return HTNOperatorStatus.Continuing;
                }

                StepIn(uid, (outpost, grid), breach);
                return HTNOperatorStatus.Continuing;

            case WFBreachStage.StepIn:
                // Past a broken tile the way on may still be shut, so the breacher stands in the gap before it looks.
                var on = Transform(uid).GridUid == outpost
                         && _maps.TileIndicesFor(outpost, grid, _transform.GetMapCoordinates(uid)) == breach.Tile;
                if (on || noPath)
                    Enter(uid, outpost, grid, breach);
                return HTNOperatorStatus.Continuing;

            default:
                return UpdateEnter(uid, (outpost, grid), breach, at, noPath);
        }
    }

    /// <summary>Stops the breacher's walking and fighting; where it got to is kept for a restart.</summary>
    public void Stop(EntityUid uid)
    {
        RemComp<NPCMeleeCombatComponent>(uid);
        _steering.Unregister(uid);
    }

    private HTNOperatorStatus UpdateEnter(EntityUid uid, Entity<MapGridComponent> outpost, WFRaidBreachComponent breach, Vector2 at, bool noPath)
    {
        if (breach.Turret is { } old && TerminatingOrDeleted(old))
        {
            breach.Turret = NearestTurret(outpost, at);
            Enter(uid, outpost, outpost.Comp, breach);
            return HTNOperatorStatus.Continuing;
        }

        if (breach.Turret is { } turret)
        {
            var turretAt = _transform.GetWorldPosition(turret);
            var melee = CompOrNull<NPCMeleeCombatComponent>(uid);
            if (noPath || melee?.Status == CombatStatus.TargetUnreachable)
            {
                if (TryBreakToward(uid, outpost, breach, turretAt))
                    return HTNOperatorStatus.Continuing;

                // No wall to break and no way to the turret: leave it and go for the middle.
                breach.Turret = null;
                Enter(uid, outpost, outpost.Comp, breach);
                return HTNOperatorStatus.Continuing;
            }

            if (Vector2.Distance(at, turretAt) <= TurretRange)
                EnsureComp<NPCMeleeCombatComponent>(uid).Target = turret;
            else
                _steering.Register(uid, new EntityCoordinates(turret, Vector2.Zero));
            return HTNOperatorStatus.Continuing;
        }

        var inside = Transform(uid).GridUid == outpost.Owner;
        if (inside && Vector2.Distance(at, _transform.ToMapCoordinates(Middle(outpost)).Position) < MiddleRange)
            return HTNOperatorStatus.Finished;
        if (!noPath)
            return HTNOperatorStatus.Continuing;
        if (TryBreakToward(uid, outpost, breach, _transform.ToMapCoordinates(Middle(outpost)).Position))
            return HTNOperatorStatus.Continuing;
        return inside ? HTNOperatorStatus.Finished : HTNOperatorStatus.Failed;
    }

    /// <summary>Walks the breacher onto the tile it just broke.</summary>
    private void StepIn(EntityUid uid, Entity<MapGridComponent> outpost, WFRaidBreachComponent breach)
    {
        RemComp<NPCMeleeCombatComponent>(uid);
        breach.Stage = WFBreachStage.StepIn;
        SteerAfresh(uid, _maps.GridTileToLocal(outpost, outpost.Comp, breach.Tile));
    }

    /// <summary>Walks the breacher on to its goal: its turret, else the outpost's middle.</summary>
    private void Enter(EntityUid uid, EntityUid outpost, MapGridComponent grid, WFRaidBreachComponent breach)
    {
        RemComp<NPCMeleeCombatComponent>(uid);
        breach.Stage = WFBreachStage.Enter;
        SteerAfresh(uid, breach.Turret is { } turret ? new EntityCoordinates(turret, Vector2.Zero) : Middle((outpost, grid)));
    }

    /// <summary>Steers to new coordinates. Steering keeps a lost path through a plain re-register, so it starts over.</summary>
    private void SteerAfresh(EntityUid uid, EntityCoordinates coordinates)
    {
        _steering.Unregister(uid);
        _steering.Register(uid, coordinates);
    }

    private static EntityCoordinates Middle(Entity<MapGridComponent> outpost)
    {
        return new EntityCoordinates(outpost, outpost.Comp.LocalAABB.Center);
    }

    private Vector2 Goal(EntityUid outpost, MapGridComponent grid, WFRaidBreachComponent breach)
    {
        return breach.Turret is { } turret
            ? _transform.GetWorldPosition(turret)
            : _transform.ToMapCoordinates(Middle((outpost, grid))).Position;
    }

    /// <summary>The live turret on the outpost nearest a point.</summary>
    private EntityUid? NearestTurret(EntityUid outpost, Vector2 from)
    {
        EntityUid? nearest = null;
        var best = float.MaxValue;
        var turrets = EntityQueryEnumerator<WFOutpostTurretComponent, TransformComponent>();
        while (turrets.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != outpost || TerminatingOrDeleted(uid))
                continue;
            var distance = Vector2.Distance(_transform.GetWorldPosition(xform), from);
            if (distance >= best)
                continue;
            best = distance;
            nearest = uid;
        }

        return nearest;
    }

    /// <summary>
    /// The edge tile to break: of those with open ground beside them, the one that is the shortest walk from the
    /// breacher plus the shortest walk on to its goal. A tile with the console on it is never picked.
    /// </summary>
    private bool TryPickEdge(Entity<MapGridComponent> outpost, WFRaidBreachComponent breach, Vector2 from, Vector2 goal)
    {
        var best = float.MaxValue;
        var tiles = _maps.GetAllTilesEnumerator(outpost, outpost.Comp);
        while (tiles.MoveNext(out var tile))
        {
            var index = tile.Value.GridIndices;
            if (breach.Tried.Contains(index) || HasConsole(outpost, index))
                continue;

            var tileAt = _maps.GridTileToWorldPos(outpost, outpost.Comp, index);
            foreach (var side in Sides)
            {
                var outside = index + side;
                if (_maps.TryGetTileRef(outpost, outpost.Comp, outside, out var next) && !next.Tile.IsEmpty)
                    continue;

                var outsideAt = _maps.GridTileToWorldPos(outpost, outpost.Comp, outside);
                var cost = Vector2.Distance(outsideAt, from) + Vector2.Distance(tileAt, goal);
                if (cost >= best)
                    continue;
                best = cost;
                breach.Tile = index;
                breach.WalkUp = outsideAt;
            }
        }

        return best < float.MaxValue;
    }

    /// <summary>When the way on is shut, sets the breacher on whatever blocks the outpost tile beside it nearest its goal.</summary>
    private bool TryBreakToward(EntityUid uid, Entity<MapGridComponent> outpost, WFRaidBreachComponent breach, Vector2 goal)
    {
        var here = _maps.TileIndicesFor(outpost, outpost.Comp, _transform.GetMapCoordinates(uid));
        var best = float.MaxValue;
        Vector2i? pick = null;
        foreach (var side in Sides)
        {
            var index = here + side;
            if (Blocker(outpost, index) == null)
                continue;
            var distance = Vector2.Distance(_maps.GridTileToWorldPos(outpost, outpost.Comp, index), goal);
            if (distance >= best)
                continue;
            best = distance;
            pick = index;
        }

        if (pick is not { } tile)
            return false;

        breach.Tile = tile;
        breach.Stage = WFBreachStage.Attack;
        _steering.Unregister(uid);
        return true;
    }

    /// <summary>A hard, destructible anchored entity on an outpost tile, other than its console, if anything blocks it.</summary>
    private EntityUid? Blocker(Entity<MapGridComponent> outpost, Vector2i index)
    {
        if (HasConsole(outpost, index))
            return null;

        var anchored = _maps.GetAnchoredEntitiesEnumerator(outpost, outpost.Comp, index);
        while (anchored.MoveNext(out var found))
        {
            var ent = found.Value;
            if (HasComp<DestructibleComponent>(ent)
                && TryComp<PhysicsComponent>(ent, out var body)
                && body.Hard
                && body.CanCollide)
                return ent;
        }

        return null;
    }

    private bool HasConsole(Entity<MapGridComponent> outpost, Vector2i index)
    {
        var anchored = _maps.GetAnchoredEntitiesEnumerator(outpost, outpost.Comp, index);
        while (anchored.MoveNext(out var found))
        {
            if (HasComp<WFOutpostConsoleComponent>(found.Value))
                return true;
        }

        return false;
    }
}

/// <summary>Breaks a raider into its outpost (<see cref="WFRaidBreachSystem"/>); the director gives it the key.</summary>
public sealed partial class WFRaidBreachOperator : HTNOperator
{
    private WFRaidBreachSystem _breach = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _breach = sysManager.GetEntitySystem<WFRaidBreachSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return (_breach.CanBreach(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner)), null);
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        base.Startup(blackboard);
        _breach.Start(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var status = _breach.Update(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
        if (status == HTNOperatorStatus.Finished)
            blackboard.Remove<bool>(WFRaidBreachSystem.BreachKey);
        return status;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _breach.Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        _breach.Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
