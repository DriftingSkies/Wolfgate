using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.CodaSites;

/// <summary>A place in the Coda placed at round start: its map, how far out, and how clear of other grids.</summary>
[Prototype("wgCodaSite")]
public sealed partial class WGCodaSitePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public ResPath Map;

    [DataField]
    public float MinDistance = 95000f;

    [DataField]
    public float MaxDistance = 125000f;

    /// <summary>The Lattice band it must sit in.</summary>
    [DataField]
    public int Band = 4;

    [DataField]
    public float Clearance = 2500f;

    /// <summary>The map is a whole map file, not a grid: it is loaded on a scratch map and its grids moved out.</summary>
    [DataField]
    public bool WholeMap;

    /// <summary>What the site is beyond its map, for sites the server sets up in code once placed (e.g. "Echo").</summary>
    [DataField]
    public string? Kind;
}
