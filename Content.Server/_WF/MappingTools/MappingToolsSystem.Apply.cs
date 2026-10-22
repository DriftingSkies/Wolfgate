using System.Linq;
using System.Numerics;
using Content.Shared.Decals;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Server._WF.MappingTools;

public sealed partial class MappingToolsSystem
{
    private static readonly SerializationOptions SnapshotOptions = SerializationOptions.Default with
    {
        MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
        ErrorOnOrphan = false,
        LogAutoInclude = null,
        ExpectPreInit = false,
    };

    /// <summary>
    /// Takes the state from one side of an edit to the other.
    /// </summary>
    private void Apply(MappingEdit edit, bool forward)
    {
        var splitGrids = new List<MapGridComponent>();
        foreach (var change in edit.Tiles)
        {
            if (TryComp(change.Grid, out MapGridComponent? grid) && grid.CanSplit)
            {
                grid.CanSplit = false;
                splitGrids.Add(grid);
            }
        }

        _applying = true;
        EntityUid? holding = null;
        try
        {
            foreach (var decal in forward ? edit.DecalsRemoved : edit.DecalsAdded)
            {
                RemoveDecal(decal);
            }

            foreach (var group in forward ? edit.Deleted : edit.Created)
            {
                RemoveGroup(group);
            }

            // New floor goes down before anything moves onto it, and old floor comes up only after it has left.
            var tiles = TargetTiles(edit, forward);
            SetTiles(tiles, filled: true);

            foreach (var move in edit.Moves)
            {
                var uid = move.Entity.Uid;
                var pose = forward ? move.New : move.Old;
                // Something protected may have come to sit under it since; it stays where it is.
                if (!TerminatingOrDeleted(uid) && !TerminatingOrDeleted(pose.Parent) && _protection.CanTouch(uid))
                    MoveEntity(uid, pose);
            }

            SetTiles(tiles, filled: false);

            foreach (var group in forward ? edit.Created : edit.Deleted)
            {
                RestoreGroup(group, ref holding);
            }

            foreach (var decal in forward ? edit.DecalsAdded : edit.DecalsRemoved)
            {
                AddDecal(decal);
            }

            foreach (var change in edit.PipeColors)
            {
                SetPipeColor(change.Entity.Uid, forward ? change.New : change.Old);
            }

            ApplySettings(edit, forward);
        }
        finally
        {
            if (holding is { } holdingMap && !TerminatingOrDeleted(holdingMap))
                Del(holdingMap);

            _applying = false;
            foreach (var grid in splitGrids)
            {
                grid.CanSplit = true;
            }
        }
    }

    /// <summary>
    /// The tile each changed cell ends up with. The last write per cell wins, so going backward restores the first.
    /// </summary>
    private Dictionary<EntityUid, Dictionary<Vector2i, Tile>> TargetTiles(MappingEdit edit, bool forward)
    {
        var byGrid = new Dictionary<EntityUid, Dictionary<Vector2i, Tile>>();
        for (var i = 0; i < edit.Tiles.Count; i++)
        {
            var change = edit.Tiles[forward ? i : edit.Tiles.Count - 1 - i];
            byGrid.GetOrNew(change.Grid)[change.Indices] = forward ? change.New : change.Old;
        }

        return byGrid;
    }

    /// <summary>
    /// Sets the filled or the empty target tiles one at a time; explosion edge tracking breaks on batched changes.
    /// </summary>
    private void SetTiles(Dictionary<EntityUid, Dictionary<Vector2i, Tile>> byGrid, bool filled)
    {
        foreach (var (gridUid, tiles) in byGrid)
        {
            if (!TryComp(gridUid, out MapGridComponent? grid) || TerminatingOrDeleted(gridUid))
                continue;

            foreach (var (indices, tile) in tiles)
            {
                if (tile.IsEmpty != filled)
                    _map.SetTile(gridUid, grid, indices, tile);
            }
        }
    }

