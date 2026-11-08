using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.NodeGroups;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.NodeContainer;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Drains the cable networks <see cref="FaunaPowerDrainComponent"/> mobs sit on and evolves them.
/// </summary>
public sealed partial class FaunaPowerDrainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<EntityUid> _batteries = new();
    private readonly List<(EntityUid Uid, EntProtoId Proto)> _evolves = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaPowerDrainComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaPowerDrainComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            if (!_mobState.IsAlive(uid)
                || TryComp<HTNComponent>(uid, out var htn) && htn.Blackboard.ContainsKey(FaunaHelperSystem.TargetKey))
            {
                SetDraining((uid, comp), xform, false);
                continue;
            }

            CollectBatteries(xform);
            SetDraining((uid, comp), xform, _batteries.Count > 0);
            foreach (var battery in _batteries)
            {
                comp.Drained -= _battery.UseCharge(battery, comp.Rate);
            }

            if (comp.EvolveInto is { } evolve && comp.Drained >= comp.EvolveAt && !AnyAlive(evolve))
                _evolves.Add((uid, evolve));
        }

        // The evolved form drains too, so it spawns after the query.
        foreach (var (evolver, proto) in _evolves)
        {
            if (!TerminatingOrDeleted(evolver) && TryComp<FaunaPowerDrainComponent>(evolver, out var comp))
                Evolve((evolver, comp), proto, Transform(evolver));
        }

        _evolves.Clear();
    }

    private void OnDamageChanged(Entity<FaunaPowerDrainComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || !ent.Comp.Draining)
            return;

        SetDraining(ent, Transform(ent), false);
        ent.Comp.NextTick = _timing.CurTime + ent.Comp.Interval;
    }

    private void SetDraining(Entity<FaunaPowerDrainComponent> ent, TransformComponent xform, bool draining)
    {
        if (ent.Comp.Draining == draining)
            return;

        ent.Comp.Draining = draining;
        if (draining && ent.Comp.DrainPopup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent);

        if (!ent.Comp.Anchor)
            return;

        if (draining)
            _transform.AnchorEntity(ent, xform);
        else if (xform.Anchored)
            _transform.Unanchor(ent, xform);
    }

    /// <summary>
    /// Fills <see cref="_batteries"/> with every battery on the networks of the cables under the mob.
    /// </summary>
    private void CollectBatteries(TransformComponent xform)
    {
        _batteries.Clear();
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var ent))
        {
            if (!HasComp<CableComponent>(ent) || !TryComp<NodeContainerComponent>(ent, out var nodes))
                continue;

            foreach (var node in nodes.Nodes.Values)
            {
                switch (node.NodeGroup)
                {
                    case PowerNet power:
                        AddBatteries(power.Dischargers);
                        break;
                    case ApcNet apc:
                        AddBatteries(apc.Apcs);
                        break;
                }
            }
        }
    }

    private void AddBatteries<T>(List<T> devices) where T : Component
    {
        foreach (var device in devices)
        {
            if (HasComp<BatteryComponent>(device.Owner))
                _batteries.Add(device.Owner);
        }
    }

    /// <summary>
    /// Whether a mob evolved from a drainer into <paramref name="proto"/> is still alive.
    /// </summary>
    private bool AnyAlive(string proto)
    {
        var query = EntityQueryEnumerator<FaunaPowerDrainEvolvedComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto && _mobState.IsAlive(uid))
                return true;
        }

        return false;
    }

    private void Evolve(Entity<FaunaPowerDrainComponent> ent, string proto, TransformComponent xform)
    {
        if (ent.Comp.EvolvePopup is { } popup)
            _popup.PopupCoordinates(Loc.GetString(popup, ("user", ent.Owner)), xform.Coordinates,
                PopupType.MediumCaution);

        var evolved = Spawn(proto, _transform.GetMapCoordinates(ent, xform));
        EnsureComp<FaunaPowerDrainEvolvedComponent>(evolved);
        QueueDel(ent);
    }
}
