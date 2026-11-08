using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A synthesizer: while powered, fills its tank with one reagent at a steady rate until the tank is full.</summary>
[RegisterComponent, Access(typeof(WFOutpostLifeSupportSystem))]
public sealed partial class WFReagentSynthesizerComponent : Component
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    /// <summary>Units made a second.</summary>
    [DataField]
    public FixedPoint2 Rate = 1;

    [DataField]
    public string Solution = "tank";
}
