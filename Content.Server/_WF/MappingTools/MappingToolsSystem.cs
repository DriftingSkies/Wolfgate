using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Decals;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Popups;
using Content.Shared._WF.MappingTools;
using Content.Shared.Administration;
using Content.Shared.Atmos;
using Content.Shared.Database;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Content.Shared.NodeContainer;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Server side of the mapping tools: edits selections and keeps each mapper's undo history, spawn-menu edits
/// included.
/// </summary>
public sealed partial class MappingToolsSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IDependencyCollection _dependency = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private MappingProtectionSystem _protection = default!;
    [Dependency] private TransformSystem _transform = default!;

    /// <summary>
    /// Largest area, in tiles, one request may touch.
    /// </summary>
    private const int MaxArea = 256 * 256;

    /// <summary>
    /// Set while this system edits the world, so its own tile changes aren't recorded as placements.
    /// </summary>
    private bool _applying;

    /// <summary>
    /// One ref per entity, so every edit touching it follows it when undo recreates it.
    /// </summary>
    private readonly Dictionary<EntityUid, MappingEntityRef> _refs = new();

    private readonly Dictionary<ICommonSession, MappingClipboard> _clipboards = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MappingToolsMoveEvent>(OnMove);
        SubscribeNetworkEvent<MappingToolsCopyEvent>(OnCopy);
        SubscribeNetworkEvent<MappingToolsPasteEvent>(OnPaste);
        SubscribeNetworkEvent<MappingToolsDeleteEvent>(OnDelete);
        SubscribeNetworkEvent<MappingToolsMirrorEvent>(OnMirror);
        SubscribeNetworkEvent<MappingToolsHistoryEvent>(OnHistory);
        SubscribeNetworkEvent<MappingToolsDrawEvent>(OnDraw);

        InitializeHistory();
        InitializeLinks();
    }

    /// <summary>
    /// Whether a player may use the tools: Mapping admins, or whoever a <see cref="WFMappingCanUseEvent"/> handler
    /// allows. <paramref name="request"/> marks a request the player sent.
    /// </summary>
    private bool CanUse(ICommonSession session, bool request = false)
    {
        if (_admin.HasAdminFlag(session, AdminFlags.Mapping))
            return true;

        var ev = new WFMappingCanUseEvent(session, request);
        RaiseLocalEvent(ev);
        return ev.Allowed;
    }

    /// <summary>
    /// Raises <paramref name="ev"/> and says whether the request may go ahead.
    /// </summary>
    private bool CanEdit(WFMappingCanEditEvent ev)
    {
        RaiseLocalEvent(ev);
        return !ev.Cancelled;
    }

    /// <summary>
    /// Whether a request may work on a selection's grid, ending up covering <paramref name="cells"/>. A move passes
    /// where each selected entity lands in <paramref name="entityCells"/>.
    /// </summary>
    private bool CanEdit(ICommonSession session, Resolved sel, WFMappingEditKind kind, Box2i cells,
        List<Vector2i>? entityCells = null, bool wholeGrid = false)
    {
        if (Transform(sel.Grid.Owner).MapUid is not { } map)
            return false;

        return CanEdit(new WFMappingCanEditEvent(session, map, kind)
        {
            Grid = sel.Grid.Owner,
            Cells = cells,
            Tiles = sel.Tiles.Count,
            // A whole-grid delete saves the grid as one root, so its cost is the entities, not the area.
            SelectionCells = wholeGrid ? 0 : sel.AreaCells,
            TouchedEntities = wholeGrid ? Tree(sel.Grid.Owner).Count : TreeCount(sel.Entities),
            Decals = sel.Decals.Count,
            EntityCells = entityCells,
            Moving = entityCells != null ? sel.Entities : null,
        });
    }

    /// <summary>
    /// Entities in the trees under <paramref name="roots"/>, roots included.
    /// </summary>
    private int TreeCount(IEnumerable<EntityUid> roots)
    {
        var count = 0;
        foreach (var root in roots)
        {
            count += Tree(root).Count;
        }

        return count;
    }

    private MappingEntityRef GetRef(EntityUid uid)
    {
        if (!_refs.TryGetValue(uid, out var entityRef))
        {
            entityRef = new MappingEntityRef(uid);
            _refs[uid] = entityRef;
        }

        return entityRef;
    }

    /// <summary>
    /// A selection resolved on the server: its grid, the tiles and decals in its area and its root entities.
    /// </summary>
    private sealed class Resolved
    {
        public Entity<MapGridComponent> Grid;
        public Box2i Extent;

        /// <summary>Cells in the requested area.</summary>
        public long AreaCells;
        public readonly List<(Vector2i Cell, Tile Tile)> Tiles = new();
        public readonly List<Decal> Decals = new();
        public readonly HashSet<EntityUid> Entities = new();
    }

    private bool TryResolve(MappingSelection selection, [NotNullWhen(true)] out Resolved? resolved)
    {
        resolved = null;
        var gridUid = GetEntity(selection.Grid);
        if (!TryComp(gridUid, out MapGridComponent? grid) || TerminatingOrDeleted(gridUid))
            return false;

        var result = new Resolved { Grid = (gridUid, grid) };
        Box2i? extent = null;

        if (selection.Area is { } area && area.Width > 0 && area.Height > 0 && (long) area.Width * area.Height <= MaxArea)
        {
            extent = area;
            result.AreaCells = (long) area.Width * area.Height;

            for (var x = area.Left; x < area.Right; x++)
            {
                for (var y = area.Bottom; y < area.Top; y++)
                {
                    var cell = new Vector2i(x, y);
                    if (_map.TryGetTileRef(gridUid, grid, cell, out var tileRef) && !tileRef.Tile.IsEmpty)
                        result.Tiles.Add((cell, tileRef.Tile));
                }
            }

            var children = Transform(gridUid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (MappingToolsMath.Contains(area, MappingToolsMath.CellOf(Transform(child).LocalPosition)) &&
                    IsSelectable(child))
                {
                    result.Entities.Add(child);
                }
            }

            var bounds = new Box2(area.Left - 1, area.Bottom - 1, area.Right + 1, area.Top + 1);
            foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, bounds))
            {
                if (MappingToolsMath.Contains(area, MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f))))
                    result.Decals.Add(decal);
            }
        }

        // Single cells outside the area, each with its tile, entities and decals.
        var picked = new HashSet<Vector2i>();
        var resolvedArea = extent;
        if (selection.Cells.Count <= MaxArea)
        {
            foreach (var cell in selection.Cells)
            {
                if (resolvedArea is { } areaBox && MappingToolsMath.Contains(areaBox, cell) || !picked.Add(cell))
                {
                    continue;
                }

                result.AreaCells++;
                extent = MappingToolsMath.Include(extent, cell);
                if (_map.TryGetTileRef(gridUid, grid, cell, out var tileRef) && !tileRef.Tile.IsEmpty)
                    result.Tiles.Add((cell, tileRef.Tile));

                var cellBounds = new Box2(cell.X - 1, cell.Y - 1, cell.X + 2, cell.Y + 2);
                foreach (var (_, decal) in _decals.GetDecalsIntersecting(gridUid, cellBounds))
                {
                    if (MappingToolsMath.CellOf(decal.Coordinates + new Vector2(0.5f)) == cell)
                        result.Decals.Add(decal);
                }
            }
        }

        if (picked.Count > 0)
        {
            var children = Transform(gridUid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (picked.Contains(MappingToolsMath.CellOf(Transform(child).LocalPosition)) && IsSelectable(child))
                    result.Entities.Add(child);
            }
        }

        foreach (var netEntity in selection.Entities)
        {
            if (!TryGetEntity(netEntity, out var uid) ||
                Transform(uid.Value).ParentUid != gridUid ||
                !IsSelectable(uid.Value))
            {
                continue;
            }

            result.Entities.Add(uid.Value);
            extent = MappingToolsMath.Include(extent, MappingToolsMath.CellOf(Transform(uid.Value).LocalPosition));
        }

        if (extent is not { } finalExtent)
            return false;

        result.Extent = finalExtent;
        resolved = result;
        return true;
    }

    /// <summary>
    /// Whether an entity can be edited: not a grid or map, and neither protected nor holding anything protected
    /// (<see cref="MappingProtectionSystem"/>).
    /// </summary>
    private bool IsSelectable(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) &&
               !HasComp<MapGridComponent>(uid) &&
               !HasComp<MapComponent>(uid) &&
               _protection.CanTouch(uid);
    }

    /// <summary>
    /// An entity and everything parented under it.
    /// </summary>
    private List<EntityUid> Tree(EntityUid root)
    {
        var tree = new List<EntityUid> { root };
        for (var i = 0; i < tree.Count; i++)
        {
            var children = Transform(tree[i]).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                tree.Add(child);
            }
        }

        return tree;
    }

    private void OnMove(MappingToolsMoveEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Move(args.SenderSession, ev.Selection, ev.Offset, ev.Turns);
    }

    private void OnCopy(MappingToolsCopyEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Copy(args.SenderSession, ev.Selection, ev.Cut);
    }

    private void OnPaste(MappingToolsPasteEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Paste(args.SenderSession, GetCoordinates(ev.Target), ev.Turns);
    }

    private void OnMirror(MappingToolsMirrorEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Mirror(args.SenderSession, ev.Selection, ev.Vertical);
    }

    private void OnDelete(MappingToolsDeleteEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            Delete(args.SenderSession, ev.Selection);
    }

    private void OnHistory(MappingToolsHistoryEvent ev, EntitySessionEventArgs args)
    {
        if (CanUse(args.SenderSession, request: true))
            StepHistory(args.SenderSession, ev.Redo);
    }

    /// <summary>
    /// Moves a selection by whole tiles and quarter turns clockwise, as one undoable edit.
    /// </summary>
    public void Move(ICommonSession session, MappingSelection selection, Vector2i offset, int turns)
    {
        if (!TryResolve(selection, out var sel))
            return;

        turns = MappingToolsMath.Normalize(turns);
        if (offset == Vector2i.Zero && turns == 0)
            return;

        var landing = LandingCells(sel, p => MappingToolsMath.TransformPoint(p, sel.Extent, offset, turns));
        if (!CanEdit(session, sel, WFMappingEditKind.Edit, MappingToolsMath.TransformBox(sel.Extent, sel.Extent, offset, turns),
                landing))
        {
            return;
        }

        var edit = TransformEdit(sel,
            turns == 0 ? "wf-mapping-tools-edit-move" : "wf-mapping-tools-edit-rotate",
            cell => MappingToolsMath.TransformCell(cell, sel.Extent, offset, turns),
            point => MappingToolsMath.TransformPoint(point, sel.Extent, offset, turns),
            (_, rotation) => rotation + MappingToolsMath.RotationDelta(turns),
            tile => RotateTile(tile, turns),
            decal => MapDecal(decal, p => MappingToolsMath.TransformPoint(p, sel.Extent, offset, turns),
                decal.Angle + MappingToolsMath.RotationDelta(turns)));

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to move {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid} by {offset}, {turns} turns");
    }

    /// <summary>
    /// Mirrors a selection in place, left-right or with <paramref name="vertical"/> top-bottom, as one undoable edit.
    /// </summary>
    public void Mirror(ICommonSession session, MappingSelection selection, bool vertical)
    {
        if (!TryResolve(selection, out var sel) ||
            !CanEdit(session, sel, WFMappingEditKind.Edit, sel.Extent,
                LandingCells(sel, p => MappingToolsMath.MirrorPoint(p, sel.Extent, vertical))))
        {
            return;
        }

        var edit = TransformEdit(sel, "wf-mapping-tools-edit-mirror",
            cell => MappingToolsMath.MirrorCell(cell, sel.Extent, vertical),
            point => MappingToolsMath.MirrorPoint(point, sel.Extent, vertical),
            (uid, rotation) => MirrorRotation(uid, rotation, vertical),
            tile => MirrorTile(tile, vertical),
            decal => MapDecal(decal, p => MappingToolsMath.MirrorPoint(p, sel.Extent, vertical),
                MappingToolsMath.MirrorAngle(decal.Angle, vertical), MirrorDecalId(decal.Id, vertical)));

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to mirror {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    /// <summary>
    /// The cell each selected entity lands in when its position is mapped by <paramref name="pointMap"/>.
    /// </summary>
    private List<Vector2i> LandingCells(Resolved sel, Func<Vector2, Vector2> pointMap)
    {
        var cells = new List<Vector2i>(sel.Entities.Count);
        foreach (var uid in sel.Entities)
        {
            cells.Add(MappingToolsMath.CellOf(pointMap(Transform(uid).LocalPosition)));
        }

        return cells;
    }

    /// <summary>
    /// An edit that carries everything in a selection to new cells, positions and facings.
    /// </summary>
    private MappingEdit TransformEdit(
        Resolved sel,
        string name,
        Func<Vector2i, Vector2i> cellMap,
        Func<Vector2, Vector2> pointMap,
        Func<EntityUid, Angle, Angle> rotationMap,
        Func<Tile, Tile> tileMap,
        Func<Decal, Decal> decalMap)
    {
        var grid = sel.Grid.Owner;
        var edit = new MappingEdit(name);

        var newTiles = new Dictionary<Vector2i, Tile>();
        foreach (var (cell, tile) in sel.Tiles)
        {
            newTiles[cellMap(cell)] = tileMap(tile);
        }

        foreach (var (cell, _) in sel.Tiles)
        {
            if (!newTiles.ContainsKey(cell))
                edit.Tiles.Add(new MappingTileChange(grid, cell, TileAt(sel.Grid, cell), Tile.Empty));
        }

        foreach (var (cell, tile) in newTiles)
        {
            edit.Tiles.Add(new MappingTileChange(grid, cell, TileAt(sel.Grid, cell), tile));
        }

        foreach (var uid in sel.Entities)
        {
            var old = PoseOf(uid);
            var pose = old with
            {
                Position = pointMap(old.Position),
                Rotation = rotationMap(uid, old.Rotation),
            };
            edit.Moves.Add(new MappingEntityMove(GetRef(uid), old, pose));
        }

        foreach (var decal in sel.Decals)
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
            edit.DecalsAdded.Add(new MappingDecal(grid, decalMap(decal)));
        }

        return edit;
    }

    /// <summary>
    /// Copies a selection to the mapper's clipboard, deleting it as one undoable edit when cutting.
    /// </summary>
    public void Copy(ICommonSession session, MappingSelection selection, bool cut)
    {
        if (!TryResolve(selection, out var sel) ||
            !CanEdit(session, sel, cut ? WFMappingEditKind.Edit : WFMappingEditKind.Read, sel.Extent))
        {
            return;
        }

        // A cut over a whole grid deletes the grid, which the copy doesn't take; the delete check applies too.
        if (cut && CoversGrid(sel) && !CanEdit(session, sel, WFMappingEditKind.Edit, sel.Extent, wholeGrid: true))
            return;

        var origin = sel.Extent.BottomLeft;
        var clip = new MappingClipboard { SourceGrid = sel.Grid.Owner, Size = sel.Extent.Size };

        foreach (var (cell, tile) in sel.Tiles)
        {
            clip.Tiles.Add((cell - origin, tile));
        }

        foreach (var decal in sel.Decals)
        {
            clip.Decals.Add(decal.WithCoordinates(decal.Coordinates - origin));
        }

        var preview = new List<MappingClipEntity>(sel.Entities.Count);
        var serializer = new EntitySerializer(_dependency, SnapshotOptions);

        // Entities that never save (mobs, ghosts) can't be copied; they stay behind, and a cut deletes them.
        var copied = sel.Entities.Where(uid => serializer.IsSerializable(uid)).ToHashSet();
        if (copied.Count > 0)
        {
            serializer.SerializeEntityRecursive(copied);
            clip.Data = serializer.Write();
            clip.EntityCount = serializer.YamlUidMap.Count;
            clip.Bytes = MappingDataSize.Estimate(clip.Data);
            foreach (var uid in serializer.YamlUidMap.Keys)
            {
                if (MetaData(uid).EntityPrototype is { } prototype)
                    clip.Prototypes.Add(prototype.ID);
            }

            foreach (var uid in copied)
            {
                var pose = PoseOf(uid) with { Parent = EntityUid.Invalid };
                pose.Position -= origin;
                clip.Roots.Add((serializer.YamlUidMap[uid], pose));
                preview.Add(new MappingClipEntity(MetaData(uid).EntityPrototype?.ID, pose.Position, pose.Rotation));
            }
        }

        _clipboards[session] = clip;
        RaiseNetworkEvent(new MappingToolsClipboardEvent(GetNetEntity(clip.SourceGrid), clip.Size,
            clip.Tiles.ConvertAll(t => t.Cell), preview), session);

        if (cut)
            Commit(session, DeleteEdit(sel, "wf-mapping-tools-edit-cut"));

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to {(cut ? "cut" : "copy")} {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    /// <summary>
    /// Pastes the mapper's clipboard centred on <paramref name="coords"/>, as one undoable edit.
    /// </summary>
    public void Paste(ICommonSession session, EntityCoordinates coords, int turns)
    {
        if (!_clipboards.TryGetValue(session, out var clip) || !coords.IsValid(EntityManager))
            return;

        var mapCoords = _transform.ToMapCoordinates(coords);
        Entity<MapGridComponent>? target = null;
        if (_map.TryFindGridAt(mapCoords, out var foundUid, out var found))
            target = (foundUid, found);
        else if (TryPasteGrid(clip.SourceGrid, mapCoords.MapId, out var sourceUid, out var source))
            target = (sourceUid, source);
        else if (_map.GetMapOrInvalid(mapCoords.MapId) is { Valid: true } pasteMap &&
                 NewGridTarget(session, pasteMap, mapCoords.Position) is { } joined)
            target = (joined, Comp<MapGridComponent>(joined));

        // Off every grid, with the source grid gone, a new grid starts with its origin at the cursor's tile.
        var newOrigin = new Vector2i((int) MathF.Floor(mapCoords.X), (int) MathF.Floor(mapCoords.Y));

        turns = MappingToolsMath.Normalize(turns);
        var cursor = target is { } t ? _map.TileIndicesFor(t, t.Comp, mapCoords) : Vector2i.Zero;
        var extent = new Box2i(Vector2i.Zero, clip.Size);
        var offset = MappingToolsMath.PasteOrigin(cursor, clip.Size, turns) - MappingToolsMath.DestinationOrigin(extent, Vector2i.Zero, turns);

        var shift = target == null ? newOrigin : Vector2i.Zero;
        var box = MappingToolsMath.TransformBox(extent, extent, offset, turns);
        var half = new Vector2(0.5f);
        var check = new WFMappingCanEditEvent(session, _map.GetMapOrInvalid(mapCoords.MapId), WFMappingEditKind.Paste)
        {
            Grid = target?.Owner,
            Cells = new Box2i(box.BottomLeft + shift, box.TopRight + shift),
            Entities = clip.EntityCount,
            Tiles = clip.Tiles.Count,
            NewGrid = target == null,
            EntityCells = clip.Roots.ConvertAll(r =>
                MappingToolsMath.CellOf(MappingToolsMath.TransformPoint(r.Pose.Position, extent, offset, turns)) + shift),
            SelectionCells = (long) clip.Size.X * clip.Size.Y,
            TouchedEntities = clip.EntityCount,
            Decals = clip.Decals.Count,
            DecalCells = clip.Decals.ConvertAll(d =>
                MappingToolsMath.CellOf(MappingToolsMath.TransformPoint(d.Coordinates + half, extent, offset, turns)) + shift),
            CarriedPrototypes = clip.Prototypes,
        };

        if (!CanEdit(check))
            return;

        if (target == null)
        {
            var newGrid = _map.CreateGridEntity(mapCoords.MapId);
            _transform.SetWorldPosition(newGrid.Owner, new Vector2(newOrigin.X, newOrigin.Y));
            target = newGrid;
        }

        var (gridUid, grid) = target.Value;

        var edit = new MappingEdit("wf-mapping-tools-edit-paste");
        foreach (var (cell, tile) in clip.Tiles)
        {
            var dest = MappingToolsMath.TransformCell(cell, extent, offset, turns);
            edit.Tiles.Add(new MappingTileChange(gridUid, dest, TileAt((gridUid, grid), dest), RotateTile(tile, turns)));
        }

        foreach (var decal in clip.Decals)
        {
            var moved = MapDecal(decal, p => MappingToolsMath.TransformPoint(p, extent, offset, turns),
                decal.Angle + MappingToolsMath.RotationDelta(turns));
            edit.DecalsAdded.Add(new MappingDecal(gridUid, moved));
        }

        var group = new MappingEntityGroup { Data = clip.Data, EntityCount = clip.EntityCount, Bytes = clip.Bytes };
        foreach (var (yaml, pose) in clip.Roots)
        {
            var placed = pose with
            {
                Parent = gridUid,
                Position = MappingToolsMath.TransformPoint(pose.Position, extent, offset, turns),
                Rotation = pose.Rotation + MappingToolsMath.RotationDelta(turns),
            };
            group.Roots.Add(new MappingGroupRoot(new MappingEntityRef(EntityUid.Invalid), yaml, placed));
        }

        if (group.Roots.Count > 0)
            edit.Created.Add(group);

        Commit(session, edit);

        var selection = new MappingSelection
        {
            Grid = GetNetEntity(gridUid),
            Area = clip.Tiles.Count > 0 || clip.Decals.Count > 0
                ? MappingToolsMath.TransformBox(extent, extent, offset, turns)
                : null,
        };

        foreach (var root in group.Roots)
        {
            if (!TerminatingOrDeleted(root.Entity.Uid))
                selection.Entities.Add(GetNetEntity(root.Entity.Uid));
        }

        RaiseNetworkEvent(new MappingToolsSelectEvent(selection), session);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to paste {group.Roots.Count} entities and {clip.Tiles.Count} tiles on {ToPrettyString(gridUid):grid}");
    }

    /// <summary>
    /// Deletes a selection, tiles included, as one undoable edit.
    /// </summary>
    public void Delete(ICommonSession session, MappingSelection selection)
    {
        if (!TryResolve(selection, out var sel) ||
            !CanEdit(session, sel, WFMappingEditKind.Edit, sel.Extent, wholeGrid: CoversGrid(sel)))
        {
            return;
        }

        Commit(session, DeleteEdit(sel, "wf-mapping-tools-edit-delete"));
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to delete {sel.Entities.Count} entities and {sel.Tiles.Count} tiles on {ToPrettyString(sel.Grid.Owner):grid}");
    }

    /// <summary>
    /// An edit that removes everything in a selection, tiles included.
    /// </summary>
    private MappingEdit DeleteEdit(Resolved sel, string name)
    {
        var grid = sel.Grid.Owner;
        var edit = new MappingEdit(name);

        // Emptying a grid deletes it, so a selection over all of it takes the whole grid as one root, which undo
        // brings back with everything on it.
        if (CoversGrid(sel))
        {
            var gridGroup = new MappingEntityGroup();
            gridGroup.Roots.Add(new MappingGroupRoot(GetRef(grid), -1, PoseOf(grid)));
            edit.Deleted.Add(gridGroup);
            return edit;
        }

        foreach (var decal in sel.Decals)
        {
            edit.DecalsRemoved.Add(new MappingDecal(grid, decal));
        }

        if (sel.Entities.Count > 0)
        {
            var group = new MappingEntityGroup();
            foreach (var uid in sel.Entities)
            {
                group.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
            }

            edit.Deleted.Add(group);
        }

        foreach (var (cell, tile) in sel.Tiles)
        {
            edit.Tiles.Add(new MappingTileChange(grid, cell, tile, Tile.Empty));
        }

        return edit;
    }

    /// <summary>
    /// Whether a selection holds every tile of its grid, on a grid that isn't also a map.
    /// </summary>
    private bool CoversGrid(Resolved sel)
    {
        var (uid, grid) = sel.Grid;
        if (sel.Tiles.Count == 0 || HasComp<MapComponent>(uid) || Transform(uid).MapUid is null)
            return false;

        var count = 0;
        var tiles = _map.GetAllTiles(uid, grid);
        while (tiles.MoveNext(out _))
        {
            if (++count > sel.Tiles.Count)
                return false;
        }

        return count == sel.Tiles.Count;
    }

    /// <summary>
    /// The clipboard's source grid, if it still exists on the map being pasted on.
    /// </summary>
    private bool TryPasteGrid(EntityUid source, MapId map, out EntityUid gridUid, [NotNullWhen(true)] out MapGridComponent? grid)
    {
        gridUid = source;
        return TryComp(source, out grid) && !TerminatingOrDeleted(source) && Transform(source).MapID == map;
    }

    private Tile TileAt(Entity<MapGridComponent> grid, Vector2i cell)
    {
        return _map.TryGetTileRef(grid, grid, cell, out var tileRef) ? tileRef.Tile : Tile.Empty;
    }

    /// <summary>
    /// Turns a rotatable tile with its selection.
    /// </summary>
    private Tile RotateTile(Tile tile, int turns)
    {
        if (turns == 0 || tile.IsEmpty || !_tileDefs[tile.TypeId].AllowRotationMirror)
            return tile;

        return new Tile(tile.TypeId, tile.Flags, tile.Variant, MappingToolsMath.RotateTileState(tile.RotationMirroring, turns));
    }

    /// <summary>
    /// Mirrors a rotatable tile with its selection.
    /// </summary>
    private Tile MirrorTile(Tile tile, bool vertical)
    {
        if (tile.IsEmpty || !_tileDefs[tile.TypeId].AllowRotationMirror)
            return tile;

        return new Tile(tile.TypeId, tile.Flags, tile.Variant, MappingToolsMath.MirrorTileState(tile.RotationMirroring, vertical));
    }

    /// <summary>
    /// The facing a mirrored entity takes. Lopsided pipe fittings (bends, T-junctions, mixers) take whichever quarter
    /// turn gives them the mirrored connections.
    /// </summary>
    private Angle MirrorRotation(EntityUid uid, Angle rotation, bool vertical)
    {
        var mirrored = MappingToolsMath.MirrorAngle(rotation, vertical);
        if (!TryComp(uid, out NodeContainerComponent? nodes))
            return mirrored;

        var pipes = nodes.Nodes.Values.OfType<PipeNode>().ToList();
        if (pipes.Count == 0)
            return mirrored;

        // The plain mirror goes first, so symmetric fittings keep the expected facing.
        foreach (var candidate in new[] { mirrored, mirrored + Math.PI / 2, mirrored + Math.PI, mirrored - Math.PI / 2 })
        {
            if (pipes.All(pipe => pipe.OriginalPipeDirection.RotatePipeDirection(candidate) ==
                                  MirrorPipeDirection(pipe.OriginalPipeDirection.RotatePipeDirection(rotation), vertical)))
            {
                return candidate.Reduced();
            }
        }

        return mirrored;
    }

    private static PipeDirection MirrorPipeDirection(PipeDirection direction, bool vertical)
    {
        var (a, b) = vertical ? (PipeDirection.North, PipeDirection.South) : (PipeDirection.East, PipeDirection.West);
        var result = direction & ~(a | b);
        if (direction.HasFlag(a))
            result |= b;
        if (direction.HasFlag(b))
            result |= a;

        return result;
    }

    private static readonly (string From, string To)[] HorizontalDecalSuffixes =
        { ("Ne", "Nw"), ("Nw", "Ne"), ("Se", "Sw"), ("Sw", "Se"), ("E", "W"), ("W", "E") };

    private static readonly (string From, string To)[] VerticalDecalSuffixes =
        { ("Ne", "Se"), ("Se", "Ne"), ("Nw", "Sw"), ("Sw", "Nw"), ("N", "S"), ("S", "N") };

    /// <summary>
    /// A one-sided decal's mirrored counterpart (corner, end or line), when one exists; sprites can't be flipped.
    /// </summary>
    private string MirrorDecalId(string id, bool vertical)
    {
        foreach (var (from, to) in vertical ? VerticalDecalSuffixes : HorizontalDecalSuffixes)
        {
            if (!id.EndsWith(from, StringComparison.Ordinal))
                continue;

            var mirrored = id[..^from.Length] + to;
            return _prototypes.HasIndex<DecalPrototype>(mirrored) ? mirrored : id;
        }

        return id;
    }

    /// <summary>
    /// A decal carried by a selection. Its coordinates are its bottom-left, so the map is applied to its centre.
    /// </summary>
    private static Decal MapDecal(Decal decal, Func<Vector2, Vector2> pointMap, Angle angle, string? id = null)
    {
        var half = new Vector2(0.5f);
        return new Decal(pointMap(decal.Coordinates + half) - half, id ?? decal.Id, decal.Color, angle, decal.ZIndex,
            decal.Cleanable);
    }
}
