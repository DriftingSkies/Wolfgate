using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.JoinProgress;

/// <summary>Tells a joining client how much uploaded content it is about to download, and how.</summary>
public sealed class MsgJoinDownloadInfo : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.String;

    /// <summary>Size of the transfer stream, in bytes.</summary>
    public long TotalBytes;

    public int FileCount;

    /// <summary>False when the files use the WebSocket channel, which the client's net statistics don't count.</summary>
    public bool OverGameConnection;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        TotalBytes = buffer.ReadInt64();
        FileCount = buffer.ReadVariableInt32();
        OverGameConnection = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(TotalBytes);
        buffer.WriteVariableInt32(FileCount);
        buffer.Write(OverGameConnection);
    }
}
