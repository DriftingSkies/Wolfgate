using System.Numerics;
using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Contracts;

[Serializable, NetSerializable]
public enum WGContractBoardUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WGContractStatus : byte
{
    Offered,
    Active,
    Completed,
    Failed,
    Abandoned,

    /// <summary>Called off through no fault of the holder; the deposit was returned.</summary>
    Void,
}

/// <summary>One contract as the board and PDA show it.</summary>
[Serializable, NetSerializable]
public sealed class WGContractView
{
    public int Id;
    public string Title = string.Empty;
    public string Briefing = string.Empty;
    public string Contractor = string.Empty;
    public Color ContractorColor = Color.White;
    public WGContractCategory Category;
    public int Tier;
    public int Reward;
    public int Deposit;
    public WGContractStatus Status;

    /// <summary>Time allowed once accepted.</summary>
    public TimeSpan Duration;

    /// <summary>When an accepted contract runs out, or when an offer leaves the board.</summary>
    public TimeSpan? Deadline;

    public WGContractSite Site;

    /// <summary>Where to go, in words.</summary>
    public string Location = string.Empty;

    /// <summary>World coordinates of the target, for a contract in open space.</summary>
    public Vector2? Coordinates;

    public List<WGContractObjectiveView> Objectives = new();

    /// <summary>Why it cannot be accepted, if it cannot.</summary>
    public string? Locked;

    /// <summary>Who holds it, for a contract a crewmate took.</summary>
    public string? Holder;

    /// <summary>Whether the viewer holds it and so may abandon it.</summary>
    public bool Mine;

    /// <summary>Standing it costs with other contractors, in words.</summary>
    public List<string> Harms = new();

    /// <summary>Faction credits it pays besides thalers, in words.</summary>
    public string? Credits;

    /// <summary>Crew members in the round needed to take it.</summary>
    public int MinCrew = 1;

    /// <summary>What extra crewmates add to the pay, in words.</summary>
    public string? CrewBonus;

    /// <summary>What the viewer's crew would be paid, and each member's share, in words.</summary>
    public string? Share;

    /// <summary>Who the work is for: the contractor and the faction or company behind it.</summary>
    public string Employer = string.Empty;

    /// <summary>Items the taker must carry, in words.</summary>
    public List<string> Requires = new();
}

[Serializable, NetSerializable]
public sealed record WGContractObjectiveView(string Text, bool Done);

/// <summary>Standing with one contractor.</summary>
[Serializable, NetSerializable]
public sealed record WGContractStandingView(string Contractor, string Description, Color Color, string Rank, int Points,
    int? NextRankAt, bool Present);

/// <summary>Everything the board shows one player.</summary>
[Serializable, NetSerializable]
public sealed class WGContractBoardState
{
    public List<WGContractView> Offers = new();
    public List<WGContractView> Active = new();
    public List<WGContractView> History = new();
    public List<WGContractStandingView> Standing = new();

    /// <summary>Contracts the viewer's crew may hold at once, by its size.</summary>
    public int MaxActive;

    /// <summary>Members of the viewer's crew in the round, 1 outside a crew.</summary>
    public int CrewSize = 1;

    /// <summary>Crew members needed for the next contract slot.</summary>
    public int NextSlotAt;

    /// <summary>Pay owed to the player that could not be paid when it was earned.</summary>
    public int Owed;

    /// <summary>False when the viewer has no character to hold contracts, such as a ghost.</summary>
    public bool CanHold;
}

/// <summary>The board's state for one viewer, sent to them alone since it holds their own contracts.</summary>
[Serializable, NetSerializable]
public sealed class WGContractBoardStateMessage(WGContractBoardState state) : BoundUserInterfaceMessage
{
    public readonly WGContractBoardState State = state;
}

/// <summary>Sent by the window once it is open, so its first state cannot arrive before it does.</summary>
[Serializable, NetSerializable]
public sealed class WGContractRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class WGContractAcceptMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

[Serializable, NetSerializable]
public sealed class WGContractAbandonMessage(int id) : BoundUserInterfaceMessage
{
    public readonly int Id = id;
}

/// <summary>The Contracts PDA app's state: the holder's accepted contracts, history and standing.</summary>
[Serializable, NetSerializable]
public sealed class WGContractCartridgeUiState(WGContractBoardState state) : BoundUserInterfaceState
{
    public readonly WGContractBoardState State = state;
}

[Serializable, NetSerializable]
public sealed class WGContractCartridgeAbandonMessage(int id) : CartridgeMessageEvent
{
    public readonly int Id = id;
}