    /// <summary>
    /// Puts an entity at a pose. Anchored entities moving along their grid stay anchored, since unanchoring makes
    /// cables cut themselves and pipes drop off their network.
    /// </summary>
    private void MoveEntity(EntityUid uid, MappingPose pose)
    {
        var xform = Transform(uid);
        if (xform.Anchored && pose.Anchored && xform.ParentUid == pose.Parent &&
            TryComp(pose.Parent, out MapGridComponent? grid))
        {
            var oldCell = _map.LocalToTile(pose.Parent, grid, xform.Coordinates);
            var newCell = _map.LocalToTile(pose.Parent, grid, new EntityCoordinates(pose.Parent, pose.Position));
            if (oldCell != newCell)
            {
                // Already anchored, so re-anchoring at the new cell raises no anchor change.
                _map.RemoveFromSnapGridCell(pose.Parent, grid, oldCell, uid);
                _transform.AnchorEntity((uid, xform), (pose.Parent, grid), newCell);
            }

            _transform.SetLocalRotation(uid, pose.Rotation, xform);

            // Lets node networks, airtightness and the like drop the old cell and pick up the new one.
            var ev = new ReAnchorEvent(uid, pose.Parent, pose.Parent, newCell, xform);
            RaiseLocalEvent(uid, ref ev);
            return;
        }

        if (xform.Anchored)
            _transform.Unanchor(uid, xform, setPhysics: false);

        _transform.SetCoordinates(uid, xform, new EntityCoordinates(pose.Parent, pose.Position), pose.Rotation);
        if (pose.Anchored)
            Anchor(uid, pose);
    }

    private void Anchor(EntityUid uid, MappingPose pose)
    {
        if (!TryComp(pose.Parent, out MapGridComponent? grid))
            return;

        var xform = Transform(uid);
        if (xform.Anchored)
            return;

        var indices = _map.LocalToTile(pose.Parent, grid, new EntityCoordinates(pose.Parent, pose.Position));
        _transform.AnchorEntity((uid, xform), (pose.Parent, grid), indices);
    }

    /// <summary>
    /// Saves the group's roots with everything inside them, then deletes them. Protected entities under a root are
    /// moved off it first, and a root that is itself protected stays.
    /// </summary>
    private void RemoveGroup(MappingEntityGroup group)
    {
        var roots = new HashSet<EntityUid>();
        foreach (var root in group.Roots)
        {
            var uid = root.Entity.Uid;
            if (TerminatingOrDeleted(uid) || _protection.IsProtected(uid))
                continue;

            root.Pose = PoseOf(uid);
            roots.Add(uid);
        }

        if (roots.Count == 0)
            return;

        foreach (var uid in roots)
        {
            _protection.MoveProtectedOff(uid);
        }

        SaveGroup(group, roots);

        foreach (var uid in roots)
        {
            Del(uid);
        }
    }

    /// <summary>
    /// Saves a group's roots with everything inside them, without deleting them.
    /// </summary>
    private void SaveGroup(MappingEntityGroup group, HashSet<EntityUid> roots)
    {
        // A grid's islands would split apart while it loads back, and the split-off parts stay on the holding map.
        foreach (var root in group.Roots)
        {
            if (!roots.Contains(root.Entity.Uid) || !TryComp(root.Entity.Uid, out MapGridComponent? grid))
                continue;

            root.CanSplit = grid.CanSplit;
            grid.CanSplit = false;
        }

        foreach (var root in group.Roots)
        {
            if (roots.Contains(root.Entity.Uid))
                root.IsGrid = HasComp<MapGridComponent>(root.Entity.Uid);
        }

        // Entities that never save (mobs, ghosts) throw in the serializer; they are deleted without a snapshot, so
        // undo can't bring them back. A failed save still lets the delete go ahead.
        var serializer = new EntitySerializer(_dependency, SnapshotOptions);
        var saved = roots.Where(uid => serializer.IsSerializable(uid)).ToHashSet();
        try
        {
            serializer.SerializeEntityRecursive(saved);
            group.Data = saved.Count > 0 ? serializer.Write() : null;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to snapshot {saved.Count} entities for mapping tools undo: {e}");
            group.Data = null;
        }

        var yamlUids = group.Data != null ? serializer.YamlUidMap : new Dictionary<EntityUid, int>();
        group.EntityCount = yamlUids.Count;
        group.Bytes = group.Data != null ? MappingDataSize.Estimate(group.Data) : 0;

        foreach (var root in group.Roots)
        {
            if (roots.Contains(root.Entity.Uid) && TryComp(root.Entity.Uid, out MapGridComponent? grid))
                grid.CanSplit = root.CanSplit;
        }

        // A root deleted by something else can't be restored from the new data.
        foreach (var root in group.Roots)
        {
            root.YamlUid = yamlUids.GetValueOrDefault(root.Entity.Uid, -1);
            _refs.Remove(root.Entity.Uid);
        }

        group.Inner.Clear();
        foreach (var (uid, yamlUid) in yamlUids)
        {
            if (!roots.Contains(uid) && _refs.Remove(uid, out var inner))
                group.Inner[yamlUid] = inner;
        }
    }

