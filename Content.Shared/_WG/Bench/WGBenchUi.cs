using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Bench;

[Serializable, NetSerializable]
public enum WGBenchTerminalUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WGBenchTicketStatus : byte
{
    Paid,
    Owing,

    /// <summary>Still owing past its deadline or over the debt cap; the offender is Wanted.</summary>
    Overdue,

    /// <summary>An Outlawry ticket; the offender is Wanted with a bounty.</summary>
    Outlawed,
    Pardoned,
}

/// <summary>Someone close enough to the judge to be ticketed.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchPersonView
{
    public NetEntity Entity;
    public string Name = string.Empty;
}

/// <summary>An ordinance the Bench posted this round.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchOrdinanceView
{
    public int Id;
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Fine;
    public WGPenaltyLevel Level;
    public string PostedBy = string.Empty;
}

[Serializable, NetSerializable]
public sealed class WGBenchChargeView
{
    public string Title = string.Empty;
    public int Fine;
    public WGPenaltyLevel Level;
}

[Serializable, NetSerializable]
public sealed class WGBenchTicketView
{
    public int Number;
    public string Offender = string.Empty;
    public string Judge = string.Empty;
    public string Ship = string.Empty;
    public string Notes = string.Empty;
    public List<WGBenchChargeView> Charges = new();
    public int Total;
    public int Paid;
    public int Owed;
    public WGPenaltyLevel Level;
    public WGBenchTicketStatus Status;
    public TimeSpan Issued;
    public TimeSpan Deadline;
}

/// <summary>Everything the Bench app and terminal show.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchUiState : BoundUserInterfaceState
{
    /// <summary>Whether the viewer may issue, pardon and post. Anyone else only reads the laws.</summary>
    public bool Judge;
    public List<WGBenchPersonView> Nearby = new();
    public List<WGBenchOrdinanceView> Ordinances = new();

    /// <summary>This round's tickets, newest first. Empty for a viewer who is not a judge.</summary>
    public List<WGBenchTicketView> Tickets = new();
}

/// <summary>One charge on a ticket being written: a law, a posted ordinance or a custom charge.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchChargeRequest
{
    /// <summary>The law, or null for an ordinance or custom charge.</summary>
    public string? Law;

    /// <summary>The posted ordinance, or null.</summary>
    public int? Ordinance;

    /// <summary>A custom charge's title; ignored for laws and ordinances.</summary>
    public string Title = string.Empty;
    public int Fine;

    /// <summary>A custom charge's level; laws and ordinances keep their own.</summary>
    public WGPenaltyLevel Level;
}

[Serializable, NetSerializable]
public abstract class WGBenchAction;

[Serializable, NetSerializable]
public sealed class WGBenchRefreshAction : WGBenchAction;

[Serializable, NetSerializable]
public sealed class WGBenchIssueAction : WGBenchAction
{
    public NetEntity Offender;
    public string Ship = string.Empty;
    public string Notes = string.Empty;
    public List<WGBenchChargeRequest> Charges = new();
}

[Serializable, NetSerializable]
public sealed class WGBenchPardonAction(int number) : WGBenchAction
{
    public int Number = number;
}

[Serializable, NetSerializable]
public sealed class WGBenchPostOrdinanceAction : WGBenchAction
{
    public string Title = string.Empty;
    public string Text = string.Empty;
    public int Fine;
    public WGPenaltyLevel Level;
}

[Serializable, NetSerializable]
public sealed class WGBenchRemoveOrdinanceAction(int id) : WGBenchAction
{
    public int Id = id;
}

/// <summary>A Bench app button press.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchCartridgeMessage(WGBenchAction action) : CartridgeMessageEvent
{
    public WGBenchAction Action = action;
}

/// <summary>A Bench terminal button press.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchTerminalMessage(WGBenchAction action) : BoundUserInterfaceMessage
{
    public WGBenchAction Action = action;
}

/// <summary>The terminal's state, sent to each viewer alone since judges see more than others.</summary>
[Serializable, NetSerializable]
public sealed class WGBenchTerminalStateMessage(WGBenchUiState state) : BoundUserInterfaceMessage
{
    public WGBenchUiState State = state;
}
