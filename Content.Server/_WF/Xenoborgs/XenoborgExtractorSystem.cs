using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Xenoborgs;
using Content.Shared.Construction.Components;
using Content.Shared.EntityTable;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.Station;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Xenoborgs;

/// <summary>
/// wizden's xenoborg extractor: anchored only on a station's grid, it charges its battery from the power it draws
/// (<see cref="PowerConsumerBatteryChargerComponent"/>) and spawns crystals each time the battery fills
/// (<see cref="SpawnOnBatteryFullComponent"/>).
/// </summary>
public sealed partial class XenoborgExtractorSystem : EntitySystem
{
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private EntityTableSystem _entityTable = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AnchorOnlyOnStationComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<GridSplitEvent>(OnGridSplit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PowerConsumerComponent, PowerConsumerBatteryChargerComponent, BatteryComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var consumer, out var charger, out var battery, out var xform))
        {
            var consuming = xform.Anchored && consumer.ReceivedPower > 0;
            _appearance.SetData(uid, XenoborgExtractorVisuals.Consuming, consuming);
            if (!consuming)
                continue;

            var charge = battery.CurrentCharge + consumer.ReceivedPower * frameTime * charger.Efficiency;
            if (charge < battery.MaxCharge || !TryComp<SpawnOnBatteryFullComponent>(uid, out var spawner))
            {
                _battery.SetCharge(uid, Math.Min(charge, battery.MaxCharge), battery);
                continue;
            }

            SpawnProduct((uid, spawner), xform);
            _battery.SetCharge(uid, 0, battery);
        }
    }

    private void SpawnProduct(Entity<SpawnOnBatteryFullComponent> ent, TransformComponent xform)
    {
        if (ent.Comp.Proto is { } proto)
        {
            Spawn(proto, xform.Coordinates);
            return;
        }

        foreach (var spawn in _entityTable.GetSpawns(ent.Comp.Table))
        {
            Spawn(spawn, xform.Coordinates);
        }
    }

    private void OnAnchorAttempt(Entity<AnchorOnlyOnStationComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled || IsOnStation(ent))
            return;

        _popup.PopupEntity(Loc.GetString(ent.Comp.PopupMessageAnchorFail), ent, args.User);
        args.Cancel();
    }

    private void OnGridSplit(ref GridSplitEvent args)
    {
        var grids = args.NewGrids.ToHashSet();
        var query = AllEntityQuery<AnchorOnlyOnStationComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.Anchored && xform.GridUid is { } grid && grids.Contains(grid) && !IsOnStation(uid))
                _transform.Unanchor(uid, xform);
        }
    }

    /// <summary>
    /// True when the entity's grid belongs to a station. Every Frontier station is one grid, so wizden's
    /// largest-grid option has nothing to choose between.
    /// </summary>
    private bool IsOnStation(EntityUid uid)
    {
        return _station.GetOwningStation(uid) != null;
    }
}
