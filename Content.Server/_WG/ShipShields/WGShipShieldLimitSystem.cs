using Content.Server._WG.HullBond;
using Content.Shared.Construction.Components;
using Content.Shared.Popups;

namespace Content.Server._WG.ShipShields;

/// <summary>
/// Keeps a ship to one shield generator: another refuses to bolt down beside it, whether by wrench, flatpack or
/// construction. A hull loaded from a save never map-inits, so one saved with two keeps them.
/// </summary>
public sealed class WGShipShieldLimitSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Ahead of the hull bond, so a generator turned away never bonds to this hull.
        SubscribeLocalEvent<WGShipShieldCostComponent, MapInitEvent>(OnMapInit, before: new[] { typeof(WGHullBondSystem) });
        SubscribeLocalEvent<WGShipShieldCostComponent, AnchorStateChangedEvent>(OnAnchorChanged, before: new[] { typeof(WGHullBondSystem) });
        SubscribeLocalEvent<WGShipShieldCostComponent, AnchorAttemptEvent>(OnAnchorAttempt);
    }

    /// <summary>Another ship generator bolted down on the hull this one stands on, if any.</summary>
    public EntityUid? FindOtherGenerator(EntityUid uid)
    {
        if (Transform(uid).GridUid is not { } grid)
            return null;

        var query = EntityQueryEnumerator<WGShipShieldCostComponent, TransformComponent>();
        while (query.MoveNext(out var other, out _, out var xform))
        {
            if (other != uid && xform.Anchored && xform.GridUid == grid && !TerminatingOrDeleted(other))
                return other;
        }
        return null;
    }

    private void OnAnchorAttempt(Entity<WGShipShieldCostComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled || FindOtherGenerator(ent) is not { } other)
            return;

        _popup.PopupEntity(RefusalText(other), ent, args.User);
        args.Cancel();
    }

    private void OnMapInit(Entity<WGShipShieldCostComponent> ent, ref MapInitEvent args)
    {
        TurnAway(ent);
    }

    private void OnAnchorChanged(Entity<WGShipShieldCostComponent> ent, ref AnchorStateChangedEvent args)
    {
        // Bolted down by construction, a rebuild or a wrench that started before the other came down.
        if (args.Anchored && LifeStage(ent) >= EntityLifeStage.MapInitialized)
            TurnAway(ent);
    }

    /// <summary>Unbolts a generator that came down beside another.</summary>
    private void TurnAway(EntityUid uid)
    {
        var xform = Transform(uid);
        if (!xform.Anchored || FindOtherGenerator(uid) is not { } other)
            return;

        _transform.Unanchor(uid, xform);
        _popup.PopupEntity(RefusalText(other), uid, PopupType.MediumCaution);
    }

    private string RefusalText(EntityUid other)
    {
        return Loc.GetString("wg-shield-limit-anchor-fail", ("generator", Name(other)));
    }
}
