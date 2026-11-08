using System.Numerics;
using Content.Shared._WF.Fauna;
using Content.Shared.Jittering;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Winds up and fires the lines of <see cref="FaunaLineAttackComponent"/> mobs.
/// </summary>
public sealed partial class FaunaLineAttackSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaLineAttackComponent, FaunaLineAttackActionEvent>(OnAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaLineAttackComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IdleAt != TimeSpan.Zero && now >= comp.IdleAt)
            {
                comp.IdleAt = TimeSpan.Zero;
                _appearance.SetData(uid, FaunaLineAttackVisuals.State, FaunaLineAttackState.Idle);
            }

            if (comp.FireAt == null && comp.Tiles.Count == 0 && comp.StrikeTiles.Count == 0)
                continue;

            if (_mobState.IsDead(uid))
            {
                if (comp.FireAt != null)
                    StopJitter((uid, comp));

                comp.FireAt = null;
                comp.Tiles.Clear();
                comp.StrikeTiles.Clear();
                comp.IdleAt = TimeSpan.Zero;
                _appearance.SetData(uid, FaunaLineAttackVisuals.State, FaunaLineAttackState.Idle);
                continue;
            }

            if (comp.StrikeTiles.Count > 0 && comp.Tiles.Count == 0 && now >= comp.StrikeAt)
                Strike((uid, comp));

            if (comp.FireAt is { } fireAt && now >= fireAt)
            {
                comp.FireAt = null;
                StopJitter((uid, comp));
                Fire((uid, comp));
                comp.NextStep = now;
                comp.FirstTile = true;
                if (comp.Tiles.Count == 0)
                    Finish((uid, comp), now);
            }

            while (comp.Tiles.Count > 0 && now >= comp.NextStep)
            {
                var tile = comp.Tiles.Dequeue();
                var proto = Pick(comp.Effect, comp.EffectStart, comp.EffectEnd, comp.FirstTile, comp.Tiles.Count == 0);
                comp.FirstTile = false;
                SpawnEffect(comp, proto, tile);
                if (comp.Strike != null)
                {
                    comp.StrikeTiles.Add(tile);
                    comp.StrikeAt = now + comp.StrikeDelay;
                }
                else if (comp.Tiles.Count == 0)
                {
                    Finish((uid, comp), now);
                }

                comp.NextStep = now + comp.StepDelay;
                if (comp.StepDelay > TimeSpan.Zero)
                    break;
            }
        }
    }

    private void OnAction(Entity<FaunaLineAttackComponent> ent, ref FaunaLineAttackActionEvent args)
    {
        if (args.Handled || ent.Comp.FireAt != null || ent.Comp.Tiles.Count > 0 || ent.Comp.StrikeTiles.Count > 0)
            return;

        if (ent.Comp.Cardinal)
        {
            var offset = _transform.GetWorldPosition(args.Target) - _transform.GetWorldPosition(ent);
            if (MathF.Min(MathF.Abs(offset.X), MathF.Abs(offset.Y)) > 0.5f)
                return;
        }

        args.Handled = true;
        ent.Comp.Target = args.Target;
        ent.Comp.FireAt = _timing.CurTime + ent.Comp.Windup;
        ent.Comp.IdleAt = TimeSpan.Zero;
        _appearance.SetData(ent, FaunaLineAttackVisuals.State, FaunaLineAttackState.Charging);
        _audio.PlayPvs(ent.Comp.WindupSound, ent);
        if (ent.Comp.WindupJitter > 0f)
            _jitter.AddJitter(ent, ent.Comp.WindupJitter, 8f);

        if (ent.Comp.WindupPopup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);
    }

    private void StopJitter(Entity<FaunaLineAttackComponent> ent)
    {
        if (ent.Comp.WindupJitter > 0f)
            RemComp<JitteringComponent>(ent);
    }

    private void Fire(Entity<FaunaLineAttackComponent> ent)
    {
        var xform = Transform(ent);
        if (ent.Comp.Target is not { } target || Deleted(target)
            || !TryComp<MapGridComponent>(xform.ParentUid, out var grid))
            return;

        var parent = xform.ParentUid;
        var origin = xform.LocalPosition;
        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(ent);
        if (direction.LengthSquared() < 0.01f)
            return;

        direction = (-_transform.GetWorldRotation(xform.ParentUid)).RotateVec(direction);
        if (ent.Comp.Cardinal)
        {
            direction = MathF.Abs(direction.X) >= MathF.Abs(direction.Y)
                ? new Vector2(MathF.Sign(direction.X), 0)
                : new Vector2(0, MathF.Sign(direction.Y));
        }

        direction = Vector2.Normalize(direction);
        ent.Comp.LineAngle = direction.ToWorldAngle();
        var last = _map.LocalToTile(parent, grid, xform.Coordinates);
        for (var step = 1; step <= ent.Comp.Range * 2 && ent.Comp.Tiles.Count < ent.Comp.Range; step++)
        {
            var point = new EntityCoordinates(parent, origin + direction * step * 0.5f);
            var tile = _map.LocalToTile(parent, grid, point);
            if (tile == last)
                continue;

            if (_turf.IsTileBlocked(parent, tile, CollisionGroup.Impassable, grid))
                break;

            last = tile;
            ent.Comp.Tiles.Enqueue(_map.GridTileToLocal(parent, grid, tile));
        }

        _audio.PlayPvs(ent.Comp.FireSound, ent);
    }

    private void Strike(Entity<FaunaLineAttackComponent> ent)
    {
        if (ent.Comp.Strike is { } strike)
        {
            var last = ent.Comp.StrikeTiles.Count - 1;
            for (var i = 0; i <= last; i++)
            {
                var proto = Pick(strike, ent.Comp.StrikeStart, ent.Comp.StrikeEnd, i == 0, i == last);
                SpawnEffect(ent.Comp, proto, ent.Comp.StrikeTiles[i]);
            }

            _audio.PlayPvs(ent.Comp.StrikeSound, ent);
        }

        ent.Comp.StrikeTiles.Clear();
        Finish(ent, _timing.CurTime);
    }

    /// <summary>
    /// Shows the firing look for <see cref="FaunaLineAttackComponent.FiringTime"/>, then goes idle.
    /// </summary>
    private void Finish(Entity<FaunaLineAttackComponent> ent, TimeSpan now)
    {
        if (ent.Comp.FiringTime <= TimeSpan.Zero)
        {
            _appearance.SetData(ent, FaunaLineAttackVisuals.State, FaunaLineAttackState.Idle);
            return;
        }

        _appearance.SetData(ent, FaunaLineAttackVisuals.State, FaunaLineAttackState.Firing);
        ent.Comp.IdleAt = now + ent.Comp.FiringTime;
    }

    private static EntProtoId Pick(EntProtoId middle, EntProtoId? start, EntProtoId? end, bool first, bool last)
    {
        if (last && end is { } endProto)
            return endProto;

        if (first && start is { } startProto)
            return startProto;

        return middle;
    }

    private void SpawnEffect(FaunaLineAttackComponent comp, EntProtoId proto, EntityCoordinates coords)
    {
        var effect = Spawn(proto, coords);
        if (comp.RotateEffects)
            _transform.SetLocalRotation(effect, comp.LineAngle);
    }
}
