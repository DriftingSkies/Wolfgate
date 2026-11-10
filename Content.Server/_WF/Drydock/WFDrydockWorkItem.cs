using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Drydock;

/// <summary>What a drydock work item does to the ship.</summary>
public enum WFDrydockWorkKind : byte
{
    Tile,
    Entity,
    Heal,
    Upgrade,
    Break,

    /// <summary>Takes a part off the ship; ranked before installs, so a mount is cleared before its new gun goes on.</summary>
    Remove,

    /// <summary>Puts a new part on the ship: <see cref="WFDrydockWorkItem.Prototype"/> at its position and rotation.</summary>
    Install,

    /// <summary>Looks a part over and changes nothing; whoever ordered the job hears of it.</summary>
    Inspect,
}

/// <summary>
/// One unit of drydock work: a hull tile, a missing entity, a heal, a refit tier, a hull tile to break, or a part to take
/// off, put on or look over.
/// </summary>
public sealed class WFDrydockWorkItem
{
    /// <summary>What this item does.</summary>
    public WFDrydockWorkKind Kind;

    /// <summary>Ship grid index of a hull tile.</summary>
    public Vector2i TileIndex;

    /// <summary>Stored tile type to restore.</summary>
    public int TileType;

    /// <summary>Repair snapshot chunk of a rebuild's entity spec.</summary>
    public Vector2i ChunkPos;

    /// <summary>Key of the entity spec in its chunk.</summary>
    public int SpecId;

    /// <summary>Entity to heal or refit.</summary>
    public EntityUid Target;

    /// <summary>Refit prototype id of a refit item.</summary>
    public string? Upgrade;

    /// <summary>Tier a refit item brings its target to.</summary>
    public int Tier;

    /// <summary>Prototype a rebuild or heal works on, for its construction materials.</summary>
    public EntProtoId? Prototype;

    /// <summary>Whether a rebuild mounts on a hardpoint, so it is built after the other parts.</summary>
    public bool Mounted;

    /// <summary>Share of a full rebuild a rebuild or heal takes, after any repair cap.</summary>
    public float Share = 1f;

    /// <summary>Ship-local position used for effects and crane targeting.</summary>
    public Vector2 LocalPos;

    /// <summary>Credits charged for this item.</summary>
    public int Cost;

    /// <summary>Share of the cost for refits, charged outside the repair cap: all of a refit item's, or a rebuild's restore.</summary>
    public int RefitCost;

    /// <summary>Tier materials of that refit share, for a console that pays in materials.</summary>
    public Dictionary<string, int>? RefitMaterials;

    /// <summary>Refit units this item pays for in full, which parts from a parts silo can pay for instead of materials.</summary>
    public List<WFDrydockRefitUnits>? RefitUnits;

    /// <summary>Material bill when quoted by a console that pays in materials, or null.</summary>
    public Dictionary<string, int>? Materials;

    /// <summary>Parts taken from a parts silo in place of materials, by prototype, or null.</summary>
    public Dictionary<string, int>? Parts;

    /// <summary>Seconds one crane spends on this item, set when the job starts.</summary>
    public float WorkTime;

    /// <summary>Rotation of a part an install puts on, in the ship's frame.</summary>
    public Angle Rotation;

    /// <summary>Multiplies an install's, removal's or inspection's work time, so a job can run faster or slower than a repair.</summary>
    public float WorkScale = 1f;
}

/// <summary>Raised on a ship when a drydock install puts a part on it.</summary>
[ByRefEvent]
public readonly record struct WFDrydockPartInstalledEvent(EntityUid Part, EntityUid Console);

/// <summary>Raised on a ship when a drydock inspection reaches one of its parts.</summary>
[ByRefEvent]
public readonly record struct WFDrydockPartInspectedEvent(EntityUid Part, EntityUid Console);

/// <summary>Raised on a drydock console when its job ends, finished or not, before its cranes park.</summary>
[ByRefEvent]
public readonly record struct WFDrydockJobEndedEvent(EntityUid Ship, bool Finished, int Skipped);

/// <summary>Units of a refit line an item brings to a tier, paid in full, by the tier each reaches now.</summary>
public sealed record WFDrydockRefitUnits(string Refit, int Tier, int[] Full);
