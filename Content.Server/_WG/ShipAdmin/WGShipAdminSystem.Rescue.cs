using Content.Server._WG.Planets;
using Content.Server._WG.Planets.Hop;
using Content.Server._WG.Tether;
using Content.Server._WG.TractorBeam;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.Planets;
using Content.Shared._WG.Tether;
using Content.Shared._WG.TractorBeam;
using Content.Shared.Database;
using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private RopeSystem _ropes = default!;
    [Dependency] private TractorBeamSystem _tractors = default!;
    [Dependency] private WGPlanetHopSystem _hop = default!;

    /// <summary>Whether a ship is down on a planet or flying in its air, below the orbit layer.</summary>
    public bool IsOnPlanet(EntityUid grid)
    {
        return OrbitOf(grid) != null;
    }

    /// <summary>
    /// Sends a ship that is down on a planet, or flying in its air, up to the planet's orbit layer, as the
    /// planet's own pull does: a short hop, taking the ships docked to it along.
    /// </summary>
    public bool ReturnToOrbit(ICommonSession admin, EntityUid grid, out string result)
    {
        if (!CanMove(grid, out result))
            return false;

        if (OrbitOf(grid) is not { } network || !TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            result = Loc.GetString("wg-ship-admin-result-not-on-planet", ("ship", Name(grid)));
            return false;
        }

        if (!_hop.TryStart((grid, shuttle), network.Comp.OrbitMap, _transform.GetWorldPosition(grid), network.Comp.Planet, true, out var reason))
        {
            result = Loc.GetString("wg-ship-admin-result-orbit-refused", ("ship", Name(grid)), ("reason", reason));
            return false;
        }

        // Only once the hop is accepted: a refused one leaves the ship tethered and moving as it was.
        ReleaseBindings(grid);
        Stop(grid);

        Audit(admin, LogImpact.Medium, grid, "sent it back to orbit");
        result = Loc.GetString("wg-ship-admin-result-orbit", ("ship", Name(grid)));
        return true;
    }

    /// <summary>The planet network whose ground or air the ship is on; null in space and in orbit.</summary>
    private Entity<WGPlanetNetworkComponent>? OrbitOf(EntityUid grid)
    {
        if (Transform(grid).MapUid is not { } map
            || HasComp<WGOrbitLayerComponent>(map)
            || !TryComp<WGPlanetLayerComponent>(map, out var layer)
            || !TryGetEntity(layer.Network, out var network)
            || !TryComp<WGPlanetNetworkComponent>(network, out var comp)
            || TerminatingOrDeleted(comp.OrbitMap)
            || comp.OrbitMap == map)
            return null;

        return (network.Value, comp);
    }

    /// <summary>Breaks the ropes and harpoons tying the ship to anything, and lets go of tractor beams on it or from it.</summary>
    private void ReleaseBindings(EntityUid grid)
    {
        var ropes = new List<EntityUid>();
        var ropeQuery = EntityQueryEnumerator<RopeComponent>();
        while (ropeQuery.MoveNext(out var uid, out var rope))
        {
            if (OnGrid(rope.BodyA, grid) || OnGrid(rope.BodyB, grid))
                ropes.Add(uid);
        }

        foreach (var rope in ropes)
            _ropes.BreakRope(rope);

        var beams = EntityQueryEnumerator<TractorBeamEmitterComponent, TransformComponent>();
        while (beams.MoveNext(out var uid, out var beam, out var xform))
        {
            if (beam.Target is not { } target)
                continue;

            if (xform.GridUid == grid || beam.SourceGrid == grid || OnGrid(target, grid))
                _tractors.Release(uid, beam);
        }
    }

    private bool OnGrid(EntityUid? uid, EntityUid grid)
    {
        return uid is { } ent && (ent == grid || CompOrNull<TransformComponent>(ent)?.GridUid == grid);
    }
}
