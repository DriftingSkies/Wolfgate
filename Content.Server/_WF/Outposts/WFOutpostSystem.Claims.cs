using System.Diagnostics.CodeAnalysis;
using Content.Server._WF.Outposts.Components;
using Content.Shared._WF.Outposts;
using Content.Server._WF.Caverns;
using Content.Server._WF.PlanetPois;
using Content.Server._WF.Planets.Flight;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._WF.Outposts;

/// <summary>
/// Claim zones: the ground within <see cref="ClaimMargin"/> tiles of an outpost's floor is its owner's. Nobody else may
/// found an outpost, lay a foundation plate or build on the raw ground there, and an outpost is only founded where its
/// zone is clear of the ground the world reserves for its sites, roads and cavern mouths.
/// </summary>
public sealed partial class WFOutpostSystem
{
    /// <summary>Tiles past an outpost's floor that its claim reaches, in every direction.</summary>
    public const int ClaimMargin = 8;

    private void InitializeClaims()
    {
        SubscribeLocalEvent<WFGroundBuildAttemptEvent>(OnGroundBuildAttempt);
        SubscribeLocalEvent<WFLandingClaimEvent>(OnLandingClaim);
    }

    /// <summary>Hulls keep off every outpost's claim, its owner's own too: they land beside it.</summary>
    private void OnLandingClaim(ref WFLandingClaimEvent args)
    {
        if (args.Claimant != null || !TryComp<MapGridComponent>(args.Ground, out var groundGrid))
            return;

        var groundMatrix = _transform.GetWorldMatrix(args.Ground);
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (outpost.Ground != args.Ground || TerminatingOrDeleted(uid))
                continue;

            var (min, max) = GetClaimZone((uid, outpost, grid));
            var size = groundGrid.TileSize;
            var zone = groundMatrix.TransformBox(new Box2(min.X * size, min.Y * size, (max.X + 1) * size, (max.Y + 1) * size));
            if (!zone.Intersects(args.Area))
                continue;

            args.Claimant = Loc.GetString("wf-outpost-claimant", ("owner", outpost.OwnerName), ("outpost", Name(uid)));
            return;
        }
    }

    private void OnGroundBuildAttempt(ref WFGroundBuildAttemptEvent args)
    {
        if (args.Cancelled
            || _transform.GetMap(args.Location) is not { } ground
            || _transform.GetGrid(args.Location) != ground
            || !TryComp<MapGridComponent>(ground, out var groundGrid))
            return;

        var index = _maps.TileIndicesFor(ground, groundGrid, args.Location);
        NetUserId? user = TryComp<ActorComponent>(args.User, out var actor) ? actor.PlayerSession.UserId : null;
        if (!TryGetClaim(ground, index, user, out var claimant)
            || CanBuild(args.User, (claimant.Value, Comp<WFOutpostComponent>(claimant.Value))))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wf-outpost-claimed", ("outpost", Name(claimant.Value))), args.User, args.User);
    }

    /// <summary>
    /// The ground tiles an outpost claims, inclusive: its floor's bounding box on the ground, grown by
    /// <see cref="ClaimMargin"/>.
    /// </summary>
    public (Vector2i Min, Vector2i Max) GetClaimZone(Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        var groundGrid = Comp<MapGridComponent>(outpost.Comp1.Ground);
        var local = outpost.Comp2.LocalAABB;
        var world = _transform.GetWorldMatrix(outpost).TransformBox(local);
        var min = _maps.TileIndicesFor(outpost.Comp1.Ground, groundGrid, new MapCoordinates(world.BottomLeft + new System.Numerics.Vector2(0.5f), Transform(outpost.Comp1.Ground).MapID));
        var max = _maps.TileIndicesFor(outpost.Comp1.Ground, groundGrid, new MapCoordinates(world.TopRight - new System.Numerics.Vector2(0.5f), Transform(outpost.Comp1.Ground).MapID));
        var margin = new Vector2i(ClaimMargin, ClaimMargin);
        return (min - margin, max + margin);
    }

    /// <summary>The outpost whose claim covers a ground tile, if any belongs to someone other than <paramref name="except"/>.</summary>
    public bool TryGetClaim(EntityUid ground, Vector2i index, NetUserId? except, [NotNullWhen(true)] out EntityUid? claimant)
    {
        var query = EntityQueryEnumerator<WFOutpostComponent, MapGridComponent>();
        while (query.MoveNext(out var uid, out var outpost, out var grid))
        {
            if (outpost.Ground != ground || except != null && outpost.Owner == except || TerminatingOrDeleted(uid))
                continue;

            var (min, max) = GetClaimZone((uid, outpost, grid));
            if (index.X < min.X || index.Y < min.Y || index.X > max.X || index.Y > max.Y)
                continue;

            claimant = uid;
            return true;
        }

        claimant = null;
        return false;
    }

    /// <summary>
    /// Whether a new outpost may stand on the square from <paramref name="origin"/>: none of it in another claim, and its
    /// own claim clear of the ground the world has reserved. <paramref name="reason"/> is a loc id when it may not.
    /// </summary>
    private bool IsClaimable(Entity<MapGridComponent> ground, Vector2i origin, int size, out string reason)
    {
        for (var x = 0; x < size; x++)
        for (var y = 0; y < size; y++)
        {
            if (TryGetClaim(ground, origin + new Vector2i(x, y), null, out _))
            {
                reason = "wf-outpost-found-claimed";
                return false;
            }
        }

        if (IsNearReserved(ground, origin, origin + new Vector2i(size - 1, size - 1)))
        {
            reason = "wf-outpost-found-too-close";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Whether reserved ground lies within the claim margin of the ground tiles from <paramref name="min"/> to <paramref name="max"/>, inclusive.</summary>
    private bool IsNearReserved(EntityUid ground, Vector2i min, Vector2i max)
    {
        for (var x = min.X - ClaimMargin; x <= max.X + ClaimMargin; x++)
        for (var y = min.Y - ClaimMargin; y <= max.Y + ClaimMargin; y++)
        {
            if (IsReserved(ground, new Vector2i(x, y)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the world keeps a ground tile for itself: inside a site's bounds, on a road, its shoulder or a footpath,
    /// or part of a cavern mouth.
    /// </summary>
    public bool IsReserved(EntityUid ground, Vector2i index)
    {
        if (TryComp<WFPlanetPoiComponent>(ground, out var pois))
        {
            if (pois.RoadTiles.Contains(index) || pois.ShoulderTiles.Contains(index) || pois.TrailTiles.Contains(index))
                return true;

            foreach (var site in pois.Sites)
            {
                // A claimed site is an outpost now, kept by its claim.
                if (site.ClaimedBy != null)
                    continue;

                // Bounds have exclusive upper edges.
                if (index.X >= site.Bounds.Left && index.X < site.Bounds.Right
                    && index.Y >= site.Bounds.Bottom && index.Y < site.Bounds.Top)
                    return true;
            }
        }

        if (TryComp<WFCavernGroundComponent>(ground, out var caverns))
        {
            foreach (var mouth in caverns.Mouths)
            {
                if (mouth.Contains(index))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Hands an outpost to another account, as a sale will.</summary>
    public void TransferOwnership(Entity<WFOutpostComponent> outpost, NetUserId owner, string ownerName)
    {
        outpost.Comp.Owner = owner;
        outpost.Comp.OwnerName = ownerName;
        if (TryComp<WFOutpostConsoleComponent>(outpost.Comp.Console, out var console))
            UpdateUi((outpost.Comp.Console, console));
    }
}
