using System.Linq;
using Content.Server.Atmos.Piping.Components;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._WF.MappingTools;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Database;
using Content.Shared.NodeContainer;
using Content.Shared.NodeContainer.NodeGroups;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    /// <summary>
    /// Places one atmos device from Pipe mode's device box on a cell: on the drawn layer, turned so its ports meet
    /// the pipes around it when auto-join is on, painted, with plain pipes beside it gaining the side it faces. A
    /// plain pipe on the cell is swapped for it when Replace is on. One undoable edit.
    /// </summary>
    private void PlacePipeDevice(ICommonSession session, Entity<MapGridComponent> grid, EntityUid map,
        MappingToolsDrawEvent ev)
    {
        LastPipeReport = null;
        if (ev.Pipes is not { } spec || !Enum.IsDefined(spec.Layer) || ev.Cells.Count != 1)
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-bad");
            return;
        }

        if (ev.Prototype is not { } requestedId ||
            !MappingToolsPipeDevices.Contains(requestedId) ||
            !_prototypes.TryIndex<EntityPrototype>(requestedId, out var prototype) ||
            DevicePorts(prototype) == PipeDirection.None)
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-device-bad");
            return;
        }

        if (!TryGetDeviceSpawn(prototype, spec.Layer, out var spawnId, out var moveLayer))
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-device-no-layer",
                ("device", prototype.Name), ("layer", (int) spec.Layer + 1));
            return;
        }

        if (!TryGetPipePrototypes(spec.Layer, out var byShape))
        {
            RefusePipes(session, ev, "wf-mapping-tools-draw-bad-prototype");
            return;
        }

        var cell = ev.Cells[0];
        if (TileAt(grid, cell).IsEmpty)
        {
            RefusePipes(session, ev, "wf-mapping-tools-pipes-device-no-floor");
            return;
        }

        var plainIds = byShape.Values.ToHashSet();
        var removals = new List<EntityUid>();
        Color? auto = null;
        if (PipesAt(grid, cell, spec.Layer, plainIds) is { } here)
        {
            if (here.Plain is not { } plainHere)
            {
                RefusePipes(session, ev, "wf-mapping-tools-pipes-device-blocked");
                return;
            }

            if (!spec.Replace)
            {
                RefusePipes(session, ev, "wf-mapping-tools-pipes-device-occupied");
                return;
            }

            removals.Add(plainHere);
            auto = ColorOfPipes(grid, cell, spec.Layer);
        }

        // Pipes beside the cell: those pointing at it, and plain ones that could gain the side.
        var neighbours = new Dictionary<PipeDirection, CellPipes>();
        var pointing = PipeDirection.None;
        var joinable = PipeDirection.None;
        if (spec.AutoJoin)
        {
            foreach (var side in MappingToolsPipes.Sides)
            {
                if (PipesAt(grid, MappingToolsPipes.Step(cell, side), spec.Layer, plainIds) is not { } next)
                    continue;

                neighbours[side] = next;
                if (next.Directions.HasDirection(side.GetOpposite()))
                    pointing |= side;
                else if (next.Plain != null)
                    joinable |= side;
            }
        }

        var ports = DevicePorts(prototype);
        var requested = MappingToolsPipes.TurnsToFace(spec.Facing);
        var turns = spec.AutoJoin ? MappingToolsPipes.DeviceTurns(ports, pointing, joinable, requested) : requested;
        var faced = MappingToolsPipes.Turn(ports, turns);

        var upgrades = new List<(Vector2i Cell, PipeShape Shape, Angle Rotation, Color? Color)>();
        foreach (var side in MappingToolsPipes.Sides)
        {
            if (!faced.HasDirection(side) || !neighbours.TryGetValue(side, out var next))
                continue;

            var nextCell = MappingToolsPipes.Step(cell, side);
            if (pointing.HasDirection(side))
            {
                auto ??= ColorOfPipes(grid, nextCell, spec.Layer);
                continue;
            }

            if (next.Plain is not { } plain ||
                !MappingToolsPipes.TryShape(next.Directions | side.GetOpposite(), out var shape, out var rotation))
            {
                continue;
            }

            var oldColor = TryComp(plain, out AtmosPipeColorComponent? paint) ? paint.Color : (Color?) null;
            auto ??= oldColor;
            removals.Add(plain);
            upgrades.Add((nextCell, shape, rotation, oldColor));
        }

        var color = spec.Color ?? (spec.AutoColor ? auto : null);
        var spawnCells = new List<Vector2i> { cell };
        spawnCells.AddRange(upgrades.Select(u => u.Cell));
        var prototypes = new List<string> { prototype.ID };
        prototypes.AddRange(upgrades.Select(u => MappingToolsPipes.PrototypeFor(u.Shape)).Distinct());

        var check = new WFMappingCanEditEvent(session, map, WFMappingEditKind.Paste)
        {
            Grid = grid.Owner,
            Cells = Bounds(spawnCells),
            Entities = Math.Max(0, spawnCells.Count - removals.Count),
            EntityCells = spawnCells,
            Moving = removals.ToHashSet(),
            SelectionCells = 1,
            TouchedEntities = spawnCells.Count + TreeCount(removals),
            Prototypes = prototypes,
        };

        if (!CanDrawPipes(session, ev, check))
            return;

        var edit = StartPipeEdit("wf-mapping-tools-edit-place-pipe-device", removals);
        var created = new List<EntityUid>
        {
            SpawnPipe(grid, spawnId, cell, MappingToolsPipes.QuarterTurns(turns), moveLayer ? spec.Layer : null, color),
        };

        foreach (var (upgradeCell, shape, rotation, oldColor) in upgrades)
        {
            created.Add(SpawnPipe(grid, byShape[shape], upgradeCell, rotation, null, oldColor));
        }

        FinishPipeEdit(session, edit, created);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{session:player} used mapping tools to place {spawnId} on layer {spec.Layer} of {ToPrettyString(grid.Owner):grid} (replacing {removals.Count}, joining {upgrades.Count})");
    }

    /// <summary>The sides a device's pipe nodes open to at rest, as its prototype has them.</summary>
    private PipeDirection DevicePorts(EntityPrototype prototype)
    {
        if (!prototype.TryGetComponent<NodeContainerComponent>(out var container, _factory))
            return PipeDirection.None;

        var ports = PipeDirection.None;
        foreach (var node in container.Nodes.Values)
        {
            if (node is PipeNode { NodeGroupID: NodeGroupID.Pipe } pipe)
                ports |= pipe.OriginalPipeDirection;
        }

        return ports & PipeDirection.Fourway;
    }

    /// <summary>
    /// What to spawn for a device on a layer: its layer alternative when the prototype names one, else the device
    /// itself moved to the layer before it anchors. False when the device can't go on that layer.
    /// </summary>
    private bool TryGetDeviceSpawn(EntityPrototype prototype, AtmosPipeLayer layer, out string id, out bool moveLayer)
    {
        id = prototype.ID;
        moveLayer = false;
        if (layer == AtmosPipeLayer.Primary)
            return true;

        if (!prototype.TryGetComponent<AtmosPipeLayersComponent>(out var layers, _factory))
            return false;

        if (_pipeLayers.TryGetAlternativePrototype(layers, layer, out var alternative) &&
            _prototypes.HasIndex<EntityPrototype>(alternative))
        {
            id = alternative;
            return true;
        }

        var locked = layers.PipeLayersLocked;
        var count = layers.NumberOfPipeLayers;
        if (locked || (int) layer >= count)
            return false;

        moveLayer = true;
        return true;
    }
}
