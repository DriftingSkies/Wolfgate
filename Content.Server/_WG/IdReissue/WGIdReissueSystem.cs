using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.NanoChat;
using Content.Server._WG.ShipAccess;
using Content.Server._WG.Traders;
using Content.Server.Administration.Logs;
using Content.Server.GameTicking;
using Content.Server.PDA;
using Content.Server.Popups;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.StationRecords.Systems;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.NanoChat;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.Traders;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.PDA;
using Content.Shared.Roles;
using Content.Shared.StationRecords;
using Content.Shared.StatusIcon;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.IdReissue;

/// <summary>
/// Reprints a player's lost ID: the lost cards are voided, the new card carries the same name, job, access and crew
/// record, and the deeds of their ships near the issuer move to it. Offered by a trader (<see cref="WGTraderIdReissueComponent"/>).
/// </summary>
public sealed partial class WGIdReissueSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private IdCardOwnerSystem _cardOwner = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private NanoChatSystem _nanoChat = default!;
    [Dependency] private PdaSystem _pda = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedIdCardSystem _idCard = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private StationRecordKeyStorageSystem _keys = default!;
    [Dependency] private TraderSystem _trader = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;

    /// <summary>The inert card left where a voided one was.</summary>
    private static readonly EntProtoId VoidCard = "WGVoidIdCard";

    /// <summary>Each character's ID as last seen, so a card that was destroyed outright can still be reprinted.</summary>
    private readonly Dictionary<(NetUserId User, string Name), WGIdTemplate> _templates = new();

    /// <summary>Ships whose deed went with a voided card while they were too far off, until a reissue near them fetches it.</summary>
    private readonly Dictionary<(NetUserId User, string Name), HashSet<EntityUid>> _strandedDeeds = new();

    /// <summary>When each character last had an ID reissued.</summary>
    private readonly Dictionary<(NetUserId User, string Name), TimeSpan> _lastReissue = new();

    public override void Initialize()
    {
        base.Initialize();

        // The crew record key is written onto the card by the station records.
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete, after: [typeof(StationRecordsSystem)]);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<IdCardOwnerComponent, EntityTerminatingEvent>(OnCardTerminating);
        InitializeTrader();
        InitializeDoorHint();
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        // The ColoTalk number is left out: the card holds a placeholder until NanoChat loads the character's own.
        if (_idCard.TryFindIdCard(ev.Mob, out var card) && Read(card, null) is { } template)
            _templates[(ev.Player.UserId, ev.Profile.Name)] = template with { ChatNumber = null };
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _templates.Clear();
        _strandedDeeds.Clear();
        _lastReissue.Clear();
        _hinted.Clear();
    }

    /// <summary>A card being destroyed mid-round is remembered as it was last, ColoTalk number and job changes included.</summary>
    private void OnCardTerminating(Entity<IdCardOwnerComponent> ent, ref EntityTerminatingEvent args)
    {
        if (_gameTicker.RunLevel == GameRunLevel.InRound)
            Remember(ent.Comp.UserId, ent.Comp.CharacterName, ent);
    }

    /// <summary>Records what a character's card looks like now, to reprint it from if it is destroyed.</summary>
    public void Remember(NetUserId user, string name, EntityUid card)
    {
        if (Read(card, _templates.GetValueOrDefault((user, name))) is { } template)
            _templates[(user, name)] = template;
    }

    /// <summary>
    /// Works out what a reissue for the user would do, without doing it. Refused while the user carries a valid card
    /// of their own, so a card is never doubled, within <paramref name="cooldown"/> of their last reissue, and when
    /// nothing is known of their ID.
    /// </summary>
    /// <param name="issuer">Ships docked to this entity's grid or within <paramref name="range"/> of it have their deeds moved.</param>
    /// <param name="error">The refusal, as the issuer says it.</param>
    public bool TryQuote(EntityUid issuer, EntityUid user, float range, TimeSpan cooldown, [NotNullWhen(true)] out ReissueQuote? quote, [NotNullWhen(false)] out string? error)
    {
        quote = null;
        if (!TryComp<ActorComponent>(user, out var actor))
        {
            error = Loc.GetString("wg-id-reissue-no-record");
            return false;
        }

        var userId = actor.PlayerSession.UserId;
        var name = Name(user);
        var owned = CardsOf(userId, name);
        if (CarriesOwnCard(user, owned))
        {
            error = Loc.GetString("wg-id-reissue-carrying");
            return false;
        }

        if (_lastReissue.TryGetValue((userId, name), out var last) && _timing.CurTime < last + cooldown)
        {
            var minutes = (int) Math.Ceiling((last + cooldown - _timing.CurTime).TotalMinutes);
            error = Loc.GetString("wg-id-reissue-cooldown", ("minutes", minutes));
            return false;
        }

        var template = _templates.GetValueOrDefault((userId, name));
        EntityUid? source = null;
        if (owned.Count > 0)
        {
            // The real ID carries the character's ColoTalk number; an extra deed card from a past reissue shares the crew
            // record but not the number. Then the card with the crew record, before one without, such as an agent ID.
            source = owned
                .OrderByDescending(card => template?.ChatNumber is { } chat && CompOrNull<NanoChatCardComponent>(card)?.Number == chat)
                .ThenByDescending(card => template?.Key is { } known && CompOrNull<StationRecordKeyStorageComponent>(card)?.Key is { } held && held.Equals(known))
                .ThenByDescending(card => CompOrNull<StationRecordKeyStorageComponent>(card)?.Key != null)
                .First();
            template = Read(source.Value, template);
        }

        if (template == null)
        {
            error = Loc.GetString("wg-id-reissue-no-record");
            return false;
        }

        var near = new List<EntityUid>();
        var distant = 0;
        foreach (var ship in OwnedShips(userId, name, owned))
        {
            if (_shipyard.DeedHolders(ship).Any(holder => HasComp<IdCardComponent>(holder) && Carries(user, holder)))
                continue;

            if (Near(issuer, ship, range))
                near.Add(ship);
            else
                distant++;
        }

        quote = new ReissueQuote(userId, name, owned, source, template, near, distant);
        error = null;
        return true;
    }

    /// <summary>
    /// Carries out a quote: prints the new card into the user's hands, moves ColoTalk and the near deeds to it, and
    /// replaces every lost card with an inert voided one.
    /// </summary>
    public ReissueResult Reissue(EntityUid user, ReissueQuote quote)
    {
        var printed = Print(quote.Template, user, quote.Name);
        if (TryComp<NanoChatCardComponent>(printed, out var chat))
        {
            if (quote.Source is { } source && TryComp<NanoChatCardComponent>(source, out var oldChat))
                _nanoChat.TransferCard((source, oldChat), (printed, chat));
            else if (quote.Template.ChatNumber is { } number)
                _nanoChat.AssignNumber((printed, chat), number);
        }

        var moved = new List<EntityUid>();
        for (var i = 0; i < quote.Near.Count; i++)
        {
            // A card holds one deed, so every ship past the first comes on a card of its own.
            var target = i == 0 ? printed : PrintCard(quote.Template, user);
            if (MoveDeed(quote.Near[i], target, quote.Name))
                moved.Add(quote.Near[i]);
        }

        if (!_strandedDeeds.TryGetValue((quote.User, quote.Name), out var stranded))
        {
            stranded = new HashSet<EntityUid>();
            _strandedDeeds[(quote.User, quote.Name)] = stranded;
        }

        stranded.ExceptWith(moved);
        var voided = new List<EntityUid>();
        foreach (var card in quote.Lost)
        {
            // The deed of a ship too far off goes with the card; this system keeps it until the ship is brought in.
            if (TryComp<ShuttleDeedComponent>(card, out var deed) && deed.ShuttleUid is { } ship)
                stranded.Add(ship);

            voided.Add(Void(card, quote.Name));
        }

        Remember(quote.User, quote.Name, printed);
        _lastReissue[(quote.User, quote.Name)] = _timing.CurTime;
        _adminLog.Add(LogType.Identity, LogImpact.Medium,
            $"{ToPrettyString(user):player} reissued their ID: voided {quote.Lost.Count} card(s), moved {moved.Count} deed(s), left {quote.Distant} far away");

        return new ReissueResult(printed, voided, moved, quote.Distant);
    }

    /// <summary>Every ID card issued to this character.</summary>
    private List<EntityUid> CardsOf(NetUserId user, string name)
    {
        var cards = new List<EntityUid>();
        var query = EntityQueryEnumerator<IdCardOwnerComponent, IdCardComponent>();
        while (query.MoveNext(out var uid, out var owner, out _))
        {
            if (owner.UserId == user && owner.CharacterName == name && !TerminatingOrDeleted(uid))
                cards.Add(uid);
        }

        return cards;
    }

    /// <summary>Whether the user carries one of their own cards.</summary>
    private bool CarriesOwnCard(EntityUid user, List<EntityUid> owned)
    {
        return owned.Any(card => Carries(user, card));
    }

    /// <summary>Whether the item sits anywhere on the user: hands, pockets, a worn PDA or a bag.</summary>
    private bool Carries(EntityUid user, EntityUid item)
    {
        var parent = Transform(item).ParentUid;
        while (parent.IsValid())
        {
            if (parent == user)
                return true;

            parent = Transform(parent).ParentUid;
        }

        return false;
    }

    /// <summary>
    /// The character's ships: those whose deed one of their cards holds or went with a card they voided, and those
    /// registered to their account under this character's name, which covers a card that was destroyed.
    /// </summary>
    private HashSet<EntityUid> OwnedShips(NetUserId userId, string name, List<EntityUid> owned)
    {
        var ships = new HashSet<EntityUid>(_strandedDeeds.GetValueOrDefault((userId, name)) ?? new HashSet<EntityUid>());
        foreach (var card in owned)
        {
            if (TryComp<ShuttleDeedComponent>(card, out var deed) && deed.ShuttleUid is { } ship)
                ships.Add(ship);
        }

        var registered = EntityQueryEnumerator<ShipOwnershipComponent, ShuttleDeedComponent>();
        while (registered.MoveNext(out var uid, out var ownership, out var deed))
        {
            if (ownership.OwnerUserId == userId && deed.ShuttleOwner?.Trim() == name.Trim())
                ships.Add(uid);
        }

        ships.RemoveWhere(ship => TerminatingOrDeleted(ship) || !HasComp<ShuttleComponent>(ship));
        return ships;
    }

    /// <summary>Whether the ship is docked to the issuer's grid or lies within range of it.</summary>
    private bool Near(EntityUid issuer, EntityUid ship, float range)
    {
        var issuerXform = Transform(issuer);
        if (issuerXform.GridUid is not { } station || station == ship)
            return false;

        foreach (var dock in _docking.GetDocks(ship))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid == station)
                return true;
        }

        if (Transform(ship).MapID != issuerXform.MapID
            || !TryComp<MapGridComponent>(station, out var stationGrid)
            || !TryComp<MapGridComponent>(ship, out var shipGrid))
        {
            return false;
        }

        var a = _transform.GetWorldMatrix(station).TransformBox(stationGrid.LocalAABB);
        var b = _transform.GetWorldMatrix(ship).TransformBox(shipGrid.LocalAABB);
        var dx = MathF.Max(0f, MathF.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = MathF.Max(0f, MathF.Max(a.Bottom - b.Top, b.Bottom - a.Top));
        return dx * dx + dy * dy <= range * range;
    }

    /// <summary>Takes the ship's deed off every ID card holding it, copies included, and puts it on the new card.</summary>
    private bool MoveDeed(EntityUid ship, EntityUid card, string name)
    {
        foreach (var holder in _shipyard.DeedHolders(ship))
        {
            if (HasComp<IdCardComponent>(holder))
                _shipyard.RevokeDeed(holder, ship);
        }

        var owner = CompOrNull<ShuttleDeedComponent>(ship)?.ShuttleOwner ?? name;
        if (!_shipyard.GrantDeed(ship, card, owner))
            return false;

        if (TryComp<WGShipAccessComponent>(ship, out var access))
            _shipAccess.OwnersChanged((ship, access));

        return true;
    }

    /// <summary>
    /// Replaces a lost card, wherever it is, with an inert voided card that is no ID at all, so no shipyard, trader,
    /// door or console can take it. Its deed, access and crew record go with it.
    /// </summary>
    private EntityUid Void(EntityUid card, string name)
    {
        var husk = SpawnNextToOrDrop(VoidCard, card);
        _metaData.SetEntityName(husk, Loc.GetString("wg-id-reissue-void-name", ("name", name)));
        Del(card);
        return husk;
    }

    /// <summary>Prints the new card, in a PDA like the one the lost card sat in, and puts it in the user's hands.</summary>
    private EntityUid Print(WGIdTemplate template, EntityUid user, string name)
    {
        if (template.Pda is not { } pdaProto || !_proto.HasIndex(pdaProto))
            return PrintCard(template, user);

        var pda = Spawn(pdaProto, Transform(user).Coordinates);
        var pdaComp = Comp<PdaComponent>(pda);
        if (pdaComp.IdSlot.Item is not { } card || !HasComp<IdCardComponent>(card))
        {
            if (pdaComp.IdSlot.Item is { } other)
                Del(other);

            card = Spawn(template.Card, Transform(user).Coordinates);
            _itemSlots.TryInsert(pda, pdaComp.IdSlot, card, null);
        }

        Apply(card, template, user);
        _pda.SetOwner(pda, pdaComp, user, name);
        _hands.PickupOrDrop(user, pda);
        return card;
    }

    /// <summary>Prints a bare card into the user's hands.</summary>
    private EntityUid PrintCard(WGIdTemplate template, EntityUid user)
    {
        var card = Spawn(template.Card, Transform(user).Coordinates);
        Apply(card, template, user);
        _hands.PickupOrDrop(user, card);
        return card;
    }

    /// <summary>Writes the template's name, job, faction, access and crew record onto a card, issued to the user.</summary>
    private void Apply(EntityUid card, WGIdTemplate template, EntityUid user)
    {
        var id = Comp<IdCardComponent>(card);
        _idCard.TryChangeFullName(card, template.FullName, id);
        _idCard.TryChangeJobTitle(card, template.JobTitle, id);
        if (_proto.TryIndex(template.JobIcon, out var icon))
            _idCard.TryChangeJobIcon(card, icon, id);

        id.JobDepartments = new List<ProtoId<DepartmentPrototype>>(template.Departments);
        Dirty(card, id);
        _idCard.TryChangeCompanyName(card, template.Company.Id, id);

        EnsureComp<AccessComponent>(card);
        _access.TrySetTags(card, template.Access);
        if (template.Key is { } key)
        {
            EnsureComp<StationRecordKeyStorageComponent>(card);
            _keys.AssignKey(card, key);
        }

        _cardOwner.Stamp(card, user, overwrite: true);
    }

    /// <summary>A card's current identity, falling back to an older template for what the card no longer shows.</summary>
    private WGIdTemplate? Read(EntityUid card, WGIdTemplate? fallback)
    {
        if (!TryComp<IdCardComponent>(card, out var id))
            return fallback;

        var proto = MetaData(card).EntityPrototype?.ID;
        EntProtoId? cardProto = proto != null ? new EntProtoId(proto) : fallback?.Card;
        if (cardProto is not { } cardId)
            return fallback;

        var pda = fallback?.Pda;
        if (_container.TryGetContainingContainer(card, out var container)
            && HasComp<PdaComponent>(container.Owner)
            && MetaData(container.Owner).EntityPrototype is { } pdaProto)
        {
            pda = pdaProto.ID;
        }

        return new WGIdTemplate(
            cardId,
            pda,
            id.FullName,
            id.LocalizedJobTitle,
            id.JobIcon,
            new List<ProtoId<DepartmentPrototype>>(id.JobDepartments),
            id.CompanyName,
            _access.TryGetTags(card)?.ToList() ?? new List<ProtoId<AccessLevelPrototype>>(),
            CompOrNull<StationRecordKeyStorageComponent>(card)?.Key ?? fallback?.Key,
            // A character's number never changes, so the first one seen stands: an extra deed card has its own.
            fallback?.ChatNumber ?? CompOrNull<NanoChatCardComponent>(card)?.Number);
    }
}

/// <summary>What a reissued card is printed from.</summary>
public sealed record WGIdTemplate(
    EntProtoId Card,
    EntProtoId? Pda,
    string? FullName,
    string? JobTitle,
    ProtoId<JobIconPrototype> JobIcon,
    List<ProtoId<DepartmentPrototype>> Departments,
    ProtoId<CompanyPrototype> Company,
    List<ProtoId<AccessLevelPrototype>> Access,
    StationRecordKey? Key,
    uint? ChatNumber);

/// <summary>
/// What a reissue would do: the lost cards to void, the one whose ColoTalk history moves over, the identity to print,
/// the ships whose deeds move and how many are too far.
/// </summary>
public sealed record ReissueQuote(NetUserId User, string Name, List<EntityUid> Lost, EntityUid? Source, WGIdTemplate Template, List<EntityUid> Near, int Distant);

/// <summary>The outcome of a reissue: the new card, the inert cards left in place of the lost ones, the ships whose deeds moved and how many ships were too far.</summary>
public sealed record ReissueResult(EntityUid Card, List<EntityUid> Voided, List<EntityUid> Moved, int Distant);
