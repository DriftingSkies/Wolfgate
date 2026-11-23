using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._WG.Unstuck;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WG.Unstuck;

/// <summary>
/// Frees a player whose body is wedged in a wall or other solid structure by moving them to the nearest clear floor.
/// </summary>
public sealed class WGUnstuckSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private VehicleInteriorSystem _vehicleInterior = default!;

    /// <summary>How far, in tiles, the first search for clear floor reaches.</summary>
    private const int SearchRadius = 5;

    /// <summary>How far, in tiles, the second search reaches when the first finds nothing.</summary>
    private const int WideSearchRadius = 10;

    /// <summary>How deep the body must sit inside a structure to count as stuck, so standing against a wall doesn't.</summary>
    private const float EmbedDepth = 0.1f;

    /// <summary>How far a body may touch a structure at its destination: a resting contact, not a wedge.</summary>
    private const float ContactSlop = 0.01f;

    /// <summary>Tiles within this much of the same distance count as a tie, which breathable air breaks.</summary>
    private const float TieDistance = 0.05f;

    /// <summary>How close, as a share of the line, two grid crossings must be to count as passing through a corner.</summary>
    private const float CornerEpsilon = 1e-5f;

    /// <summary>How far past its outermost tiles something anchored to a grid may reach, such as hull armor.</summary>
    private const float GridOverhang = 1f;

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly Dictionary<NetUserId, TimeSpan> _nextUse = new();
    private readonly HashSet<EntityUid> _intersecting = new();
    private List<Entity<MapGridComponent>> _nearGrids = new();

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeNetworkEvent<WGUnstuckRequestEvent>(OnRequest);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _nextUse.Clear());
    }

    private void OnRequest(WGUnstuckRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;
        if (session.AttachedEntity is not { } user)
            return;

        if (!_admin.IsAdmin(session) && _nextUse.TryGetValue(session.UserId, out var next) && _timing.CurTime < next)
        {
            var seconds = (int) Math.Ceiling((next - _timing.CurTime).TotalSeconds);
            _popup.PopupEntity(Loc.GetString("wg-unstuck-cooldown", ("seconds", seconds)), user, user, PopupType.SmallCaution);
            return;
        }

        if (!TryUnstuck(user, out var message))
        {
            _popup.PopupEntity(message, user, user, PopupType.SmallCaution);
            return;
        }

        _nextUse[session.UserId] = _timing.CurTime + Cooldown;
        _popup.PopupEntity(message, user, user);
    }

    /// <summary>
    /// Puts the entity outside if it is inside a vehicle; otherwise moves it to the nearest clear floor if it is
    /// embedded in a structure it collides with, on the side it was mostly on, within <see cref="SearchRadius"/> tiles,
    /// or <see cref="WideSearchRadius"/> when nothing is that close. With no floor in reach it goes to open space.
    /// </summary>
    /// <param name="message">The player-facing result.</param>
    public bool TryUnstuck(EntityUid user, out string message)
    {
        if (_vehicleInterior.TryGetVehicleFromInterior(user, out var vehicle) && vehicle is { } vehicleUid)
            return TryLeaveVehicle(user, vehicleUid, out message);

        if (HasComp<GhostComponent>(user) || !_physicsQuery.TryComp(user, out var body) || !body.CanCollide)
        {
            message = Loc.GetString("wg-unstuck-no-body");
            return false;
        }

        if (_container.IsEntityInContainer(user))
        {
            message = Loc.GetString("wg-unstuck-in-container");
            return false;
        }

        if (_buckle.IsBuckled(user))
        {
            message = Loc.GetString("wg-unstuck-buckled");
            return false;
        }

        var xform = Transform(user);
        if (!IsEmbedded(user, body.CollisionMask, xform, out var blocker))
        {
            message = Loc.GetString("wg-unstuck-not-stuck");
            return false;
        }

        // A body out in space has no grid of its own, so it searches the grid of whatever it is stuck in.
        if ((xform.GridUid ?? Transform(blocker).GridUid) is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid)
            || !TryFindClearSpot((gridUid, grid), user, xform, body.CollisionMask, out var target))
        {
            message = Loc.GetString("wg-unstuck-no-room");
            return false;
        }

        if (TryComp<PullableComponent>(user, out var pullable) && pullable.BeingPulled)
            _pulling.TryStopPull(user, pullable);

        if (TryComp<PullerComponent>(user, out var puller)
            && puller.Pulling is { } pulled
            && TryComp<PullableComponent>(pulled, out var pulledComp))
            _pulling.TryStopPull(pulled, pulledComp);

        var from = xform.Coordinates;
        _transform.SetMapCoordinates((user, xform), _transform.ToMapCoordinates(target));
        _physics.SetLinearVelocity(user, Vector2.Zero, body: body);

        _adminLog.Add(LogType.Teleport, LogImpact.Low,
            $"{ToPrettyString(user):player} used Unstick me, moved from {from} to {target}");

        message = Loc.GetString("wg-unstuck-done");
        return true;
    }

    /// <summary>
    /// Puts an occupant, living, dead or a ghost, out through the vehicle's exits, unbuckling them from their seat.
    /// A locked vehicle still holds the living, as do cuffs, stuns and being down.
    /// </summary>
    private bool TryLeaveVehicle(EntityUid user, EntityUid vehicle, out string message)
    {
        var ghost = HasComp<GhostComponent>(user);
        if (!ghost && _container.IsEntityInContainer(user))
        {
            message = Loc.GetString("wg-unstuck-in-container");
            return false;
        }

        var living = !ghost && !_mobState.IsDead(user);
        if (living && TryComp<VehicleLockComponent>(vehicle, out var vehicleLock) && vehicleLock.Locked)
        {
            message = Loc.GetString("wg-unstuck-vehicle-locked");
            return false;
        }

        // The living get out only as they could through a door: not cuffed, stunned or down.
        // Being knocked down doesn't block interaction, so it is checked on its own.
        if (living && (!_actionBlocker.CanInteract(user, null) || HasComp<KnockedDownComponent>(user)))
        {
            message = Loc.GetString("wg-unstuck-vehicle-restrained");
            return false;
        }

        if (TryComp<BuckleComponent>(user, out var buckle) && buckle.BuckledTo != null)
            _buckle.Unbuckle((user, buckle), null);

        if (!_vehicleInterior.TryExitVehicle(vehicle, user))
        {
            // A ghost needs no floor, so it can always come out on the vehicle itself.
            if (!ghost)
            {
                message = Loc.GetString("wg-unstuck-vehicle-blocked");
                return false;
            }

            _transform.SetMapCoordinates(user, _transform.GetMapCoordinates(vehicle));
        }

        _adminLog.Add(LogType.Teleport, LogImpact.Low,
            $"{ToPrettyString(user):player} used Unstick me to leave {ToPrettyString(vehicle):vehicle}, now at {Transform(user).Coordinates}");

        message = Loc.GetString("wg-unstuck-vehicle-done");
        return true;
    }

    /// <summary>Whether the body sits inside a static structure it would collide with, and the first one found.</summary>
    private bool IsEmbedded(EntityUid user, int mask, TransformComponent xform, out EntityUid blocker)
    {
        var aabb = _lookup.GetWorldAABB(user, xform);
        var size = Vector2.Max(aabb.Size - new Vector2(EmbedDepth * 2), new Vector2(0.01f));
        var core = Box2.CenteredAround(aabb.Center, size);

        foreach (var other in StaticIntersecting(xform.MapID, new Box2Rotated(core)))
        {
            if (other == user || !Blocks(other, mask))
                continue;

            blocker = other;
            return true;
        }

        blocker = default;
        return false;
    }

    /// <summary>
    /// Searches for clear floor within <see cref="SearchRadius"/>, then <see cref="WideSearchRadius"/>. A body already
    /// out over space, such as one wedged against a hull's armor, may come out in open space when no floor is in reach.
    /// </summary>
    private bool TryFindClearSpot(Entity<MapGridComponent> grid, EntityUid user, TransformComponent xform, int mask, out EntityCoordinates target)
    {
        var local = _transform.WithEntityId(xform.Coordinates, grid).Position;
        var origin = new Vector2i((int) MathF.Floor(local.X / grid.Comp.TileSize), (int) MathF.Floor(local.Y / grid.Comp.TileSize));
        // The whole body must fit at the destination; only a contact's worth is allowed, so a body exactly a corridor
        // wide still fits.
        var body = _lookup.GetWorldAABB(user, xform).Size - new Vector2(ContactSlop * 2);
        var footprint = Vector2.Max(body, new Vector2(0.01f));
        var map = xform.MapUid ?? grid.Owner;

        if (TryFindNearest(grid, local, origin, footprint, map, mask, SearchRadius, false, out target)
            || TryFindNearest(grid, local, origin, footprint, map, mask, WideSearchRadius, false, out target))
            return true;

        if (!IsSpaceTile(grid, origin))
            return false;

        return TryFindNearest(grid, local, origin, footprint, map, mask, SearchRadius, true, out target)
            || TryFindNearest(grid, local, origin, footprint, map, mask, WideSearchRadius, true, out target);
    }

    /// <summary>
    /// Picks the clear floor tile, or with <paramref name="space"/> the clear space tile, nearest the entity's exact
    /// position, so it comes out on the side it was mostly on. A tile behind another solid tile doesn't count, a tile
    /// where the body would overlap a structure doesn't either, and breathable air only breaks a tie.
    /// </summary>
    private bool TryFindNearest(
        Entity<MapGridComponent> grid,
        Vector2 local,
        Vector2i origin,
        Vector2 footprint,
        EntityUid map,
        int mask,
        int radius,
        bool space,
        out EntityCoordinates target)
    {
        target = default;
        var tileSize = grid.Comp.TileSize;

        var found = false;
        var bestSafe = false;
        var bestDistance = float.MaxValue;

        for (var dx = -radius; dx <= radius; dx++)
        for (var dy = -radius; dy <= radius; dy++)
        {
            var indices = origin + new Vector2i(dx, dy);
            if (IsSpaceTile(grid, indices) != space)
                continue;

            var center = new Vector2((indices.X + 0.5f) * tileSize, (indices.Y + 0.5f) * tileSize);
            var distance = (center - local).Length();
            if (found && distance > bestDistance + TieDistance)
                continue;

            if (IsObstructed(grid, indices, mask)
                || Overlaps(grid, center, footprint, mask)
                || BehindSolid(grid, local, center, origin, indices, mask))
                continue;

            // The nearer tile wins; within a tie, breathable air wins, then the nearer.
            var safe = !space && _atmosphere.IsTileMixtureProbablySafe(grid.Owner, map, indices);
            var better = !found
                || distance < bestDistance - TieDistance
                || safe && !bestSafe
                || safe == bestSafe && distance < bestDistance;
            if (!better)
                continue;

            found = true;
            bestSafe = safe;
            bestDistance = distance;
            target = new EntityCoordinates(grid, center);
        }

        return found;
    }

    /// <summary>
    /// Whether the way from the entity to a tile crosses a solid tile other than the one it is stuck in. Walks every
    /// tile the straight line enters, so one it only clips at a corner still counts.
    /// </summary>
    private bool BehindSolid(Entity<MapGridComponent> grid, Vector2 from, Vector2 to, Vector2i origin, Vector2i target, int mask)
    {
        var tileSize = grid.Comp.TileSize;
        var start = from / tileSize;
        var delta = to / tileSize - start;
        var x = (int) MathF.Floor(start.X);
        var y = (int) MathF.Floor(start.Y);
        var stepX = Math.Sign(delta.X);
        var stepY = Math.Sign(delta.Y);

        // The share of the line between grid lines on each axis, and how far along it the next one is.
        var spanX = stepX != 0 ? 1f / MathF.Abs(delta.X) : float.PositiveInfinity;
        var spanY = stepY != 0 ? 1f / MathF.Abs(delta.Y) : float.PositiveInfinity;
        var nextX = stepX > 0 ? (x + 1 - start.X) * spanX : stepX < 0 ? (start.X - x) * spanX : float.PositiveInfinity;
        var nextY = stepY > 0 ? (y + 1 - start.Y) * spanY : stepY < 0 ? (start.Y - y) * spanY : float.PositiveInfinity;

        var steps = Math.Abs(target.X - x) + Math.Abs(target.Y - y);
        for (var i = 0; i < steps && (x != target.X || y != target.Y); i++)
        {
            if (MathF.Abs(nextX - nextY) < CornerEpsilon)
            {
                // Through a corner: both tiles beside it count, so a body can't slip between two that meet there.
                if (Crosses(x + stepX, y) || Crosses(x, y + stepY))
                    return true;

                x += stepX;
                y += stepY;
                nextX += spanX;
                nextY += spanY;
                i++;
            }
            else if (nextX < nextY)
            {
                x += stepX;
                nextX += spanX;
            }
            else
            {
                y += stepY;
                nextY += spanY;
            }

            if (Crosses(x, y))
                return true;
        }

        return false;

        bool Crosses(int tileX, int tileY)
        {
            var indices = new Vector2i(tileX, tileY);
            return indices != origin && indices != target && IsObstructed(grid, indices, mask);
        }
    }

    /// <summary>Whether the grid has no floor at the tile.</summary>
    private bool IsSpaceTile(Entity<MapGridComponent> grid, Vector2i indices)
    {
        return !_map.TryGetTileRef(grid, grid.Comp, indices, out var tile) || _turf.IsSpace(tile);
    }

    /// <summary>
    /// Whether a body with the given world-axis size, centred on a grid-local point, would sit inside a structure it
    /// collides with, including one anchored on a neighbouring tile or another grid that reaches over.
    /// </summary>
    private bool Overlaps(Entity<MapGridComponent> grid, Vector2 center, Vector2 footprint, int mask)
    {
        var worldCenter = Vector2.Transform(center, _transform.GetWorldMatrix(grid));
        var bounds = new Box2Rotated(Box2.CenteredAround(worldCenter, footprint));
        var mapId = Transform(grid).MapID;

        foreach (var other in StaticIntersecting(mapId, bounds))
        {
            if (Blocks(other, mask))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The static entities intersecting a world box. The map-wide lookup only searches grids whose tiles the box
    /// touches, so this also searches grids near enough for something anchored at their edge to reach into it.
    /// </summary>
    private HashSet<EntityUid> StaticIntersecting(MapId mapId, Box2Rotated bounds)
    {
        _intersecting.Clear();
        _lookup.GetEntitiesIntersecting(_map.GetMapOrInvalid(mapId), bounds, _intersecting, LookupFlags.Static);

        _nearGrids.Clear();
        _map.FindGridsIntersecting(mapId, bounds.CalcBoundingBox().Enlarged(GridOverhang), ref _nearGrids,
            approx: true, includeMap: false);
        foreach (var grid in _nearGrids)
        {
            _lookup.GetEntitiesIntersecting(grid.Owner, bounds, _intersecting, LookupFlags.Static);
        }

        return _intersecting;
    }

    /// <summary>Whether something anchored on the tile would block the entity.</summary>
    private bool IsObstructed(Entity<MapGridComponent> grid, Vector2i indices, int mask)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(grid, grid.Comp, indices);
        while (anchored.MoveNext(out var uid))
        {
            if (Blocks(uid.Value, mask))
                return true;
        }

        return false;
    }

    private bool Blocks(EntityUid uid, int mask)
    {
        return !_gridQuery.HasComp(uid)
            && _physicsQuery.TryComp(uid, out var body)
            && body.CanCollide
            && body.Hard
            && body.BodyType == BodyType.Static
            && (body.CollisionLayer & mask) != 0;
    }
}
