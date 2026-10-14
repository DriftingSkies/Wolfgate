using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.PlanetPois;
using Robust.Shared.Maths;

namespace Content.Server._WF.PlanetPois;

/// <summary>A reserved site and its absolute road entrances.</summary>
public sealed record WFPlanetRoadSite(Box2i Bounds, WFPlanetPoiConnection Connection, IReadOnlyList<WFPlanetRoadSocket> Sockets);

/// <summary>An absolute entrance tile with an outward direction.</summary>
public sealed record WFPlanetRoadSocket(Vector2i Position, WFPlanetPoiDirection Direction);

/// <summary>A connected road network, or an explicit routing failure with no paintable tiles.</summary>
public sealed record WFPlanetRoadPlan(bool Success, string? Error, HashSet<Vector2i> RoadTiles, HashSet<Vector2i> ShoulderTiles)
{
    /// <summary>The tiles down the middle of the routed roads; the short approaches into the sites are not among them.</summary>
    public HashSet<Vector2i> Centreline { get; init; } = new();
}

/// <summary>Routes a bounded connected network without crossing reserved buildings or pinned terrain.</summary>
public static class WFPlanetRoadPlanner
{
    private const int SearchLimit = 100000;

    /// <summary>Plans all entrances atomically; a failed entrance rejects the complete network.</summary>
    public static WFPlanetRoadPlan Plan(Vector2 centre, float radius, float centreClear,
        IReadOnlyList<WFPlanetRoadSite> sites, IReadOnlySet<Vector2i> blocked,
        int width, int shoulderWidth, int seed)
    {
        WFPlanetRoadPlan Fail(string reason) => new(false, reason, new(), new());
        if (!float.IsFinite(radius) || !float.IsFinite(centreClear) || !float.IsFinite(centre.X) || !float.IsFinite(centre.Y)
            || width < 1 || width > 15 || width % 2 == 0 || shoulderWidth < 0 || shoulderWidth > 8 || radius <= 0 || radius > 4096 || centreClear < 0)
            return Fail("Invalid road dimensions or world bounds.");

        var low = -(width / 2);
        var high = low + width - 1;
        var clearance = Math.Max(-low, high) + shoulderWidth;
        var entrances = new List<WFPlanetRoadSocket>();
        var approaches = new Dictionary<WFPlanetRoadSite, HashSet<Vector2i>>();
        foreach (var site in sites)
        {
            if (site.Bounds.Right <= site.Bounds.Left || site.Bounds.Top <= site.Bounds.Bottom || !Enum.IsDefined(site.Connection))
                return Fail("A site has invalid reserved bounds or connection mode.");
            if (site.Connection == WFPlanetPoiConnection.Isolated)
                continue;
            IReadOnlyList<WFPlanetRoadSocket> sockets = site.Connection == WFPlanetPoiConnection.Through ? site.Sockets : site.Sockets.Take(1).ToArray();
            if (sockets.Count == 0 || site.Connection == WFPlanetPoiConnection.Through && (sockets.Select(s => s.Position).Distinct().Count() < 2 || sockets.Select(s => s.Direction).Distinct().Count() < 2))
                return Fail("A connected site has insufficient distinct entrances.");
            var siteApproaches = new HashSet<Vector2i>();
            if (!approaches.TryAdd(site, siteApproaches))
                return Fail("A connected site is listed more than once.");
            foreach (var socket in sockets)
            {
                if (!Enum.IsDefined(socket.Direction))
                    return Fail("An entrance has an invalid outward direction.");
                var direction = Direction(socket.Direction);
                // A socket may start on the outermost reserved tile, never deep inside a building.
                if (!Inside(socket.Position, site.Bounds) || Inside(socket.Position + direction, site.Bounds))
                    return Fail("An entrance does not face out of its reserved footprint.");
                entrances.Add(socket);
                for (var step = 0; step <= clearance + 1; step++)
                {
                    var point = socket.Position + direction * step;
                    for (var side = -clearance; side <= clearance; side++)
                        siteApproaches.Add(point + new Vector2i(-direction.Y, direction.X) * side);
                }
            }
        }

        bool TileAllowed(Vector2i tile)
        {
            var position = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
            var distance = Vector2.Distance(position, centre);
            // Include the complete tile square inside the world circle.
            if (distance + 0.707107f > radius || centreClear > 0 && distance - 0.707107f < centreClear || blocked.Contains(tile))
                return false;
            return sites.All(site => !Inside(tile, site.Bounds)
                || approaches.TryGetValue(site, out var allowed) && allowed.Contains(tile));
        }
        var clearanceCache = new Dictionary<Vector2i, bool>();
        bool Allowed(Vector2i point)
        {
            if (clearanceCache.TryGetValue(point, out var cached))
                return cached;
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
                if (!TileAllowed(point + new Vector2i(x, y)))
                    return clearanceCache[point] = false;
            return clearanceCache[point] = true;
        }

        var network = new HashSet<Vector2i>();
        var random = new Random(seed);
        var directions = new[] { new Vector2i(0, 1), new Vector2i(1, 0), new Vector2i(0, -1), new Vector2i(-1, 0) };
        for (var i = directions.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }
        var totalVisited = 0;
        foreach (var socket in entrances)
        {
            // Keep the complete width outside the building after the approach.
            var direction = Direction(socket.Direction);
            var start = socket.Position + direction * (clearance + 1);
            for (var step = 0; step <= clearance + 1; step++)
            {
                var point = socket.Position + direction * step;
                // At the entrance, width runs across the socket; it must not paint back into the building.
                for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
                {
                    var lane = point + new Vector2i(-direction.Y, direction.X) * side;
                    if (!TileAllowed(lane))
                        return Fail($"The approach from the entrance at {socket.Position} is blocked or outside the world at {lane}.");
                }
            }
            if (!Allowed(start))
                return Fail($"The entrance at {socket.Position} lacks road clearance at {start}.");
            if (network.Count > 0)
            {
                var goal = network.OrderBy(p => Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y)).ThenBy(p => p.X).ThenBy(p => p.Y).First();
                var frontier = new PriorityQueue<Vector2i, (int Cost, int Order)>();
                var costs = new Dictionary<Vector2i, int> { [start] = 0 };
                var previous = new Dictionary<Vector2i, Vector2i>();
                var order = 0;
                frontier.Enqueue(start, (0, order++));
                var found = false;
                var visited = 0;
                while (frontier.TryDequeue(out var current, out _) && visited++ < SearchLimit && totalVisited++ < SearchLimit * 5)
                {
                    if (current == goal) { found = true; break; }
                    foreach (var delta in directions)
                    {
                        var next = current + delta;
                        var cost = costs[current] + 1;
                        if (costs.TryGetValue(next, out var old) && old <= cost || !Allowed(next))
                            continue;
                        costs[next] = cost;
                        previous[next] = current;
                        var heuristic = Math.Abs(next.X - goal.X) + Math.Abs(next.Y - goal.Y);
                        frontier.Enqueue(next, (cost + heuristic, order++));
                    }
                }
                if (!found)
                    return Fail($"The entrance at {socket.Position} cannot reach the road network within the routing budget.");
                for (var point = goal; point != start; point = previous[point])
                    network.Add(point);
            }
            network.Add(start);
            // Approach centerlines are painted separately and never used as routing targets.
        }

