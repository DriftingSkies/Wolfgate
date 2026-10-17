using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Drydock;

/// <summary>
/// A drydock console that charges materials from its cranes' storage and their linked silos instead of credits.
/// </summary>
[RegisterComponent]
public sealed partial class WFDrydockMaterialPaymentComponent : Component
{
    /// <summary>Material units charged per credit of work that has no material bill of its own.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> Rates = new();

    /// <summary>Share of its construction materials rebuilding anything but a wall bills.</summary>
    [DataField]
    public float RepairShare = 1f;

    /// <summary>Share of a wall's construction materials a hull break recovers.</summary>
    [DataField]
    public float StructureYield = 0.02f;

    /// <summary>Rebuilding a wall or tile bills this many times what breaking it recovers.</summary>
    [DataField]
    public float StructureRepairFactor = 1.3f;

    /// <summary>
    /// Share of every construction material a hull break recovers from what it strips, unless <see cref="BreakYield"/>
    /// lists the material. With neither set, the console can't break hulls.
    /// </summary>
    [DataField]
    public float BreakDefaultYield;

    /// <summary>Share a hull break recovers of particular construction materials, in place of the default.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, float> BreakYield = new();

    /// <summary>Material units a hull break recovers from each tile it lifts.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> BreakTileYield = new();

    /// <summary>Material units a hull break pays per tile it lifts on top of the hull's own, such as scrap; rebuilding the tile never bills them.</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> BreakTileSalvage = new();
}
