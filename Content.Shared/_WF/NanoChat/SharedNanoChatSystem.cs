using Content.Shared.Examine;

namespace Content.Shared._WF.NanoChat;

/// <summary>Reads and changes NanoChat cards; every change to a card goes through here.</summary>
public abstract class SharedNanoChatSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NanoChatCardComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<NanoChatCardComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(ent.Comp.Number is { } number
            ? Loc.GetString("nanochat-card-examine-number", ("number", FormatNumber(number)))
            : Loc.GetString("nanochat-card-examine-no-number"));
    }

    /// <summary>The highest card number; chat IDs above it are groups.</summary>
    public const uint MaxNumber = 9999;

    /// <summary>The first group chat ID.</summary>
    public const uint FirstGroupId = 10000;

    /// <summary>Whether a chat ID is a group rather than a card's number.</summary>
    public static bool IsGroup(uint chat) => chat >= FirstGroupId;

    /// <summary>A number as players see it, four digits.</summary>
    public static string FormatNumber(uint number) => $"{number:D4}";

    /// <summary>Cuts text to a length, ending it with the overflow text.</summary>
    public static string Truncate(string text, int maxLength, string overflowText = "...")
    {
        return text.Length > maxLength
            ? text[..(maxLength - overflowText.Length)] + overflowText
            : text;
    }

    public void SetNumber(Entity<NanoChatCardComponent> card, uint number)
    {
        if (card.Comp.Number == number)
            return;

        card.Comp.Number = number;
        Dirty(card);
    }

    public void SetPda(Entity<NanoChatCardComponent> card, EntityUid? pda)
    {
        card.Comp.PdaUid = pda;
    }

    public NanoChatRecipient? GetRecipient(Entity<NanoChatCardComponent> card, uint number)
    {
        return card.Comp.Recipients.TryGetValue(number, out var recipient) ? recipient : null;
    }

    public void SetRecipient(Entity<NanoChatCardComponent> card, uint number, NanoChatRecipient recipient)
    {
        card.Comp.Recipients[number] = recipient;
    }

    /// <summary>Adds a message to a chat, dropping the oldest past the card's limit.</summary>
    public void AddMessage(Entity<NanoChatCardComponent> card, uint number, NanoChatMessage message)
    {
        if (!card.Comp.Messages.TryGetValue(number, out var messages))
        {
            messages = new List<NanoChatMessage>();
            card.Comp.Messages[number] = messages;
        }

        messages.Add(message);
        var excess = messages.Count - card.Comp.MaxMessagesPerChat;
        if (excess > 0)
            messages.RemoveRange(0, excess);
    }

    public void SetCurrentChat(Entity<NanoChatCardComponent> card, uint? number)
    {
        card.Comp.CurrentChat = number;
    }

    public void SetNotificationsMuted(Entity<NanoChatCardComponent> card, bool muted)
    {
        card.Comp.NotificationsMuted = muted;
    }

    public void ToggleChatMuted(Entity<NanoChatCardComponent> card, uint number)
    {
        if (!card.Comp.MutedChats.Remove(number))
            card.Comp.MutedChats.Add(number);
    }

    public void SetListNumber(Entity<NanoChatCardComponent> card, bool listed)
    {
        card.Comp.ListNumber = listed;
    }

    /// <summary>Removes a contact, and its messages unless kept. False when there was no such contact.</summary>
    public bool TryDeleteChat(Entity<NanoChatCardComponent> card, uint number, bool keepMessages = false)
    {
        var removed = card.Comp.Recipients.Remove(number);
        if (!keepMessages)
            card.Comp.Messages.Remove(number);

        if (card.Comp.CurrentChat == number)
            card.Comp.CurrentChat = null;

        return removed;
    }

    /// <summary>Makes sure a contact and its chat exist, adding the contact from the info given. False when it can't.</summary>
    public bool EnsureRecipientExists(Entity<NanoChatCardComponent> card, uint number, NanoChatRecipient? info = null)
    {
        if (!card.Comp.Recipients.ContainsKey(number))
        {
            if (info == null)
                return false;

            card.Comp.Recipients[number] = info.Value;
        }

        if (!card.Comp.Messages.ContainsKey(number))
            card.Comp.Messages[number] = new List<NanoChatMessage>();

        return true;
    }

    /// <summary>Gives every chat's messages to a function that may rewrite or move them.</summary>
    public void RewriteMessages(Entity<NanoChatCardComponent> card, Action<Dictionary<uint, List<NanoChatMessage>>> rewrite)
    {
        rewrite(card.Comp.Messages);
    }
}
