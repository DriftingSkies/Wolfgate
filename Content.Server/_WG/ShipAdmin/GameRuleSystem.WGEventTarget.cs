using Content.Server._WG.ShipAdmin;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.GameTicking.Rules;

public abstract partial class GameRuleSystem<T>
{
    /// <summary>The rule whose start is running, so the random pickers below can honour where staff aimed it.</summary>
    private EntityUid? _wgStartingRule;

    /// <summary>Runs a rule's start with it marked as the one picking targets.</summary>
    private void WgStarted(EntityUid uid, T component, GameRuleComponent ruleData, GameRuleStartedEvent args)
    {
        var outer = _wgStartingRule;
        _wgStartingRule = uid;
        try
        {
            Started(uid, component, ruleData, args);
        }
        finally
        {
            _wgStartingRule = outer;
        }
    }

    /// <summary>
    /// Whether the starting rule was aimed by staff. If so, <paramref name="station"/> is the station it was aimed
    /// at, or null when the rule's own filter turns that station down: an aimed event never falls back to a random one.
    /// </summary>
    private bool WgAimedStation(Func<EntityUid, bool>? filter, out EntityUid? station)
    {
        station = null;
        if (!TryComp<WGEventTargetComponent>(_wgStartingRule, out var target))
            return false;

        if (!TerminatingOrDeleted(target.Station) && (filter == null || filter(target.Station)))
            station = target.Station;
        else
            WgMissed(_wgStartingRule!.Value, target);

        return true;
    }

    /// <summary>A random open floor tile on the grid the starting rule was aimed at, when it was aimed.</summary>
    private bool WgAimedTile(out bool found,
        out Vector2i tile,
        out EntityUid? targetStation,
        out EntityUid targetGrid,
        out EntityCoordinates targetCoords)
    {
        found = false;
        tile = default;
        targetStation = null;
        targetGrid = EntityUid.Invalid;
        targetCoords = EntityCoordinates.Invalid;
        if (!TryComp<WGEventTargetComponent>(_wgStartingRule, out var target))
            return false;

        if (TerminatingOrDeleted(target.Station) || !TryComp<MapGridComponent>(target.Grid, out var grid))
        {
            WgMissed(_wgStartingRule!.Value, target);
            return true;
        }

        targetStation = target.Station;
        targetGrid = target.Grid;
        var aabb = grid.LocalAABB;
        var map = Transform(target.Grid).MapUid;

        // A ship is far smaller than a station, so it gets more tries than the upstream picker's ten.
        for (var i = 0; i < 50; i++)
        {
            tile = new Vector2i(RobustRandom.Next((int) aabb.Left, (int) aabb.Right), RobustRandom.Next((int) aabb.Bottom, (int) aabb.Top));
            if (_atmosphere.IsTileSpace(target.Grid, map, tile) || _atmosphere.IsTileAirBlockedCached(target.Grid, tile))
                continue;

            found = true;
            targetCoords = _map.GridTileToLocal(target.Grid, grid, tile);
            break;
        }

        if (!found)
            WgMissed(_wgStartingRule!.Value, target);

        return true;
    }

    /// <summary>Tells whoever aimed the rule, once, that it found nothing on its target.</summary>
    private void WgMissed(EntityUid rule, WGEventTargetComponent target)
    {
        if (target.Missed)
            return;

        target.Missed = true;
        var ev = new WGEventTargetMissedEvent();
        RaiseLocalEvent(rule, ref ev);
    }
}
