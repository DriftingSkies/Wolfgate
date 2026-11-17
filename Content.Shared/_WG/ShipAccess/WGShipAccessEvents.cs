using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipAccess;

/// <summary>The owner selects the ship-wide access mode.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetModeMessage(WGShipAccessMode mode) : BoundUserInterfaceMessage
{
    public WGShipAccessMode Mode = mode;
}

/// <summary>The owner flips the ship lock from the console's access tab.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetLockedMessage : BoundUserInterfaceMessage
{
    public bool Locked;

    public WGShipAccessSetLockedMessage(bool locked)
    {
        Locked = locked;
    }
}

/// <summary>The owner adds the card of a humanoid standing near the console, or a card in their own hands, to the allow list.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessAddPlayerMessage : BoundUserInterfaceMessage
{
    public NetEntity Target;

    public WGShipAccessAddPlayerMessage(NetEntity target)
    {
        Target = target;
    }
}

/// <summary>The owner removes a card from the allow list.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessRemoveMessage : BoundUserInterfaceMessage
{
    public WGShipAccessKey Key;

    public WGShipAccessRemoveMessage(WGShipAccessKey key)
    {
        Key = key;
    }
}

/// <summary>The owner marks or unmarks a listed card as a builder's.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetBuilderMessage : BoundUserInterfaceMessage
{
    public WGShipAccessKey Key;
    public bool Builder;

    public WGShipAccessSetBuilderMessage(WGShipAccessKey key, bool builder)
    {
        Key = key;
        Builder = builder;
    }
}

/// <summary>The owner sets a door's rule from the door diagram.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetDoorRuleMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public WGDoorAccessRule Rule;

    public WGShipAccessSetDoorRuleMessage(NetEntity door, WGDoorAccessRule rule)
    {
        Door = door;
        Rule = rule;
    }
}

/// <summary>The owner sets every door on the ship to one rule.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetAllDoorRulesMessage : BoundUserInterfaceMessage
{
    public WGDoorAccessRule Rule;

    public WGShipAccessSetAllDoorRulesMessage(WGDoorAccessRule rule)
    {
        Rule = rule;
    }
}

/// <summary>The owner ticks or unticks an allow-listed card on a door's own list.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetDoorPlayerMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public WGShipAccessKey Key;
    public bool Listed;

    public WGShipAccessSetDoorPlayerMessage(NetEntity door, WGShipAccessKey key, bool listed)
    {
        Door = door;
        Key = key;
        Listed = listed;
    }
}

/// <summary>The owner asks the console for the ship and door codes; the server answers with <see cref="WGShipAccessCodesEvent"/>.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessRequestCodesMessage : BoundUserInterfaceMessage
{
}

/// <summary>The owner sets (4 digits) or clears (null) the ship code.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetShipCodeMessage : BoundUserInterfaceMessage
{
    public string? Code;

    public WGShipAccessSetShipCodeMessage(string? code)
    {
        Code = code;
    }
}

/// <summary>The owner sets (4 digits) or clears (null) a door's own code.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSetDoorCodeMessage : BoundUserInterfaceMessage
{
    public NetEntity Door;
    public string? Code;

    public WGShipAccessSetDoorCodeMessage(NetEntity door, string? code)
    {
        Door = door;
        Code = code;
    }
}

/// <summary>Sent to the owner's client only: the ship's codes and the keypad alert counts. Never broadcast.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessCodesEvent : EntityEventArgs
{
    public NetEntity Grid;
    public string? ShipCode;
    public Dictionary<NetEntity, string> DoorCodes;
    public int Misses;
    public int LockedOut;

    public WGShipAccessCodesEvent(NetEntity grid, string? shipCode, Dictionary<NetEntity, string> doorCodes, int misses, int lockedOut)
    {
        Grid = grid;
        ShipCode = shipCode;
        DoorCodes = doorCodes;
        Misses = misses;
        LockedOut = lockedOut;
    }
}

/// <summary>Sent to the owner's client after a keypad miss: failed attempts this round and people locked out now.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessCodeAlertEvent : EntityEventArgs
{
    public NetEntity Grid;
    public int Misses;
    public int LockedOut;

    public WGShipAccessCodeAlertEvent(NetEntity grid, int misses, int lockedOut)
    {
        Grid = grid;
        Misses = misses;
        LockedOut = lockedOut;
    }
}

/// <summary>The server tells one client to open the keypad for a door, after its Enter Code verb.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessOpenKeypadEvent : EntityEventArgs
{
    public NetEntity Door;

    public WGShipAccessOpenKeypadEvent(NetEntity door)
    {
        Door = door;
    }
}

/// <summary>A client submits a keypad code for a door.</summary>
[Serializable, NetSerializable]
public sealed class WGShipAccessSubmitCodeMessage : EntityEventArgs
{
    public NetEntity Door;
    public string Code;

    public WGShipAccessSubmitCodeMessage(NetEntity door, string code)
    {
        Door = door;
        Code = code;
    }
}
