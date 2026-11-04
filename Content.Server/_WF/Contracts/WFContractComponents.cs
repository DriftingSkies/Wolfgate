namespace Content.Server._WF.Contracts;

/// <summary>
/// A contract board: lists its contractors' offers and takes acceptances. Packages and crates for the board are left
/// beside it.
/// </summary>
[RegisterComponent]
public sealed partial class WFContractBoardComponent : Component
{
    /// <summary>Which contractors post here, by their <see cref="Shared._WF.Contracts.WFContractorPrototype.Board"/>.</summary>
    [DataField]
    public string Board = Shared._WF.Contracts.WFContractBoards.Central;
}

/// <summary>
/// A spot on a station where contract cargo is handed out. Without one, a station's warp point is used, then its
/// centre.
/// </summary>
[RegisterComponent]
public sealed partial class WFContractDropComponent : Component;

/// <summary>A package, crate or recovered item that belongs to a live contract.</summary>
[RegisterComponent]
public sealed partial class WFContractItemComponent : Component
{
    [ViewVariables]
    public int Contract;

    /// <summary>Set once it has counted towards its contract, so its removal does not fail it.</summary>
    [ViewVariables]
    public bool Delivered;
}

/// <summary>Marks the Contracts PDA app.</summary>
[RegisterComponent]
public sealed partial class WFContractCartridgeComponent : Component;
