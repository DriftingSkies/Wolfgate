using System.Numerics;
using Robust.Shared.Player;

namespace Content.Server._WF.MappingTools;

/// <summary>
/// Raised when a player without the Mapping admin flag uses the mapping tools; a handler sets
/// <see cref="Allowed"/> to let them. <see cref="Request"/> is set for requests the player sends, so a handler can
/// rate-limit them.
/// </summary>
public sealed class WFMappingCanUseEvent(ICommonSession session, bool request) : EntityEventArgs
{
    public readonly ICommonSession Session = session;
    public readonly bool Request = request;
    public bool Allowed;
}

/// <summary>What a mapping tools request does to the map it targets.</summary>
public enum WFMappingEditKind : byte
{
    /// <summary>Reads it, as Copy does.</summary>
    Read,

    /// <summary>Changes what is already there: move, mirror, cut, delete, undo and redo.</summary>
    Edit,

    /// <summary>Adds the clipboard's contents.</summary>
    Paste,

    /// <summary>Adds a z-level next to it.</summary>
    AddLevel,

    /// <summary>Deletes it from its z-stack.</summary>
    RemoveLevel,

    /// <summary>Only asks for <see cref="WFMappingCanEditEvent.HistoryLimits"/>; the map is unused.</summary>
    Limits,
}

/// <summary>How much undo history a mapper keeps: steps, and the estimated bytes of their saved entities.</summary>
public readonly record struct WFMappingHistoryLimits(int MaxSteps, long MaxBytes);

/// <summary>
/// Raised before a mapping tools request changes or reads a map; cancelling it drops the request. The canceller tells
/// the player why. Nothing cancels it outside mapping mode.
/// </summary>
public sealed class WFMappingCanEditEvent(ICommonSession session, EntityUid map, WFMappingEditKind kind)
    : CancellableEntityEventArgs
{
    public readonly ICommonSession Session = session;
    public readonly EntityUid Map = map;
    public readonly WFMappingEditKind Kind = kind;

    /// <summary>The grid the request works on, or null for a new grid.</summary>
    public EntityUid? Grid;

    /// <summary>The cells the result covers: local to <see cref="Grid"/>, or map tiles when it is null.</summary>
    public Box2i? Cells;

    /// <summary>Entities the request adds.</summary>
    public int Entities;

    /// <summary>Tiles the request touches.</summary>
    public int Tiles;

    /// <summary>The request makes a new grid.</summary>
    public bool NewGrid;

    /// <summary>The cell of each root entity the request adds, in the same frame as <see cref="Cells"/>.</summary>
    public List<Vector2i>? EntityCells;

    /// <summary>Entities the request moves, left out of the per-tile counts it checks <see cref="EntityCells"/> against.</summary>
    public HashSet<EntityUid>? Moving;

    /// <summary>Cells the request's selection covers.</summary>
    public long SelectionCells;

    /// <summary>Entities, contents included, the request saves, loads, moves or deletes.</summary>
    public int TouchedEntities;

    /// <summary>Decals the request copies or adds.</summary>
    public int Decals;

    /// <summary>The cell of each decal the request adds, in the same frame as <see cref="Cells"/>.</summary>
    public List<Vector2i>? DecalCells;

    /// <summary>Entity prototypes the request spawns by id.</summary>
    public List<string>? Prototypes;

    /// <summary>
    /// Prototypes of every entity a paste brings back, contents included. Unlike <see cref="Prototypes"/> these may
    /// be hidden from the spawn menu; only what they carry is checked.
    /// </summary>
    public IReadOnlyCollection<string>? CarriedPrototypes;

    /// <summary>Set by a handler that limits the player's undo history; null keeps the default.</summary>
    public WFMappingHistoryLimits? HistoryLimits;

    /// <summary>Only asks; the canceller doesn't tell the player.</summary>
    public bool Quiet;

    /// <summary>The Fluent id of the refusal, when cancelled.</summary>
    public string? Reason;
}

/// <summary>Raised after the levels of a z-stack change, with the levels bottom-up and the index of depth 0.</summary>
public sealed class WFMappingStackChangedEvent(List<EntityUid> levels, int ground) : EntityEventArgs
{
    public readonly List<EntityUid> Levels = levels;
    public readonly int Ground = ground;
}

/// <summary>Raised after the mapping tools change a map.</summary>
public sealed class WFMappingEditedEvent(EntityUid map) : EntityEventArgs
{
    public readonly EntityUid Map = map;
}

/// <summary>
/// Raised before a mapping tools request starts a new grid for tiles drawn or pasted off every grid. A handler that
/// sets <see cref="Grid"/> sends the tiles onto that grid instead. Nothing handles it outside mapping mode.
/// </summary>
public sealed class WFMappingNewGridTargetEvent(ICommonSession session, EntityUid map, Vector2 point) : EntityEventArgs
{
    public readonly ICommonSession Session = session;
    public readonly EntityUid Map = map;

    /// <summary>Where the tiles go, in map space.</summary>
    public readonly Vector2 Point = point;

    public EntityUid? Grid;
}
