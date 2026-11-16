using Robust.Shared.Serialization;

namespace Content.Shared._WF.NanoChat;

/// <summary>What the NanoChat app shows. Only the open chat's messages are sent.</summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiState : BoundUserInterfaceState
{
    public readonly Dictionary<uint, NanoChatRecipient> Recipients;
    public readonly Dictionary<uint, List<NanoChatMessage>> Messages;
    public readonly HashSet<uint> MutedChats;

    /// <summary>The directory of listed numbers, or null when there is no signal.</summary>
    public readonly List<NanoChatRecipient>? Contacts;

    public readonly uint? CurrentChat;
    public readonly uint OwnNumber;
    public readonly int MaxRecipients;
    public readonly bool NotificationsMuted;
    public readonly bool ListNumber;

    /// <summary>The open chat's group, when it is one.</summary>
    public readonly NanoChatGroupInfo? Group;

    public NanoChatUiState(
        Dictionary<uint, NanoChatRecipient> recipients,
        Dictionary<uint, List<NanoChatMessage>> messages,
        HashSet<uint> mutedChats,
        List<NanoChatRecipient>? contacts,
        uint? currentChat,
        uint ownNumber,
        int maxRecipients,
        bool notificationsMuted,
        bool listNumber,
        NanoChatGroupInfo? group = null)
    {
        Recipients = recipients;
        Messages = messages;
        MutedChats = mutedChats;
        Contacts = contacts;
        CurrentChat = currentChat;
        OwnNumber = ownNumber;
        MaxRecipients = maxRecipients;
        NotificationsMuted = notificationsMuted;
        ListNumber = listNumber;
        Group = group;
    }
}

/// <summary>A group chat's name and members, for the open chat.</summary>
[Serializable, NetSerializable]
public sealed class NanoChatGroupInfo
{
    public readonly uint Id;
    public readonly string Name;

    /// <summary>Whether it is a company's group, whose members the company decides.</summary>
    public readonly bool IsCompany;

    /// <summary>The number that manages a room; null for a company group.</summary>
    public readonly uint? Owner;

    /// <summary>Members by name, in the order they joined.</summary>
    public readonly List<NanoChatRecipient> Members;

    public NanoChatGroupInfo(uint id, string name, bool isCompany, uint? owner, List<NanoChatRecipient> members)
    {
        Id = id;
        Name = name;
        IsCompany = isCompany;
        Owner = owner;
        Members = members;
    }
}
