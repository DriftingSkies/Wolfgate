using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Roles;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's command console (<see cref="WGShipConsoleComponent"/>): the bridge front end for everything the
/// <c>wg_ship*</c> commands do. Zones and the crew's answers, the command lists and the admins' powers, price rules,
/// the roster, the crew's standing orders, player jobs, the viewer's own presets, the recovery pads' access, and the service log. Officers see it
/// and change nothing; whoever configures the ship (<see cref="WGShipCommandSystem.CanConfigure"/>) changes it; the
/// log is for the owner and admins. Every change goes through the same system call the commands use, so it is logged
/// the same way.
/// </summary>
public sealed class WGShipConsoleSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private WGRecoveryPadSystem _pads = default!;
    [Dependency] private WGShipAccessSystem _access = default!;
    [Dependency] private WGShipBehaviourSystem _behaviour = default!;
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipJobsSystem _jobs = default!;
    [Dependency] private WGShipPresetSystem _presets = default!;
    [Dependency] private WGShipRosterSystem _roster = default!;
    [Dependency] private WGShipServiceSystem _service = default!;

    /// <summary>The most service log entries the console shows, newest first.</summary>
    public const int LogLines = 200;

    /// <summary>The most people aboard the people tab lists.</summary>
    public const int AboardLimit = 60;

    /// <summary>The most open slots the console sets for one job.</summary>
    public const int MaxJobSlots = 10;

    /// <summary>The longest automatic restaffing delay, in seconds.</summary>
    public const int MaxRestaffDelay = 3600;

    /// <summary>The highest hourly cap on automatic restaffs.</summary>
    public const int MaxRestaffsPerHour = 60;

    /// <summary>The longest price rule name, and company id.</summary>
    public const int MaxRuleName = 32;

    /// <summary>The most price rules one ship keeps.</summary>
    public const int MaxRules = 64;

    /// <summary>The most presets one player keeps for one vessel.</summary>
    public const int MaxPresets = 24;

    private static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RefreshMessageGap = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PresetReadGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PresetActionGap = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ChangedGap = TimeSpan.FromSeconds(0.25);
    private TimeSpan _nextRefresh;
    private TimeSpan _nextChangedSend;

    /// <summary>Consoles whose viewers are owed a new state, sent together so a stream of messages can't force one each.</summary>
    private readonly HashSet<EntityUid> _changed = new();

    /// <summary>When each viewer's refresh button may next rebuild their state.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _refreshAt = new();

    /// <summary>When each player may next save, load or delete a preset.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _presetAction = new();

    /// <summary>When each viewer's refresh may next read their presets.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _presetRead = new();

    /// <summary>Each player's presets for each vessel, as last read from the database, kept while they have a console open.</summary>
    private readonly Dictionary<(NetUserId, string), List<WGShipConsolePreset>> _presetLists = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipConsoleComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleRefreshMessage>(OnRefresh);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleZoneMessage>(OnZone);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleResponseMessage>(OnResponse);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleSwitchMessage>(OnSwitch);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleAddPersonMessage>(OnAddPerson);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleRemovePersonMessage>(OnRemovePerson);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleSetRuleMessage>(OnSetRule);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleToggleRuleMessage>(OnToggleRule);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleRemoveRuleMessage>(OnRemoveRule);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleStaffMessage>(OnStaff);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleStandDownMessage>(OnStandDown);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleRestaffMessage>(OnRestaff);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleOrdersMessage>(OnOrders);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleJobSlotsMessage>(OnJobSlots);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsoleReleaseJobMessage>(OnReleaseJob);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsolePresetMessage>(OnPreset);
        SubscribeLocalEvent<WGShipConsoleComponent, WGShipConsolePadAccessMessage>(OnPadAccess);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (_changed.Count > 0 && now >= _nextChangedSend)
        {
            _nextChangedSend = now + ChangedGap;
            foreach (var console in _changed)
            {
                if (!TerminatingOrDeleted(console))
                    SendAll(console);
            }

            _changed.Clear();
        }

        if (now < _nextRefresh)
            return;

        _nextRefresh = now + RefreshGap;
        var viewers = new HashSet<NetUserId>();
        var consoles = EntityQueryEnumerator<WGShipConsoleComponent>();
        while (consoles.MoveNext(out var uid, out _))
        {
            SendAll(uid);
            foreach (var actor in _ui.GetActors(uid, WGShipConsoleUiKey.Key))
            {
                if (UserOf(actor) is { } user)
                    viewers.Add(user);
            }
        }

        // Presets are read again when a window opens, so a player who closed theirs or left needn't keep them.
        foreach (var key in _presetLists.Keys.Where(k => !viewers.Contains(k.Item1)).ToList())
            _presetLists.Remove(key);
    }

    #region State

    /// <summary>The commanded ship the console stands on.</summary>
    public bool TryGetShip(EntityUid console, out Entity<WGShipCommandComponent> ship)
    {
        ship = default;
        if (Transform(console).GridUid is not { } grid || !TryComp<WGShipCommandComponent>(grid, out var command))
            return false;

        ship = (grid, command);
        return true;
    }

    /// <summary>The console's state as one viewer sees it.</summary>
    public WGShipConsoleState State(EntityUid console, EntityUid viewer)
    {
        var state = new WGShipConsoleState();
        if (!TryGetShip(console, out var ship))
            return state;

        var comp = ship.Comp;
        state.Commanded = true;
        state.Ship = Name(ship);
        state.Authority = _command.GetAuthority(viewer, ship);
        state.CanView = _command.CanOperate(viewer, ship);
        state.CanConfigure = _command.CanConfigure(viewer, ship);
        state.CanSeeLog = state.Authority >= WGShipAuthority.Admin;
        if (!state.CanView)
            return state;

        if (TryComp<WGShipZonesComponent>(ship, out var zones))
        {
            var counts = _command.ZoneCounts(ship);
            foreach (var zone in zones.Zones)
            {
                var (doors, lockers) = counts.GetValueOrDefault(zone.Id);
                state.Zones.Add(new WGShipConsoleZone(zone.Id, Loc.GetString(zone.Name), zone.Mode, doors, lockers));
            }
        }

        state.Responses[WGShipConsoleResponseSlot.Staff] = comp.StaffZoneResponse;
        state.Responses[WGShipConsoleResponseSlot.Locked] = comp.LockedZoneResponse;
        state.Responses[WGShipConsoleResponseSlot.Sealed] = comp.SealedZoneResponse;
        state.Responses[WGShipConsoleResponseSlot.Barred] = comp.BarredResponse;
        foreach (var which in Enum.GetValues<WGShipConsoleSwitch>())
            state.Switches[which] = GetSwitch(ship, which);

        state.Officers = comp.Officers.ConvertAll(Person);
        state.Staff = comp.Staff.ConvertAll(Person);
        state.Barred = comp.Barred.ConvertAll(Person);
        state.Aboard = Aboard(ship);

        foreach (var rule in comp.PriceRules)
        {
            var who = rule.Audience switch
            {
                WGShipPriceAudience.Person => rule.Person?.Name ?? string.Empty,
                WGShipPriceAudience.Company => rule.Company ?? string.Empty,
                _ => string.Empty,
            };
            state.Rules.Add(new WGShipConsoleRule(rule.Name, rule.Audience, who, rule.Services, rule.Multiplier, rule.Enabled));
        }

        if (TryComp<WGShipRosterComponent>(ship, out var roster))
        {
            state.HasRoster = true;
            state.RestaffDelay = (int) roster.RestaffDelay.TotalSeconds;
            state.RestaffsPerHour = roster.RestaffsPerHour;
            state.MarineOrders = roster.MarineOrders;
            state.GunnerPolicy = roster.GunnerPolicy;
            state.PilotStance = roster.PilotStance;
            state.Callsign = roster.Callsign;
            foreach (var slot in roster.Slots.Values.OrderBy(s => s.Id))
            {
                var held = slot.Holder is { } holder && Exists(holder);
                var slotState = held ? WGShipConsoleSlotState.Player
                    : slot.StoodDown ? WGShipConsoleSlotState.StoodDown
                    : _roster.Held(slot) ? WGShipConsoleSlotState.Staffed
                    : WGShipConsoleSlotState.Empty;
                var who = (held ? slot.Holder : slot.Occupant) is { } mob && Exists(mob) ? Name(mob) : string.Empty;
                state.Roster.Add(new WGShipConsoleSlot(slot.Id, What(slot), slot.Kind == WGShipRosterKind.Counter, slotState, who));
            }
        }

        if (TryComp<WGShipJobsComponent>(ship, out var jobs))
        {
            state.HasJobs = true;
            var slots = _jobs.GetSlots((ship, jobs));
            var holders = _jobs.Holders(ship);
            foreach (var job in jobs.Jobs.Keys.OrderBy(j => j.Id))
            {
                var name = _protos.TryIndex(job, out var proto) ? proto.LocalizedName : job.Id;
                var held = holders.Where(h => h.Comp.Job == job)
                    .Select(h => new WGShipConsoleHolder(GetNetEntity(h.Owner), Name(h.Owner)))
                    .ToList();
                state.Jobs.Add(new WGShipConsoleJob(job.Id, name, slots.GetValueOrDefault(job), held));
            }
        }

        state.Vessel = _presets.VesselOf(ship);
        if (state.Vessel is { } vessel && UserOf(viewer) is { } user && _presetLists.TryGetValue((user, vessel), out var presets))
            state.Presets = presets;

        var pads = EntityQueryEnumerator<WGRecoveryPadComponent, TransformComponent>();
        while (pads.MoveNext(out var uid, out var pad, out var xform))
        {
            if (xform.GridUid == ship.Owner)
                state.Pads.Add(new WGShipConsolePad(GetNetEntity(uid), Name(uid), pad.Access));
        }

        if (state.CanSeeLog && TryComp<WGShipServiceLedgerComponent>(ship, out var ledger))
        {
            for (var i = ledger.Entries.Count - 1; i >= 0 && state.Log.Count < LogLines; i--)
            {
                var e = ledger.Entries[i];
                state.Log.Add(new WGShipConsoleLogEntry(e.Time, e.Customer, e.Kind, e.Service, e.What, e.BasePrice, e.Charged, e.Rule));
            }
        }

        return state;
    }

    private static WGShipConsolePerson Person(WGShipCommandPerson person)
    {
        return new WGShipConsolePerson(person.Name, person.Key != null, person.User != null);
    }

    private string What(WGShipRosterSlot slot)
    {
        if (slot.Kind == WGShipRosterKind.Counter)
            return slot.Trader is { } trader && _protos.TryIndex(trader, out var proto) ? proto.Name : slot.What;

        return _protos.TryIndex(slot.Role, out var role) ? Loc.GetString(role.Title) : slot.What;
    }

    /// <summary>Everyone aboard with a mind (players and the characters they left), by name, with their standing.</summary>
    public List<WGShipConsoleAboard> Aboard(Entity<WGShipCommandComponent> ship)
    {
        var result = new List<WGShipConsoleAboard>();
        var query = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out _, out var xform))
        {
            if (xform.GridUid != ship.Owner || HasComp<GhostComponent>(uid) || !_mind.TryGetMind(uid, out _, out _))
                continue;

            result.Add(new WGShipConsoleAboard(GetNetEntity(uid), Name(uid), _command.GetAuthority(uid, ship)));
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        if (result.Count > AboardLimit)
            result.RemoveRange(AboardLimit, result.Count - AboardLimit);
        return result;
    }

    private NetUserId? UserOf(EntityUid viewer)
    {
        return _player.TryGetSessionByEntity(viewer, out var session) ? session.UserId : null;
    }

    private void SendState(EntityUid console, EntityUid viewer)
    {
        _ui.ServerSendUiMessage(console, WGShipConsoleUiKey.Key, new WGShipConsoleStateMessage(State(console, viewer)), viewer);
    }

    /// <summary>Sends every viewer of the console their state within a quarter second.</summary>
    private void MarkChanged(EntityUid console)
    {
        _changed.Add(console);
    }

    /// <summary>Sends every viewer of the console their state.</summary>
    private void SendAll(EntityUid console)
    {
        foreach (var actor in _ui.GetActors(console, WGShipConsoleUiKey.Key).ToList())
            SendState(console, actor);
    }

    private void OnOpened(Entity<WGShipConsoleComponent> console, ref BoundUIOpenedEvent args)
    {
        SendState(console, args.Actor);
        _ = LoadPresets(console, args.Actor);
    }

    private void OnRefresh(Entity<WGShipConsoleComponent> console, ref WGShipConsoleRefreshMessage args)
    {
        // A held-down refresh shouldn't rebuild the whole state every tick.
        var now = _timing.CurTime;
        if (now < _refreshAt.GetValueOrDefault(args.Actor))
            return;

        if (_refreshAt.Count > 64)
            _refreshAt.Clear();
        _refreshAt[args.Actor] = now + RefreshMessageGap;
        SendState(console, args.Actor);
        if (now < _presetRead.GetValueOrDefault(args.Actor))
            return;

        // A held-down refresh shouldn't turn into a stream of database reads.
        _presetRead[args.Actor] = now + PresetReadGap;
        if (_presetRead.Count > 64)
            _presetRead.Clear();
        _ = LoadPresets(console, args.Actor);
    }

    #endregion

    #region Changes

    /// <summary>
    /// The ship, when the actor may change its settings. Otherwise they are told they aren't cleared and the console
    /// changes nothing.
    /// </summary>
    private bool Configuring(EntityUid console, EntityUid actor, out Entity<WGShipCommandComponent> ship)
    {
        if (TryGetShip(console, out ship) && _command.CanConfigure(actor, ship))
            return true;

        _popup.PopupEntity(Loc.GetString("wg-ship-console-not-cleared"), console, actor, PopupType.MediumCaution);
        return false;
    }

    private void OnZone(Entity<WGShipConsoleComponent> console, ref WGShipConsoleZoneMessage args)
    {
        if (!Enum.IsDefined(args.Mode) || !Configuring(console, args.Actor, out var ship)
            || !TryComp<WGShipZonesComponent>(ship, out var zones))
            return;

        var ids = args.Zone is { } one ? new List<string> { one } : zones.Zones.Select(z => z.Id).ToList();
        foreach (var id in ids)
            _command.SetZoneMode((ship, zones), id, args.Mode, args.Actor);
        MarkChanged(console);
    }

    private void OnResponse(Entity<WGShipConsoleComponent> console, ref WGShipConsoleResponseMessage args)
    {
        if (!Enum.IsDefined(args.Slot) || !Enum.IsDefined(args.Response) || !Configuring(console, args.Actor, out var ship))
            return;

        var comp = ship.Comp;
        switch (args.Slot)
        {
            case WGShipConsoleResponseSlot.Staff:
                comp.StaffZoneResponse = args.Response;
                break;
            case WGShipConsoleResponseSlot.Locked:
                comp.LockedZoneResponse = args.Response;
                break;
            case WGShipConsoleResponseSlot.Sealed:
                comp.SealedZoneResponse = args.Response;
                break;
            case WGShipConsoleResponseSlot.Barred:
                comp.BarredResponse = args.Response;
                break;
        }

        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(args.Actor):actor} set the crew's {args.Slot} response on {ToPrettyString(ship.Owner):grid} to {args.Response}");
        MarkChanged(console);
    }

    /// <summary>Where one of the owner's switches stands; automatic restaffing is off on a ship without a roster.</summary>
    public bool GetSwitch(Entity<WGShipCommandComponent> ship, WGShipConsoleSwitch which)
    {
        var comp = ship.Comp;
        return which switch
        {
            WGShipConsoleSwitch.AdminsInZones => comp.AdminsInZones,
            WGShipConsoleSwitch.AdminsAtConsole => comp.AdminsAtConsole,
            WGShipConsoleSwitch.AdminsNeverTargeted => comp.AdminsNeverTargeted,
            WGShipConsoleSwitch.AdminsServedFree => comp.AdminsServedFree,
            WGShipConsoleSwitch.EventStaffAreAdmins => comp.EventStaffAreAdmins,
            WGShipConsoleSwitch.OwnerServedFree => comp.OwnerServedFree,
            WGShipConsoleSwitch.DocksWelcome => comp.DocksWelcome,
            WGShipConsoleSwitch.AutoRestaff => CompOrNull<WGShipRosterComponent>(ship)?.AutoRestaff ?? false,
            _ => false,
        };
    }

    /// <summary>Sets one of the owner's switches. Admin-logged. False when the actor may not, or nothing changed.</summary>
    public bool SetSwitch(EntityUid console, EntityUid actor, WGShipConsoleSwitch which, bool value)
    {
        if (!Enum.IsDefined(which) || !Configuring(console, actor, out var ship) || GetSwitch(ship, which) == value)
            return false;

        var comp = ship.Comp;
        switch (which)
        {
            case WGShipConsoleSwitch.AdminsInZones:
                _command.SetAdminsInZones(ship, value);
                break;
            case WGShipConsoleSwitch.AdminsAtConsole:
                comp.AdminsAtConsole = value;
                break;
            case WGShipConsoleSwitch.AdminsNeverTargeted:
                comp.AdminsNeverTargeted = value;
                break;
            case WGShipConsoleSwitch.AdminsServedFree:
                comp.AdminsServedFree = value;
                break;
            case WGShipConsoleSwitch.EventStaffAreAdmins:
                comp.EventStaffAreAdmins = value;
                _command.RefreshZoneKeys(ship);
                break;
            case WGShipConsoleSwitch.OwnerServedFree:
                comp.OwnerServedFree = value;
                break;
            case WGShipConsoleSwitch.DocksWelcome:
                comp.DocksWelcome = value;
                break;
            case WGShipConsoleSwitch.AutoRestaff:
                if (!TryComp<WGShipRosterComponent>(ship, out var roster))
                    return false;
                _roster.SetAutoRestaff((ship, roster), value, actor);
                break;
        }

        Dirty(ship);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} switched {which} {(value ? "on" : "off")} on {ToPrettyString(ship.Owner):grid}");
        return true;
    }

    private void OnSwitch(Entity<WGShipConsoleComponent> console, ref WGShipConsoleSwitchMessage args)
    {
        SetSwitch(console, args.Actor, args.Which, args.Value);
        MarkChanged(console);
    }

    /// <summary>
    /// Puts someone aboard on a command list by the card they carry and their player, taking them off the other lists.
    /// False when the actor may not, or the person is not aboard.
    /// </summary>
    public bool AddPerson(EntityUid console, EntityUid actor, EntityUid person, WGShipCommandList list)
    {
        if (!Enum.IsDefined(list) || !Configuring(console, actor, out var ship)
            || Transform(person).GridUid != ship.Owner || !_mind.TryGetMind(person, out _, out var mind))
            return false;

        WGShipAccessKey? key = _access.TryGetCard(person, out var card) && _access.TryGetKey(card, out var found) ? found : null;
        var user = UserOf(person) ?? mind.UserId;
        return _command.AddPerson(ship, list, key, user, Name(person));
    }

    private void OnAddPerson(Entity<WGShipConsoleComponent> console, ref WGShipConsoleAddPersonMessage args)
    {
        if (GetEntity(args.Person) is { Valid: true } person)
            AddPerson(console, args.Actor, person, args.List);
        MarkChanged(console);
    }

    private void OnRemovePerson(Entity<WGShipConsoleComponent> console, ref WGShipConsoleRemovePersonMessage args)
    {
        if (!Enum.IsDefined(args.List) || !Configuring(console, args.Actor, out var ship))
            return;

        var people = args.List switch
        {
            WGShipCommandList.Officers => ship.Comp.Officers,
            WGShipCommandList.Staff => ship.Comp.Staff,
            _ => ship.Comp.Barred,
        };

        if (args.Index >= 0 && args.Index < people.Count && people[args.Index].Name == args.Name)
        {
            var person = people[args.Index];
            _command.RemovePerson(ship, args.List, person.Key, person.User);
        }

        MarkChanged(console);
    }

    /// <summary>
    /// Adds a price rule or replaces the one of the same name, after checking it: a name (not "owner" or "admins", which
    /// name the free-service switches), some services, a multiplier from 0 to 10, and the person or company it needs.
    /// </summary>
    public bool SetRule(EntityUid console, EntityUid actor, WGShipConsoleSetRuleMessage rule)
    {
        var name = rule.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxRuleName || name is "owner" or "admins"
            || !Enum.IsDefined(rule.Audience) || rule.Services == WGShipServiceKind.None
            || (rule.Services & ~WGShipServiceKind.All) != 0
            || !float.IsFinite(rule.Multiplier) || rule.Multiplier is < 0 or > 10)
            return false;

        if (!Configuring(console, actor, out var ship)
            || ship.Comp.PriceRules.Count >= MaxRules && !ship.Comp.PriceRules.Exists(r => r.Name == name))
            return false;

        WGShipCommandPerson? person = null;
        string? company = null;
        switch (rule.Audience)
        {
            case WGShipPriceAudience.Person:
                if (rule.Person is not { } net || GetEntity(net) is not { Valid: true } target
                    || Transform(target).GridUid != ship.Owner || !_mind.TryGetMind(target, out _, out var mind))
                    return false;
                WGShipAccessKey? key = _access.TryGetCard(target, out var card) && _access.TryGetKey(card, out var found) ? found : null;
                person = new WGShipCommandPerson { Key = key, User = UserOf(target) ?? mind.UserId, Name = Name(target) };
                if (person.Key == null && person.User == null)
                    return false;
                break;
            case WGShipPriceAudience.Company:
                company = rule.Company?.Trim();
                if (string.IsNullOrEmpty(company) || company.Length > MaxRuleName)
                    return false;
                break;
        }

        _service.SetRule(ship, new WGShipPriceRule
        {
            Name = name,
            Audience = rule.Audience,
            Person = person,
            Company = company,
            Services = rule.Services,
            Multiplier = rule.Multiplier,
        }, actor);
        return true;
    }

    private void OnSetRule(Entity<WGShipConsoleComponent> console, ref WGShipConsoleSetRuleMessage args)
    {
        if (!SetRule(console, args.Actor, args))
            _popup.PopupEntity(Loc.GetString("wg-ship-console-bad-rule"), console, args.Actor, PopupType.MediumCaution);
        MarkChanged(console);
    }

    private void OnToggleRule(Entity<WGShipConsoleComponent> console, ref WGShipConsoleToggleRuleMessage args)
    {
        var name = args.Name;
        if (Configuring(console, args.Actor, out var ship) && ship.Comp.PriceRules.Exists(r => r.Name == name))
            _service.ToggleRule(ship, name, args.Actor, out _);
        MarkChanged(console);
    }

    private void OnRemoveRule(Entity<WGShipConsoleComponent> console, ref WGShipConsoleRemoveRuleMessage args)
    {
        if (Configuring(console, args.Actor, out var ship))
            _service.RemoveRule(ship, args.Name, args.Actor);
        MarkChanged(console);
    }

    private bool Roster(EntityUid console, EntityUid actor, out Entity<WGShipRosterComponent> roster)
    {
        roster = default;
        if (!Configuring(console, actor, out var ship) || !TryComp<WGShipRosterComponent>(ship, out var comp))
            return false;

        roster = (ship, comp);
        return true;
    }

    private void OnStaff(Entity<WGShipConsoleComponent> console, ref WGShipConsoleStaffMessage args)
    {
        if (Roster(console, args.Actor, out var roster) && (args.Slot == null || roster.Comp.Slots.ContainsKey(args.Slot)))
            _roster.Staff(roster, args.Slot, args.Actor);
        MarkChanged(console);
    }

    private void OnStandDown(Entity<WGShipConsoleComponent> console, ref WGShipConsoleStandDownMessage args)
    {
        if (Roster(console, args.Actor, out var roster) && roster.Comp.Slots.ContainsKey(args.Slot))
            _roster.SetStoodDown(roster, args.Slot, args.Down, args.Actor);
        MarkChanged(console);
    }

    private void OnRestaff(Entity<WGShipConsoleComponent> console, ref WGShipConsoleRestaffMessage args)
    {
        if (Roster(console, args.Actor, out var roster))
        {
            roster.Comp.RestaffDelay = TimeSpan.FromSeconds(Math.Clamp(args.Delay, 0, MaxRestaffDelay));
            roster.Comp.RestaffsPerHour = Math.Clamp(args.PerHour, 0, MaxRestaffsPerHour);
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):actor} set automatic restaffing on {ToPrettyString(roster.Owner):grid} to {roster.Comp.RestaffDelay.TotalSeconds} s, {roster.Comp.RestaffsPerHour} an hour");
        }

        MarkChanged(console);
    }

    /// <summary>Changes the crew's standing orders; null leaves one as it is. False when the actor may not.</summary>
    public bool SetOrders(EntityUid console, EntityUid actor, WGShipMarineOrders? marines, WGGunnerPolicy? gunners, WGShipPilotStance? pilot, string? callsign)
    {
        if (marines is { } m && !Enum.IsDefined(m) || gunners is { } g && !Enum.IsDefined(g) || pilot is { } p && !Enum.IsDefined(p)
            || !Roster(console, actor, out var roster))
            return false;

        _behaviour.Set(roster, marines, gunners, pilot, callsign, actor);
        return true;
    }

    private void OnOrders(Entity<WGShipConsoleComponent> console, ref WGShipConsoleOrdersMessage args)
    {
        SetOrders(console, args.Actor, args.Marines, args.Gunners, args.Pilot, args.Callsign);
        MarkChanged(console);
    }

    private void OnJobSlots(Entity<WGShipConsoleComponent> console, ref WGShipConsoleJobSlotsMessage args)
    {
        if (Configuring(console, args.Actor, out var ship) && TryComp<WGShipJobsComponent>(ship, out var jobs)
            && jobs.Jobs.ContainsKey(args.Job))
            _jobs.SetSlots((ship, jobs), args.Job, Math.Clamp(args.Slots, 0, MaxJobSlots), args.Actor);
        MarkChanged(console);
    }

    private void OnReleaseJob(Entity<WGShipConsoleComponent> console, ref WGShipConsoleReleaseJobMessage args)
    {
        if (Configuring(console, args.Actor, out var ship) && GetEntity(args.Holder) is { Valid: true } holder
            && TryComp<WGShipJobHolderComponent>(holder, out var comp) && comp.Grid == ship.Owner)
            _jobs.Leave((holder, comp));
        MarkChanged(console);
    }

    private void OnPadAccess(Entity<WGShipConsoleComponent> console, ref WGShipConsolePadAccessMessage args)
    {
        if (TryGetShip(console, out var ship) && GetEntity(args.Pad) is { Valid: true } pad
            && TryComp<WGRecoveryPadComponent>(pad, out var comp) && Transform(pad).GridUid == ship.Owner)
            _pads.SetAccess((pad, comp), args.Actor, args.Access);
        MarkChanged(console);
    }

    #endregion

    #region Presets

    private void OnPreset(Entity<WGShipConsoleComponent> console, ref WGShipConsolePresetMessage args)
    {
        if (!Enum.IsDefined(args.Action) || !Configuring(console, args.Actor, out var ship))
            return;

        if (!WGShipPresetSystem.ValidName(args.Name))
        {
            _popup.PopupEntity(Loc.GetString("wg-ship-console-bad-preset", ("max", WGShipPresetSystem.MaxNameLength)), console, args.Actor, PopupType.MediumCaution);
            return;
        }

        // Each of these is a database write and a fresh read of the list.
        var now = _timing.CurTime;
        if (now < _presetAction.GetValueOrDefault(args.Actor))
            return;

        _presetAction[args.Actor] = now + PresetActionGap;
        if (_presetAction.Count > 64)
            _presetAction.Clear();

        _ = RunPreset(console, args.Actor, ship, args.Action, args.Name);
    }

    /// <summary>Saves, loads or deletes one of the actor's own presets, then reads their list again.</summary>
    private async Task RunPreset(EntityUid console, EntityUid actor, EntityUid ship, WGShipConsolePresetAction action, string name)
    {
        if (UserOf(actor) is not { } user || _presets.VesselOf(ship) is not { } vessel)
            return;

        try
        {
            var line = action switch
            {
                WGShipConsolePresetAction.Save => "wg-ship-console-preset-saved",
                WGShipConsolePresetAction.Load => "wg-ship-console-preset-loaded",
                _ => "wg-ship-console-preset-deleted",
            };
            switch (action)
            {
                case WGShipConsolePresetAction.Save:
                    var stored = await _db.GetShipCommandPresetsAsync(user.UserId, vessel);
                    if (stored.Count >= MaxPresets && !stored.Exists(p => p.Name == name))
                    {
                        line = "wg-ship-console-preset-full";
                        break;
                    }

                    if (TerminatingOrDeleted(ship))
                        return;

                    await _presets.Save(ship, user, name);
                    break;
                case WGShipConsolePresetAction.Load:
                    var data = await _presets.Fetch(user, vessel, name);
                    if (data == null || TerminatingOrDeleted(ship) || !Exists(actor) || !_command.CanConfigure(actor, ship))
                    {
                        line = "wg-ship-console-preset-missing";
                        break;
                    }

                    _presets.Apply(ship, data, actor, name);
                    break;
                case WGShipConsolePresetAction.Delete:
                    if (!await _db.DeleteShipCommandPresetAsync(user.UserId, vessel, name))
                        line = "wg-ship-console-preset-missing";
                    break;
            }

            if (Exists(console) && Exists(actor))
                _popup.PopupEntity(Loc.GetString(line, ("name", name)), console, actor);
        }
        catch (Exception e)
        {
            _log.GetSawmill("wg.shipcommand").Error($"Ship console preset {action} failed: {e}");
        }

        await LoadPresets(console, actor);
    }

    /// <summary>Reads the viewer's presets for the console's vessel, then sends them their state.</summary>
    private async Task LoadPresets(EntityUid console, EntityUid viewer)
    {
        if (!TryGetShip(console, out var ship) || _presets.VesselOf(ship) is not { } vessel || UserOf(viewer) is not { } user
            || !_command.CanConfigure(viewer, ship))
            return;

        try
        {
            var stored = await _db.GetShipCommandPresetsAsync(user.UserId, vessel);
            _presetLists[(user, vessel)] = stored.Select(p => new WGShipConsolePreset(p.Name, p.Saved)).ToList();
        }
        catch (Exception e)
        {
            _log.GetSawmill("wg.shipcommand").Error($"Ship console preset list failed: {e}");
            return;
        }

        if (Exists(console) && Exists(viewer) && _ui.IsUiOpen(console, WGShipConsoleUiKey.Key, viewer))
            SendState(console, viewer);
    }

    #endregion
}
