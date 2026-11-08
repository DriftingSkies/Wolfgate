using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.OutpostIndustry.Components;
using Content.Server._WF.OutpostRaids;
using Content.Server.Gatherable;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Server.Gatherable.Components;
using Content.Shared.Mining.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.OutpostIndustry;

/// <summary>
/// Mining bots: a dock makes its bots and stores their ore. By day, while the dock may run, a bot walks to the nearest
/// ore-bearing rock in the dock's range, drills it, picks up the ore and carries a load home; at night (the Active
/// window) or when the dock stops, it goes home and rests.
/// </summary>
public sealed partial class WFOutpostIndustrySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GatherableSystem _gatherable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tags = default!;
    [Dependency] private WFRaidWindowSystem _window = default!;

    private static readonly ProtoId<TagPrototype> OreTag = "Ore";

    private static readonly Vector2i[] Sides = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    /// <summary>How close to a rock a bot drills it.</summary>
    private const float DrillRange = 1.6f;

    /// <summary>How close to its dock a bot unloads.</summary>
    private const float DockRange = 1.5f;

    /// <summary>How long a bot that found no rock rests before it looks again.</summary>
    private static readonly TimeSpan LookInterval = TimeSpan.FromSeconds(15);

    /// <summary>How far round itself a bot picks up the ore it drilled.</summary>
    private const float PickupRange = 1.5f;

    private readonly HashSet<Entity<OreVeinComponent>> _veins = new();
    private readonly HashSet<EntityUid> _near = new();

    private void InitializeBots()
    {
        SubscribeLocalEvent<WFMiningBotComponent, ComponentInit>(OnBotInit);
        SubscribeLocalEvent<WFMiningBotDockComponent, ComponentShutdown>(OnDockShutdown);
    }

    private void OnBotInit(Entity<WFMiningBotComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Hold = _container.EnsureContainer<Container>(ent, WFMiningBotComponent.HoldId);
    }

    private void OnDockShutdown(Entity<WFMiningBotDockComponent> ent, ref ComponentShutdown args)
    {
        foreach (var bot in ent.Comp.Spawned)
        {
            QueueDel(bot);
        }

        ent.Comp.Spawned.Clear();
    }

    /// <summary>Makes a dock's missing bots, or takes them away when it is packed up or off an outpost.</summary>
    private void UpdateDocks(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFMiningBotDockComponent>();
        while (query.MoveNext(out var uid, out var dock))
        {
            var lost = dock.Spawned.RemoveAll(bot => TerminatingOrDeleted(bot) || !_mobState.IsAlive(bot));
            if (lost > 0)
                dock.NextBot = now + dock.Rebuild;

            if (Gate(uid, out _) == "wf-industry-gate-outpost")
            {
                foreach (var bot in dock.Spawned)
                {
                    QueueDel(bot);
                }

                dock.Spawned.Clear();
                continue;
            }

            while (dock.Spawned.Count < dock.Bots && now >= dock.NextBot)
            {
                var bot = Spawn(dock.BotPrototype, Transform(uid).Coordinates);
                EnsureComp<WFMiningBotComponent>(bot).Dock = uid;
                dock.Spawned.Add(bot);
            }
        }
    }

    /// <summary>Whether a bot's dock may send it out now: it may run, and it is day.</summary>
    public bool CanMine(EntityUid dock)
    {
        if (Gate(dock, out var outpost) != null)
            return false;

        return !_window.TryGetWindowOn(outpost.Comp1.Ground, out var active, out _) || !active;
    }

    /// <summary>Whether a bot has a dock to work for.</summary>
    public bool HasDock(EntityUid bot)
    {
        return TryComp<WFMiningBotComponent>(bot, out var comp) && HasComp<WFMiningBotDockComponent>(comp.Dock) && !TerminatingOrDeleted(comp.Dock);
    }

    /// <summary>Moves a bot on a stage; it only fails when its dock is gone.</summary>
    public HTNOperatorStatus UpdateBot(EntityUid uid)
    {
        if (!TryComp<WFMiningBotComponent>(uid, out var bot) || !TryComp<WFMiningBotDockComponent>(bot.Dock, out var dock)
            || TerminatingOrDeleted(bot.Dock))
            return HTNOperatorStatus.Failed;

        var working = CanMine(bot.Dock);
        var full = bot.Hold.ContainedEntities.Count >= bot.Capacity;
        var noPath = CompOrNull<NPCSteeringComponent>(uid)?.Status == SteeringStatus.NoPath;
        switch (bot.Stage)
        {
            case WFMiningBotStage.Rest:
                if (working && !full && _timing.CurTime >= bot.NextLook)
                    bot.Stage = WFMiningBotStage.Seek;
                return HTNOperatorStatus.Continuing;

            case WFMiningBotStage.Seek:
                if (!working || full)
                {
                    GoHome(uid, bot);
                    return HTNOperatorStatus.Continuing;
                }

                if (bot.Rock is not { } rock || TerminatingOrDeleted(rock))
                {
                    if (PickRock(uid, bot, dock) is not { } picked)
                    {
                        bot.NextLook = _timing.CurTime + LookInterval;
                        GoHome(uid, bot);
                        return HTNOperatorStatus.Continuing;
                    }

                    if (WalkUp(picked, _transform.GetWorldPosition(uid)) is not { } spot)
                    {
                        bot.Unreachable.Add(picked);
                        return HTNOperatorStatus.Continuing;
                    }

                    bot.Rock = rock = picked;
                    _steering.Register(uid, spot).Range = 0.4f;
                }

                if (Distance(uid, rock) <= DrillRange)
                {
                    _steering.Unregister(uid);
                    bot.Stage = WFMiningBotStage.Drill;
                    bot.DrillUntil = _timing.CurTime + bot.DrillTime;
                }
                else if (noPath)
                {
                    bot.Unreachable.Add(rock);
                    bot.Rock = null;
                    _steering.Unregister(uid);
                }

                return HTNOperatorStatus.Continuing;

            case WFMiningBotStage.Drill:
                if (bot.Rock is not { } drilled || TerminatingOrDeleted(drilled))
                {
                    bot.Stage = WFMiningBotStage.Seek;
                    bot.Rock = null;
                    return HTNOperatorStatus.Continuing;
                }

                if (_timing.CurTime < bot.DrillUntil)
                    return HTNOperatorStatus.Continuing;

                _gatherable.Gather(drilled, uid, spawnOnGatherer: true);
                bot.Rock = null;
                PickUpOre(uid, bot);
                bot.Stage = WFMiningBotStage.Seek;
                return HTNOperatorStatus.Continuing;

            default:
                if (Distance(uid, bot.Dock) <= DockRange || noPath && Distance(uid, bot.Dock) <= DockRange * 2)
                {
                    _steering.Unregister(uid);
                    Unload(bot, bot.Dock);
                    bot.Stage = WFMiningBotStage.Rest;
                }
                else if (!HasComp<NPCSteeringComponent>(uid))
                {
                    GoHome(uid, bot);
                }

                return HTNOperatorStatus.Continuing;
        }
    }

    /// <summary>Stops a bot walking; what it carries stays in its hold, and it picks its rock afresh on a restart.</summary>
    public void StopBot(EntityUid uid)
    {
        _steering.Unregister(uid);
        if (TryComp<WFMiningBotComponent>(uid, out var bot) && bot.Stage == WFMiningBotStage.Seek)
            bot.Rock = null;
    }

    private void GoHome(EntityUid uid, WFMiningBotComponent bot)
    {
        bot.Rock = null;
        bot.Stage = WFMiningBotStage.Return;
        _steering.Register(uid, Transform(bot.Dock).Coordinates).Range = DockRange - 0.3f;
    }

    /// <summary>The nearest rock in the dock's range with ore left in it that the bot hasn't failed to reach.</summary>
    private EntityUid? PickRock(EntityUid uid, WFMiningBotComponent bot, WFMiningBotDockComponent dock)
    {
        _veins.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(bot.Dock), dock.Range, _veins, LookupFlags.Static);
        var at = _transform.GetWorldPosition(uid);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var vein in _veins)
        {
            if (vein.Comp.CurrentOre == null || vein.Comp.PreventSpawning || bot.Unreachable.Contains(vein)
                || !HasComp<GatherableComponent>(vein))
                continue;

            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(vein), at);
            if (distance >= bestDistance)
                continue;

            best = vein;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Puts the loose ore round the bot into its hold, up to its capacity.</summary>
    private void PickUpOre(EntityUid uid, WFMiningBotComponent bot)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(uid).Coordinates, PickupRange, _near);
        foreach (var ore in _near)
        {
            if (bot.Hold.ContainedEntities.Count >= bot.Capacity)
                return;
            if (!_tags.HasTag(ore, OreTag) || _container.IsEntityInContainer(ore))
                continue;

            _container.Insert(ore, bot.Hold);
        }
    }

    /// <summary>Puts a bot's load in its dock's storage, or beside the dock when it is full, and books its value.</summary>
    private void Unload(WFMiningBotComponent bot, EntityUid dock)
    {
        var value = 0.0;
        foreach (var ore in new List<EntityUid>(bot.Hold.ContainedEntities))
        {
            value += _pricing.GetPrice(ore);
            _container.Remove(ore, bot.Hold);
            if (!_storage.Insert(dock, ore, out _, playSound: false))
                _transform.SetCoordinates(ore, Transform(dock).Coordinates);
        }

        if (value > 0 && Transform(dock).GridUid is { } outpost)
            Record(outpost, value);
    }

    /// <summary>The open tile beside a rock nearest a bot, to stand on and drill it from.</summary>
    private EntityCoordinates? WalkUp(EntityUid rock, Vector2 from)
    {
        var xform = Transform(rock);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return null;

        var tile = _maps.TileIndicesFor(gridUid, grid, xform.Coordinates);
        EntityCoordinates? best = null;
        var bestDistance = float.MaxValue;
        foreach (var side in Sides)
        {
            var index = tile + side;
            if (!_maps.TryGetTileRef(gridUid, grid, index, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;
            if (_maps.GetAnchoredEntitiesEnumerator(gridUid, grid, index).MoveNext(out _))
                continue;

            var spot = _maps.GridTileToLocal(gridUid, grid, index);
            var distance = Vector2.DistanceSquared(_transform.ToMapCoordinates(spot).Position, from);
            if (distance >= bestDistance)
                continue;

            best = spot;
            bestDistance = distance;
        }

        return best;
    }

    private float Distance(EntityUid a, EntityUid b)
    {
        return Vector2.Distance(_transform.GetWorldPosition(a), _transform.GetWorldPosition(b));
    }
}

/// <summary>Works a mining bot for its dock (<see cref="WFOutpostIndustrySystem"/>).</summary>
public sealed partial class WFMiningBotOperator : HTNOperator
{
    private WFOutpostIndustrySystem _industry = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _industry = sysManager.GetEntitySystem<WFOutpostIndustrySystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        return (_industry.HasDock(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner)), null);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        return _industry.UpdateBot(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        base.TaskShutdown(blackboard, status);
        _industry.StopBot(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void PlanShutdown(NPCBlackboard blackboard)
    {
        base.PlanShutdown(blackboard);
        _industry.StopBot(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
