using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MameArcade;

public enum MameArcadeStreamKind : byte
{
    /// <summary>The game's sound, for players near the cabinet.</summary>
    Audio,

    /// <summary>The game's screen, for players watching it.</summary>
    Video,
}

/// <summary>
/// A packet of the cabinet's sound or picture, from the player whose game it is, through the server to the people
/// near it or watching. The server only checks the sender and the rate; the packets are opaque to it.
/// </summary>
public sealed class MsgMameArcadeStream : NetMessage
{
    // Their own channels, so a large picture can't hold up the sound or a 2-player game's inputs.
    private const int AudioChannel = 14;
    private const int VideoChannel = 15;

    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    public override int SequenceChannel => Kind == MameArcadeStreamKind.Video ? VideoChannel : AudioChannel;

    public NetEntity Cabinet;
    public MameArcadeStreamKind Kind;
    public byte[] Data = Array.Empty<byte>();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Cabinet = new NetEntity(buffer.ReadInt32());
        Kind = (MameArcadeStreamKind) buffer.ReadByte();
        var length = buffer.ReadInt32();
        if (length is < 0 or > MameArcadeProtocol.MaxStreamPacket)
        {
            Data = Array.Empty<byte>();
            return;
        }

        Data = buffer.ReadBytes(length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Cabinet.Id);
        buffer.Write((byte) Kind);
        buffer.Write(Data.Length);
        buffer.Write(Data);
    }

    public override int EstimateBufferSize() => 9 + Data.Length;
}
