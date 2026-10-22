using System.Numerics;
using Content.Shared.Access;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// A handle to an entity that stays valid when undo deletes and recreates it under a new uid.
/// </summary>
public sealed class MappingEntityRef(EntityUid uid)
{
    public EntityUid Uid = uid;
}

/// <summary>
/// Where a root entity sits: its parent (a grid, or a map for loose entities), local position and rotation.
/// </summary>
public record struct MappingPose(EntityUid Parent, Vector2 Position, Angle Rotation, bool Anchored);

/// <summary>
/// Root entities saved with the engine serializer so they can be deleted and recreated with their contents.
/// </summary>
public sealed class MappingEntityGroup
{
    /// <summary>
    /// The saved entities, or null while they exist and haven't been saved yet.
    /// </summary>
    public MappingDataNode? Data;

    /// <summary>Entities in <see cref="Data"/>, contents included.</summary>
    public int EntityCount;

    /// <summary>Estimated size of <see cref="Data"/>, for the history budget.</summary>
    public long Bytes;

    public readonly List<MappingGroupRoot> Roots = new();

    /// <summary>
    /// Refs of entities saved inside the roots, by yaml uid, so they follow a restore too.
    /// </summary>
    public readonly Dictionary<int, MappingEntityRef> Inner = new();
}

/// <summary>
/// One root of a <see cref="MappingEntityGroup"/>, matched to its saved data by yaml uid.
/// </summary>
public sealed class MappingGroupRoot(MappingEntityRef entity, int yamlUid, MappingPose pose)
{
    public readonly MappingEntityRef Entity = entity;
    public int YamlUid = yamlUid;
    public MappingPose Pose = pose;

    /// <summary>
    /// Whether a grid root could split; it is saved unsplittable so loading doesn't break it apart.
    /// </summary>
    public bool CanSplit = true;

    /// <summary>Whether the root was a grid when it was saved.</summary>
    public bool IsGrid;
}

public record struct MappingTileChange(EntityUid Grid, Vector2i Indices, Tile Old, Tile New);

public record struct MappingEntityMove(MappingEntityRef Entity, MappingPose Old, MappingPose New);

/// <summary>A pipe painted another colour, which stays the same entity.</summary>
public record struct MappingPipeColorChange(MappingEntityRef Entity, Color Old, Color New);

/// <summary>The signal links between a source and a sink before and after; an empty list is no link.</summary>
public record struct MappingLinkChange(
    MappingEntityRef Source,
    MappingEntityRef Sink,
    List<(string Source, string Sink)> Old,
    List<(string Source, string Sink)> New);

/// <summary>A device list's devices before and after.</summary>
public record struct MappingDeviceListChange(
    MappingEntityRef Holder,
    List<MappingEntityRef> Old,
    List<MappingEntityRef> New);

/// <summary>
/// A reader's access lists before and after, and whether it carried a <c>WFMappingAccessFill</c> marker.
/// </summary>
public record struct MappingAccessChange(
    MappingEntityRef Reader,
    List<HashSet<ProtoId<AccessLevelPrototype>>> Old,
    List<HashSet<ProtoId<AccessLevelPrototype>>> New,
    bool OldFill,
    bool NewFill);

/// <summary>
/// A decal by value, with grid-local coordinates; decal ids change when one is re-added.
/// </summary>
public record struct MappingDecal(EntityUid Grid, Decal Decal);

/// <summary>
/// One undoable change. Applying it forward goes from the old state to the new one, backward the reverse.
/// </summary>
public sealed class MappingEdit(string name)
{
    /// <summary>
    /// Fluent id naming the edit in the undo and redo popups.
    /// </summary>
    public readonly string Name = name;

    /// <summary>
    /// The tick a placement edit was recorded on; placements in the same tick merge into one edit.
    /// </summary>
    public GameTick? PlacementTick;

