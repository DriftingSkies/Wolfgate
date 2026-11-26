using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.CodaSites;

/// <summary>Every power receiver on this grid needs no power, including anything anchored later.</summary>
[RegisterComponent]
public sealed partial class WGSelfPoweredGridComponent : Component;

/// <summary>On a receiver a self-powered grid exempted: which grid, so it needs power again once it leaves.</summary>
[RegisterComponent]
public sealed partial class WGSelfPoweredReceiverComponent : Component
{
    [ViewVariables]
    public EntityUid Grid;
}

/// <summary>Tops every tile of the grid back up to one mix, so a breach never drains it.</summary>
[RegisterComponent]
public sealed partial class WGFixedAtmosphereComponent : Component
{
    [DataField]
    public float Pressure = 70f;

    [DataField]
    public float Temperature = 250f;

    /// <summary>Oxygen's partial pressure, kept at the usual so thin air stays breathable.</summary>
    [DataField]
    public float OxygenPressure = 21.27f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(5);

    [ViewVariables]
    public TimeSpan Next;
}

/// <summary>
/// The hull heals: damage mends once a piece is left alone, and a wall, window, door or floor that is destroyed grows
/// back where it stood, flesh first and then stone.
/// </summary>
[RegisterComponent]
public sealed partial class WGHullRegenComponent : Component
{
    /// <summary>Share of a piece's damage mended each second.</summary>
    [DataField]
    public float HealFraction = 0.05f;

    /// <summary>How long a piece must go unhit before it mends.</summary>
    [DataField]
    public TimeSpan HealDelay = TimeSpan.FromSeconds(5);

    /// <summary>How long after a piece is destroyed the flesh starts to grow.</summary>
    [DataField]
    public TimeSpan RegrowDelay = TimeSpan.FromSeconds(60);

    /// <summary>How long the flesh stands before it hardens into the piece again.</summary>
    [DataField]
    public TimeSpan FleshTime = TimeSpan.FromSeconds(15);

    [DataField]
    public EntProtoId Flesh = "WallFlesh";

    /// <summary>The hull as mapped: each tile's airtight piece.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, string> Hull = new();

    /// <summary>The floor as mapped.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, string> Floors = new();

    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> Pieces = new();

    [ViewVariables]
    public Dictionary<Vector2i, TimeSpan> Gone = new();

    [ViewVariables]
    public Dictionary<Vector2i, EntityUid> Growing = new();

    /// <summary>When each growing flesh appeared; it hardens <see cref="FleshTime"/> after.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, TimeSpan> FleshAt = new();

    /// <summary>Each piece's damage when last seen, and when it was last hit.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, (FixedPoint2 Total, TimeSpan Hit)> Seen = new();

    [ViewVariables]
    public bool Recorded;

    [ViewVariables]
    public TimeSpan Next;
}
