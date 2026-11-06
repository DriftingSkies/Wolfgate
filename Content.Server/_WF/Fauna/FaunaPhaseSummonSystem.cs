using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Fires the health phases of <see cref="FaunaPhaseSummonComponent"/> mobs.
/// </summary>
public sealed partial class FaunaPhaseSummonSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    private readonly List<(EntityUid Uid, FaunaSummonPhase Phase)> _firing = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaPhaseSummonComponent, DamageChangedEvent>(OnDamageChanged);
    }

    // Phases fire on the next tick, since damage can land inside another system's component loop.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _firing.Clear();
        var query = EntityQueryEnumerator<FaunaPhaseSummonComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var comp, out var damageable))
        {
            if (!comp.Pending)
                continue;

            comp.Pending = false;
            if (!_mobState.IsAlive(uid) || !_thresholds.TryGetDeadThreshold(uid, out var dead))
                continue;

            var health = 1f - (damageable.TotalDamage / dead.Value).Float();
            while (comp.NextPhase < comp.Phases.Count && health < comp.Phases[comp.NextPhase].Fraction)
            {
                _firing.Add((uid, comp.Phases[comp.NextPhase]));
                comp.NextPhase++;
            }
        }

        foreach (var (uid, phase) in _firing)
        {
            if (!TerminatingOrDeleted(uid) && TryComp<FaunaPhaseSummonComponent>(uid, out var comp))
                Fire((uid, comp), phase);
        }
    }

    private void OnDamageChanged(Entity<FaunaPhaseSummonComponent> ent, ref DamageChangedEvent args)
    {
        if (args.DamageIncreased && ent.Comp.NextPhase < ent.Comp.Phases.Count)
            ent.Comp.Pending = true;
    }

    private void Fire(Entity<FaunaPhaseSummonComponent> ent, FaunaSummonPhase phase)
    {
        if (ent.Comp.Effect is { } effect)
            Spawn(effect, Transform(ent).Coordinates);

        if (phase.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.LargeCaution);

        _audio.PlayPvs(phase.Sound, ent);

        if (ent.Comp.TeleportRange > 0 && PickFreeTile(ent, ent.Comp.TeleportRange) is { } landing)
        {
            if (ent.Comp.Gore is { } gore)
                Spawn(gore, Transform(ent).Coordinates);

            _transform.SetCoordinates(ent, landing);
            if (ent.Comp.Effect is { } arrival)
                Spawn(arrival, landing);
        }

        foreach (var proto in phase.Spawns)
        {
            var coords = PickFreeTile(ent, ent.Comp.SpawnRange) ?? Transform(ent).Coordinates;
            Spawn(proto, coords);
        }

        if (phase.ArrivalPopup is { } arrivalPopup)
            _popup.PopupEntity(Loc.GetString(arrivalPopup, ("user", ent.Owner)), ent, PopupType.LargeCaution);
    }

    /// <summary>
    /// A random unblocked floor tile within <paramref name="range"/> of the mob, other than its own. Null off a grid.
    /// </summary>
    private EntityCoordinates? PickFreeTile(EntityUid uid, int range)
    {
        var xform = Transform(uid);
        var grid = xform.ParentUid;
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return null;

        var centre = _map.TileIndicesFor(grid, gridComp, xform.Coordinates);
        if (!_helper.TryPickFreeTile(grid, gridComp, centre, range, FaunaTileShape.SquareNoCentre, out var tile))
            return null;

        return _map.GridTileToLocal(grid, gridComp, tile);
    }
}
