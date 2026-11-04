// WOLFGATE(Genitals): consent system, ported from HardLight
// SPDX-FileCopyrightText: Copyright (c) 2024-2025 Space Wizards Federation
// SPDX-License-Identifier: MIT

using Content.Shared._WF.NetGuard; // WOLFGATE(NetGuard)
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using System.IO;

namespace Content.Shared._Common.Consent;

/// <summary>
/// Sent from client to server to update the player's consent settings, or from server to client after it's been
/// updated, or after connecting.
/// </summary>
public sealed class MsgUpdateConsent : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public PlayerConsentSettings Consent = default!;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        var length = buffer.ReadGuardedLength(); // WOLFGATE(NetGuard): reject lengths past the packet end
        using var stream = new MemoryStream(length);
        buffer.ReadAlignedMemory(stream, length);
        serializer.DeserializeDirect(stream, out Consent);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Consent);
        buffer.WriteVariableInt32((int) stream.Length);
        buffer.Write(stream.AsSpan());
    }
}
