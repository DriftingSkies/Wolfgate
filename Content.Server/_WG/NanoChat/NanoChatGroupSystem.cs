using System.Linq;
using Content.Server._WG.Companies;
using Content.Shared._WG.NanoChat;
using Content.Shared.GameTicking;
using Robust.Shared.Timing;

namespace Content.Server._WG.NanoChat;

/// <summary>A group chat: a room a player made, or a company's.</summary>
public sealed class NanoChatGroup
{
    public uint Id;
    public string Name = string.Empty;

    /// <summary>The number that manages a room; null for a company group.</summary>
    public uint? Owner;

    /// <summary>The company whose members are in it, for a company group.</summary>
    public int? CompanyId;

    /// <summary>Member numbers, in the order they joined.</summary>
    public readonly List<uint> Members = new();

    /// <summary>Recent messages, given to a card that joins.</summary>
    public readonly List<NanoChatMessage> History = new();
}

/// <summary>
/// Group chats for the round. A room has an owner who adds contacts and kicks members; anyone in it may leave, and the
/// next member takes it over when the owner does. A company group holds every card whose character is in the company,
/// checked every few seconds. Members are numbers, so whoever holds a member's card reads the group.
/// </summary>
public sealed class NanoChatGroupSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private NanoChatCartridgeSystem _cartridge = default!;
    [Dependency] private NanoChatSystem _nanoChat = default!;
    [Dependency] private WGCompanySystem _companies = default!;

    public const int MaxMembers = 20;
    public const int MaxNameLength = 32;
    private const int HistoryLength = 100;
    private static readonly TimeSpan CompanySyncInterval = TimeSpan.FromSeconds(5);

    private readonly Dictionary<uint, NanoChatGroup> _groups = new();
    private readonly Dictionary<int, uint> _companyGroups = new();

    /// <summary>The character each spawned card number belongs to, for company membership.</summary>
    private readonly Dictionary<uint, int> _profiles = new();

    private uint _nextId = SharedNanoChatSystem.FirstGroupId;
    private TimeSpan _nextCompanySync;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _groups.Clear();
        _companyGroups.Clear();
        _profiles.Clear();
        _nextId = SharedNanoChatSystem.FirstGroupId;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCompanySync)
            return;

        _nextCompanySync = _timing.CurTime + CompanySyncInterval;
        foreach (var (number, profile) in _profiles)
        {
            SetCompany(number, _companies.GetCompanyOf(profile) is { } id && _companies.CompanyIdentity(id) is { } identity
                ? (id, identity.Name)
                : null);
        }
    }

    public NanoChatGroup? Get(uint id) => _groups.GetValueOrDefault(id);

    /// <summary>Notes the character a spawned card's number belongs to, so it joins that character's company group.</summary>
    public void RegisterCharacter(uint number, int profileId)
    {
        _profiles[number] = profileId;
        _nextCompanySync = TimeSpan.Zero;
    }

    /// <summary>Makes a room owned by a number, with it and the given numbers in it.</summary>
    public NanoChatGroup Create(string name, uint owner, IEnumerable<uint> members)
    {
        var group = new NanoChatGroup { Id = _nextId++, Name = name, Owner = owner };
        _groups[group.Id] = group;
        Add(group, owner, false);
        foreach (var member in members)
        {
            if (group.Members.Count >= MaxMembers)
                break;

            Add(group, member, true);
        }

        return group;
    }

    /// <summary>Puts a number in a group and gives the group to the cards holding it. False when it can't join; only rooms have a member limit.</summary>
    public bool Add(NanoChatGroup group, uint number, bool notify)
    {
        if (SharedNanoChatSystem.IsGroup(number)
            || group.Members.Contains(number)
            || group.CompanyId == null && group.Members.Count >= MaxMembers)
            return false;

        group.Members.Add(number);
        foreach (var card in CardsWith(number))
        {
            JoinCard(card, group);
            if (notify)
                _cartridge.NotifyCard(card, group.Name, Loc.GetString("nano-chat-group-added", ("group", group.Name)));

            _cartridge.UpdateUIForCard(card);
        }

        return true;
    }

    /// <summary>Takes a number out of a group and the group off its cards. An emptied room is gone.</summary>
    public bool Remove(NanoChatGroup group, uint number, bool kicked)
    {
        if (!group.Members.Remove(number))
            return false;

        foreach (var card in CardsWith(number))
        {
            _nanoChat.TryDeleteChat(card, group.Id);
            if (kicked)
                _cartridge.NotifyCard(card, group.Name, Loc.GetString("nano-chat-group-kicked", ("group", group.Name)));

            _cartridge.UpdateUIForCard(card);
        }

        if (group.Owner == number)
            group.Owner = group.Members.Count > 0 ? group.Members[0] : null;

        if (group.Members.Count == 0 && group.CompanyId == null)
            _groups.Remove(group.Id);
        else
            RefreshMembers(group);

        return true;
    }

    /// <summary>Renames a room on every member's card.</summary>
    public void Rename(NanoChatGroup group, string name)
    {
        group.Name = name;
        foreach (var number in group.Members)
        {
            foreach (var card in CardsWith(number))
            {
                if (_nanoChat.GetRecipient(card, group.Id) is { } entry)
                    _nanoChat.SetRecipient(card, group.Id, entry with { Name = name });
            }
        }

        RefreshMembers(group);
    }

    /// <summary>Keeps a message for cards that join later.</summary>
    public void AddHistory(NanoChatGroup group, NanoChatMessage message)
    {
        group.History.Add(message);
        if (group.History.Count > HistoryLength)
            group.History.RemoveRange(0, group.History.Count - HistoryLength);
    }

    /// <summary>Gives a card the group's chat and its recent messages, if it lacks the chat. True when it was added.</summary>
    public bool JoinCard(Entity<NanoChatCardComponent> card, NanoChatGroup group)
    {
        if (_nanoChat.GetRecipient(card, group.Id) != null)
            return false;

        var kind = Loc.GetString(group.CompanyId != null ? "nano-chat-company-group" : "nano-chat-group");
        _nanoChat.SetRecipient(card, group.Id, new NanoChatRecipient(group.Id, group.Name, kind));
        _nanoChat.RewriteMessages(card, chats => chats[group.Id] = new List<NanoChatMessage>(group.History));
        return true;
    }

    /// <summary>
    /// Puts a number in its company's group and out of every other company group, or out of all of them for no company.
    /// The number's cards get the group back if they lost it, as a new card does after a respawn.
    /// </summary>
    public void SetCompany(uint number, (int Id, string Name)? company)
    {
        foreach (var (companyId, groupId) in _companyGroups)
        {
            if (companyId != company?.Id && _groups.TryGetValue(groupId, out var other))
                Remove(other, number, false);
        }

        if (company is not { } found)
            return;

        var (id, name) = found;

        if (!_companyGroups.TryGetValue(id, out var existing) || !_groups.TryGetValue(existing, out var group))
        {
            group = new NanoChatGroup { Id = _nextId++, Name = name, CompanyId = id };
            _groups[group.Id] = group;
            _companyGroups[id] = group.Id;
        }

        if (group.Name != name)
            Rename(group, name);

        if (Add(group, number, false))
            return;

        foreach (var card in CardsWith(number))
        {
            if (JoinCard(card, group))
                _cartridge.UpdateUIForCard(card);
        }
    }

    /// <summary>Every card holding a number.</summary>
    public List<Entity<NanoChatCardComponent>> CardsWith(uint number)
    {
        var cards = new List<Entity<NanoChatCardComponent>>();
        var query = EntityQueryEnumerator<NanoChatCardComponent>();
        while (query.MoveNext(out var uid, out var card))
        {
            if (card.Number == number)
                cards.Add((uid, card));
        }

        return cards;
    }

    /// <summary>Refreshes members looking at the group, so its member list stays current.</summary>
    private void RefreshMembers(NanoChatGroup group)
    {
        foreach (var number in group.Members.ToList())
        {
            foreach (var card in CardsWith(number))
            {
                if (card.Comp.CurrentChat == group.Id)
                    _cartridge.UpdateUIForCard(card);
            }
        }
    }
}
