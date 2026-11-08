using Content.Shared._WF.ShipAccess;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Outposts.Components;

/// <summary>A founded outpost: the anchored grid a player built from an outpost console.</summary>
[RegisterComponent, Access(typeof(WFOutpostSystem))]
public sealed partial class WFOutpostComponent : Component
{
    /// <summary>Account that founded the outpost; one live outpost per account.</summary>
    [ViewVariables]
    public NetUserId Owner;

    /// <summary>The founder's character name, for display.</summary>
    [ViewVariables]
    public string OwnerName = string.Empty;

    /// <summary>The console that holds the outpost down; destroying it releases the grid.</summary>
    [ViewVariables]
    public EntityUid Console;

    /// <summary>The ground map the outpost stands on.</summary>
    [ViewVariables]
    public EntityUid Ground;

    /// <summary>Pathfinding portals joining each edge tile to the ground outside it.</summary>
    [ViewVariables]
    public List<int> Portals = new();

    /// <summary>Floor tiles the outpost was founded with, which the console kit paid for; plates paid for the rest.</summary>
    [DataField]
    public int FoundingTiles;

    /// <summary>What a claimed site was worth when it was claimed, taken off every sale; saved with it.</summary>
    [DataField]
    public int ClaimBaseline;

    /// <summary>
    /// Which outpost this is through its saves and loads, as a GUID: set at founding, kept by every save of it
    /// (<see cref="WFOutpostSystem.GetLineage"/>).
    /// </summary>
    [DataField]
    public string Lineage = string.Empty;

    /// <summary>The owner's save slot this outpost was last saved into or loaded from; autosaves go there.</summary>
    [ViewVariables]
    public int? Slot;

    /// <summary>When the outpost is next autosaved, if it changed.</summary>
    [ViewVariables]
    public TimeSpan NextAutosave;

    /// <summary>What the outpost looked like when it was last saved (<see cref="WFOutpostSystem.Fingerprint"/>).</summary>
    [ViewVariables]
    public int SavedFingerprint;

    /// <summary>Ground tiles carved out from under the outpost, by ground index, put back when it goes.</summary>
    [ViewVariables]
    public Dictionary<Vector2i, Tile> Carved = new();

    /// <summary>The outpost's basement grid one depth down, if it has dug one.</summary>
    [ViewVariables]
    public EntityUid? Basement;

    /// <summary>Floor tiles opened as hatches down to the basement; saved, so a save without its basement can close them.</summary>
    [DataField]
    public List<Vector2i> Hatches = new();

    /// <summary>
    /// The allow list by account and character as of the last save, since the live list holds this round's record
    /// keys. Written just before each save.
    /// </summary>
    [DataField]
    public List<WFOutpostAccessGrant> Access = new();

    /// <summary>Saved grants not yet listed this round, waiting for their character to play.</summary>
    [ViewVariables]
    public List<WFOutpostAccessGrant> PendingAccess = new();

    /// <summary>Who holds each listed card, as far as has been seen: the player and character a grant is saved under.</summary>
    [ViewVariables]
    public Dictionary<WFShipAccessKey, (NetUserId User, string Character)> Holders = new();

    /// <summary>Trade tab orders paid for and not yet landed; lost with the round.</summary>
    [ViewVariables]
    public List<WFOutpostTradeOrder> Orders = new();

    /// <summary>
    /// The console was destroyed: the outpost stands and keeps its claim, but frees its owner's slot, and only the owner
    /// can found it again with a new console kit.
    /// </summary>
    [ViewVariables]
    public bool Abandoned;

    /// <summary>Whether the outpost is sold to the game when the round ends, the owner paid even if offline.</summary>
    [ViewVariables]
    public bool SellAtRoundEnd;

    /// <summary>The owner's standing offer of the outpost to a player at its console.</summary>
    [ViewVariables]
    public WFOutpostSaleOffer? Offer;
}

/// <summary>An offer of an outpost to one player, for a price, until it runs out.</summary>
public sealed class WFOutpostSaleOffer
{
    public NetUserId Buyer;

    public string BuyerName = string.Empty;

    public int Price;

    public TimeSpan Expires;
}

/// <summary>A Trade tab order on its way: what was bought, when it drops, and the delivery once it is falling.</summary>
public sealed class WFOutpostTradeOrder
{
    public ProtoId<CargoProductPrototype> Product;

    public TimeSpan Drops;

    public EntityUid? Delivery;
}

/// <summary>One allow list entry of an outpost as a save keeps it: whose character it was, and how it was listed.</summary>
[DataDefinition]
public sealed partial class WFOutpostAccessGrant
{
    /// <summary>The account, as a GUID.</summary>
    [DataField]
    public string User = string.Empty;

    /// <summary>The character's name; their card is listed again when they play that character.</summary>
    [DataField]
    public string Character = string.Empty;

    [DataField]
    public string Name = string.Empty;

    [DataField]
    public string Label = string.Empty;

    [DataField]
    public bool Builder;
}
