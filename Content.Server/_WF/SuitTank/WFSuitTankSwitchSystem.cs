using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.SuitTank;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.SuitTank;

/// <summary>
/// Moves internals to a fresh tank in the worn suit when the connected suit tank runs low.
/// </summary>
public sealed class WFSuitTankSwitchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GasTankSystem _gasTank = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private WFSuitTankStorageSystem _suitTank = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        var query = EntityQueryEnumerator<InternalsComponent>();
        while (query.MoveNext(out var uid, out var internals))
        {
            if (!TryComp<GasTankComponent>(internals.GasTankEntity, out var current)
                || !current.IsLowPressure
                || !_suitTank.TryGetWearer(internals.GasTankEntity.Value, out var wearer)
                || wearer != uid)
            {
                continue;
            }

            var tank = internals.GasTankEntity.Value;
            if (_suitTank.FindFullestTank(uid, except: tank) is not { } next || next.Comp.IsLowPressure)
                continue;

            _gasTank.DisconnectFromInternals((tank, current), forced: true);
            if (_gasTank.ConnectToInternals(next))
                _popup.PopupEntity(Loc.GetString("wf-suit-tank-switched"), uid, uid);
        }
    }
}
