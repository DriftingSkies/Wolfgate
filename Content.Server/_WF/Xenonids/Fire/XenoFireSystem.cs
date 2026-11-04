using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.RMCCore.Map;
using Content.Shared._WF.Xenonids.Fire;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Xenonids.Fire;

/// <summary>
/// RMC's caste fire immunities and burning weeds on top of upstream <see cref="FlammableComponent"/>.
/// </summary>
public sealed partial class XenoFireSystem : EntitySystem
{
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private RMCMapSystem _rmcMap = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(0.5);
    private static readonly Direction[] Cardinals = [Direction.North, Direction.East, Direction.South, Direction.West];

    private EntityQuery<FlammableComponent> _flammableQuery;
    private EntityQuery<XenoWeedsBurnComponent> _weedsBurnQuery;
    private TimeSpan _nextCheck;
    private readonly List<(EntityUid Weeds, EntityUid Source)> _toIgnite = new();

    public override void Initialize()
    {
        _flammableQuery = GetEntityQuery<FlammableComponent>();
        _weedsBurnQuery = GetEntityQuery<XenoWeedsBurnComponent>();

        SubscribeLocalEvent<RMCImmuneToIgnitionComponent, TryIgniteEvent>(OnImmuneTryIgnite);
        SubscribeLocalEvent<RMCImmuneToFireTileDamageComponent, GetFireProtectionEvent>(OnImmuneGetFireProtection);
        SubscribeLocalEvent<XenoWeedsBurnComponent, IgnitedEvent>(OnWeedsIgnited);
        SubscribeLocalEvent<XenoWeedsBurnComponent, ExtinguishedEvent>(OnWeedsExtinguished);
    }

    private void OnWeedsIgnited(Entity<XenoWeedsBurnComponent> ent, ref IgnitedEvent args)
    {
        EnsureComp<XenoWeedsBurningComponent>(ent);
    }

    private void OnWeedsExtinguished(Entity<XenoWeedsBurnComponent> ent, ref ExtinguishedEvent args)
    {
        ent.Comp.SpreadAt = null;
        ent.Comp.Spread = false;
        RemCompDeferred<XenoWeedsBurningComponent>(ent);
    }

    private void OnImmuneTryIgnite(Entity<RMCImmuneToIgnitionComponent> ent, ref TryIgniteEvent args)
    {
        args.Cancelled = true;
    }

    private void OnImmuneGetFireProtection(Entity<RMCImmuneToFireTileDamageComponent> ent, ref GetFireProtectionEvent args)
    {
        args.Multiplier *= ent.Comp.BurnDamageMultiplier;
    }

    public override void Update(float frameTime)
    {
        var time = _timing.CurTime;
        if (time < _nextCheck)
            return;

        _nextCheck = time + CheckEvery;

        // stacks added without Ignite skip TryIgniteEvent
        var immune = EntityQueryEnumerator<RMCImmuneToIgnitionComponent, FlammableComponent>();
        while (immune.MoveNext(out var uid, out _, out var flammable))
        {
            if (flammable.OnFire)
                _flammable.Extinguish(uid, flammable);
        }

        // Only weeds on fire carry the burning marker; unlit weeds are skipped. Igniting adds the marker, so
        // neighbours are lit after the walk.
        _toIgnite.Clear();
        var weeds = EntityQueryEnumerator<XenoWeedsBurningComponent, XenoWeedsBurnComponent, FlammableComponent, TransformComponent>();
        while (weeds.MoveNext(out var uid, out _, out var burn, out var flammable, out var xform))
        {
            if (!flammable.OnFire)
            {
                burn.SpreadAt = null;
                burn.Spread = false;
                RemCompDeferred<XenoWeedsBurningComponent>(uid);
                continue;
            }

            if (burn.Spread)
                continue;

            burn.SpreadAt ??= time + burn.SpreadDelay;
            if (time < burn.SpreadAt || !xform.Anchored)
                continue;

            burn.Spread = true;
            foreach (var dir in Cardinals)
            {
                var anchored = _rmcMap.GetAnchoredEntitiesEnumerator(uid, dir);
                while (anchored.MoveNext(out var neighbour))
                {
                    if (!_weedsBurnQuery.HasComp(neighbour) ||
                        !_flammableQuery.TryComp(neighbour, out var other) ||
                        other.OnFire)
                    {
                        continue;
                    }

                    _toIgnite.Add((neighbour, uid));
                }
            }
        }

        foreach (var (target, source) in _toIgnite)
        {
            if (!_flammableQuery.TryComp(target, out var other) || other.OnFire)
                continue;

            _flammable.SetFireStacks(target, other.MaximumFireStacks, other);
            _flammable.Ignite(target, source, other);
        }
    }
}
