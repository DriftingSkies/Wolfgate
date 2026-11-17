using Content.Shared._WG.Encounters;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Remade.Sector;

/// <summary>A place a Monolith Scion is put into the sector: which encounter holds it, on which ship, and how far gone it is.</summary>
[Prototype("wgRemadeSite")]
public sealed partial class WGRemadeSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ProtoId<WGEncounterPrototype> Encounter;

    /// <summary>Key of the encounter ship the Scion is put aboard.</summary>
    [DataField]
    public string Ship = "hulk";

    /// <summary>Key of the Choir's ship in the encounter, if it has one; it hunts whoever takes the Scion.</summary>
    [DataField]
    public string? Choir;

    [DataField]
    public WGRemadeSiteKind Kind;

    /// <summary>Weight among the sites the discovery rule picks from.</summary>
    [DataField]
    public float Weight = 1f;

    [DataField]
    public EntProtoId Scion = "WGMonolithScion";

    /// <summary>Resonance the Scion starts with.</summary>
    [DataField]
    public float Resonance = 1f;
}

public enum WGRemadeSiteKind : byte
{
    /// <summary>A dormant Scion the crew has to go and find.</summary>
    Discovery,

    /// <summary>A hull already deep in an outbreak, calling for help.</summary>
    Adrift,
}
