using Content.Server.Emoting;
using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Emoting;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs the wind-up and the hit of <see cref="FaunaSlamComponent"/>.
/// </summary>
public sealed partial class FaunaSlamSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private AnimatedEmotesSystem _emotes = default!;

    private readonly HashSet<Entity<MobStateComponent>> _hits = new();
    private readonly HashSet<Entity<DamageableComponent>> _structures = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaSlamComponent, FaunaSlamActionEvent>(OnSlamAction);
    }

    private void OnSlamAction(Entity<FaunaSlamComponent> ent, ref FaunaSlamActionEvent args)
    {
        if (args.Handled || ent.Comp.SlamAt != null)
            return;

        args.Handled = true;
        var origin = _transform.GetWorldPosition(ent);
        var target = _transform.GetWorldPosition(args.Target);
        ent.Comp.Direction = (target - origin).ToWorldAngle();
        ent.Comp.SlamAt = _timing.CurTime + ent.Comp.Delay;
        _popup.PopupEntity(Loc.GetString("fauna-slam-windup", ("user", ent.Owner)), ent, PopupType.MediumCaution);

        if (ent.Comp.Telegraph is { } telegraph)
            SpawnOnCone(ent, telegraph);

        if (ent.Comp.Emote is { } emote)
            _emotes.PlayEmoteAnimation(ent, EnsureComp<AnimatedEmotesComponent>(ent), emote);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaSlamComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.SlamAt is not { } slamAt || slamAt > now)
                continue;

            comp.SlamAt = null;
            if (_mobState.IsAlive(uid))
                Slam((uid, comp));
        }
    }

    private void Slam(Entity<FaunaSlamComponent> ent)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var halfArc = Angle.FromDegrees(ent.Comp.Arc / 2);

        _audio.PlayPvs(ent.Comp.HitSound, ent);
        if (ent.Comp.Effect is { } effect)
            SpawnOnCone(ent, effect);

        _hits.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.Range, _hits);
        foreach (var hit in _hits)
        {
            if (hit.Owner == ent.Owner || !_mobState.IsAlive(hit))
                continue;

            var direction = (_transform.GetWorldPosition(hit) - origin.Position).ToWorldAngle();
            if (Math.Abs(Angle.ShortestDistance(ent.Comp.Direction, direction).Degrees) > halfArc.Degrees)
                continue;

            _damageable.TryChangeDamage(hit, ent.Comp.Damage, origin: ent);
            _stun.TryKnockdown(hit, ent.Comp.Knockdown, true);
            if (ent.Comp.Throw > 0)
                _throwing.TryThrow(hit, direction.ToWorldVec() * ent.Comp.Throw, 10f, ent, pushbackRatio: 0f);
        }

        if (ent.Comp.StructureDamage is not { } structureDamage)
            return;

        _structures.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.Range, _structures, LookupFlags.Static);
        foreach (var structure in _structures)
        {
            if (!Transform(structure).Anchored || HasComp<MobStateComponent>(structure))
                continue;

            var direction = (_transform.GetWorldPosition(structure) - origin.Position).ToWorldAngle();
            if (Math.Abs(Angle.ShortestDistance(ent.Comp.Direction, direction).Degrees) <= halfArc.Degrees)
                _damageable.TryChangeDamage(structure, structureDamage, origin: ent);
        }
    }

    /// <summary>
    /// Spawns a prototype on every floor tile whose centre lies inside the slam's cone.
    /// </summary>
    private void SpawnOnCone(Entity<FaunaSlamComponent> ent, EntProtoId proto)
    {
        var xform = Transform(ent);
        if (xform.GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        var origin = _transform.GetWorldPosition(xform);
        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var radius = (int) MathF.Ceiling(ent.Comp.Range);
        var halfArc = ent.Comp.Arc / 2;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                var indices = center + new Vector2i(x, y);
                if (!_map.TryGetTileRef(gridUid, grid, indices, out var tile) || tile.Tile.IsEmpty)
                    continue;

                var coords = _map.GridTileToLocal(gridUid, grid, indices);
                var offset = _transform.ToMapCoordinates(coords).Position - origin;
                if (offset.Length() > ent.Comp.Range)
                    continue;

                if ((x != 0 || y != 0) &&
                    Math.Abs(Angle.ShortestDistance(ent.Comp.Direction, offset.ToWorldAngle()).Degrees) > halfArc)
                    continue;

                Spawn(proto, coords);
            }
        }
    }
}
