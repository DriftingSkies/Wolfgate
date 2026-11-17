using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._WG.Drydock;
using Content.Server.Administration.Logs;
using Content.Server.CartridgeLoader;
using Content.Server.Chat.Managers;
using Content.Server.CriminalRecords.Systems;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server.StationRecords.Systems;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WG.Bench;
using Content.Shared._WG.CCVar;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Roles.Jobs;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Bench;

/// <summary>
/// The Bench: judges ticket offenders against the Common Writ from a PDA app or terminal. The fine is taken from the
/// offender's bank at once, any shortfall is garnished from what they earn later, and debt left too long or too high,
/// or an Outlawry charge, makes them Wanted. Round-scoped; cleared at round restart.
/// </summary>
public sealed partial class WGBenchSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IServerPreferencesManager _prefs = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private StationRecordsSystem _records = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private WGShipbreakerWarrantSystem _warrants = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedJobSystem _jobs = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private PaperSystem _paper = default!;

    public static readonly EntProtoId Cartridge = "WGBenchCartridge";
    public static readonly EntProtoId TicketPaper = "WGBenchTicket";

    /// <summary>Jobs that sit on the Bench: judges and the Overseer they answer to.</summary>
    public static readonly HashSet<string> JudgeJobs = new() { "SecurityGuard", "StationRepresentative" };

    private const int MaxTitle = 60;
    private const int MaxText = 300;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly List<WGBenchTicket> _tickets = new();
    private readonly List<WGBenchOrdinance> _ordinances = new();
    private int _nextTicket = 1;
    private int _nextOrdinance = 1;
    private TimeSpan _nextRefresh;

    public IReadOnlyList<WGBenchTicket> Tickets => _tickets;
    public IReadOnlyList<WGBenchOrdinance> Ordinances => _ordinances;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
        SubscribeLocalEvent<BalanceChangedEvent>(OnBalanceChanged);
        SubscribeLocalEvent<WGBenchCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeReady);
        SubscribeLocalEvent<WGBenchCartridgeComponent, CartridgeMessageEvent>(OnCartridgeMessage);
        SubscribeLocalEvent<WGBenchTerminalComponent, BoundUIOpenedEvent>(OnTerminalOpened);
        SubscribeLocalEvent<WGBenchTerminalComponent, WGBenchTerminalMessage>(OnTerminalMessage);
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _tickets.Clear();
        _ordinances.Clear();
        _debtors.Clear();
        _nextTicket = 1;
        _nextOrdinance = 1;
    }

    /// <summary>Installs the Bench app on a judge's PDA.</summary>
    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        if (ev.JobId is not { } job || !JudgeJobs.Contains(job)
            || !_inventory.TryGetSlotEntity(ev.Mob, "id", out var pda)
            || !HasComp<CartridgeLoaderComponent>(pda))
            return;

        _cartridgeLoader.InstallProgram(pda.Value, Cartridge, deinstallable: false);
    }

    /// <summary>Whether someone sits on the Bench.</summary>
    public bool IsJudge(EntityUid user)
    {
        return _mind.TryGetMind(user, out var mindId, out _)
            && _jobs.MindTryGetJobId(mindId, out var job)
            && job is { } id
            && JudgeJobs.Contains(id.Id);
    }

    /// <summary>
    /// Issues a ticket: takes what the offender's bank can pay now, records the rest as debt, writes it to their
    /// record and hands the judge a stamped copy. Returns an error to show the judge, or null on success.
    /// </summary>
    public string? Issue(EntityUid judge, EntityUid offender, IReadOnlyList<WGBenchChargeRequest> requests, string ship,
        string notes, out WGBenchTicket? ticket)
    {
        ticket = null;
        if (!TryComp<ActorComponent>(offender, out var actor))
            return Loc.GetString("wg-bench-error-no-account");
        if (offender == judge)
            return Loc.GetString("wg-bench-error-self");

        var charges = new List<WGBenchChargeView>();
        var seen = new HashSet<string>();
        foreach (var request in requests)
        {
            if (charges.Count >= WGBenchMath.MaxCharges)
                break;

            // A law or ordinance counts once per ticket; custom charges are told apart by title.
            var key = request.Law ?? (request.Ordinance is { } id ? $"ordinance:{id}" : $"custom:{request.Title.Trim().ToLowerInvariant()}");
            if (seen.Add(key) && Resolve(request) is { } charge)
                charges.Add(charge);
        }

        if (charges.Count == 0)
            return Loc.GetString("wg-bench-error-no-charges");

        var (uncapped, level) = WGBenchMath.Sum(charges.Select(c => (c.Fine, c.Level)));
        var user = actor.PlayerSession.UserId;
        var fined = _tickets.Where(t => t.User == user && !t.Pardoned).Sum(t => t.Total);
        var total = WGBenchMath.Cap(uncapped, _cfg.GetCVar(BenchCVars.TicketCap), fined, _cfg.GetCVar(BenchCVars.RoundCap));
        var now = _timing.CurTime;
        ticket = new WGBenchTicket
        {
            Number = _nextTicket++,
            Offender = Name(offender),
            User = actor.PlayerSession.UserId,
            Body = offender,
            Judge = Name(judge),
            Ship = Trim(ship, MaxTitle),
            Notes = Trim(notes, MaxText),
            Charges = charges,
            Total = total,
            Uncapped = uncapped,
            Level = level,
            Issued = now,
            Deadline = now + TimeSpan.FromMinutes(_cfg.GetCVar(BenchCVars.DeadlineMinutes)),
        };
        _tickets.Add(ticket);

        Collect(actor.PlayerSession, ticket);
        Record(ticket);
        if (level == WGPenaltyLevel.Outlawry)
            Outlaw(ticket);
        else
            Escalate(ticket.User);

        HandOver(judge, ticket);
        Notify(actor.PlayerSession, offender, ticket);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(judge):judge} issued Bench ticket #{ticket.Number} to {ToPrettyString(offender):offender}: {total} fine, {level}, {ticket.Paid} taken");
        return null;
    }

    /// <summary>Pardons a ticket: returns what it took, drops its debt and lifts any Wanted it caused.</summary>
    public bool Pardon(int number, string by)
    {
        if (_tickets.FirstOrDefault(t => t.Number == number) is not { Pardoned: false } ticket)
            return false;

        ticket.Pardoned = true;
        ticket.Owed = 0;
        if (ticket.Paid > 0 && SessionOf(ticket.User) is { } session && Give(session, ticket.Paid))
        {
            _bank.TrySectorWithdraw(_nfsd, ticket.Paid, LedgerEntryType.StationWithdrawalOther);
            _chat.DispatchServerMessage(session, Loc.GetString("wg-bench-pardoned", ("number", number), ("amount", ticket.Paid)));
        }

        if (ticket.Level == WGPenaltyLevel.Outlawry && Exists(ticket.Body)
            && !_tickets.Any(t => t != ticket && t.User == ticket.User && t.Wanting))
            RemCompDeferred<WGShipbreakerWarrantComponent>(ticket.Body);

        if (RecordKey(ticket.Offender) is { } key)
            _criminalRecords.TryAddHistory(key, Loc.GetString("wg-bench-history-pardon", ("number", number)), by);

        Escalate(ticket.User);
        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{by} pardoned Bench ticket #{number} to {ticket.Offender}, refunding {ticket.Paid}");
        return true;
    }

    /// <summary>Posts an ordinance for the round, chargeable from every Bench app.</summary>
    public WGBenchOrdinance? PostOrdinance(string title, string text, int fine, WGPenaltyLevel level, string by)
    {
        title = Trim(title, MaxTitle);
        if (title.Length == 0)
            return null;

        var ordinance = new WGBenchOrdinance
        {
            Id = _nextOrdinance++,
            Title = title,
            Text = Trim(text, MaxText),
            Fine = level == WGPenaltyLevel.Outlawry ? 0 : WGBenchMath.Clamp(fine, 0, WGBenchMath.MaxCustomFine),
            Level = level,
            PostedBy = by,
        };
        _ordinances.Add(ordinance);
        return ordinance;
    }

    /// <summary>A charge as it goes on the ticket, or null if the request names nothing valid.</summary>
    private WGBenchChargeView? Resolve(WGBenchChargeRequest request)
    {
        if (request.Law is { } lawId)
        {
            if (!_proto.TryIndex<WGLawPrototype>(lawId, out var law))
                return null;
            return new WGBenchChargeView
            {
                Title = Loc.GetString(law.Name),
                Fine = law.Level == WGPenaltyLevel.Outlawry ? 0 : WGBenchMath.Clamp(request.Fine, law.MinFine, law.MaxFine),
                Level = law.Level,
            };
        }

        if (request.Ordinance is { } ordinanceId)
        {
            if (_ordinances.FirstOrDefault(o => o.Id == ordinanceId) is not { } ordinance)
                return null;
            return new WGBenchChargeView { Title = ordinance.Title, Fine = ordinance.Fine, Level = ordinance.Level };
        }

        var title = Trim(request.Title, MaxTitle);
        if (title.Length == 0)
            return null;
        return new WGBenchChargeView
        {
            Title = title,
            Fine = request.Level == WGPenaltyLevel.Outlawry ? 0 : WGBenchMath.Clamp(request.Fine, 0, WGBenchMath.MaxCustomFine),
            Level = request.Level,
        };
    }

    /// <summary>The offender's sector record, kept by name.</summary>
    private StationRecordKey? RecordKey(string name)
    {
        var station = _sectorService.GetServiceEntity();
        if (!station.IsValid() || _records.GetRecordByName(station, name) is not { } id)
            return null;
        return new StationRecordKey(id, station);
    }

    private void Record(WGBenchTicket ticket)
    {
        if (RecordKey(ticket.Offender) is not { } key)
            return;

        var charges = string.Join(", ", ticket.Charges.Select(c => c.Title));
        _criminalRecords.TryAddHistory(key, Loc.GetString("wg-bench-history-ticket",
            ("number", ticket.Number), ("charges", charges), ("total", ticket.Total), ("paid", ticket.Paid)), ticket.Judge);
    }

    /// <summary>An Outlawry ticket: Wanted at once and a bounty on the board.</summary>
    private void Outlaw(WGBenchTicket ticket)
    {
        Escalate(ticket.User);
        var charges = string.Join(", ", ticket.Charges.Where(c => c.Level == WGPenaltyLevel.Outlawry).Select(c => c.Title));
        _warrants.Issue(ticket.Body, ticket.Ship, _cfg.GetCVar(BenchCVars.OutlawBounty),
            Loc.GetString("wg-bench-warrant-description", ("name", ticket.Offender), ("charges", charges)),
            Loc.GetString("wg-bench-warrant-author"), "wg-bench-warrant-paid");
    }

    /// <summary>Spawns the stamped ticket in the judge's hand for them to give over.</summary>
    private void HandOver(EntityUid judge, WGBenchTicket ticket)
    {
        var paper = Spawn(TicketPaper, Transform(judge).Coordinates);
        if (TryComp<PaperComponent>(paper, out var comp))
        {
            _paper.SetContent((paper, comp), TicketText(ticket));
            _paper.TryStamp((paper, comp), new StampDisplayInfo
            {
                StampedName = Loc.GetString("wg-bench-stamp"),
                StampedColor = Color.FromHex("#37946e"),
            }, "paper_stamp-centcom");
        }

        _hands.PickupOrDrop(judge, paper);
    }

    private string TicketText(WGBenchTicket ticket)
    {
        var lines = new List<string>
        {
            Loc.GetString("wg-bench-ticket-head"),
            Loc.GetString("wg-bench-ticket-number", ("number", ticket.Number)),
            Loc.GetString("wg-bench-ticket-offender", ("name", Escape(ticket.Offender))),
        };
        if (ticket.Ship.Length > 0)
            lines.Add(Loc.GetString("wg-bench-ticket-ship", ("ship", Escape(ticket.Ship))));
        lines.Add(Loc.GetString("wg-bench-ticket-judge", ("name", Escape(ticket.Judge)), ("time", RoundTime(ticket.Issued))));
        lines.Add(string.Empty);
        foreach (var charge in ticket.Charges)
        {
            lines.Add(Loc.GetString("wg-bench-ticket-charge", ("title", Escape(charge.Title)),
                ("level", LevelName(charge.Level)), ("fine", charge.Fine)));
        }

        lines.Add(string.Empty);
        lines.Add(Loc.GetString("wg-bench-ticket-total", ("total", ticket.Total), ("level", LevelName(ticket.Level))));
        if (ticket.Total < ticket.Uncapped)
            lines.Add(Loc.GetString("wg-bench-ticket-capped", ("uncapped", ticket.Uncapped)));
        lines.Add(Loc.GetString("wg-bench-ticket-paid", ("paid", ticket.Paid), ("owed", ticket.Owed)));
        if (ticket.Level == WGPenaltyLevel.Outlawry)
            lines.Add(Loc.GetString("wg-bench-ticket-outlawry"));
        else if (ticket.Owed > 0)
            lines.Add(Loc.GetString("wg-bench-ticket-debt", ("minutes", _cfg.GetCVar(BenchCVars.DeadlineMinutes))));
        if (ticket.Notes.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add(Loc.GetString("wg-bench-ticket-notes", ("notes", Escape(ticket.Notes))));
        }

        return string.Join("\n", lines);
    }

    private void Notify(ICommonSession session, EntityUid offender, WGBenchTicket ticket)
    {
        var message = ticket.Level == WGPenaltyLevel.Outlawry
            ? Loc.GetString("wg-bench-notify-outlawry", ("number", ticket.Number))
            : Loc.GetString("wg-bench-notify", ("number", ticket.Number), ("paid", ticket.Paid), ("owed", ticket.Owed));
        _popup.PopupEntity(message, offender, offender, PopupType.LargeCaution);
        _chat.DispatchServerMessage(session, message);
    }

    public string LevelName(WGPenaltyLevel level)
    {
        return Loc.GetString($"wg-penalty-{level.ToString().ToLowerInvariant()}");
    }

    private string RoundTime(TimeSpan time)
    {
        var since = time - _ticker.RoundStartTimeSpan;
        return since < TimeSpan.Zero ? "00:00" : since.ToString(@"hh\:mm");
    }

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static string Escape(string text)
    {
        return Robust.Shared.Utility.FormattedMessage.EscapeText(text);
    }
}
