using Content.Shared._NF.Bank.Components;

namespace Content.Server._WF.Outposts.Components;

/// <summary>The console at the heart of an outpost.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostConsoleComponent : Component
{
    /// <summary>The outpost grid this console holds down.</summary>
    [ViewVariables]
    public EntityUid? Outpost;

    /// <summary>The sector accounts each Trade tab purchase is taxed into, as a share of its price.</summary>
    [DataField]
    public Dictionary<SectorBankAccount, float> TaxAccounts = new();
}
