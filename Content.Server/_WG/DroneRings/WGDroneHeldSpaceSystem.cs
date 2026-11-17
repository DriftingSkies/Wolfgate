using Content.Server._WG.SectorControl.Systems;
using Content.Shared._WG.DroneRings;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.DroneRings;

/// <summary>Keeps drones from spawning in sector cells a faction holds or has just freed.</summary>
public sealed partial class WGDroneHeldSpaceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;

    private HashSet<string>? _sources;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGDebrisPlacingEvent>(OnPlacing);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnReloaded);
    }

    private void OnReloaded(PrototypesReloadedEventArgs args)
    {
        _sources = null;
    }

    private void OnPlacing(ref WGDebrisPlacingEvent args)
    {
        if (Sources().Contains(args.Proto) && _territory.IsHeldOrClearing(args.At))
            args.Cancelled = true;
    }

    /// <summary>The debris-table entries that spawn drones.</summary>
    private HashSet<string> Sources()
    {
        if (_sources != null)
            return _sources;

        _sources = new HashSet<string>();
        foreach (var list in _prototypes.EnumeratePrototypes<WGDroneSourcesPrototype>())
        {
            foreach (var spawner in list.Spawners)
            {
                _sources.Add(spawner);
            }
        }

        return _sources;
    }
}