    /// <summary>
    /// Recreates a saved group at its roots' poses and points their refs at the new entities. The holding map is made
    /// on first use and shared by every group of one apply; the caller deletes it.
    /// </summary>
    private void RestoreGroup(MappingEntityGroup group, ref EntityUid? holding)
    {
        if (group.Data == null)
            return;

        var byYaml = new Dictionary<int, MappingGroupRoot>();
        foreach (var root in group.Roots)
        {
            if (TerminatingOrDeleted(root.Entity.Uid))
                byYaml[root.YamlUid] = root;
        }

        if (byYaml.Count == 0)
            return;

        // The roots start up on a paused holding map, not in nullspace, where some startup handlers throw (a
        // conveyor's construction node snaps it to the grid).
        holding ??= _map.CreateMap(out _, runMapInit: false);
        var options = new MapLoadOptions
        {
            MergeMap = Comp<MapComponent>(holding.Value).MapId,
            DeserializationOptions = DeserializationOptions.Default with
            {
                StoreYamlUids = true,
                LogInvalidEntities = false,
            },
        };

        if (!_loader.TryLoadGeneric(group.Data.Copy(), "mapping tools", out var result, options))
        {
            Log.Error("Failed to restore a mapping tools snapshot");

            // Leftovers would be taken for the next group's roots, whose yaml uids overlap.
            Del(holding.Value);
            holding = null;
            return;
        }

        PlaceRestored(group, byYaml, holding.Value, result);
    }

    /// <summary>
    /// Moves restored roots from the holding map to their poses and points their refs at the new entities.
    /// </summary>
    private void PlaceRestored(MappingEntityGroup group, Dictionary<int, MappingGroupRoot> byYaml, EntityUid holding,
        LoadResult result)
    {
        var orphans = new List<EntityUid>();
        var children = Transform(holding).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            orphans.Add(child);
        }

        var placed = new List<EntityUid>();
        foreach (var orphan in orphans)
        {
            if (!TryComp(orphan, out YamlUidComponent? yaml) ||
                !byYaml.Remove(yaml.Uid, out var root) ||
                TerminatingOrDeleted(root.Pose.Parent))
            {
                Del(orphan);
                continue;
            }

            var xform = Transform(orphan);
            _transform.SetCoordinates(orphan, xform, new EntityCoordinates(root.Pose.Parent, root.Pose.Position), root.Pose.Rotation);
            if (root.Pose.Anchored)
                Anchor(orphan, root.Pose);

            var old = root.Entity.Uid;
            root.Entity.Uid = orphan;
            _refs[orphan] = root.Entity;
            placed.Add(orphan);

            if (TryComp(orphan, out MapGridComponent? grid))
            {
                grid.CanSplit = root.CanSplit;
                RemapGrid(old, orphan);
            }
        }

        foreach (var uid in result.Entities)
        {
            if (TerminatingOrDeleted(uid) || !TryComp(uid, out YamlUidComponent? yaml))
                continue;

            if (group.Inner.Remove(yaml.Uid, out var inner))
            {
                inner.Uid = uid;
                _refs[uid] = inner;
            }

            RemComp(uid, yaml);
        }

