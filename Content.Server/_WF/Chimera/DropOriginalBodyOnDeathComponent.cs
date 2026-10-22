using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Chimera;

/// <summary>
/// When this mob dies or is gibbed while it is a polymorph, the body it was made from falls out of it and takes back
/// the mind. The mob's own corpse stays.
/// </summary>
[RegisterComponent, Access(typeof(DropOriginalBodyOnDeathSystem))]
public sealed partial class DropOriginalBodyOnDeathComponent : Component
{
    /// <summary>
    /// Reagents removed from every solution of the dropped body, so what turned it can't turn it again.
    /// </summary>
    [DataField]
    public List<ProtoId<ReagentPrototype>> PurgedReagents = new();

    /// <summary>
    /// Popup shown over the dropped body; gets the body as <c>body</c>.
    /// </summary>
    [DataField]
    public LocId Popup = "wf-chimera-original-body-drop";

    /// <summary>
    /// How long Letoferol can't turn the dropped body again, so other fleshbeasts can't bring it straight back.
    /// </summary>
    [DataField]
    public TimeSpan TurnCooldown = TimeSpan.FromMinutes(3);
}
