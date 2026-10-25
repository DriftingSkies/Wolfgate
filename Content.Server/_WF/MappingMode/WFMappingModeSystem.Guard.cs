using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Server._WF.MappingTools;
using Content.Server.Decals;
using Content.Shared._WF.MappingMode;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared._Mono.Pvs;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Placement;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode;

public sealed partial class WFMappingModeSystem
{
    [Dependency] private PvsOverrideSystem _pvsOverride = default!;

    /// <summary>
    /// The last tile change seen this tick, so the placement event right after it can be reverted. Cleared every
    /// tick, and only claimed by a placement on the same grid and cell.
    /// </summary>
    private (EntityUid Grid, Vector2i Cell, Tile Old, GameTick Tick)? _lastTileChange;

    private void InitializeGuard()
    {
        SubscribeLocalEvent<PlacementEntityEvent>(OnPlacementEntity);
        SubscribeLocalEvent<PlacementTileEvent>(OnPlacementTile);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<WFMappingEditedEvent>(OnEdited);
        SubscribeLocalEvent<GlobalPvsComponent, ComponentStartup>(OnGlobalPvsStartup);
        SubscribeNetworkEvent<RequestDecalPlacementEvent>(OnDecalPlacement,
            after: [typeof(MappingToolsSystem)], before: [typeof(DecalSystem)]);
        SubscribeNetworkEvent<RequestDecalRemovalEvent>(OnDecalRemoval,
            after: [typeof(MappingToolsSystem)], before: [typeof(DecalSystem)]);
    }

    /// <summary>
    /// Map markers that every client sees wherever it is (radar edge markers, biome sources) are normal ship content,
    /// so they aren't denied; on a paused map in mapping mode they lose that override, so mappers can't push entities
    /// to everyone. A real round adds it back when the saved map loads.
    /// </summary>
    private void OnGlobalPvsStartup(Entity<GlobalPvsComponent> ent, ref ComponentStartup args)
    {
        if (Enabled && Transform(ent).MapUid is { } map && MetaData(map).EntityPaused)
            _pvsOverride.RemoveGlobalOverride(ent);
    }

    /// <summary>Marks the session a map belongs to as changed since its last save.</summary>
    private void MarkDirty(EntityUid? map)
    {
        if (map is { } uid && TryGetSessionOfMap(uid, out var session))
            MarkDirty(session);
    }

    private void MarkDirty(WFMappingSession session)
    {
        if (session.Dirty)
            return;

        session.Dirty = true;
        SendState(session);
    }

    private void OnEdited(WFMappingEditedEvent ev)
    {
        if (!TryGetSessionOfMap(ev.Map, out var session))
            return;

        MarkDirty(session);
        session.CountedAt = TimeSpan.Zero;
    }

    private void OnTileChanged(ref TileChangedEvent ev)
    {
        if (!Enabled || ev.Changes.Length == 0)
            return;

        var change = ev.Changes[^1];
        _lastTileChange = (ev.Entity.Owner, change.GridIndices, change.OldTile, _timing.CurTick);
        MarkDirty(Transform(ev.Entity).MapUid);
    }

