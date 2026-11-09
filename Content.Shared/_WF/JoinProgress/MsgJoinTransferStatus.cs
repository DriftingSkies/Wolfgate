using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.JoinProgress;

/// <summary>Tells a joining client which channel its file transfers ended up on, so the join console can show it.</summary>
public sealed class MsgJoinTransferStatus : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.String;

    public JoinTransferMode Mode;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        // A server newer than this client may send a mode it doesn't know; treat it as no fast channel.
        var raw = buffer.ReadByte();
        Mode = raw <= (byte) JoinTransferMode.WebSocketFailed ? (JoinTransferMode) raw : JoinTransferMode.GameConnection;
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Mode);
    }
}

/// <summary>How a client's file transfers travel.</summary>
public enum JoinTransferMode : byte
{
    /// <summary>Over the game connection, because the server has the fast channel off.</summary>
    GameConnection,

    /// <summary>Over the fast websocket channel.</summary>
    WebSocket,

    /// <summary>Over the game connection, because the fast channel failed for this client.</summary>
    WebSocketFailed,
}
