using System;
using Lidgren.Network;

namespace Content.Shared._WF.NetGuard;

/// <summary>
/// Length checks for client-sent net messages, so a forged length can't make the receiver allocate memory the packet
/// doesn't hold.
/// </summary>
public static class NetGuardExt
{
    /// <summary>Reads a variable-length byte count and throws if it is negative or runs past the end of the message.</summary>
    public static int ReadGuardedLength(this NetIncomingMessage buffer)
    {
        var length = buffer.ReadVariableInt32();
        if (!IsLengthValid(length, buffer.LengthBytes - buffer.PositionInBytes))
            throw new InvalidOperationException($"Length {length} exceeds the {buffer.LengthBytes - buffer.PositionInBytes} bytes left in the message.");

        return length;
    }

    /// <summary>Whether a byte count fits in the bytes left in a message.</summary>
    public static bool IsLengthValid(int length, int remainingBytes)
    {
        return length >= 0 && length <= remainingBytes;
    }
}