    /// <summary>
    /// Backstop for created entities: one a mapper placed off their session's maps goes. Erasures can't be undone
    /// here, as the engine deletes on the next line; the placement gate refuses them first.
    /// </summary>
    private void OnPlacementEntity(PlacementEntityEvent ev)
    {
        if (!Enabled || !_players.TryGetSessionById(ev.PlacerNetUserId, out var player))
            return;

        var map = Transform(ev.EditedEntity).MapUid;
        if (ev.PlacementEventAction == PlacementEventAction.Create && !IsAdminMapper(player) &&
            (map is not { } mapUid || !CanEdit(player, mapUid)))
        {
            Log.Warning($"Deleted {ToPrettyString(ev.EditedEntity)}, placed by {player} off their session's maps");
            QueueDel(ev.EditedEntity);
            return;
        }

        if (map is { } edited && TryGetSessionOfMap(edited, out var session))
        {
            MarkDirty(session);
            if (ev.PlacementEventAction == PlacementEventAction.Erase)
                session.CountedAt = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Backstop for tiles: a tile a mapper laid off their session's maps is put back, and a grid made past the
    /// session's grid cap goes. Only a change this placement made is touched: the engine raises the event for a tile
    /// that didn't change too.
    /// </summary>
    private void OnPlacementTile(PlacementTileEvent ev)
    {
        var last = _lastTileChange;
        _lastTileChange = null;

        if (!Enabled ||
            !_players.TryGetSessionById(ev.PlacerNetUserId, out var player) ||
            IsAdminMapper(player) ||
            last is not { } change ||
            !TryComp(change.Grid, out MapGridComponent? grid) ||
            !IsPlacementCell(ev.Coordinates, change.Grid, change.Cell, change.Tick))
        {
            return;
        }

        var map = Transform(change.Grid).MapUid;
        if (map is not { } mapUid || !CanEdit(player, mapUid))
        {
            Log.Warning($"Reverted a tile laid by {player} on {ToPrettyString(change.Grid)}, off their session's maps");
            _map.SetTile(change.Grid, grid, change.Cell, change.Old);
            return;
        }

        if (!TryGetSessionOfMap(mapUid, out var session))
            return;

        var cap = GridCap(session);
        if (CountGrids(session) > cap && IsSingleTile(change.Grid, grid))
        {
            Log.Info($"Deleted {ToPrettyString(change.Grid)}, a grid {player} made past their session's cap");
            MoveRidersOff(change.Grid);
            QueueDel(change.Grid);
            Refuse(player, "wf-mapping-mode-limit-grids", ("max", cap));
        }
    }

    private bool IsSingleTile(EntityUid uid, MapGridComponent grid)
    {
        var tiles = _map.GetAllTiles(uid, grid);
        return tiles.MoveNext(out _) && !tiles.MoveNext(out _);
    }

    /// <summary>
    /// Whether a tile placement at <paramref name="coords"/> lands on <paramref name="cell"/> of
    /// <paramref name="grid"/> this tick, resolved the way the engine's placement resolves it.
    /// </summary>
    private bool IsPlacementCell(EntityCoordinates coords, EntityUid grid, Vector2i cell, GameTick tick)
    {
        if (tick != _timing.CurTick || !coords.IsValid(EntityManager))
            return false;

        EntityUid gridUid;
        MapGridComponent? gridComp;
        if (TryComp(coords.EntityId, out gridComp))
            gridUid = coords.EntityId;
        else if (!_map.TryFindGridAt(_transform.ToMapCoordinates(coords), out gridUid, out gridComp))
            return false;

        return gridUid == grid && _map.TileIndicesFor(gridUid, gridComp, coords) == cell;
    }

    /// <summary>
    /// Lays a mapper's decal on their session's map. The decal system itself only does it for Spawn admins, so
    /// nothing is placed twice.
    /// </summary>
    private void OnDecalPlacement(RequestDecalPlacementEvent ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!TryGetDecalTarget(player, ev.Coordinates, out var session, out var coords, out var grid))
            return;

        var decal = ev.Decal;
        var cell = MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f));
        var centre = Vector2.Transform(decal.Coordinates + new Vector2(0.5f), _transform.GetWorldMatrix(grid));
        if (!InBounds(centre))
        {
            Refuse(player, "wf-mapping-mode-limit-area", ("size", MapSize));
            return;
        }

        var maxPerTile = _cfg.GetCVar(WFMappingModeCVars.MaxDecalsPerTile);
        if (DecalsOnTile(grid, cell) >= maxPerTile)
        {
            Refuse(player, "wf-mapping-mode-limit-decals-per-tile", ("max", maxPerTile));
            return;
        }

        if (!_decals.TryAddDecal(decal, coords, out _))
            return;

        _adminLog.Add(LogType.CrayonDraw, LogImpact.Low,
            $"{player:player} drew a {decal.Color} {decal.Id} at {ev.Coordinates} while mapping");
        MarkDirty(session);
    }

    /// <summary>Removes a mapper's decals the way the decal system does for admins.</summary>
    private void OnDecalRemoval(RequestDecalRemovalEvent ev, EntitySessionEventArgs args)
    {
        var player = args.SenderSession;
        if (!TryGetDecalTarget(player, ev.Coordinates, out var session, out _, out var grid))
            return;

        foreach (var (id, decal) in _decals.GetDecalsInRange(grid, ev.Coordinates.Position))
        {
            _adminLog.Add(LogType.CrayonDraw, LogImpact.Low,
                $"{player:player} removed a {decal.Color} {decal.Id} at {ev.Coordinates} while mapping");
            _decals.RemoveDecal(grid, id);
        }

        MarkDirty(session);
    }

    /// <summary>
    /// The grid a mapper's decal request works on, if they are a present member, not a Spawn admin, within their
    /// edit rate, and on their session's map.
    /// </summary>
    private bool TryGetDecalTarget(
        ICommonSession player,
        NetCoordinates netCoords,
        [NotNullWhen(true)] out WFMappingSession? session,
        out EntityCoordinates coords,
        out EntityUid grid)
    {
        session = null;
        coords = default;
        grid = default;

        if (!Enabled ||
            _admin.HasAdminFlag(player, AdminFlags.Spawn) ||
            !TryGetPresent(player, out var present) ||
            !TryThrottle(_editThrottle, player, "decal"))
        {
            return false;
        }

        if (IsTesting(player))
        {
            Refuse(player, "wf-mapping-test-no-edit");
            return false;
        }

        coords = GetCoordinates(netCoords);
        if (!coords.IsValid(EntityManager) ||
            _transform.GetMap(coords) is not { } map ||
            !IsSessionMap(map, present))
        {
            Log.Info($"Dropped a decal request from {player} at {netCoords}, off their session's maps");
            Refuse(player, "wf-mapping-mode-not-your-map");
            return false;
        }

        if (_transform.GetGrid(coords) is not { } gridUid || !HasComp<MapGridComponent>(gridUid))
            return false;

        session = present;
        grid = gridUid;
        return true;
    }
}
