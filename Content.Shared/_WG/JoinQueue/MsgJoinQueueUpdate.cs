using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.JoinQueue;

/// <summary>Tells a queued client where it stands. The first one puts the client on the queue screen.</summary>
public sealed class MsgJoinQueueUpdate : NetMessage
{
    /// <summary>Sent as <see cref="EstimateSeconds"/> when too few slots have opened to guess a wait.</summary>
    public const int NoEstimate = -1;

    public override MsgGroups MsgGroup => MsgGroups.Command;

    /// <summary>Place in the queue, 1 being next.</summary>
    public int Position;

    /// <summary>Players in the queue.</summary>
    public int Total;

    /// <summary>Players in the game.</summary>
    public int Players;

    /// <summary>The player cap.</summary>
    public int Cap;

    /// <summary>Seconds this player has waited, counting time before a reconnect.</summary>
    public int WaitedSeconds;

    /// <summary>Rough seconds until this player's turn, or <see cref="NoEstimate"/>.</summary>
    public int EstimateSeconds;

    /// <summary>Seconds a place is held after a disconnect.</summary>
    public int HoldSeconds;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Position = buffer.ReadVariableInt32();
        Total = buffer.ReadVariableInt32();
        Players = buffer.ReadVariableInt32();
        Cap = buffer.ReadVariableInt32();
        WaitedSeconds = buffer.ReadVariableInt32();
        EstimateSeconds = buffer.ReadVariableInt32();
        HoldSeconds = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Position);
        buffer.WriteVariableInt32(Total);
        buffer.WriteVariableInt32(Players);
        buffer.WriteVariableInt32(Cap);
        buffer.WriteVariableInt32(WaitedSeconds);
        buffer.WriteVariableInt32(EstimateSeconds);
        buffer.WriteVariableInt32(HoldSeconds);
    }
}