    public readonly List<MappingTileChange> Tiles = new();
    public readonly List<MappingEntityMove> Moves = new();
    public readonly List<MappingEntityGroup> Created = new();
    public readonly List<MappingEntityGroup> Deleted = new();
    public readonly List<MappingDecal> DecalsRemoved = new();
    public readonly List<MappingDecal> DecalsAdded = new();
    public readonly List<MappingPipeColorChange> PipeColors = new();
    public readonly List<MappingLinkChange> Links = new();
    public readonly List<MappingDeviceListChange> DeviceLists = new();
    public readonly List<MappingAccessChange> Access = new();

    public bool IsEmpty => Tiles.Count == 0 && Moves.Count == 0 && Created.Count == 0 && Deleted.Count == 0 &&
                           DecalsRemoved.Count == 0 && DecalsAdded.Count == 0 && PipeColors.Count == 0 &&
                           Links.Count == 0 && DeviceLists.Count == 0 && Access.Count == 0;

    /// <summary>Entities whose settings (colour, links, device list, access) change without moving.</summary>
    public int SettingChanges => PipeColors.Count + 2 * Links.Count + DeviceLists.Count + Access.Count;

    /// <summary>
    /// Appends another edit that happened after this one.
    /// </summary>
    public void Merge(MappingEdit other)
    {
        Tiles.AddRange(other.Tiles);
        Moves.AddRange(other.Moves);
        Created.AddRange(other.Created);
        Deleted.AddRange(other.Deleted);
        DecalsRemoved.AddRange(other.DecalsRemoved);
        DecalsAdded.AddRange(other.DecalsAdded);
        PipeColors.AddRange(other.PipeColors);
        Links.AddRange(other.Links);
        DeviceLists.AddRange(other.DeviceLists);
        Access.AddRange(other.Access);
    }

    /// <summary>Estimated memory the edit holds, for the history budget.</summary>
    public long Bytes()
    {
        const int perChange = 64;
        var bytes = (long) perChange * (Tiles.Count + Moves.Count + DecalsRemoved.Count + DecalsAdded.Count +
                                        SettingChanges);
        foreach (var list in DeviceLists)
        {
            bytes += 8L * (list.Old.Count + list.New.Count);
        }
        foreach (var group in Created)
        {
            bytes += group.Bytes + perChange * group.Roots.Count;
        }

        foreach (var group in Deleted)
        {
            bytes += group.Bytes + perChange * group.Roots.Count;
        }

        return bytes;
    }
}

/// <summary>Rough sizes of saved entity data.</summary>
public static class MappingDataSize
{
    /// <summary>The characters of every key and value in a node tree, plus a little per node.</summary>
    public static long Estimate(DataNode? root)
    {
        if (root == null)
            return 0;

        var bytes = 0L;
        var stack = new Stack<DataNode>();
        stack.Push(root);
        while (stack.TryPop(out var node))
        {
            bytes += 8;
            switch (node)
            {
                case ValueDataNode value:
                    bytes += value.Value.Length;
                    break;
                case MappingDataNode mapping:
                    foreach (var (key, child) in mapping.Children)
                    {
                        bytes += key.Length;
                        stack.Push(child);
                    }

                    break;
                case SequenceDataNode sequence:
                    foreach (var child in sequence.Sequence)
                    {
                        stack.Push(child);
                    }

                    break;
            }
        }

        return bytes;
    }
}

/// <summary>
/// A copied selection. Positions are relative to the bottom-left of a box of <see cref="Size"/>.
/// </summary>
public sealed class MappingClipboard
{
    /// <summary>
    /// The grid it was copied from, which a paste off every grid lands on.
    /// </summary>
    public EntityUid SourceGrid;

    public Vector2i Size;
    public readonly List<(Vector2i Cell, Tile Tile)> Tiles = new();
    public readonly List<Decal> Decals = new();
    public MappingDataNode? Data;

    /// <summary>
    /// Entities in <see cref="Data"/>, contents included.
    /// </summary>
    public int EntityCount;

    /// <summary>Estimated size of <see cref="Data"/>.</summary>
    public long Bytes;

    public readonly List<(int YamlUid, MappingPose Pose)> Roots = new();

    /// <summary>The prototype of every entity in <see cref="Data"/>, so a paste can be checked against them.</summary>
    public readonly HashSet<string> Prototypes = new();
}
