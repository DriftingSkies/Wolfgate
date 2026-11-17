using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipTiers;

/// <summary>A faction capital: it holds one of its faction's capital slots until it is lost.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class WGCapitalShipComponent : Component
{
    /// <summary>The faction whose slot it holds.</summary>
    [DataField]
    public ProtoId<CompanyPrototype> Company;

    /// <summary>When construction ends and the ship can fly; null once it has.</summary>
    [DataField, AutoPausedField]
    public TimeSpan? ReadyAt;

    /// <summary>Set once the ship has been powered and crewed since construction ended; only then can it go adrift.</summary>
    [DataField]
    public bool SeenActive;

    /// <summary>Set when the ship is sold, so its deletion isn't taken for a loss.</summary>
    [ViewVariables]
    public bool Sold;

    /// <summary>Set once the ship has been lost, so its lockout starts only once.</summary>
    [DataField]
    public bool Lost;

    /// <summary>When the lockout its loss started ends; cleared if the ship turns out not to be lost after all.</summary>
    [DataField, AutoPausedField]
    public TimeSpan? LockedUntil;
}
