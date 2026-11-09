using System.Numerics;
using Content.Server._WF.Caverns;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Server.Parallax;
using Content.Shared._WF.OutpostRaids;
using Content.Shared._WF.Planets;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.OutpostRaids;

public sealed partial class WFRaidDirectorSystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private TurfSystem _turf = default!;

    /// <summary>Tries for a walk-in spot round an outpost.</summary>
    private const int WalkInAttempts = 64;

    /// <summary>Tiles a walk-in spot keeps from a point of interest's bounds.</summary>
    private const int PoiClearance = 8;

    /// <summary>Metres a walk-in spot keeps from any grid other than the ground, such as a crashed hull.</summary>
    private const float GridClearance = 12f;

    /// <summary>Metres a walk-in spot keeps from the edge of the world.</summary>
    private const float EdgeClearance = 6f;

    /// <summary>Tiles round the claim-edge point searched for open ground for the raid to walk to.</summary>
    private const int EdgeSearch = 4;

    /// <summary>Tiles past the claim's edge where a tunnelling raid comes up.</summary>
    private const int TunnelGap = 2;

    /// <summary>Metres a tunnel exit keeps from any grid other than the ground.</summary>
    private const float TunnelGridClearance = 2f;

    private List<Entity<MapGridComponent>> _nearGrids = new();

    /// <summary>
    /// A spot a raid walks in from: between <see cref="OutpostRaidCVars.SpawnMin"/> and
    /// <see cref="OutpostRaidCVars.SpawnMax"/> tiles out from the outpost's claim, far enough from every player, inside the
    /// world, clear of points of interest and other grids, on open ground. <paramref name="edge"/> is the point on the
    /// claim's edge it faces. With <paramref name="toward"/>, a world position, it lies on that side of the outpost.
    /// </summary>
    public bool TryFindWalkIn(Entity<WFOutpostComponent, MapGridComponent> outpost, out EntityCoordinates spot, out EntityCoordinates edge,
        Vector2? toward = null)
    {
        spot = default;
        edge = default;
        var ground = outpost.Comp1.Ground;
        if (!TryComp<MapGridComponent>(ground, out var groundGrid))
            return false;

        var (min, max) = _outposts.GetClaimZone(outpost);
        var centre = new Vector2(min.X + max.X + 1, min.Y + max.Y + 1) / 2f;
        var half = new Vector2(max.X - min.X + 1, max.Y - min.Y + 1) / 2f;
        var near = _cfg.GetCVar(OutpostRaidCVars.SpawnMin);
        var far = Math.Max(near, _cfg.GetCVar(OutpostRaidCVars.SpawnMax));
        var clearance = _cfg.GetCVar(OutpostRaidCVars.PlayerClearance);
        var bounds = CompOrNull<WFPlanetBoundsComponent>(ground);
        var sites = CompOrNull<WFPlanetPoiComponent>(ground)?.Sites;
        var mapId = Transform(ground).MapID;
        var groundMatrix = _transform.GetWorldMatrix(ground);
        Angle? bearing = null;
        if (toward is { } target)
        {
            var offset = Vector2.Transform(target, _transform.GetInvWorldMatrix(ground)) - centre;
            if (offset.LengthSquared() > 1f)
                bearing = new Angle(offset);
        }

        for (var attempt = 0; attempt < WalkInAttempts; attempt++)
        {
            var dir = bearing is { } side
                ? (side + Angle.FromDegrees(_random.NextFloat(-CampArc, CampArc))).ToVec()
                : new Angle(_random.NextFloat(MathF.Tau)).ToVec();
            var toEdge = MathF.Min(
                MathF.Abs(dir.X) < 0.001f ? float.MaxValue : half.X / MathF.Abs(dir.X),
                MathF.Abs(dir.Y) < 0.001f ? float.MaxValue : half.Y / MathF.Abs(dir.Y));
            var local = centre + dir * (toEdge + _random.Next(near, far + 1));
            var index = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));
            var world = Vector2.Transform(local, groundMatrix);

            if (bounds != null && !bounds.Contains(local + dir * EdgeClearance))
                continue;
            if (sites != null && NearSite(sites, index))
                continue;
            if (NearPlayer(mapId, world, clearance))
                continue;

            _nearGrids.Clear();
            _maps.FindGridsIntersecting(mapId, Box2.CenteredAround(world, new Vector2(GridClearance * 2f)), ref _nearGrids, approx: true, includeMap: false);
            if (_nearGrids.Count > 0)
                continue;

            if (!TryOpenGround((ground, groundGrid), index))
                continue;

            // A rock on the claim-edge point would leave the raid no path to it.
            var edgeAt = centre + dir * (toEdge + 1f);
            if (!TryOpenNear((ground, groundGrid), new Vector2i((int) MathF.Floor(edgeAt.X), (int) MathF.Floor(edgeAt.Y)), out var edgeIndex))
                continue;

            spot = _maps.GridTileToLocal(ground, groundGrid, index);
            edge = _maps.GridTileToLocal(ground, groundGrid, edgeIndex);
            return true;
        }

        return false;
    }

    /// <summary>
    /// A spot just outside an outpost's claim where a tunnelling raid comes up from the cavern below: inside the world,
    /// clear of points of interest, claims and other grids, on open ground. Only a world with a cavern has one. The raid
    /// is at its claim edge as it comes up, so <paramref name="edge"/> is the spot itself.
    /// </summary>
    public bool TryFindTunnelExit(Entity<WFOutpostComponent, MapGridComponent> outpost, out EntityCoordinates spot, out EntityCoordinates edge)
    {
        spot = default;
        edge = default;
        var ground = outpost.Comp1.Ground;
        if (!HasComp<WFCavernGroundComponent>(ground) || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return false;

        var (min, max) = _outposts.GetClaimZone(outpost);
        var centre = new Vector2(min.X + max.X + 1, min.Y + max.Y + 1) / 2f;
        var half = new Vector2(max.X - min.X + 1, max.Y - min.Y + 1) / 2f;
        var bounds = CompOrNull<WFPlanetBoundsComponent>(ground);
        var sites = CompOrNull<WFPlanetPoiComponent>(ground)?.Sites;
        var mapId = Transform(ground).MapID;
        var groundMatrix = _transform.GetWorldMatrix(ground);

        for (var attempt = 0; attempt < WalkInAttempts; attempt++)
        {
            var dir = new Angle(_random.NextFloat(MathF.Tau)).ToVec();
            var toEdge = MathF.Min(
                MathF.Abs(dir.X) < 0.001f ? float.MaxValue : half.X / MathF.Abs(dir.X),
                MathF.Abs(dir.Y) < 0.001f ? float.MaxValue : half.Y / MathF.Abs(dir.Y));
            var local = centre + dir * (toEdge + TunnelGap);
            var index = new Vector2i((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y));

            if (bounds != null && !bounds.Contains(local + dir * EdgeClearance))
                continue;
            if (sites != null && NearSite(sites, index))
                continue;
            if (_outposts.TryGetClaim(ground, index, null, out _))
                continue;

            _nearGrids.Clear();
            var world = Vector2.Transform(local, groundMatrix);
            _maps.FindGridsIntersecting(mapId, Box2.CenteredAround(world, new Vector2(TunnelGridClearance * 2f)), ref _nearGrids, approx: true, includeMap: false);
            if (_nearGrids.Count > 0 || !TryOpenGround((ground, groundGrid), index))
                continue;

            spot = _maps.GridTileToLocal(ground, groundGrid, index);
            edge = spot;
            return true;
        }

        return false;
    }

    private static bool NearSite(List<WFPlanetPoiSite> sites, Vector2i index)
    {
        foreach (var site in sites)
        {
            var box = site.Bounds;
            if (index.X >= box.Left - PoiClearance && index.X <= box.Right + PoiClearance
                && index.Y >= box.Bottom - PoiClearance && index.Y <= box.Top + PoiClearance)
                return true;
        }

        return false;
    }

    private bool NearPlayer(MapId map, Vector2 world, float clearance)
    {
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.MapID == map && (_transform.GetWorldPosition(xform) - world).Length() < clearance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a ground tile and the eight round it are open to stand on. Unloaded ground is judged by what the biome
    /// would lay there: a rock it would put down when the chunk loads would wall the raid in. The spot's own tile is laid
    /// at once, so a raid never walks in over a hole.
    /// </summary>
    private bool TryOpenGround(Entity<MapGridComponent> ground, Vector2i index)
    {
        var biome = CompOrNull<BiomeComponent>(ground);
        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (!IsOpen(ground, biome, index + new Vector2i(x, y)))
                    return false;
            }
        }

        if (_maps.TryGetTileRef(ground, ground.Comp, index, out var tile) && !tile.Tile.IsEmpty)
            return true;
        if (biome == null || !_biome.TryGetBiomeTile(index, biome.Layers, biome.Seed, ground, out var natural))
            return false;

        _maps.SetTile(ground, ground.Comp, index, natural.Value);
        return true;
    }

    /// <summary>The open ground tile nearest <paramref name="index"/>, within <see cref="EdgeSearch"/> tiles.</summary>
    private bool TryOpenNear(Entity<MapGridComponent> ground, Vector2i index, out Vector2i open)
    {
        var biome = CompOrNull<BiomeComponent>(ground);
        for (var ring = 0; ring <= EdgeSearch; ring++)
        {
            for (var x = -ring; x <= ring; x++)
            {
                for (var y = -ring; y <= ring; y++)
                {
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                        continue;

                    open = index + new Vector2i(x, y);
                    if (IsOpen(ground, biome, open))
                        return true;
                }
            }
        }

        open = index;
        return false;
    }

    private bool IsOpen(Entity<MapGridComponent> ground, BiomeComponent? biome, Vector2i index)
    {
        if (_maps.TryGetTileRef(ground, ground.Comp, index, out var tile) && !tile.Tile.IsEmpty)
            return !_turf.IsTileBlocked(tile, CollisionGroup.Impassable);

        return biome != null
               && _biome.TryGetBiomeTile(index, biome.Layers, biome.Seed, ground, out var natural)
               && !_biome.TryGetEntity(index, biome.Layers, natural.Value, biome.Seed, ground, out _);
    }
}
