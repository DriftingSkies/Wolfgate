using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WF.Outposts;

/// <summary>A ready-made outpost anyone may bring down instead of a save of their own, for a fixed price.</summary>
[Prototype("wfOutpostPreset")]
public sealed partial class WFOutpostPresetPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public LocId Description;

    /// <summary>The grid, under SharedMaps so clients can preview it.</summary>
    [DataField(required: true)]
    public ResPath Grid;

    /// <summary>What it costs to bring down: at least its appraisal with the load markup.</summary>
    [DataField(required: true)]
    public int Price;

    /// <summary>Where it sits in lists, lowest first.</summary>
    [DataField]
    public int Order;
}
