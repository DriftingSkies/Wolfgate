using System.Threading.Tasks;
using Content.Server._Mono.MonoCoins;
using Content.Server._NF.Bank;
using Content.Server._NF.CryoSleep;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Savings;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.Savings;

/// <summary>
/// Moves money between a character's bank and the player's savings at an ATM, and pays savings interest at round end.
/// </summary>
public sealed class WGSavingsSystem : EntitySystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private MonoCoinsManager _coins = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>When each account first spawned this round.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _firstSpawn = new();

    /// <summary>When each account last left the round alive through cryo.</summary>
    private readonly Dictionary<NetUserId, TimeSpan> _cryoAt = new();

    /// <summary>Accounts with a transfer in flight; a second click waits for the first.</summary>
    private readonly HashSet<NetUserId> _pending = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BankATMComponent, WGSavingsTransferMessage>(OnTransfer);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawned);
        SubscribeLocalEvent<BankAccountComponent, CryosleepBeforeMindRemovedEvent>(OnCryo);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevel);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnCleanup);
    }

    private void OnTransfer(EntityUid uid, BankATMComponent component, WGSavingsTransferMessage args)
    {
        if (args.Actor is not { Valid: true } player || !_players.TryGetSessionByEntity(player, out var session))
            return;

        if (HasComp<IronmanComponent>(player))
        {
            Deny(uid, component, player, "wg-savings-error-ironman");
            return;
        }

        if (args.Amount <= 0)
        {
            Deny(uid, component, player, "wg-savings-error-amount");
            return;
        }

        if (_pending.Add(session.UserId))
            TransferAsync(uid, component, player, session.UserId, args.Amount, args.ToSavings, args.UiKey);
    }

    private async void TransferAsync(EntityUid atm, BankATMComponent component, EntityUid player, NetUserId user,
        int amount, bool toSavings, Enum uiKey)
    {
        try
        {
            // Loads the balance into the cache, so the moves below land in one tick.
            var savings = await _coins.GetMonoCoinsBalanceAsync(user);

            if (Deleted(player) || !TryComp<BankAccountComponent>(player, out var bank))
            {
                Deny(atm, component, player, "bank-atm-menu-no-bank");
                return;
            }

            if (toSavings ? bank.Balance < amount : savings < amount)
            {
                Deny(atm, component, player, "bank-insufficient-funds");
                return;
            }

            if (!toSavings && amount > int.MaxValue - bank.Balance)
            {
                Deny(atm, component, player, "wg-savings-error-bank-full");
                return;
            }

            var limits = _prototype.Index(WGSavingsInterestPrototype.Default);
            if (toSavings && amount > limits.Room(savings))
            {
                _popup.PopupEntity(Loc.GetString("wg-savings-error-full",
                    ("max", BankSystemExtensions.ToSpesoString(limits.MaxBalance)),
                    ("room", BankSystemExtensions.ToSpesoString(limits.Room(savings)))), player, player);
                _audio.PlayPvs(component.ErrorSound, atm);
                return;
            }

            Task saved;
            if (toSavings)
            {
                if (!_bank.TryBankWithdraw(player, amount))
                {
                    Deny(atm, component, player, "bank-atm-menu-transaction-denied");
                    return;
                }

                saved = _coins.AddMonoCoinsAsync(user, amount);
            }
            else
            {
                saved = _coins.AddMonoCoinsAsync(user, -amount);

                // Taxed like any deposit: what the tax takes goes straight back to savings.
                if (!_bank.TryBankDeposit(player, amount))
                {
                    await saved;
                    await _coins.AddMonoCoinsAsync(user, amount);
                    Deny(atm, component, player, "bank-atm-menu-transaction-denied");
                    return;
                }
            }

            _popup.PopupEntity(Loc.GetString(toSavings ? "wg-savings-moved-to-savings" : "wg-savings-moved-to-bank",
                ("amount", BankSystemExtensions.ToSpesoString(amount))), player, player);
            _audio.PlayPvs(component.ConfirmSound, atm);
            _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
                $"{ToPrettyString(player):actor} moved {amount} {(toSavings ? "from bank to savings" : "from savings to bank")} at {ToPrettyString(atm)}");

            await saved;
        }
        catch (Exception e)
        {
            Log.Error($"Savings transfer of {amount} for {user} failed: {e}");
        }
        finally
        {
            _pending.Remove(user);
            if (!Deleted(atm) && !Deleted(player))
                _bank.UpdateAtmUi(atm, component, player, uiKey);
        }
    }

    private void Deny(EntityUid atm, BankATMComponent component, EntityUid player, LocId message)
    {
        _popup.PopupEntity(Loc.GetString(message), player, player);
        _audio.PlayPvs(component.ErrorSound, atm);
    }

    private void OnSpawned(PlayerSpawnCompleteEvent ev)
    {
        RecordSpawn(ev.Player.UserId, _timing.CurTime);
    }

    /// <summary>
    /// Counts the account as in the round from this time, unless it already spawned earlier.
    /// </summary>
    public void RecordSpawn(NetUserId user, TimeSpan at)
    {
        _firstSpawn.TryAdd(user, at);
    }

    private void OnCryo(EntityUid uid, BankAccountComponent component, CryosleepBeforeMindRemovedEvent args)
    {
        if (args.User is { } user && !_mobState.IsDead(uid))
            _cryoAt[user] = _timing.CurTime;
    }

    private void OnRunLevel(GameRunLevelChangedEvent ev)
    {
        if (ev.New == GameRunLevel.PostRound)
            PayRoundInterest();
    }

    private void OnCleanup(RoundRestartCleanupEvent ev)
    {
        _firstSpawn.Clear();
        _cryoAt.Clear();
    }

    /// <summary>
    /// Pays interest once to every account that was in the round long enough and ended it alive, in a body with a
    /// bank account or gone to cryo.
    /// </summary>
    public void PayRoundInterest()
    {
        if (!_prototype.TryIndex(WGSavingsInterestPrototype.Default, out var interest))
        {
            Log.Error($"No {nameof(WGSavingsInterestPrototype)} {WGSavingsInterestPrototype.Default}; no savings interest paid.");
            return;
        }

        var now = _timing.CurTime;
        var survived = new Dictionary<NetUserId, TimeSpan>(_cryoAt);

        var query = EntityQueryEnumerator<MindComponent>();
        while (query.MoveNext(out var mind))
        {
            if (mind.UserId is { } user &&
                mind.OwnedEntity is { } body &&
                HasComp<BankAccountComponent>(body) &&
                !_mobState.IsDead(body))
            {
                survived[user] = now;
            }
        }

        foreach (var (user, until) in survived)
        {
            if (_firstSpawn.TryGetValue(user, out var spawned) && until - spawned >= interest.MinimumTime)
                PayInterest(user, interest);
        }
    }

    private async void PayInterest(NetUserId user, WGSavingsInterestPrototype interest)
    {
        try
        {
            var earned = interest.InterestOn(await _coins.GetMonoCoinsBalanceAsync(user));
            if (earned <= 0)
                return;

            var total = await _coins.AddMonoCoinsAsync(user, earned);
            _adminLog.Add(LogType.ATMUsage, LogImpact.Low, $"{user} earned {earned} savings interest, savings now {total}");

            if (_players.TryGetSessionById(user, out var session))
            {
                _chat.DispatchServerMessage(session, Loc.GetString("wg-savings-interest-paid",
                    ("amount", BankSystemExtensions.ToSpesoString(earned)),
                    ("rate", interest.Rate * 100),
                    ("total", BankSystemExtensions.ToSpesoString(total))));
            }
        }
        catch (Exception e)
        {
            Log.Error($"Savings interest for {user} failed: {e}");
        }
    }
}
