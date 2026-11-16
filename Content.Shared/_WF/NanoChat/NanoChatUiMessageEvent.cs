using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.NanoChat;

/// <summary>An action from the NanoChat app.</summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiMessageEvent : CartridgeMessageEvent
{
    public readonly NanoChatUiMessageType Type;

    /// <summary>The other party's number or the group's ID, where the action has one.</summary>
    public readonly uint? RecipientNumber;

    /// <summary>The numbers a group action adds or removes.</summary>
    public readonly List<uint>? Numbers;

    /// <summary>The message text, or the contact or group name for a new or edited chat.</summary>
    public readonly string? Content;

    /// <summary>The contact's job title for a new or edited chat.</summary>
    public readonly string? RecipientJob;

    /// <summary>A message ghosts don't see.</summary>
    public readonly bool Subtle;

    /// <summary>How a sent message is drawn.</summary>
    public readonly NanoChatEffect Effect;

    public NanoChatUiMessageEvent(NanoChatUiMessageType type,
        uint? recipientNumber = null,
        string? content = null,
        string? recipientJob = null,
        bool subtle = false,
        List<uint>? numbers = null,
        NanoChatEffect effect = NanoChatEffect.None)
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
        Subtle = subtle;
        Numbers = numbers;
        Effect = effect;
    }
}

[Serializable, NetSerializable]
public enum NanoChatUiMessageType : byte
{
    NewChat,
    SelectChat,
    EditChat,
    CloseChat,
    SendMessage,
    DeleteChat,
    ToggleMute,
    ToggleMuteChat,
    ToggleListNumber,
    CreateGroup,
    AddMembers,
    KickMember,
    LeaveGroup,
    RenameGroup,
}

/// <summary>How a message is drawn in the bubble.</summary>
[Serializable, NetSerializable]
public enum NanoChatEffect : byte
{
    None,
    Loud,
    Whisper,
    Shake,
    Wave,
    Rainbow,
}

/// <summary>A contact on a card.</summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatRecipient
{
    public uint Number;

    public string Name;

    public string? JobTitle;

    /// <summary>Whether the chat holds messages not yet read.</summary>
    public bool HasUnread;

    public NanoChatRecipient(uint number, string name, string? jobTitle = null, bool hasUnread = false)
    {
        Number = number;
        Name = name;
        JobTitle = jobTitle;
        HasUnread = hasUnread;
    }
}

/// <summary>One message in a chat.</summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatMessage
{
    public const int MaxContentLength = 256;

    public TimeSpan Timestamp;

    public string Content;

    /// <summary>The sender's number.</summary>
    public uint SenderId;

    /// <summary>Whether it reached nobody: no signal at either end, or no PDA holds the number.</summary>
    public bool DeliveryFailed;

    /// <summary>Sent out of ghosts' sight.</summary>
    public bool Subtle;

    /// <summary>The sender's name, shown on group messages.</summary>
    public string? SenderName;

    public NanoChatEffect Effect;

    public NanoChatMessage(TimeSpan timestamp, string content, uint senderId, bool deliveryFailed = false, bool subtle = false)
    {
        Timestamp = timestamp;
        Content = content;
        SenderId = senderId;
        DeliveryFailed = deliveryFailed;
        Subtle = subtle;
    }
}