        foreach (var uid in placed)
        {
            MatchMap(uid);
        }
    }

    /// <summary>
    /// Map-initialises and pauses a restored entity tree to match the map it landed on.
    /// </summary>
    private void MatchMap(EntityUid root)
    {
        if (Transform(root).MapUid is not { } map)
            return;

        var init = _map.IsInitialized(map);
        var paused = MetaData(map).EntityPaused;
        foreach (var uid in Tree(root))
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var meta = MetaData(uid);
            if (init && meta.EntityLifeStage == EntityLifeStage.Initialized)
                EntityManager.RunMapInit(uid, meta);

            _meta.SetEntityPaused(uid, paused, meta);
        }
    }

    private MappingPose PoseOf(EntityUid uid)
    {
        var xform = Transform(uid);
        return new MappingPose(xform.ParentUid, xform.LocalPosition, xform.LocalRotation, xform.Anchored);
    }

    private void RemoveDecal(MappingDecal target)
    {
        if (TerminatingOrDeleted(target.Grid))
            return;

        var pos = target.Decal.Coordinates;
        var bounds = new Box2(pos - new Vector2(0.05f), pos + new Vector2(0.05f));
        foreach (var (id, decal) in _decals.GetDecalsIntersecting(target.Grid, bounds))
        {
            if (!SameDecal(decal, target.Decal))
                continue;

            _decals.RemoveDecal(target.Grid, id);
            return;
        }
    }

    private void AddDecal(MappingDecal decal)
    {
        if (TerminatingOrDeleted(decal.Grid))
            return;

        _decals.TryAddDecal(decal.Decal, new EntityCoordinates(decal.Grid, decal.Decal.Coordinates), out _);
    }

    private static bool SameDecal(Decal a, Decal b)
    {
        return a.Id == b.Id &&
               a.Color == b.Color &&
               a.Angle.EqualsApprox(b.Angle) &&
               a.ZIndex == b.ZIndex &&
               a.Cleanable == b.Cleanable &&
               (a.Coordinates - b.Coordinates).LengthSquared() < 0.001f;
    }

    /// <summary>
    /// Points every recorded edit and clipboard at a grid's new uid after undo recreated it.
    /// </summary>
    private void RemapGrid(EntityUid old, EntityUid grid)
    {
        EntityUid Map(EntityUid uid) => uid == old ? grid : uid;

        foreach (var history in _histories.Values)
        {
            foreach (var edit in history.Undo.Concat(history.Redo))
            {
                for (var i = 0; i < edit.Tiles.Count; i++)
                {
                    edit.Tiles[i] = edit.Tiles[i] with { Grid = Map(edit.Tiles[i].Grid) };
                }

                for (var i = 0; i < edit.Moves.Count; i++)
                {
                    var move = edit.Moves[i];
                    edit.Moves[i] = move with
                    {
                        Old = move.Old with { Parent = Map(move.Old.Parent) },
                        New = move.New with { Parent = Map(move.New.Parent) },
                    };
                }

                foreach (var group in edit.Created.Concat(edit.Deleted))
                {
                    foreach (var root in group.Roots)
                    {
                        root.Pose = root.Pose with { Parent = Map(root.Pose.Parent) };
                    }
                }

                for (var i = 0; i < edit.DecalsRemoved.Count; i++)
                {
                    edit.DecalsRemoved[i] = edit.DecalsRemoved[i] with { Grid = Map(edit.DecalsRemoved[i].Grid) };
                }

                for (var i = 0; i < edit.DecalsAdded.Count; i++)
                {
                    edit.DecalsAdded[i] = edit.DecalsAdded[i] with { Grid = Map(edit.DecalsAdded[i].Grid) };
                }
            }
        }

        foreach (var clip in _clipboards.Values)
        {
            clip.SourceGrid = Map(clip.SourceGrid);
        }
    }
}
