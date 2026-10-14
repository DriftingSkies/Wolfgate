using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets;
using Content.Shared._WF.Planets;
using Content.Shared._WF.PlanetSignals;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetSignals;

/// <summary>Keeps each sector body's list of the vessels on its world's layers and the sites on its ground.</summary>
public sealed class WFPlanetSignalsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedStationSystem _station = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>Grids smaller than this many square metres are debris, not vessels.</summary>
    private const float MinVesselArea = 6f;

    private TimeSpan _next;
    private readonly Dictionary<EntityUid, (EntityUid Body, WFPlanetSignalLayer Layer)> _layers = new();
    private readonly Dictionary<EntityUid, List<WFPlanetSignal>> _found = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        _layers.Clear();
        _found.Clear();

        var networks = EntityQueryEnumerator<WFPlanetNetworkComponent>();
        while (networks.MoveNext(out var network, out var comp))
        {
            if (comp.Planet is not { } body || !HasComp<WFSectorPlanetComponent>(body))
                continue;

            var signals = new List<WFPlanetSignal>();
            _found[body] = signals;
            foreach (var layer in comp.Layers.Append(comp.OrbitMap).Append(comp.GroundMap))
            {
                _layers[layer] = (body, layer == comp.OrbitMap ? WFPlanetSignalLayer.Orbit
                    : layer == comp.GroundMap ? WFPlanetSignalLayer.Ground
                    : WFPlanetSignalLayer.Air);
            }

            AddSites(comp.GroundMap, signals);
        }

        var grids = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (grids.MoveNext(out var grid, out var gridComp, out var xform))
        {
            if (xform.MapUid is not { } map || map == grid || !_layers.TryGetValue(map, out var at)
                || gridComp.LocalAABB.Width * gridComp.LocalAABB.Height < MinVesselArea)
                continue;

            var iff = CompOrNull<IFFComponent>(grid);
            if (iff != null && (iff.Flags & IFFFlags.Hide) != 0)
                continue;

            var name = iff != null && (iff.Flags & IFFFlags.HideLabel) != 0 ? string.Empty : Name(grid);
            var centre = Vector2.Transform(gridComp.LocalAABB.Center, _transform.GetWorldMatrix(xform));
            _found[at.Body].Add(new WFPlanetSignal(WFPlanetSignalKind.Vessel, name, centre.Floored(), GetNetEntity(map), at.Layer, null));
        }

        foreach (var (body, signals) in _found)
        {
            // A steady order, so an unchanged world is never resent.
            signals.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
                : a.Layer != b.Layer ? a.Layer.CompareTo(b.Layer)
                : string.CompareOrdinal(a.Name, b.Name) is var name and not 0 ? name
                : a.Position.X != b.Position.X ? a.Position.X.CompareTo(b.Position.X)
                : a.Position.Y.CompareTo(b.Position.Y));

            var comp = EnsureComp<WFPlanetSignalsComponent>(body);
            if (comp.Signals.SequenceEqual(signals))
                continue;

            comp.Signals = signals;
            Dirty(body, comp);
        }

        // A world whose network is gone shows nothing.
        var stale = EntityQueryEnumerator<WFPlanetSignalsComponent>();
        while (stale.MoveNext(out var body, out var comp))
        {
            if (_found.ContainsKey(body) || comp.Signals.Count == 0)
                continue;

            comp.Signals = new List<WFPlanetSignal>();
            Dirty(body, comp);
        }
    }

    /// <summary>Every site on a ground, named once identified, with the grid of any ship running an expedition there.</summary>
    private void AddSites(EntityUid ground, List<WFPlanetSignal> signals)
    {
        if (!TryComp<WFPlanetPoiComponent>(ground, out var record))
            return;

        var map = GetNetEntity(ground);
        foreach (var site in record.Sites)
        {
            if (!TryComp<WFPoiSignalComponent>(site.Signal, out var signal))
                continue;

            NetEntity? expedition = null;
            if (site.ExpeditionClaim is { } station && _station.GetLargestGrid(station) is { } ship)
                expedition = GetNetEntity(ship);

            signals.Add(new WFPlanetSignal(WFPlanetSignalKind.Site, signal.Revealed ? site.Name.Id : string.Empty,
                signal.Origin, map, WFPlanetSignalLayer.Ground, expedition));
        }
    }
}
