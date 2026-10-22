using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Xenonids.MassScanner;

/// <summary>Gives a xeno an action that opens the mass scanner (radar) view centred on itself.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class XenoMassScannerComponent : Component
{
    [DataField]
    public EntProtoId Action = "WFActionXenoMassScanner";

    [DataField]
    public EntityUid? ActionEntity;
}
