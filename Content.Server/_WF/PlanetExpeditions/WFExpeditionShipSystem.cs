using Content.Server._NF.Shipyard;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.Planets.Flight;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetExpeditions;

/// <summary>
/// A ship carrying an expedition console is built for the worlds: every linear thruster on it is converted for landing,
/// as the kit converts one. Conversion happens on purchase and keeps up with thrusters built or consoles installed later.
/// </summary>
public sealed class WFExpeditionShipSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    private TimeSpan _nextSweep;
    private readonly HashSet<EntityUid> _expeditionGrids = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MapGridComponent, ShipBoughtEvent>(OnShipBought);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSweep)
            return;
        _nextSweep = _timing.CurTime + SweepInterval;

        _expeditionGrids.Clear();
        var consoles = EntityQueryEnumerator<SalvageExpeditionConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } grid && xform.Anchored)
                _expeditionGrids.Add(grid);
        }

        if (_expeditionGrids.Count == 0)
            return;

        var thrusters = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (thrusters.MoveNext(out var uid, out var thruster, out var xform))
        {
            if (xform.GridUid is { } grid && _expeditionGrids.Contains(grid))
                Convert(uid, thruster);
        }
    }

    private void OnShipBought(EntityUid ship, MapGridComponent grid, ShipBoughtEvent args)
    {
        if (!HasExpeditionConsole(ship))
            return;

        var converted = 0;
        var query = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var thruster, out var xform))
        {
            if (xform.GridUid == ship && Convert(uid, thruster))
                converted++;
        }

        if (converted > 0)
            Log.Info($"Converted {converted} thrusters on {ToPrettyString(ship)} for landing: it carries an expedition console.");
    }

    /// <summary>Converts one linear thruster for landing; false when it is not linear or already converted.</summary>
    private bool Convert(EntityUid uid, ThrusterComponent thruster)
    {
        if (thruster.Type != ThrusterType.Linear || HasComp<WFLandingThrusterComponent>(uid))
            return false;

        EnsureComp<WFLandingThrusterComponent>(uid);
        _thrusters.WfRefreshAtmosphereThruster(uid, thruster);
        return true;
    }

    private bool HasExpeditionConsole(EntityUid ship)
    {
        var query = EntityQueryEnumerator<SalvageExpeditionConsoleComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == ship)
                return true;
        }

        return false;
    }
}
