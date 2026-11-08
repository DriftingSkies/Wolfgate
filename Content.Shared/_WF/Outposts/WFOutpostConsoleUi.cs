using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Outposts;

[Serializable, NetSerializable]
public enum WFOutpostConsoleUiKey : byte
{
    Key,
}

/// <summary>What the outpost console's tabs show.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostConsoleState : BoundUserInterfaceState
{
    public readonly string Name;
    public readonly string OwnerName;
    public readonly NetUserId Owner;
    public readonly int Tiles;
    public readonly int MaxTiles;
    public readonly int Entities;
    public readonly int MaxEntities;

    /// <summary>The owner's save slots, empty ones as null; null while they are still being read.</summary>
    public readonly List<WFOutpostSaveSlot?>? Slots;

    /// <summary>A save or load in progress; the Save tab's buttons wait for it.</summary>
    public readonly bool Busy;

    /// <summary>The save on its way down onto this outpost, if any.</summary>
    public readonly WFOutpostPendingLoad? Pending;

    /// <summary>What the Trade tab shows.</summary>
    public readonly WFOutpostTradeView Trade;

    /// <summary>What the Sell tab shows.</summary>
    public readonly WFOutpostSaleView Sale;

    /// <summary>The raid window on the outpost's world; null where nothing raids.</summary>
    public WFOutpostRaidView? Raid;

    public WFOutpostConsoleState(string name, string ownerName, NetUserId owner, int tiles, int maxTiles, int entities, int maxEntities,
        List<WFOutpostSaveSlot?>? slots, bool busy, WFOutpostPendingLoad? pending, WFOutpostTradeView trade, WFOutpostSaleView sale)
    {
        Name = name;
        OwnerName = ownerName;
        Owner = owner;
        Tiles = tiles;
        MaxTiles = maxTiles;
        Entities = entities;
        MaxEntities = maxEntities;
        Slots = slots;
        Busy = busy;
        Pending = pending;
        Trade = trade;
        Sale = sale;
    }
}

/// <summary>One filled save slot, as the Save tab lists it.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostSaveSlot(string Name, int Tiles, int Entities, int Price, DateTime Saved, bool InUse);

/// <summary>A save being brought down onto the outpost, and when it lands.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostPendingLoad(string Name, int Price, TimeSpan Lands);

/// <summary>Whether the outpost's trade hub can take a drop.</summary>
[Serializable, NetSerializable]
public enum WFOutpostHubStatus : byte
{
    /// <summary>No trade hub stands on the outpost.</summary>
    None,

    /// <summary>The hub has no power.</summary>
    Unpowered,

    /// <summary>A roof covers the hub.</summary>
    Covered,

    Ready,
}

/// <summary>The Trade tab: the hub, the owner's balance and the orders on their way.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostTradeView(WFOutpostHubStatus Hub, int Balance, List<WFOutpostOrderView> Orders);

/// <summary>An order on its way: the listing, and when it drops or whether it is already falling.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostOrderView(string Product, TimeSpan Drops, bool Falling);

/// <summary>
/// The Sell tab: what the outpost sells to the game for, the round-end sale switch, the players at the console the owner
/// can offer it to, and the standing offer.
/// </summary>
[Serializable, NetSerializable]
public sealed record WFOutpostSaleView(int Value, bool SellAtRoundEnd, List<WFOutpostBuyerView> Buyers, WFOutpostOfferView? Offer);

/// <summary>A player standing at the console, who the outpost can be offered to.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostBuyerView(NetUserId User, string Name);

/// <summary>The owner's standing offer of the outpost to one player.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostOfferView(NetUserId Buyer, string BuyerName, int Price);

/// <summary>The owner sells the outpost to the game: its structures and machines are paid for and taken away.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostSellMessage : BoundUserInterfaceMessage;

/// <summary>The owner sets whether the outpost is sold to the game when the round ends.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostSellAtRoundEndMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public WFOutpostSellAtRoundEndMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

/// <summary>The owner offers the outpost to a player standing at the console, for a price.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostOfferMessage : BoundUserInterfaceMessage
{
    public readonly NetUserId Buyer;
    public readonly int Price;

    public WFOutpostOfferMessage(NetUserId buyer, int price)
    {
        Buyer = buyer;
        Price = price;
    }
}

/// <summary>The owner takes back their offer.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostWithdrawOfferMessage : BoundUserInterfaceMessage;

/// <summary>The owner looks again at who stands at the console.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostRefreshBuyersMessage : BoundUserInterfaceMessage;

/// <summary>The owner orders one of a catalogue listing, delivered by parachute onto the trade hub.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostOrderMessage : BoundUserInterfaceMessage
{
    public readonly string Product;

    public WFOutpostOrderMessage(string product)
    {
        Product = product;
    }
}

/// <summary>The owner renames the outpost.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostRenameMessage : BoundUserInterfaceMessage
{
    public readonly string Name;

    public WFOutpostRenameMessage(string name)
    {
        Name = name;
    }
}

/// <summary>The owner saves the outpost into a slot, replacing what was there.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostSaveMessage : BoundUserInterfaceMessage
{
    public readonly int Slot;

    public WFOutpostSaveMessage(int slot)
    {
        Slot = slot;
    }
}

/// <summary>The owner empties a save slot.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostDeleteSaveMessage : BoundUserInterfaceMessage
{
    public readonly int Slot;

    public WFOutpostDeleteSaveMessage(int slot)
    {
        Slot = slot;
    }
}

/// <summary>The owner brings a saved outpost down in place of this one.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostLoadMessage : BoundUserInterfaceMessage
{
    public readonly int Slot;

    public WFOutpostLoadMessage(int slot)
    {
        Slot = slot;
    }
}

/// <summary>The owner brings a preset outpost down in place of this one.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostLoadPresetMessage : BoundUserInterfaceMessage
{
    public readonly string Preset;

    public WFOutpostLoadPresetMessage(string preset)
    {
        Preset = preset;
    }
}

/// <summary>The owner calls off a load before it lands, for a refund.</summary>
[Serializable, NetSerializable]
public sealed class WFOutpostCancelLoadMessage : BoundUserInterfaceMessage;

/// <summary>Limits shared by the console and its window.</summary>
public static class WFOutpostLimits
{
    /// <summary>The job an outpost's owner joins the round as, in one of its cryopods.</summary>
    public const string SettlerJob = "WFOutpostSettler";

    /// <summary>Longest outpost name, in characters.</summary>
    public const int NameLength = 30;

    /// <summary>Save slots per account.</summary>
    public const int SaveSlots = 3;

    /// <summary>Trade orders one outpost may have on their way at once.</summary>
    public const int OrdersInFlight = 5;

    /// <summary>The cargo product group the Trade tab lists.</summary>
    public const string TradeGroup = "WFOutpost";

    /// <summary>Highest price an outpost may be offered to a player for.</summary>
    public const int MaxOfferPrice = 10_000_000;
}

/// <summary>Whether the outpost can be raided now, and when that changes; <see cref="Changes"/> is null when it never does.</summary>
[Serializable, NetSerializable]
public sealed record WFOutpostRaidView(bool Active, TimeSpan? Changes);
