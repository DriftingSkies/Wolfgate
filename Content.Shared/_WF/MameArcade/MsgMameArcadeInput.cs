using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.MameArcade;

/// <summary>
/// One player's controls for consecutive frames of a 2-player game, client to server and on to the partner. A raw
/// net message rather than a UI message: those wait for the game tick they were stamped with on both ends, which
/// adds several ticks of lag that lockstep feels on every frame.
/// </summary>
public sealed class MsgMameArcadeInput : NetMessage
{
    // Its own channel, so a large reliable message elsewhere can't hold inputs back.
    private const int Channel = 13;

    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    public override int SequenceChannel => Channel;

    public NetEntity Cabinet;
    public int FirstFrame;
    public ushort[] Inputs = Array.Empty<ushort>();

    /// <summary>The frame <see cref="Checksum"/> was taken after, or <see cref="MameArcadeProtocol.NoChecksum"/>.</summary>
    public int ChecksumFrame = MameArcadeProtocol.NoChecksum;

    public uint Checksum;

    /// <summary>How many frames early the sender gets the receiver's inputs; the receiver tunes its delay from it.</summary>
    public sbyte Margin = sbyte.MinValue;

    /// <summary>The sender's input delay in frames; player 2 adopts player 1's.</summary>
    public byte Delay;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Cabinet = new NetEntity(buffer.ReadInt32());
        FirstFrame = buffer.ReadInt32();

        // A longer message is malformed; the server drops one with no inputs.
        var count = (int) buffer.ReadByte();
        if (count > MameArcadeProtocol.MaxInputsPerMessage)
        {
            Inputs = Array.Empty<ushort>();
            return;
        }

        Inputs = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            Inputs[i] = buffer.ReadUInt16();
        }

        ChecksumFrame = buffer.ReadInt32();
        Checksum = buffer.ReadUInt32();
        Margin = buffer.ReadSByte();
        Delay = buffer.ReadByte();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Cabinet.Id);
        buffer.Write(FirstFrame);
        buffer.Write((byte) Inputs.Length);
        foreach (var input in Inputs)
        {
            buffer.Write(input);
        }

        buffer.Write(ChecksumFrame);
        buffer.Write(Checksum);
        buffer.Write(Margin);
        buffer.Write(Delay);
    }

    public override int EstimateBufferSize() => 19 + Inputs.Length * 2;
}
