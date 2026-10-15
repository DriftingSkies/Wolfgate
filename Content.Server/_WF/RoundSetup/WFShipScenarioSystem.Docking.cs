using Content.Server._NF.SectorServices;
using Content.Server.Shuttles.Systems;
using Content.Shared.Station.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFShipScenarioSystem
{
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;

    /// <summary>Uses a collision-checked berth on the sector's central station before charging or spawning players.</summary>
    private bool TryDockAtCentral(EntityUid hull)
    {
        var stations = EntityQueryEnumerator<StationSectorServiceHostComponent, StationDataComponent>();
        while (stations.MoveNext(out _, out _, out var station))
        {
            foreach (var grid in station.Grids)
            {
                if (TerminatingOrDeleted(grid) || !HasComp<MapGridComponent>(grid) || Transform(grid).MapID != _ticker.DefaultMap ||
                    _docking.GetDockingConfig(hull, grid) is not { } config)
                    continue;
                _shuttles.FTLDock((hull, Transform(hull)), config);
                foreach (var (dock, target, component, _) in config.Docks)
                {
                    if (component.DockedWith == target)
                        return true;
                }
            }
        }
        return false;
    }
}
