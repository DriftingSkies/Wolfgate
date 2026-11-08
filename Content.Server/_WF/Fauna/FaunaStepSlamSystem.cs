using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Runs the step shockwave of <see cref="FaunaStepSlamComponent"/>.
/// </summary>
public sealed partial class FaunaStepSlamSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaStepSlamComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var gridComp))
                continue;

            var tile = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
            if (comp.LastTile == tile)
                continue;

            comp.LastTile = tile;
            if (now < comp.NextSlam || !_mobState.IsAlive(uid))
                continue;

            var range = TryComp<FaunaEnrageComponent>(uid, out var enrage) && enrage.Enraged
                ? comp.EnragedRange
                : comp.Range;
            var origin = _transform.GetWorldPosition(xform);
            _mobs.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, range, _mobs);
            var hit = false;
            foreach (var mob in _mobs)
            {
                if (mob.Owner == uid || !_mobState.IsAlive(mob, mob.Comp))
                    continue;

                hit = true;
                if (comp.Damage != null)
                    _damageable.TryChangeDamage(mob.Owner, comp.Damage, origin: uid);

                var away = _transform.GetWorldPosition(mob) - origin;
                if (away.LengthSquared() > 0.01f)
                    _throwing.TryThrow(mob, away.Normalized() * comp.Throw, 10f, uid, pushbackRatio: 0f);

                if (comp.Popup is { } popup)
                    _popup.PopupEntity(Loc.GetString(popup, ("user", uid)), mob, mob, PopupType.MediumCaution);
            }

            if (!hit)
                continue;

            comp.NextSlam = now + comp.Cooldown;
            _audio.PlayPvs(comp.Sound, uid);
        }
    }
}
