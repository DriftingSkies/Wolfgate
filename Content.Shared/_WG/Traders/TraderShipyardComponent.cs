using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traders;

/// <summary>
/// Lets a trader front the listings of one or more shipyard consoles. Everything that makes a
/// listing what it is - UI key, stock, job title, access levels, taxes, company rules - is read off
/// the console entity prototype, so any console can be sold over a counter without new code.
/// </summary>
[RegisterComponent]
public sealed partial class TraderShipyardComponent : Component
{
    /// <summary>
    /// Console prototypes this trader is allowed to front. A dialogue option names one in its
    /// <see cref="TraderDialogueOption.Argument"/>.
    /// </summary>
    [DataField(required: true)]
    public List<TraderShipyardListing> Consoles = new();

    /// <summary>
    /// Console prototype currently copied onto the trader, if a customer has a listing open.
    /// </summary>
    [ViewVariables]
    public EntProtoId? ActiveConsole;

    /// <summary>
    /// UI key the active listing is served on.
    /// </summary>
    [ViewVariables]
    public Enum? ActiveKey;

    /// <summary>
    /// Deed card the customer has been asked about giving up.
    /// </summary>
    [ViewVariables]
    public EntityUid? PendingUnassign;

    /// <summary>
    /// Deed card the customer has been asked to name a ship for.
    /// </summary>
    [ViewVariables]
    public EntityUid? PendingRename;

    /// <summary>
    /// Lets the hosted console run its unassign path for the one call the dealer makes.
    /// </summary>
    [ViewVariables]
    public bool AllowUnassign;
}

/// <summary>
/// One shipyard console prototype a trader can front.
/// </summary>
[DataDefinition]
public sealed partial class TraderShipyardListing
{
    /// <summary>
    /// Shipyard console entity prototype, e.g. ComputerShipyard.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Console;
}
