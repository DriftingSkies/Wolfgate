using Content.Shared._WG.SectorControl;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.AdsIncursion.Components;

/// <summary>An interdictor that won its challenge, on its encounter entity: it holds the cell it took, which frees when it dies.</summary>
[RegisterComponent]
public sealed partial class WGAdsPicketComponent : Component
{
    /// <summary>The cell it holds.</summary>
    [ViewVariables]
    public WGSectorCell Cell;

    /// <summary>The key of its lead ship.</summary>
    [ViewVariables]
    public string Lead = string.Empty;

    /// <summary>The faction it took the cell from.</summary>
    [ViewVariables]
    public ProtoId<WGSectorFactionPrototype>? Holder;

    /// <summary>When it took the cell; the oldest go home first when there are too many.</summary>
    [ViewVariables]
    public TimeSpan Since;
}
