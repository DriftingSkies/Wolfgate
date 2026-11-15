using System.Linq;
using Content.Server._WF.PlanetComms;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.CartridgeLoader;
using Content.Server.Chat.Managers;
using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Shared._WF.NanoChat;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.PDA;
using Content.Shared.Popups;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._WF.NanoChat;

/// <summary>
/// The NanoChat app: its window, and sending messages. A message rides the cartridge's radio channel, so it needs
/// a telecom server (or a planet's mass transceiver) where it is sent from, and the receiving PDA must be able to
/// hear that channel, as headsets do. Ghosts read delivered messages in chat unless they are sent subtle; admin
/// ghosts read those too. Group chats (<see cref="NanoChatGroupSystem"/>) reach every member the same way.
/// </summary>
public sealed class NanoChatCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _loader = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private NanoChatGroupSystem _groups = default!;
    [Dependency] private NanoChatTypingSystem _typing = default!;
    [Dependency] private NanoChatSystem _nanoChat = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;
    [Dependency] private WFPlanetCommsSystem _planetComms = default!;

    private static readonly EntProtoId Program = "NanoChatCartridge";

    /// <summary>The shortest gap between two messages from one sender.</summary>
    private static readonly TimeSpan SendCooldown = TimeSpan.FromSeconds(1);

    private const int NotificationMaxLength = 64;
    private const int NotificationTitleMaxLength = 32;

    private readonly Dictionary<EntityUid, TimeSpan> _lastSent = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<NanoChatCartridgeComponent, CartridgeMessageEvent>(OnMessage);
        SubscribeLocalEvent<WFPdaProgramsEvent>(OnPdaPrograms);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _lastSent.Clear());
    }

    private void OnPdaPrograms(ref WFPdaProgramsEvent ev)
    {
        ev.Programs.Add(Program);
    }

    private void OnUiReady(Entity<NanoChatCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        UpdateUI(ent, args.Loader);
    }

    private void OnMessage(Entity<NanoChatCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not NanoChatUiMessageEvent msg)
            return;

        var loader = GetEntity(args.LoaderUid);
        if (!_nanoChat.TryGetCard(loader, out var card))
            return;

        switch (msg.Type)
        {
            case NanoChatUiMessageType.NewChat:
                HandleNewChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.SelectChat:
                HandleSelectChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.EditChat:
                HandleEditChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.CloseChat:
                _nanoChat.SetCurrentChat(card.Value, null);
                break;
            case NanoChatUiMessageType.ToggleMute:
                _nanoChat.SetNotificationsMuted(card.Value, !card.Value.Comp.NotificationsMuted);
                break;
            case NanoChatUiMessageType.ToggleMuteChat:
                if (msg.RecipientNumber is { } chat)
                    _nanoChat.ToggleChatMuted(card.Value, chat);
                break;
            case NanoChatUiMessageType.DeleteChat:
                HandleDeleteChat(card.Value, msg);
                break;
            case NanoChatUiMessageType.SendMessage:
                HandleSendMessage(ent, card.Value, msg);
                break;
            case NanoChatUiMessageType.ToggleListNumber:
                _nanoChat.SetListNumber(card.Value, !card.Value.Comp.ListNumber);
                UpdateOpenDirectories();
                break;
            case NanoChatUiMessageType.CreateGroup:
                HandleCreateGroup(card.Value, msg);
                break;
            case NanoChatUiMessageType.AddMembers:
                HandleAddMembers(card.Value, msg);
                break;
            case NanoChatUiMessageType.KickMember:
                HandleKickMember(card.Value, msg);
                break;
            case NanoChatUiMessageType.LeaveGroup:
                if (msg.RecipientNumber is { } left)
                    LeaveGroup(card.Value, left, msg.Actor);
                break;
            case NanoChatUiMessageType.RenameGroup:
                HandleRenameGroup(card.Value, msg);
                break;
        }

        UpdateUI(ent, loader);
    }

    private static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    private void HandleNewChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || number == card.Comp.Number
            || number == 0
            || SharedNanoChatSystem.IsGroup(number)
            || Clean(msg.Content, IdCardConsoleComponent.MaxFullNameLength) is not { } name
            || card.Comp.Recipients.Count >= card.Comp.MaxRecipients && !card.Comp.Recipients.ContainsKey(number))
            return;

        var job = Clean(msg.RecipientJob, IdCardConsoleComponent.MaxJobTitleLength);
        _nanoChat.SetRecipient(card, number, new NanoChatRecipient(number, name, job));
        _nanoChat.EnsureRecipientExists(card, number);

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} created a NanoChat chat with #{SharedNanoChatSystem.FormatNumber(number)} ({name})");
    }

    private void HandleSelectChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number)
            return;

        _nanoChat.SetCurrentChat(card, number);
        if (_nanoChat.GetRecipient(card, number) is { } recipient)
            _nanoChat.SetRecipient(card, number, recipient with { HasUnread = false });
    }

    private void HandleEditChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || SharedNanoChatSystem.IsGroup(number)
            || _nanoChat.GetRecipient(card, number) is not { } recipient
            || Clean(msg.Content, IdCardConsoleComponent.MaxFullNameLength) is not { } name)
            return;

        recipient.Name = name;
        recipient.JobTitle = Clean(msg.RecipientJob, IdCardConsoleComponent.MaxJobTitleLength);
        _nanoChat.SetRecipient(card, number, recipient);
    }

    private void HandleDeleteChat(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is { } group && SharedNanoChatSystem.IsGroup(group))
        {
            LeaveGroup(card, group, msg.Actor);
            return;
        }

        if (msg.RecipientNumber is not { } number || !_nanoChat.TryDeleteChat(card, number, true))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} deleted the NanoChat chat with #{SharedNanoChatSystem.FormatNumber(number)}");
    }

    private void HandleSendMessage(Entity<NanoChatCartridgeComponent> cartridge,
        Entity<NanoChatCardComponent> card,
        NanoChatUiMessageEvent msg)
    {
        if (msg.RecipientNumber is not { } number
            || card.Comp.Number is not { } ownNumber
            || number == ownNumber
            || Clean(msg.Content, NanoChatMessage.MaxContentLength) is not { } content)
            return;

        var now = _timing.CurTime;
        if (_lastSent.TryGetValue(msg.Actor, out var last) && now - last < SendCooldown)
        {
            _popup.PopupEntity(Loc.GetString("nano-chat-too-fast"), msg.Actor, msg.Actor, PopupType.SmallCaution);
            return;
        }

        content = Clean(_typing.Impair(msg.Actor, content), NanoChatMessage.MaxContentLength) ?? content;

        if (SharedNanoChatSystem.IsGroup(number))
        {
            SendToGroup(cartridge, card, msg, number, ownNumber, content);
            return;
        }

        if (!_nanoChat.EnsureRecipientExists(card, number, GetCardInfo(number)))
            return;

        _lastSent[msg.Actor] = now;
        var recipients = FindReachable(cartridge, number);
        var message = new NanoChatMessage(now, content, ownNumber, recipients.Count == 0, msg.Subtle)
        {
            Effect = EffectOf(msg),
        };
        _nanoChat.AddMessage(card, number, message);

        var names = recipients.Count > 0
            ? string.Join(", ", recipients.Select(r => ToPrettyString(r.Owner)))
            : $"#{SharedNanoChatSystem.FormatNumber(number)}";
        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} sent NanoChat message to {names}: {content}{(recipients.Count == 0 ? " [DELIVERY FAILED]" : "")}{(msg.Subtle ? " [SUBTLE]" : "")}");

        foreach (var recipient in recipients)
        {
            Deliver(ownNumber, recipient, message);
        }

        if (recipients.Count > 0)
            ShowGhosts(msg.Actor, NameOf(ownNumber), NameOf(number), content, msg.Subtle);
    }

    private static NanoChatEffect EffectOf(NanoChatUiMessageEvent msg)
    {
        return Enum.IsDefined(msg.Effect) ? msg.Effect : NanoChatEffect.None;
    }

    private string NameOf(uint number) => GetCardInfo(number)?.Name ?? $"#{SharedNanoChatSystem.FormatNumber(number)}";

    /// <summary>Sends to every member of a group the sender is in. With no signal it reaches no one.</summary>
    private void SendToGroup(Entity<NanoChatCartridgeComponent> cartridge,
        Entity<NanoChatCardComponent> card,
        NanoChatUiMessageEvent msg,
        uint groupId,
        uint ownNumber,
        string content)
    {
        if (_groups.Get(groupId) is not { } group || !group.Members.Contains(ownNumber))
            return;

        _lastSent[msg.Actor] = _timing.CurTime;
        var channel = _prototype.Index(cartridge.Comp.RadioChannel);
        var canSend = CanSend(cartridge, channel);
        var recipients = new List<Entity<NanoChatCardComponent>>();
        if (canSend)
        {
            foreach (var member in group.Members)
            {
                if (member != ownNumber)
                    recipients.AddRange(Reachable(cartridge, channel, member));
            }
        }

        var message = new NanoChatMessage(_timing.CurTime, content, ownNumber, !canSend, msg.Subtle)
        {
            SenderName = InfoOf(card, ownNumber).Name,
            Effect = EffectOf(msg),
        };
        _groups.JoinCard(card, group);
        _nanoChat.AddMessage(card, group.Id, message);

        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} sent NanoChat message to group {group.Name} ({recipients.Count} reached): {content}{(canSend ? "" : " [DELIVERY FAILED]")}{(msg.Subtle ? " [SUBTLE]" : "")}");

        if (!canSend)
            return;

        _groups.AddHistory(group, message);
        foreach (var recipient in recipients)
        {
            DeliverToGroup(group, recipient, message);
        }

        ShowGhosts(msg.Actor, message.SenderName, group.Name, content, msg.Subtle);
    }

    private void DeliverToGroup(NanoChatGroup group, Entity<NanoChatCardComponent> card, NanoChatMessage message)
    {
        // A card that just joined got the message with the group's history.
        if (!_groups.JoinCard(card, group))
            _nanoChat.AddMessage(card, group.Id, message);

        if (_nanoChat.GetRecipient(card, group.Id) is not { } entry)
            return;

        var title = SharedNanoChatSystem.Truncate(
            Loc.GetString("nano-chat-group-message-title", ("sender", message.SenderName ?? string.Empty), ("group", group.Name)),
            NotificationTitleMaxLength,
            " \\[...\\]");
        Arrived(card, group.Id, entry, message, title);
    }

    private void HandleCreateGroup(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (card.Comp.Number is not { } ownNumber
            || Clean(msg.Content, NanoChatGroupSystem.MaxNameLength) is not { } name)
            return;

        // Only the creator's own contacts can be put in.
        var members = (msg.Numbers ?? new List<uint>())
            .Where(n => n != ownNumber && !SharedNanoChatSystem.IsGroup(n) && card.Comp.Recipients.ContainsKey(n))
            .Distinct()
            .Take(NanoChatGroupSystem.MaxMembers - 1)
            .ToList();

        var group = _groups.Create(name, ownNumber, members);
        _nanoChat.SetCurrentChat(card, group.Id);
        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} created NanoChat group {name} with {string.Join(", ", members.Select(n => SharedNanoChatSystem.FormatNumber(n)))}");
    }

    /// <summary>The room the card's number owns, or null.</summary>
    private NanoChatGroup? OwnedRoom(Entity<NanoChatCardComponent> card, uint? groupId)
    {
        return groupId is { } id && _groups.Get(id) is { CompanyId: null } group && group.Owner == card.Comp.Number
            ? group
            : null;
    }

    private void HandleAddMembers(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (OwnedRoom(card, msg.RecipientNumber) is not { } group || msg.Numbers == null)
            return;

        foreach (var number in msg.Numbers.Distinct())
        {
            if (!card.Comp.Recipients.ContainsKey(number) || !_groups.Add(group, number, true))
                continue;

            _adminLogger.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(msg.Actor):user} added #{SharedNanoChatSystem.FormatNumber(number)} to NanoChat group {group.Name}");
        }
    }

    private void HandleKickMember(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (OwnedRoom(card, msg.RecipientNumber) is not { } group
            || msg.Numbers is not { Count: > 0 } numbers
            || numbers[0] == group.Owner
            || !_groups.Remove(group, numbers[0], true))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} kicked #{SharedNanoChatSystem.FormatNumber(numbers[0])} from NanoChat group {group.Name}");
    }

    private void HandleRenameGroup(Entity<NanoChatCardComponent> card, NanoChatUiMessageEvent msg)
    {
        if (OwnedRoom(card, msg.RecipientNumber) is not { } group
            || Clean(msg.Content, NanoChatGroupSystem.MaxNameLength) is not { } name)
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(msg.Actor):user} renamed NanoChat group {group.Name} to {name}");
        _groups.Rename(group, name);
    }

    /// <summary>Leaves a room. A company group can't be left; the company decides who is in it.</summary>
    private void LeaveGroup(Entity<NanoChatCardComponent> card, uint groupId, EntityUid actor)
    {
        if (card.Comp.Number is not { } ownNumber)
            return;

        if (_groups.Get(groupId) is not { } group)
        {
            // A group that ended this round, or last round, still on the card.
            _nanoChat.TryDeleteChat(card, groupId);
            return;
        }

        if (group.CompanyId != null || !_groups.Remove(group, ownNumber, false))
            return;

        _adminLogger.Add(LogType.Action, LogImpact.Low, $"{ToPrettyString(actor):user} left NanoChat group {group.Name}");
    }

    /// <summary>Puts a delivered message in ghosts' chat, or only admin ghosts' if it was sent subtle.</summary>
    private void ShowGhosts(EntityUid sender, string? senderName, string recipientName, string content, bool subtle)
    {
        var filter = Filter.Empty().AddWhereAttachedEntity(uid => HasComp<GhostComponent>(uid));
        if (subtle)
            filter.RemoveWhere(session => !_admin.IsAdmin(session));

        var wrapped = Loc.GetString(subtle ? "nano-chat-ghost-message-subtle" : "nano-chat-ghost-message",
            ("sender", FormattedMessage.EscapeText(senderName ?? string.Empty)),
            ("recipient", FormattedMessage.EscapeText(recipientName)),
            ("message", FormattedMessage.EscapeText(content)));

        _chat.ChatMessageToManyFiltered(filter, ChatChannel.Radio, content, wrapped, sender, false, !subtle, null);
    }

    /// <summary>The cards holding a number whose PDA this cartridge's message reaches.</summary>
    private List<Entity<NanoChatCardComponent>> FindReachable(Entity<NanoChatCartridgeComponent> sender, uint number)
    {
        var channel = _prototype.Index(sender.Comp.RadioChannel);
        return CanSend(sender, channel) ? Reachable(sender, channel, number) : new List<Entity<NanoChatCardComponent>>();
    }

    /// <summary>The cards holding a number whose PDA hears the channel, for a sender that can send.</summary>
    private List<Entity<NanoChatCardComponent>> Reachable(EntityUid sender, RadioChannelPrototype channel, uint number)
    {
        var found = new List<Entity<NanoChatCardComponent>>();
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number != number
                || _nanoChat.GetPda((uid, card)) is not { } pda
                || !_loader.TryGetProgram<NanoChatCartridgeComponent>(pda, out var receiver, out _))
                continue;

            var ev = new RadioReceiveAttemptEvent(channel, sender, receiver.Value);
            RaiseLocalEvent(ref ev);
            RaiseLocalEvent(receiver.Value, ref ev);
            if (!ev.Cancelled)
                found.Add((uid, card));
        }

        return found;
    }

    /// <summary>Whether a message can go out from here: nothing blocks the channel and there is a server to carry it.</summary>
    private bool CanSend(EntityUid sender, RadioChannelPrototype channel)
    {
        var ev = new RadioSendAttemptEvent(channel, sender, channel.Frequency);
        RaiseLocalEvent(ref ev);
        RaiseLocalEvent(sender, ref ev);
        return !ev.Cancelled && HasSignal(sender, channel.ID);
    }

    /// <summary>Whether a telecom server for the channel is up on this map, or a planet's mass transceiver relays.</summary>
    private bool HasSignal(EntityUid uid, string channel)
    {
        if (_planetComms.RelaysTelecom(uid))
            return true;

        var map = Transform(uid).MapID;
        if (map == MapId.Nullspace)
            return false;

        var servers = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (servers.MoveNext(out _, out var keys, out var power, out var xform))
        {
            if (xform.MapID == map && power.Powered && keys.Channels.Contains(channel))
                return true;
        }

        return false;
    }

    private void Deliver(uint senderNumber, Entity<NanoChatCardComponent> recipient, NanoChatMessage message)
    {
        if (!_nanoChat.EnsureRecipientExists(recipient, senderNumber, GetCardInfo(senderNumber)))
            return;

        _nanoChat.AddMessage(recipient, senderNumber, message with { DeliveryFailed = false });
        if (_nanoChat.GetRecipient(recipient, senderNumber) is not { } sender)
            return;

        var from = string.IsNullOrEmpty(sender.JobTitle)
            ? sender.Name
            : SharedNanoChatSystem.Truncate(
                Loc.GetString("nano-chat-new-message-title-recipient", ("sender", sender.Name), ("jobTitle", sender.JobTitle)),
                NotificationTitleMaxLength,
                " \\[...\\]");
        Arrived(recipient, senderNumber, sender, message, Loc.GetString("nano-chat-new-message-title", ("sender", from)));
    }

    /// <summary>Marks a chat unread and notifies the card's PDA unless it is looking at the chat or muted.</summary>
    private void Arrived(Entity<NanoChatCardComponent> card,
        uint chat,
        NanoChatRecipient entry,
        NanoChatMessage message,
        string title)
    {
        if (card.Comp.CurrentChat != chat)
            _nanoChat.SetRecipient(card, chat, entry with { HasUnread = true });

        if (_nanoChat.GetPda(card) is not { } pda || !TryComp<CartridgeLoaderComponent>(pda, out var loader))
            return;

        var viewing = card.Comp.CurrentChat == chat
            && _ui.IsUiOpen(pda, PdaUiKey.Key)
            && HasComp<NanoChatCartridgeComponent>(loader.ActiveProgram);

        if (!viewing && !card.Comp.NotificationsMuted && !card.Comp.MutedChats.Contains(chat))
        {
            _loader.SendNotification(pda,
                title,
                Loc.GetString("nano-chat-new-message-body",
                    ("message", SharedNanoChatSystem.Truncate(message.Content, NotificationMaxLength, " [...]"))),
                loader);
        }

        UpdateUIForPda(pda);
    }

    /// <summary>Sends a notice to the PDA reading a card, unless its notifications are muted.</summary>
    public void NotifyCard(Entity<NanoChatCardComponent> card, string title, string body)
    {
        if (card.Comp.NotificationsMuted
            || _nanoChat.GetPda(card) is not { } pda
            || !TryComp<CartridgeLoaderComponent>(pda, out var loader))
            return;

        _loader.SendNotification(pda, SharedNanoChatSystem.Truncate(title, NotificationTitleMaxLength), body, loader);
    }

    /// <summary>The name and job on the ID card holding a number.</summary>
    private NanoChatRecipient? GetCardInfo(uint number)
    {
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number == number)
                return InfoOf(uid, number);
        }

        return null;
    }

    /// <summary>The name and job on a card.</summary>
    private NanoChatRecipient InfoOf(EntityUid card, uint number)
    {
        if (!TryComp<IdCardComponent>(card, out var id))
            return new NanoChatRecipient(number, MetaData(card).EntityName);

        var name = string.IsNullOrWhiteSpace(id.FullName) ? Loc.GetString("nano-chat-unknown-name") : id.FullName;
        return new NanoChatRecipient(number, name, id.LocalizedJobTitle);
    }

    /// <summary>Refreshes the app on the PDA a card sits in.</summary>
    public void UpdateUIForCard(Entity<NanoChatCardComponent> card)
    {
        if (_nanoChat.GetPda(card) is { } pda)
            UpdateUIForPda(pda);
    }

    /// <summary>Refreshes a PDA's NanoChat app if someone is looking at it.</summary>
    public void UpdateUIForPda(EntityUid pda)
    {
        if (_loader.TryGetProgram<NanoChatCartridgeComponent>(pda, out var program, out var comp))
            UpdateUI((program.Value, comp), pda);
    }

    private void UpdateUI(Entity<NanoChatCartridgeComponent> ent, EntityUid loader)
    {
        if (!TryComp<CartridgeLoaderComponent>(loader, out var loaderComp)
            || loaderComp.ActiveProgram != ent.Owner
            || !_ui.IsUiOpen(loader, loaderComp.UiKey))
            return;

        var contacts = HasSignal(ent, ent.Comp.RadioChannel.Id) ? Directory() : null;
        NanoChatUiState state;
        if (_nanoChat.TryGetCard(loader, out var card))
        {
            var comp = card.Value.Comp;
            var messages = new Dictionary<uint, List<NanoChatMessage>>();
            if (comp.CurrentChat is { } current && comp.Messages.TryGetValue(current, out var chat))
                messages[current] = new List<NanoChatMessage>(chat);

            state = new NanoChatUiState(new Dictionary<uint, NanoChatRecipient>(comp.Recipients),
                messages,
                new HashSet<uint>(comp.MutedChats),
                contacts,
                comp.CurrentChat,
                comp.Number ?? 0,
                comp.MaxRecipients,
                comp.NotificationsMuted,
                comp.ListNumber,
                comp.CurrentChat is { } open ? GroupInfo(open) : null);
        }
        else
        {
            state = new NanoChatUiState(new Dictionary<uint, NanoChatRecipient>(),
                new Dictionary<uint, List<NanoChatMessage>>(),
                new HashSet<uint>(),
                contacts,
                null,
                0,
                0,
                false,
                false);
        }

        _loader.UpdateCartridgeUiState(loader, state, loader: loaderComp);
    }

    private NanoChatGroupInfo? GroupInfo(uint chat)
    {
        if (!SharedNanoChatSystem.IsGroup(chat) || _groups.Get(chat) is not { } group)
            return null;

        var members = group.Members.Select(n => GetCardInfo(n) ?? new NanoChatRecipient(n, $"#{SharedNanoChatSystem.FormatNumber(n)}")).ToList();
        return new NanoChatGroupInfo(group.Id, group.Name, group.CompanyId != null, group.Owner, members);
    }

    /// <summary>Refreshes every open NanoChat app, so a number listed or unlisted shows in directories at once.</summary>
    private void UpdateOpenDirectories()
    {
        var query = EntityQueryEnumerator<NanoChatCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out var app, out var cartridge))
        {
            if (cartridge.LoaderUid is { } loader)
                UpdateUI((uid, app), loader);
        }
    }

    /// <summary>Every listed number on a named ID card that a PDA reads, by name.</summary>
    private List<NanoChatRecipient> Directory()
    {
        var contacts = new List<NanoChatRecipient>();
        var seen = new HashSet<uint>();
        var query = EntityQueryEnumerator<NanoChatCardComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var card, out var id))
        {
            if (card.ListNumber
                && card.Number is { } number
                && !string.IsNullOrWhiteSpace(id.FullName)
                && _nanoChat.GetPda((uid, card)) != null
                && seen.Add(number))
                contacts.Add(new NanoChatRecipient(number, id.FullName, id.LocalizedJobTitle));
        }

        contacts.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return contacts;
    }
}
