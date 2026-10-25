using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Shared._WF.MappingMode;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingMode;

/// <summary>
/// Keeps every grid on a session's levels on whole world tiles at a right angle, so levels line up, and sends tiles
/// laid off every grid onto the nearest grid unless the mapper asked for a new one.
/// </summary>
public sealed partial class WFMappingModeSystem
{
    /// <summary>Mappers whose next tiles off every grid start a new grid.</summary>
    private readonly HashSet<NetUserId> _newGridNext = new();

    private void InitializeWorldGrid()
    {
        SubscribeNetworkEvent<WFMappingNewGridRequest>(OnNewGridRequest);
        SubscribeLocalEvent<WFMappingNewGridTargetEvent>(OnNewGridTarget);
    }

    /// <summary>Snaps any grid on a session level that sits off whole tiles, whatever put it there.</summary>
    private void UpdateWorldGrid()
    {
        foreach (var (map, entry) in _registry)
        {
            if (entry.Kind != WFMappingMapKind.Session || TerminatingOrDeleted(map))
                continue;

            var children = Transform(map).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (HasComp<MapGridComponent>(child) && !TerminatingOrDeleted(child) && !IsAligned(child))
                    SnapGrid(child);
            }
        }
    }

    /// <summary>Moves a grid onto whole tiles and the nearest right angle.</summary>
    public void SnapGrid(EntityUid grid)
    {
        var (position, rotation) = SnappedPose(grid);
        _transform.SetLocalPositionRotation(grid, position, rotation);
    }

    /// <summary>
    /// The grid tiles laid at a point off every grid join: the nearest grid on the map, or null when it has none or
    /// the mapper asked for a new grid, which uses up that request.
    /// </summary>
    public EntityUid? NewGridTarget(ICommonSession player, EntityUid map, Vector2 point)
    {
        if (_newGridNext.Remove(player.UserId))
        {
            RaiseNetworkEvent(new WFMappingNewGridStateEvent(false), player);
            return null;
        }

        EntityUid? nearest = null;
        var best = float.MaxValue;
        var children = Transform(map).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            if (!TryComp<MapGridComponent>(child, out var grid) || TerminatingOrDeleted(child))
                continue;

            var bounds = _transform.GetWorldMatrix(child).TransformBox(grid.LocalAABB);
            var closest = Vector2.Clamp(point, bounds.BottomLeft, bounds.TopRight);
            var distance = Vector2.DistanceSquared(point, closest);
            if (distance >= best)
                continue;

            best = distance;
            nearest = child;
        }

        return nearest;
    }

    /// <summary>
    /// Sends an engine tile placement off every grid onto the nearest grid, or onto the middle of a whole world tile
    /// so the engine's new grid starts on whole tiles.
    /// </summary>
    private void RouteOffGridTile(ICommonSession player, EntityUid map, MapCoordinates mapCoords, MsgPlacement msg)
    {
        var cell = new Vector2(MathF.Floor(mapCoords.X) + 0.5f, MathF.Floor(mapCoords.Y) + 0.5f);
        if (NewGridTarget(player, map, cell) is { } grid)
        {
            var local = Vector2.Transform(cell, _transform.GetInvWorldMatrix(grid));
            msg.NetCoordinates = GetNetCoordinates(new EntityCoordinates(grid, local));
            return;
        }

        msg.NetCoordinates = GetNetCoordinates(new EntityCoordinates(map, cell));
    }

    /// <summary>
    /// Sends an admin's tile placement off every grid on a session map onto the nearest grid, as mappers' placements
    /// are; admins skip the placement gate, so without this each tile off a grid starts a grid of its own.
    /// </summary>
    public void RouteAdminPlacement(ICommonSession player, MsgPlacement msg)
    {
        if (!Enabled || msg.PlaceType != PlacementManagerMessage.RequestPlacement || !msg.IsTile || msg.TileType == 0)
            return;

        var coords = GetCoordinates(msg.NetCoordinates);
        if (!coords.IsValid(EntityManager) || HasComp<MapGridComponent>(coords.EntityId) ||
            _transform.GetMap(coords) is not { } map || !TryGetSessionOfMap(map, out _))
        {
            return;
        }

        var mapCoords = _transform.ToMapCoordinates(coords);
        if (!_map.TryFindGridAt(mapCoords, out _, out _))
            RouteOffGridTile(player, map, mapCoords, msg);
    }

    private void OnNewGridTarget(WFMappingNewGridTargetEvent ev)
    {
        if (!Enabled || !TryGetPresent(ev.Session, out var session) || !IsSessionMap(ev.Map, session))
            return;

        ev.Grid = NewGridTarget(ev.Session, ev.Map, ev.Point);
    }

    private void OnNewGridRequest(WFMappingNewGridRequest ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!Enabled || !TryGetPresent(player, out _))
            return;

        if (ev.Enabled)
            _newGridNext.Add(player.UserId);
        else
            _newGridNext.Remove(player.UserId);

        RaiseNetworkEvent(new WFMappingNewGridStateEvent(ev.Enabled), player);
    }
}
