using System.Linq;
using Content.Server._NF.GameRule;
using Content.Server.Warps;
using Content.Shared._WF.Contracts;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.Contracts;

/// <summary>Finding contractors' stations, the board, and where cargo is handed out.</summary>
public sealed partial class WFContractSystem
{
    /// <summary>Each contractor in the round and its home station: the first of its maps that exists.</summary>
    public Dictionary<string, EntityUid> ContractorHomes()
    {
        var homes = new Dictionary<string, EntityUid>();
        foreach (var contractor in _proto.EnumeratePrototypes<WFContractorPrototype>())
        {
            foreach (var map in contractor.Stations)
            {
                if (StationOf(map) is not { } station)
                    continue;

                homes[contractor.ID] = station;
                break;
            }
        }

        return homes;
    }

    /// <summary>
    /// The station a game map spawned, if it is in the round, by the name the round gives it: the point of interest's or
    /// the map's. Stations keep no record of their map, so a renamed station is not found.
    /// </summary>
    public EntityUid? StationOf(string map)
    {
        if (!_proto.TryIndex<Content.Shared.Maps.GameMapPrototype>(map, out var gameMap))
            return null;

        var names = new HashSet<string> { gameMap.MapName };
        if (_proto.TryIndex<PointOfInterestPrototype>(map, out var poi))
            names.Add(poi.Name);

        var query = EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (names.Contains(MetaData(uid).EntityName))
                return uid;
        }

        return null;
    }

    /// <summary>The station the public board at Caelestinus stands on, where board pickups and drop-offs are; no board, no offers.</summary>
    public EntityUid? BoardStation()
    {
        var query = EntityQueryEnumerator<WFContractBoardComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var board, out _))
        {
            if (board.Board == WFContractBoards.Central && _station.GetOwningStation(uid) is { } station)
                return station;
        }

        return null;
    }

    /// <summary>The tile in front of a board, where its cargo is left.</summary>
    private EntityCoordinates? BoardDrop(EntityUid board)
    {
        if (TerminatingOrDeleted(board))
            return null;

        var xform = Transform(board);
        return xform.Coordinates.Offset(xform.LocalRotation.RotateVec(new System.Numerics.Vector2(0f, -1f)));
    }

    /// <summary>Where a station hands out cargo: a contract drop, else its warp point, else a floor tile near its centre.</summary>
    private EntityCoordinates? Anchor(EntityUid station)
    {
        var drops = EntityQueryEnumerator<WFContractDropComponent, TransformComponent>();
        while (drops.MoveNext(out var uid, out _, out var xform))
        {
            if (_station.GetOwningStation(uid, xform) == station)
                return xform.Coordinates;
        }

        var warps = EntityQueryEnumerator<WarpPointComponent, TransformComponent>();
        while (warps.MoveNext(out var uid, out var warp, out var xform))
        {
            if (!warp.AdminOnly && _station.GetOwningStation(uid, xform) == station)
                return xform.Coordinates;
        }

        if (_station.GetLargestGrid(station) is not { } largest || !TryComp<MapGridComponent>(largest, out var gridComp))
            return null;

        // The floor tile nearest the grid's middle.
        var centre = gridComp.LocalAABB.Center;
        Vector2i? best = null;
        var bestDistance = float.MaxValue;
        foreach (var tile in _map.GetAllTiles(largest, gridComp))
        {
            if (_turf.IsTileBlocked(tile, Content.Shared.Physics.CollisionGroup.Impassable))
                continue;

            var distance = (tile.GridIndices + new System.Numerics.Vector2(0.5f) - centre).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = tile.GridIndices;
        }

        return best is { } index ? _map.GridTileToLocal(largest, gridComp, index) : null;
    }

    /// <summary>Metres between two stations' largest grids.</summary>
    private float Distance(EntityUid a, EntityUid b)
    {
        if (_station.GetLargestGrid(a) is not { } gridA || _station.GetLargestGrid(b) is not { } gridB)
            return 0f;

        var posA = _transform.GetMapCoordinates(gridA);
        var posB = _transform.GetMapCoordinates(gridB);
        return posA.MapId == posB.MapId ? (posA.Position - posB.Position).Length() : 0f;
    }

    public string StationName(EntityUid? station)
    {
        return station is { } uid && !TerminatingOrDeleted(uid) ? Name(uid) : Loc.GetString("wf-contract-station-unknown");
    }
}
