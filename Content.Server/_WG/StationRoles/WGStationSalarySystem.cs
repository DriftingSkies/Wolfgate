using Content.Server._NF.Bank;
using Content.Server._NF.Station.Components;
using Content.Server._WG.Companies;
using Content.Server._WG.RoundSetup;
using Content.Server.Afk;
using Content.Server.Chat.Managers;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Companies;
using Content.Shared._WG.StationRoles;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.StationRoles;

/// <summary>Pays station roles for time on duty, without anyone having to hand it out.</summary>
public sealed partial class WGStationSalarySystem : EntitySystem
{
    [Dependency] private IAfkManager _afk = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private WGRoundSetupSystem _crews = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How often duty time is counted.</summary>
    private static readonly TimeSpan Sample = TimeSpan.FromMinutes(1);

    /// <summary>Duty seconds per payday.</summary>
    public const int PaySeconds = 900;

    private TimeSpan _nextSample;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextSample)
            return;
        _nextSample = _timing.CurTime + Sample;
        SampleDuty((int) Sample.TotalSeconds);
    }

    /// <summary>The salary a job earns on this station: none on a ship or a private scenario station, or for an unpaid job.</summary>
    public WGStationSalaryPrototype? SalaryFor(string? job, EntityUid station)
    {
        if (job == null || HasComp<ExtraShuttleInformationComponent>(station) || !_crews.IsStandardStation(station))
            return null;
        return _prototypes.TryIndex<WGStationSalaryPrototype>(job, out var salary) ? salary : null;
    }

    private void OnSpawned(PlayerSpawnCompleteEvent args)
    {
        if (SalaryFor(args.JobId, args.Station) is not { } salary ||
            _preferences.GetPreferencesOrNull(args.Player.UserId) is not { } prefs ||
            WGCompanySystem.SpawnedSlot(prefs, args.Profile) is not { } slot)
            return;
        AssignRole(args.Mob, args.Player, salary.ID, slot);
        _chat.DispatchServerMessage(args.Player, Loc.GetString("wg-station-roles-on-duty", ("wage", salary.Wage)));
    }

    /// <summary>Puts a body on the payroll for a job.</summary>
    public void AssignRole(EntityUid body, ICommonSession player, string job, int slot)
    {
        var role = EnsureComp<WGStationRoleComponent>(body);
        role.User = player.UserId;
        role.Job = job;
        role.Slot = slot;
    }

    /// <summary>Counts duty time for every role being worked and pays what has come due.</summary>
    public void SampleDuty(int seconds)
    {
        var due = new Dictionary<SectorBankAccount, List<(EntityUid Body, WGStationRoleComponent Role, ICommonSession Session, int Wage)>>();
        var query = EntityQueryEnumerator<WGStationRoleComponent, ActorComponent, MobStateComponent>();
        while (query.MoveNext(out var body, out var role, out var actor, out var state))
        {
            var session = actor.PlayerSession;
            if (session.UserId != role.User || session.Status != SessionStatus.InGame || state.CurrentState == MobState.Dead ||
                _afk.IsAfk(session) || _crews.IsInCrew(role.User) ||
                !_prototypes.TryIndex<WGStationSalaryPrototype>(role.Job, out var salary))
                continue;
            role.UnpaidSeconds += seconds;
            if (role.UnpaidSeconds < PaySeconds)
                continue;
            var paydays = role.UnpaidSeconds / PaySeconds;
            role.UnpaidSeconds -= paydays * PaySeconds;
            var wage = (int) ((long) salary.Wage * paydays * PaySeconds / WGCompanyPolicy.PayrollHourSeconds);
            if (!due.TryGetValue(salary.Account, out var list))
                due[salary.Account] = list = new();
            list.Add((body, role, session, wage));
        }
        foreach (var (account, wages) in due)
            PayWages(account, wages);
    }

    /// <summary>Pays in full when the account can, otherwise everyone the same share of what's there.</summary>
    private void PayWages(SectorBankAccount account, List<(EntityUid Body, WGStationRoleComponent Role, ICommonSession Session, int Wage)> wages)
    {
        var drawn = account != SectorBankAccount.Invalid;
        var percent = 100;
        if (drawn)
        {
            var total = 0L;
            foreach (var entry in wages)
                total += entry.Wage;
            percent = WGCompanyPolicy.PayrollShare(_bank.TryGetBalance(account, out var balance) ? balance : 0, total);
        }

        foreach (var (body, role, session, wage) in wages)
        {
            var amount = (int) (wage * (long) percent / 100);
            if (amount <= 0 || !Deposit(body, role, session, amount))
                continue;
            if (drawn)
                _bank.TrySectorWithdraw(account, amount, LedgerEntryType.StationWithdrawalPayroll);
            _chat.DispatchServerMessage(session, percent < 100
                ? Loc.GetString("wg-station-roles-paid-short", ("amount", amount), ("percent", percent))
                : Loc.GetString("wg-station-roles-paid", ("amount", amount)));
        }
    }

    /// <summary>Deposits into the spawned character's bank, keeping the body's balance in step when it is the selected one.</summary>
    private bool Deposit(EntityUid body, WGStationRoleComponent role, ICommonSession session, int amount)
    {
        if (_preferences.GetPreferencesOrNull(session.UserId) is not { } prefs)
            return false;
        if (prefs.SelectedCharacterIndex == role.Slot && HasComp<BankAccountComponent>(body))
            return _bank.TryBankDeposit(body, amount, false);
        return prefs.Characters.GetValueOrDefault(role.Slot) is HumanoidCharacterProfile profile &&
            _bank.TryBankDeposit(session, prefs, profile, amount, out _);
    }
}
