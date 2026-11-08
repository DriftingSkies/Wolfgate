using Content.Shared.Materials;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.OutpostIndustry.Components;

/// <summary>Presses the sheets put in it into a sellable product, one every <see cref="Interval"/>.</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(WFOutpostIndustrySystem))]
public sealed partial class WFPartsPressComponent : Component
{
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>The material each pressing uses, in material units (100 a sheet).</summary>
    [DataField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Cost = new();

    [DataField(required: true)]
    public EntProtoId Product;

    [DataField, AutoPausedField]
    public TimeSpan NextPress;
}
