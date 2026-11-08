using Content.Shared._CE.ZLevels.Core.Components;
using Content.Server._WF.Planets.Flight;
using Robust.Shared.Map.Components;

namespace Content.Server._CE.ZLevels.Core;

public sealed partial class CEZLevelsSystem
{
    /// <summary>How often a hull sinking without lift over claimed ground looks for clear ground to fall on instead.</summary>
    private static readonly TimeSpan WFClaimShiftInterval = TimeSpan.FromSeconds(0.5);

    /// <summary>The farthest, in metres, a falling hull is moved off claimed ground.</summary>
    private const int WFClaimShiftReach = 128;

    /// <summary>Directions tried at each distance when moving a falling hull off claimed ground.</summary>
    private const int WFClaimShiftDirections = 16;

    private readonly Dictionary<EntityUid, TimeSpan> _wfNextClaimShift = new();

    /// <summary>Who claims ground under any of the hulls, or null when none of it is claimed.</summary>
    public string? WfClaimUnder(IEnumerable<EntityUid> hulls, EntityUid ground)
    {
        return WfTryGetHullArea(hulls, out var area) ? WfClaimant(ground, area) : null;
    }

    /// <summary>Who claims ground anywhere in the world-space area, or null when none of it is claimed.</summary>
    public string? WfClaimant(EntityUid ground, Box2 area)
    {
        var ev = new WFLandingClaimEvent(ground, area);
        RaiseLocalEvent(ref ev);
        return ev.Claimant;
    }

    /// <summary>The world-space box round every real tile of the hulls.</summary>
    private bool WfTryGetHullArea(IEnumerable<EntityUid> hulls, out Box2 area)
    {
        area = default;
        var any = false;
        foreach (var hull in hulls)
        {
            if (!TryComp<MapGridComponent>(hull, out var grid))
                continue;

            var box = _transform.GetWorldMatrix(hull).TransformBox(grid.LocalAABB);
            area = any ? area.Union(box) : box;
            any = true;
        }

        return any;
    }

    /// <summary>
    /// Moves a hull falling into the gap over a planet's ground sideways to the nearest ground nobody claims, so a crash
    /// never comes down on someone's outpost.
    /// </summary>
    private void WfShiftOffClaims(EntityUid grid, HashSet<EntityUid> set)
    {
        if (Transform(grid).MapUid is not { } transit
            || !TryComp<CEZTransitMapComponent>(transit, out var gap)
            || gap.LowerMap is not { } ground
            || !WfIsGroundLayer(ground)
            || _timing.CurTime < _wfNextClaimShift.GetValueOrDefault(grid))
            return;

        _wfNextClaimShift[grid] = _timing.CurTime + WFClaimShiftInterval;
        if (!WfTryGetHullArea(set, out var area) || WfClaimant(ground, area) == null)
            return;

        for (var distance = 1; distance <= WFClaimShiftReach; distance++)
        {
            for (var i = 0; i < WFClaimShiftDirections; i++)
            {
                var offset = new Angle(MathF.Tau * i / WFClaimShiftDirections).ToVec() * distance;
                if (WfClaimant(ground, area.Translated(offset)) != null)
                    continue;

                foreach (var member in set)
                {
                    _transform.SetWorldPosition(member, _transform.GetWorldPosition(member) + offset);
                }

                Log.Info($"Moved falling {ToPrettyString(grid)} {offset} off claimed ground.");
                return;
            }
        }
    }
}
