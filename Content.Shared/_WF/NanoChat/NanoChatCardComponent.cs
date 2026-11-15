using Robust.Shared.GameStates;

namespace Content.Shared._WF.NanoChat;

/// <summary>An ID card's NanoChat number, contacts and message history.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedNanoChatSystem), Other = AccessPermissions.ReadExecute)]
[AutoGenerateComponentState]
public sealed partial class NanoChatCardComponent : Component
{
    /// <summary>The card's number. A character's own card takes the number saved for that character.</summary>
    [DataField, AutoNetworkedField]
    public uint? Number;

    /// <summary>Contacts, keyed by number.</summary>
    [DataField]
    public Dictionary<uint, NanoChatRecipient> Recipients = new();

    /// <summary>Messages, keyed by the other party's number.</summary>
    [DataField]
    public Dictionary<uint, List<NanoChatMessage>> Messages = new();

    /// <summary>Chats that never notify.</summary>
    [DataField]
    public HashSet<uint> MutedChats = new();

    /// <summary>The open chat.</summary>
    [DataField]
    public uint? CurrentChat;

    /// <summary>How many contacts the card holds.</summary>
    [DataField]
    public int MaxRecipients = 50;

    /// <summary>How many messages a chat keeps; older ones drop off.</summary>
    [DataField]
    public int MaxMessagesPerChat = 100;

    /// <summary>Whether no message notifies.</summary>
    [DataField]
    public bool NotificationsMuted;

    /// <summary>Whether the number shows in the directory.</summary>
    [DataField]
    public bool ListNumber = true;

    /// <summary>The PDA the card sits in.</summary>
    [DataField]
    public EntityUid? PdaUid;
}
