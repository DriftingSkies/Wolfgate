using Content.Shared._WF.LandingGuide;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    [Dependency] private WFLandingSurveySystem _wfLandingSurvey = default!;

    /// <summary>How long a clear or blocked landing spot is trusted before it is looked at again.</summary>
    private static readonly TimeSpan WFLandingCheckInterval = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan WFLandingRefusalCooldown = TimeSpan.FromSeconds(4);

    private readonly Dictionary<EntityUid, (TimeSpan Until, string? Blocked)> _wfLandingChecks = new();
    private readonly Dictionary<EntityUid, TimeSpan> _wfNextLandingRefusal = new();

    /// <summary>
    /// True when a hull setting down gently would come to rest on something solid, such as rock or a building's walls,
    /// or on claimed ground: it holds just above instead, and its pilots are told to find clear ground. A crash still
    /// comes down regardless, moved off claimed ground first (<see cref="WfShiftOffClaims"/>).
    /// </summary>
    /// <param name="fresh">Look again whatever the last look said: the hull may have slid over something since.</param>
    private bool WfHoldsOffObstacles(EntityUid grid, HashSet<EntityUid> transitSet, EntityUid ground, bool fresh = false)
    {
        if (!fresh && _wfLandingChecks.TryGetValue(grid, out var cached) && _timing.CurTime < cached.Until)
            return cached.Blocked != null;

        // The reason it holds off, as a message for its pilots.
        string? blocked = null;
        if (WfClaimUnder(transitSet, ground) is { } claimant)
            blocked = Loc.GetString("wf-flight-landing-claimed", ("claimant", claimant));

        foreach (var member in transitSet)
        {
            if (blocked != null)
                break;

            if (WfObstacleUnder(member, ground))
                blocked = Loc.GetString("wf-flight-landing-blocked");
        }
        _wfLandingChecks[grid] = (_timing.CurTime + WFLandingCheckInterval, blocked);

        if (blocked != null && _timing.CurTime >= _wfNextLandingRefusal.GetValueOrDefault(grid))
        {
            _wfNextLandingRefusal[grid] = _timing.CurTime + WFLandingRefusalCooldown;
            var pilots = EntityQueryEnumerator<PilotComponent>();
            while (pilots.MoveNext(out var pilotUid, out var pilot))
            {
                if (pilot.Console is { } console && !TerminatingOrDeleted(console) && transitSet.Contains(Transform(console).GridUid ?? EntityUid.Invalid))
                    _popup.PopupEntity(blocked, console, pilotUid);
            }
        }
        return blocked != null;
    }

    /// <summary>Whether a hull with lift holds its height over this plane until its pilot holds descend: a planet's ground.</summary>
    private bool WfHoldsOverGround(EntityUid grid, EntityUid plane)
    {
        return WfIsGroundLayer(plane) && WfIsPlanetFlight(grid);
    }

    /// <summary>Whether anything hard and anchored on the ground below stands under one of the hull's real tiles.</summary>
    public bool WfObstacleUnder(EntityUid hull, EntityUid ground)
    {
        return TryComp<MapGridComponent>(hull, out var hullGrid) && TryComp<MapGridComponent>(ground, out var groundGrid)
            && _wfLandingSurvey.ObstacleUnder((hull, hullGrid), (ground, groundGrid));
    }
}
