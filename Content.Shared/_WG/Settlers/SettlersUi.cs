using Robust.Shared.Serialization;

namespace Content.Shared._WG.Settlers;

[Serializable, NetSerializable]
public enum WGSettlersUiKey : byte
{
    Key,
}

public enum SettlersActionKind : byte
{
    Roll,
    BuildLane,
    BuildOutpost,
    BuildColony,
    BuyCard,

    /// <summary>Play the contract <see cref="SettlersAction.Target"/>; a windfall takes First and Second, an embargo First.</summary>
    PlayCard,
    EndSurvey,
    MovePirates,
    Steal,

    /// <summary>Discard the cards in <see cref="SettlersAction.Give"/>.</summary>
    Discard,

    /// <summary>Give the bank the resource First at the player's ratio for one Second.</summary>
    BankTrade,

    /// <summary>Put Give for Want to the table, or withdraw the player's offer when both are empty.</summary>
    Offer,

    /// <summary>Take the offer of the seat in Target.</summary>
    Accept,
    EndTurn,
}

/// <summary>A move in the game. Target is the corner, side, tile, seat or card it acts on.</summary>
[Serializable, NetSerializable]
public sealed class SettlersAction
{
    public SettlersActionKind Kind;
    public int Target;
    public int First;
    public int Second;
    public int[] Give = Array.Empty<int>();
    public int[] Want = Array.Empty<int>();

    public SettlersAction(SettlersActionKind kind, int target = 0, int first = 0, int second = 0)
    {
        Kind = kind;
        Target = target;
        First = first;
        Second = second;
    }
}

[Serializable, NetSerializable]
public sealed class SettlersActionMessage : BoundUserInterfaceMessage
{
    public SettlersAction Action;

    public SettlersActionMessage(SettlersAction action)
    {
        Action = action;
    }
}

/// <summary>Take a seat, or leave it with -1.</summary>
[Serializable, NetSerializable]
public sealed class SettlersSeatMessage : BoundUserInterfaceMessage
{
    public int Seat;

    public SettlersSeatMessage(int seat)
    {
        Seat = seat;
    }
}

/// <summary>Start the game with the players seated.</summary>
[Serializable, NetSerializable]
public sealed class SettlersStartMessage : BoundUserInterfaceMessage;

/// <summary>Clear the board for a new game.</summary>
[Serializable, NetSerializable]
public sealed class SettlersNewGameMessage : BoundUserInterfaceMessage;

/// <summary>Sent by a window as it opens, for the server to answer with the board.</summary>
[Serializable, NetSerializable]
public sealed class SettlersRequestStateMessage : BoundUserInterfaceMessage;

/// <summary>The board as one viewer may see it, and who sits where.</summary>
[Serializable, NetSerializable]
public sealed class SettlersStateMessage : BoundUserInterfaceMessage
{
    public SettlersState State;
    public NetEntity?[] Seats;
    public string?[] SeatNames;

    public SettlersStateMessage(SettlersState state, NetEntity?[] seats, string?[] seatNames)
    {
        State = state;
        Seats = seats;
        SeatNames = seatNames;
    }
}
