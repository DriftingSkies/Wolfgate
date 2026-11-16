using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipTiers;

/// <summary>A faction capital: it holds one of its faction's capital slots until it is lost.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WFCapitalShipComponent : Component
{
    /// <summary>The faction whose slot it holds.</summary>
    [DataField]
    public ProtoId<CompanyPrototype> Company;

    /// <summary>When construction ends and the ship can fly; null once it has.</summary>
    [DataField, AutoPausedField]
    public TimeSpan? ReadyAt;

    /// <summary>Set once the ship has been lost, so its lockout starts only once.</summary>
    [DataField]
    public bool Lost;
}
