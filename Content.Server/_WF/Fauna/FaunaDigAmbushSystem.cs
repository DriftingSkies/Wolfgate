using Content.Shared._WF.Fauna;
using Content.Shared.ActionBlocker;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Digs <see cref="FaunaDigAmbushComponent"/> mobs in and erupts them beside their target, or away from danger.
/// </summary>
public sealed partial class FaunaDigAmbushSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    private const string ContainerId = "fauna_dig_ambush";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaDigAmbushComponent, FaunaDigAmbushActionEvent>(OnAction);
        SubscribeLocalEvent<FaunaDigAmbushComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaDigAmbushComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.FleePending && now >= comp.FleeAt)
            {
                comp.FleePending = false;
                if (_mobState.IsAlive(uid) && _actionBlocker.CanMove(uid))
                    TryDig((uid, comp), null);
            }

            if (comp.HoleEntity is not { } hole)
                continue;

            if (Deleted(hole))
            {
                comp.HoleEntity = null;
                continue;
            }

            if (comp.Heal != null && now >= comp.NextHeal)
            {
                _damageable.TryChangeDamage(uid, comp.Heal, true);
                comp.NextHeal = now + TimeSpan.FromSeconds(1);
            }

            if (now >= comp.EruptAt)
                Erupt((uid, comp), hole);
        }
    }

    private void OnAction(Entity<FaunaDigAmbushComponent> ent, ref FaunaDigAmbushActionEvent args)
    {
        if (!args.Handled && TryDig(ent, args.Target))
            args.Handled = true;
    }

    private void OnDamageChanged(Entity<FaunaDigAmbushComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.FleeHealth <= 0 || !args.DamageIncreased || ent.Comp.FleePending || ent.Comp.HoleEntity != null ||
            _timing.CurTime < ent.Comp.NextFlee || !_mobState.IsAlive(ent) ||
            !_thresholds.TryGetDeadThreshold(ent, out var dead) ||
            1f - (args.Damageable.TotalDamage / dead.Value).Float() > ent.Comp.FleeHealth)
            return;

        ent.Comp.FleePending = true;
        ent.Comp.FleeAt = _timing.CurTime + ent.Comp.FleeDelay;
        ent.Comp.NextFlee = ent.Comp.FleeAt + ent.Comp.FleeCooldown;
    }

    /// <summary>
    /// Digs the mob in. A null target means it is fleeing and comes up away from the hole.
    /// </summary>
    private bool TryDig(Entity<FaunaDigAmbushComponent> ent, EntityUid? target)
    {
        if (ent.Comp.HoleEntity != null || _container.IsEntityInContainer(ent))
            return false;

        var coords = Transform(ent).Coordinates;
        if (!AllowedTile(ent, coords))
            return false;

        var hole = Spawn(ent.Comp.Hole, coords);
        var container = _container.EnsureContainer<ContainerSlot>(hole, ContainerId);
        if (!_container.Insert(ent.Owner, container))
        {
            QueueDel(hole);
            return false;
        }

        ent.Comp.HoleEntity = hole;
        ent.Comp.Target = target;
        ent.Comp.Fleeing = target == null;
        ent.Comp.EruptAt = _timing.CurTime + ent.Comp.Duration;
        ent.Comp.NextHeal = _timing.CurTime + TimeSpan.FromSeconds(1);
        Effects(ent, coords);
        _popup.PopupCoordinates(Loc.GetString("fauna-dig-ambush-burrow", ("user", ent.Owner)), coords);
        return true;
    }

    private void Erupt(Entity<FaunaDigAmbushComponent> ent, EntityUid hole)
    {
        ent.Comp.HoleEntity = null;
        var coords = ent.Comp.Fleeing
            ? PickSpot(ent, hole, hole, ent.Comp.FleeDistance, true)
            : PickEruptSpot(ent, hole);
        ent.Comp.Fleeing = false;
        _container.TryRemoveFromContainer(ent.Owner);
        _transform.SetCoordinates(ent, coords);
        QueueDel(hole);

        Effects(ent, coords);
        _popup.PopupCoordinates(Loc.GetString("fauna-dig-ambush-erupt", ("user", ent.Owner)), coords,
            PopupType.MediumCaution);

        if (ent.Comp.EruptDamage == null && ent.Comp.EruptKnockdown == null
            || ent.Comp.Target is not { } target
            || Deleted(target))
            return;

        if (!_transform.InRange(Transform(target).Coordinates, coords, 1.5f))
            return;

        if (ent.Comp.EruptDamage is { } damage)
            _damageable.TryChangeDamage(target, damage, origin: ent);

        if (ent.Comp.EruptKnockdown is { } knockdown)
            _stun.TryKnockdown(target, knockdown, true);

        _popup.PopupEntity(Loc.GetString("fauna-dig-ambush-tear", ("user", ent.Owner), ("target", target)), target,
            PopupType.MediumCaution);
    }

    /// <summary>
    /// A random free tile next to the target, or the hole itself when there is none.
    /// </summary>
    private EntityCoordinates PickEruptSpot(Entity<FaunaDigAmbushComponent> ent, EntityUid hole)
    {
        if (ent.Comp.Target is not { } target || Deleted(target))
            return Transform(hole).Coordinates;

        return PickSpot(ent, hole, target, 1, false);
    }

    /// <summary>
    /// A random free, allowed tile around <paramref name="centre"/>, within <paramref name="range"/> or exactly that
    /// far when <paramref name="ring"/> is set. Falls back to the hole.
    /// </summary>
    private EntityCoordinates PickSpot(Entity<FaunaDigAmbushComponent> ent, EntityUid hole, EntityUid centre, int range,
        bool ring)
    {
        var fallback = Transform(hole).Coordinates;
        var xform = Transform(centre);
        var grid = xform.ParentUid;
        if (grid != Transform(hole).ParentUid || !TryComp<MapGridComponent>(grid, out var gridComp))
            return fallback;

        var middle = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        var shape = ring ? FaunaTileShape.Ring : FaunaTileShape.Square;
        if (!_helper.TryPickFreeTile(grid, gridComp, middle, range, shape, out var pick,
                predicate: tile => AllowedTile(ent, _map.GridTileToLocal(grid, gridComp, tile))))
            return fallback;

        return _map.GridTileToLocal(grid, gridComp, pick);
    }

    /// <summary>
    /// Whether the mob may dig at a spot, given <see cref="FaunaDigAmbushComponent.Tiles"/>.
    /// </summary>
    private bool AllowedTile(Entity<FaunaDigAmbushComponent> ent, EntityCoordinates coords)
    {
        if (ent.Comp.Tiles.Count == 0)
            return true;

        return _turf.TryGetTileRef(coords, out var tile) &&
               ent.Comp.Tiles.Contains(_turf.GetContentTileDefinition(tile.Value).ID);
    }

    private void Effects(Entity<FaunaDigAmbushComponent> ent, EntityCoordinates coords)
    {
        _audio.PlayPvs(ent.Comp.Sound, coords);
        if (ent.Comp.Splash is { } splash)
            Spawn(splash, coords);
    }
}
