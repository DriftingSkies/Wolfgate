using System.Numerics;
using Content.Server._WF.Planets;
using Content.Server._WF.Planets.Flight;
using Content.Shared._WF.Planets;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RoundSetup;

/// <summary>
/// Where a crash comes down: a sanctioned world of at most 3 g, Carcinoma only when the crew opts in, and a spot in
/// orbit clear of every outpost's claim and of other crash sites on that world.
/// </summary>
public sealed partial class WFCrashScenarioSystem
{
    /// <summary>Worlds pulling harder than this, in gees, are never crash sites.</summary>
    public const float MaxGravity = 3f;

    /// <summary>The world a crew only crashes on when it opts in.</summary>
    public static readonly ProtoId<WFPlanetSurfacePrototype> CarcinomaSurface = "WFSurfaceCarcinoma";

    /// <summary>Metres kept between a crash hull and any outpost's claim zone.</summary>
    public const float ClaimClearance = 32f;

    /// <summary>Metres kept between a crash hull and another crew's crash on the same world.</summary>
    public const float SiteSpacing = 64f;

    /// <summary>Spots in orbit tried for a clear site before the hull is left at the last one.</summary>
    private const int SiteAttempts = 24;

    /// <summary>Whether a crew may crash on this world. Carcinoma, never sanctioned, takes the crew's opt-in instead.</summary>
    public bool IsCrashWorld(Entity<WFSectorPlanetComponent> planet, bool carcinoma)
    {
        if (planet.Comp.Surface is not { } surface
            || (surface == CarcinomaSurface ? !carcinoma : !planet.Comp.Sanctioned))
            return false;

        return GetGravity(planet, surface) <= MaxGravity;
    }

    /// <summary>The world's gravity, live off its ground layer once built (Planet Control can change it).</summary>
    private float GetGravity(Entity<WFSectorPlanetComponent> planet, ProtoId<WFPlanetSurfacePrototype> surface)
    {
        if (planet.Comp.Network is { } net && TryGetEntity(net, out var network)
            && TryComp<WFPlanetNetworkComponent>(network, out var built)
            && TryComp<WFPlanetLayerComponent>(built.GroundMap, out var ground))
            return ground.Gravity;

        return _prototypes.TryIndex(surface, out var proto) ? proto.Gravity : 1f;
    }

    /// <summary>
    /// Moves a freshly loaded hull round orbit until it sits clear of claim zones and other crash sites. False when no
    /// clear spot turned up; the hull is then left at the last one tried and the landing guard shifts it off any claim.
    /// </summary>
    private bool TryPlaceSite(EntityUid hull, EntityUid networkUid, WFPlanetNetworkComponent network)
    {
        for (var attempt = 0; attempt < SiteAttempts; attempt++)
        {
            if (attempt > 0)
            {
                _transform.SetWorldPosition(hull, PickOrbitSpot(network, attempt));
                _bounds.ClampHull(hull);
            }

            if (IsSiteClear(hull, networkUid, network))
                return true;
        }

        Log.Info($"No clear crash site for {ToPrettyString(hull)} over {ToPrettyString(network.GroundMap)}");
        return false;
    }

    /// <summary>A random spot in orbit; later attempts range wider over a bounded world.</summary>
    private Vector2 PickOrbitSpot(WFPlanetNetworkComponent network, int attempt)
    {
        // A bounded world only lets a hull down from inside its circle.
        if (!_bounds.TryGetBounds(network.OrbitMap, out var centre, out var radius))
            return network.Centre + _random.NextVector2(500f, 1200f);

        return attempt < SiteAttempts / 2
            ? centre + _random.NextVector2(radius * 0.2f, radius * 0.5f)
            : centre + _random.NextVector2(radius * 0.1f, radius * 0.8f);
    }

    /// <summary>Whether the hull's footprint, grown by the clearances, misses every claim and other crash site.</summary>
    public bool IsSiteClear(EntityUid hull, EntityUid networkUid, WFPlanetNetworkComponent network)
    {
        if (!TryGetArea(hull, out var area))
            return false;

        var claim = new WFLandingClaimEvent(network.GroundMap, area.Enlarged(ClaimClearance));
        RaiseLocalEvent(ref claim);
        if (claim.Claimant != null)
            return false;

        var spaced = area.Enlarged(SiteSpacing);
        foreach (var scenario in _scenarios.Values)
        {
            if (scenario.Network != networkUid)
                continue;

            foreach (var grid in scenario.Grids)
            {
                if (grid != hull && !TerminatingOrDeleted(grid) && TryGetArea(grid, out var other) && spaced.Intersects(other))
                    return false;
            }
        }

        return true;
    }

    private bool TryGetArea(EntityUid grid, out Box2 area)
    {
        area = default;
        if (!TryComp<MapGridComponent>(grid, out var comp))
            return false;

        area = _transform.GetWorldMatrix(grid).TransformBox(comp.LocalAABB);
        return true;
    }
}
