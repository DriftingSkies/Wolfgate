using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hideouts;

/// <summary>The outpost console's Hideout tab: the modules' levels, the build under way and the hangar.</summary>
[Serializable, NetSerializable]
public sealed record WFHideoutView(
    Dictionary<string, int> Levels,
    string? Building,
    TimeSpan BuildEnds,
    int Balance,
    bool Company,
    WFHideoutHangarView Hangar,
    Dictionary<string, int> Held,
    string? HomeWorld,
    List<WFHideoutWorldView> Worlds,
    int MoveFee);

/// <summary>A world an outpost may call home.</summary>
[Serializable, NetSerializable]
public sealed record WFHideoutWorldView(string Id, string Name);

/// <summary>The owner moves the outpost's home world, for the fee; it lands there from the next round.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutMoveHomeMessage(string world) : BoundUserInterfaceMessage
{
    public readonly string World = world;
}

/// <summary>The hangar and the owner's ship.</summary>
[Serializable, NetSerializable]
public sealed record WFHideoutHangarView(
    bool HasPad,
    WFHideoutShipStatus Status,
    string? ShipName,
    int Value,
    int InsuredValue,
    int Premium,
    int Deductible,
    string? Unregistered,
    bool Busy);

/// <summary>Where the owner's hangar ship is.</summary>
[Serializable, NetSerializable]
public enum WFHideoutShipStatus : byte
{
    /// <summary>No ship is registered.</summary>
    None,

    /// <summary>The ship stands in the hangar.</summary>
    Parked,

    /// <summary>The ship is out.</summary>
    Away,

    /// <summary>The ship is kept but hasn't been brought out this round.</summary>
    Stored,

    /// <summary>The ship was destroyed; only a claim brings it back.</summary>
    Lost,
}

/// <summary>The owner starts the next level of a module.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutBuildMessage(string module) : BoundUserInterfaceMessage
{
    public readonly string Module = module;
}

/// <summary>The owner registers the hull parked in the hangar as their hangar ship.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRegisterShipMessage : BoundUserInterfaceMessage;

/// <summary>The owner gives up their hangar ship's place; the hull stays as an ordinary ship.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutUnregisterShipMessage : BoundUserInterfaceMessage;

/// <summary>The owner brings a stored ship out onto the hangar pad.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutRecallShipMessage : BoundUserInterfaceMessage;

/// <summary>The owner insures the parked ship as it stands.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutInsureMessage : BoundUserInterfaceMessage;

/// <summary>The owner claims on the policy: the ship comes back as it was insured.</summary>
[Serializable, NetSerializable]
public sealed class WFHideoutClaimMessage : BoundUserInterfaceMessage;