        var roads = new HashSet<Vector2i>();
        var shoulders = new HashSet<Vector2i>();
        foreach (var point in network)
        {
            for (var x = low - shoulderWidth; x <= high + shoulderWidth; x++)
            for (var y = low - shoulderWidth; y <= high + shoulderWidth; y++)
            {
                var tile = point + new Vector2i(x, y);
                if (!TileAllowed(tile))
                    return Fail("The completed network violates its clearance.");
                if (x >= low && x <= high && y >= low && y <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        foreach (var socket in entrances)
        {
            var direction = Direction(socket.Direction);
            for (var step = 0; step <= clearance + 1; step++)
            for (var side = low - shoulderWidth; side <= high + shoulderWidth; side++)
            {
                var tile = socket.Position + direction * step + new Vector2i(-direction.Y, direction.X) * side;
                if (side >= low && side <= high) roads.Add(tile);
                else shoulders.Add(tile);
            }
        }
        shoulders.ExceptWith(roads);
        return new(true, null, roads, shoulders) { Centreline = network };
    }

    private static bool Inside(Vector2i p, Box2i bounds) => p.X >= bounds.Left && p.X < bounds.Right && p.Y >= bounds.Bottom && p.Y < bounds.Top;

    /// <summary>Converts an entrance direction to its unit tile offset.</summary>
    public static Vector2i Direction(WFPlanetPoiDirection direction) => direction switch
    {
        WFPlanetPoiDirection.North => new(0, 1),
        WFPlanetPoiDirection.East => new(1, 0),
        WFPlanetPoiDirection.South => new(0, -1),
        WFPlanetPoiDirection.West => new(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };
}
