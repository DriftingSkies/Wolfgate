using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Atmos.Piping.Components;
using Content.Server.Atmos.Piping.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Database;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private AtmosPipeColorSystem _pipeColor = default!;
    [Dependency] private AtmosPipeLayersSystem _pipeLayers = default!;

    private static readonly PipeShape[] PipeShapes =
    {
        PipeShape.Half,
        PipeShape.Straight,
        PipeShape.Bend,
        PipeShape.TJunction,
        PipeShape.Fourway,
    };

    private readonly List<EntityUid> _anchored = new();

    /// <summary>The Fluent id the last pipe draw, paint or device placement ended with, refusal or note; null for none.</summary>
    internal string? LastPipeReport { get; private set; }

    /// <summary>The pipes on one cell and one layer: their open sides, and the plain pipe when it is the only one.</summary>
    private readonly record struct CellPipes(PipeDirection Directions, EntityUid? Plain);

    /// <summary>
    /// Lays a pipe along a dragged path: each cell gets the plain pipe that opens to its neighbours, joined to the
    /// pipes around it as the spec says, with plain pipes rebuilt or gaining a side. One undoable edit.
    /// </summary>
    private void DrawPipes(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev)
    {
        LastPipeReport = null;
        if (ev.Pipes is not { } spec || !Enum.IsDefined(spec.Layer))
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        if (MappingToolsPipes.Normalise(ev.Cells, MaxDrawCells) is not { } path)
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-too-many", ("max", MaxDrawCells));
            return;
        }

        if (!TryGetPipePrototypes(spec.Layer, out var byShape))
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-bad-prototype");
            return;
        }

        var plainIds = byShape.Values.ToHashSet();
        var cells = new Dictionary<Vector2i, CellPipes?>();
        CellPipes? At(Vector2i cell)
        {
            if (!cells.TryGetValue(cell, out var pipes))
            {
                pipes = PipesAt(grid, cell, spec.Layer, plainIds);
                cells[cell] = pipes;
            }

            return pipes;
        }

        var plan = MappingToolsPipes.Plan(path,
            cell => At(cell) is { } pipes ? new MappingExistingPipe(pipes.Directions, pipes.Plain != null) : null,
            spec.AutoJoin, spec.Replace, spec.Facing);

        var noFloor = plan.Placed.Count(p => TileAt(grid, p.Cell).IsEmpty);
        if (noFloor > 0)
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-no-floor", ("count", noFloor));
            return;
        }

        Color? auto = null;
        if (spec.Color == null && spec.AutoColor)
        {
            foreach (var cell in plan.Joined)
            {
                if (ColorOfPipes(grid, cell, spec.Layer) is { } color)
                {
                    auto = color;
                    break;
                }
            }
        }

        // What happens to each cell: a pipe deleted and laid again with new sides, or only painted.
        var removals = new List<EntityUid>();
        var spawns = new List<(Vector2i Cell, PipeShape Shape, Angle Rotation, Color? Color)>();
        var paints = new List<(EntityUid Uid, Color Old, Color New)>();
        void Build(MappingPipeCell target, bool onPath)
        {
            if (!MappingToolsPipes.TryShape(target.Directions, out var shape, out var rotation))
                return;

            var old = At(target.Cell) is { Plain: { } plain } ? plain : (EntityUid?) null;
            var oldColor = old is { } oldUid && TryComp(oldUid, out AtmosPipeColorComponent? oldPaint)
                ? oldPaint.Color
                : (Color?) null;
            var color = spec.Color ?? (old == null && onPath ? auto : oldColor);
            if (old is { } existing && At(target.Cell)!.Value.Directions == target.Directions)
            {
                if (color is { } paint && oldColor is { } current && paint != current)
                    paints.Add((existing, current, paint));

                return;
            }

            if (old is { } replaced)
                removals.Add(replaced);

            spawns.Add((target.Cell, shape, rotation, color));
        }

        foreach (var placed in plan.Placed)
        {
            Build(placed, true);
        }

        foreach (var upgraded in plan.Upgraded)
        {
            Build(upgraded, false);
        }

        var report = MappingToolsPipes.ReportKey(spawns.Count + paints.Count, plan.Skipped, plan.Blocked);
        var reportArgs = new (string, object)[]
        {
            ("laid", spawns.Count + paints.Count),
            ("skipped", plan.Skipped),
            ("blocked", plan.Blocked),
        };

        if (spawns.Count == 0 && paints.Count == 0)
        {
            RefusePipes(session, ev, report ?? "wf-mapping-tools-pipes-nothing", reportArgs);
            return;
        }

        // A pipe that carries something protected (a following ghost) isn't replaced.
        if (removals.Any(uid => !IsSelectable(uid)))
        {
            Refuse(session, ev, "wf-mapping-tools-protected");
            return;
        }

        var touched = spawns.Select(s => s.Cell).Concat(paints.Select(p => CellOf(grid, p.Uid))).ToList();
        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = Bounds(touched.Count > 0 ? touched : path),
            Entities = Math.Max(0, spawns.Count - removals.Count),
            EntityCells = spawns.Select(s => s.Cell).ToList(),
            Moving = removals.ToHashSet(),
            SelectionCells = path.Count,
            TouchedEntities = spawns.Count + TreeCount(removals) + paints.Count,
            Prototypes = spawns.Select(s => MappingToolsPipes.PrototypeFor(s.Shape)).Distinct().ToList(),
        };

        if (!CanDrawPipes(session, ev, check))
            return;

        var edit = StartPipeEdit("wf-mapping-tools-edit-draw-pipes", removals);
        var created = new List<EntityUid>();
        foreach (var (cell, shape, rotation, color) in spawns)
        {
            created.Add(SpawnPipe(grid, byShape[shape], cell, rotation, null, color));
        }

        foreach (var (uid, old, paint) in paints)
        {
            SetPipeColor(uid, paint);
            edit.PipeColors.Add(new MappingPipeColorChange(GetRef(uid), old, paint));
        }

        FinishPipeEdit(session, edit, created);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to lay {spawns.Count} pipes (replacing {removals.Count}, painting {paints.Count}) on layer {spec.Layer} of {ToPrettyString(grid.Owner):grid}");

        LastPipeReport = report;
        if (report != null)
            _popup.PopupCursor(Loc.GetString(report, reportArgs), session);
    }

    /// <summary>
    /// Paints the pipe network at a cell: every pipe its nodes reach through sides that face each other on one
    /// layer, worked out here from the pipes on the grid. One undoable edit.
    /// </summary>
    private void PaintPipes(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev)
    {
        LastPipeReport = null;
        if (ev.Pipes is not { Color: { } color } spec || ev.Cells.Count != 1 || !Enum.IsDefined(spec.Layer))
        {
            RefusePipes(session, ev, ev.Pipes is { Color: null } ? "wf-mapping-tools-pipes-needs-color" : "wf-mapping-tools-draw-bad");
            return;
        }

        var start = ev.Cells[0];
        var startNodes = new List<PipeNode>();
        foreach (var layer in new[] { spec.Layer }.Concat(Enum.GetValues<AtmosPipeLayer>()))
        {
            CollectNodes(grid, start, layer, startNodes);
            if (startNodes.Count > 0)
                break;
        }

        if (startNodes.Count == 0)
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-no-pipe");
            return;
        }

        var network = PipeNetwork(grid, startNodes, MaxDrawCells);
        if (network == null)
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-too-many", ("max", MaxDrawCells));
            return;
        }

        var edit = new MappingEdit("wf-mapping-tools-edit-paint-pipes");
        var cells = new List<Vector2i>();
        foreach (var uid in network)
        {
            if (!TryComp(uid, out AtmosPipeColorComponent? paint) || paint.Color == color)
                continue;

            edit.PipeColors.Add(new MappingPipeColorChange(GetRef(uid), paint.Color, color));
            cells.Add(CellOf(grid, uid));
        }

        if (edit.IsEmpty)
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-paint-nothing");
            return;
        }

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Edit)
        {
            Grid = grid.Owner,
            Cells = Bounds(cells),
            SelectionCells = cells.Count,
            TouchedEntities = edit.PipeColors.Count,
        };

        if (!CanDrawPipes(session, ev, check))
            return;

        Commit(session, edit);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to paint {edit.PipeColors.Count} pipes {color.ToHex()} on {ToPrettyString(grid.Owner):grid}");
    }

    /// <summary>
    /// The entities whose pipe nodes connect to <paramref name="start"/>: a flood through anchored pipes on the
    /// grid whose sides face each other on the same layer, as pipe nets join. Null past <paramref name="max"/>.
    /// </summary>
    private List<EntityUid>? PipeNetwork(Entity<MapGridComponent> grid, List<PipeNode> start, int max)
    {
        var seen = new HashSet<PipeNode>(start);
        var queue = new Queue<PipeNode>(start);
        var owners = new List<EntityUid>();
        var owned = new HashSet<EntityUid>();
        var neighbours = new List<PipeNode>();
        while (queue.TryDequeue(out var node))
        {
            if (owned.Add(node.Owner))
            {
                owners.Add(node.Owner);
                if (owners.Count > max)
                    return null;
            }

            var cell = CellOf(grid, node.Owner);
            foreach (var side in MappingToolsPipes.Sides)
            {
                if (!node.CurrentPipeDirection.HasDirection(side))
                    continue;

                neighbours.Clear();
                CollectNodes(grid, MappingToolsPipes.Step(cell, side), node.CurrentPipeLayer, neighbours);
                foreach (var next in neighbours)
                {
                    if (next.NodeGroupID == node.NodeGroupID &&
                        next.CurrentPipeDirection.HasDirection(side.GetOpposite()) &&
                        seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return owners;
    }

    /// <summary>Adds the pipe nodes on one layer of the entities anchored on a cell.</summary>
    private void CollectNodes(Entity<MapGridComponent> grid, Vector2i cell, AtmosPipeLayer layer, List<PipeNode> nodes)
    {
        _anchored.Clear();
        _map.GetAnchoredEntities(grid, cell, _anchored);
        foreach (var uid in _anchored)
        {
            if (!TryComp(uid, out NodeContainerComponent? container))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node is PipeNode { NodeGroupID: NodeGroupID.Pipe } pipe && pipe.CurrentPipeLayer == layer)
                    nodes.Add(pipe);
            }
        }
    }

    /// <summary>The pipes on a cell and layer, or null for none.</summary>
    private CellPipes? PipesAt(Entity<MapGridComponent> grid, Vector2i cell, AtmosPipeLayer layer,
        HashSet<string> plainIds)
    {
        var nodes = new List<PipeNode>();
        CollectNodes(grid, cell, layer, nodes);
        if (nodes.Count == 0)
            return null;

        var directions = PipeDirection.None;
        var owners = new HashSet<EntityUid>();
        foreach (var node in nodes)
        {
            directions |= node.CurrentPipeDirection;
            owners.Add(node.Owner);
        }

        var first = nodes[0].Owner;
        var plain = owners.Count == 1 && nodes.Count == 1 &&
                    MetaData(first).EntityPrototype?.ID is { } id && plainIds.Contains(id)
            ? first
            : (EntityUid?) null;

        return new CellPipes(directions & PipeDirection.Fourway, plain);
    }

    /// <summary>The colour of the first painted pipe on a cell and layer.</summary>
    private Color? ColorOfPipes(Entity<MapGridComponent> grid, Vector2i cell, AtmosPipeLayer layer)
    {
        var nodes = new List<PipeNode>();
        CollectNodes(grid, cell, layer, nodes);
        foreach (var node in nodes)
        {
            if (TryComp(node.Owner, out AtmosPipeColorComponent? paint))
                return paint.Color;
        }

        return null;
    }

    /// <summary>The prototype of every plain pipe shape on a layer: the primary pipe or its layer alternative.</summary>
    private bool TryGetPipePrototypes(AtmosPipeLayer layer, out Dictionary<PipeShape, string> byShape)
    {
        byShape = new Dictionary<PipeShape, string>();
        foreach (var shape in PipeShapes)
        {
            var id = MappingToolsPipes.PrototypeFor(shape);
            if (!_prototypes.TryIndex<EntityPrototype>(id, out var prototype))
                return false;

            if (layer != AtmosPipeLayer.Primary)
            {
                if (!prototype.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _factory) ||
                    !_pipeLayers.TryGetAlternativePrototype(layers, layer, out var alternative) ||
                    !_prototypes.HasIndex<EntityPrototype>(alternative))
                {
                    return false;
                }

                id = alternative;
            }

            byShape[shape] = id;
        }

        return true;
    }

    /// <summary>Refuses a pipe draw, paint or device and keeps the reason for <see cref="LastPipeReport"/>.</summary>
    private void RefusePipes(ICommonSession session, MappingToolsDrawEvent ev, string key,
        params (string, object)[] args)
    {
        LastPipeReport = key;
        Refuse(session, ev, key, args);
    }

    /// <summary><see cref="CanDraw"/>, keeping the canceller's reason for <see cref="LastPipeReport"/>.</summary>
    private bool CanDrawPipes(ICommonSession session, MappingToolsDrawEvent ev, WFMappingCanEditEvent check)
    {
        if (CanDraw(session, ev, check))
            return true;

        LastPipeReport = check.Reason ?? "wf-mapping-tools-draw-not-allowed";
        return false;
    }

    /// <summary>A pipe edit whose replaced pipes are already gone, so the new ones never overlap them.</summary>
    private MappingEdit StartPipeEdit(string name, List<EntityUid> removals)
    {
        var edit = new MappingEdit(name);
        if (removals.Count == 0)
            return edit;

        var deleted = new MappingEntityGroup();
        foreach (var uid in removals)
        {
            deleted.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
        }

        edit.Deleted.Add(deleted);
        Apply(edit, forward: true);
        return edit;
    }

    /// <summary>Adds the spawned pipes to a pipe edit and pushes it onto the session's history.</summary>
    private void FinishPipeEdit(ICommonSession session, MappingEdit edit, List<EntityUid> spawned)
    {
        if (spawned.Count > 0)
        {
            var created = new MappingEntityGroup();
            foreach (var uid in spawned)
            {
                created.Roots.Add(new MappingGroupRoot(GetRef(uid), -1, PoseOf(uid)));
            }

            edit.Created.Add(created);
        }

        Push(session, edit);
        RaiseEdited(edit);
    }

    /// <summary>
    /// Spawns a pipe or device at a cell's centre with a rotation, moved to <paramref name="layer"/> before it
    /// anchors when that is set, and painted.
    /// </summary>
    private EntityUid SpawnPipe(Entity<MapGridComponent> grid, string id, Vector2i cell, Angle rotation,
        AtmosPipeLayer? layer, Color? color)
    {
        var coordinates = new EntityCoordinates(grid.Owner, new Vector2(cell.X + 0.5f, cell.Y + 0.5f));
        EntityUid uid;
        if (layer is { } moved)
        {
            uid = EntityManager.CreateEntityUninitialized(id, coordinates, null, rotation);
            if (TryComp(uid, out AtmosPipeLayersComponent? layers))
                _pipeLayers.SetPipeLayer((uid, layers), moved);

            EntityManager.InitializeAndStartEntity(uid, _transform.GetMapId(coordinates));
        }
        else
        {
            uid = SpawnAttachedTo(id, coordinates, rotation: rotation);
        }

        if (color is { } paint)
            SetPipeColor(uid, paint);

        return uid;
    }

    /// <summary>Paints a pipe, if it still exists and can be painted.</summary>
    private void SetPipeColor(EntityUid uid, Color color)
    {
        if (!TerminatingOrDeleted(uid) && TryComp(uid, out AtmosPipeColorComponent? paint))
            _pipeColor.SetColor(uid, paint, color);
    }

    /// <summary>The grid cell an entity sits on.</summary>
    private Vector2i CellOf(Entity<MapGridComponent> grid, EntityUid uid)
    {
        return _map.TileIndicesFor(grid, grid.Comp, Transform(uid).Coordinates);
    }
}
