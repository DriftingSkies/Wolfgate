#nullable enable
using Content.IntegrationTests.Pair;
using Content.Server._Mono.MonoCoins;
using Content.Server._NF.Bank;
using Content.Server._WG.Savings;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.Savings;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Savings;

/// <summary>
/// An ATM moves money between the bank and savings within what each holds, and round-end interest goes only to an
/// account that played long enough and is still alive.
/// </summary>
[TestFixture]
[TestOf(typeof(WGSavingsSystem))]
public sealed class WGSavingsTest
{
    [TestCase(1_000_000, 0, 400_000, true, 600_000, 400_000, TestName = "BankMovesIntoSavings")]
    [TestCase(0, 1_000_000, 250_000, false, 250_000, 750_000, TestName = "SavingsMoveIntoTheBank")]
    [TestCase(100, 0, 500, true, 100, 0, TestName = "TheBankCantMoveMoreThanItHolds")]
    [TestCase(0, 100, 500, false, 0, 100, TestName = "SavingsCantMoveMoreThanTheyHold")]
    [TestCase(1_000_000, 19_500_000, 600_000, true, 1_000_000, 19_500_000, TestName = "SavingsCantPassTheirMax")]
    [TestCase(1_000_000, 19_500_000, 500_000, true, 500_000, 20_000_000, TestName = "SavingsFillUpToTheirMax")]
    public async Task Transfer(int bank, long savings, int amount, bool toSavings, int bankAfter, long savingsAfter)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair, bank, savings);
        var server = pair.Server;
        var entMan = server.EntMan;

        var atm = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            atm = entMan.SpawnEntity("ComputerBankATM", bench.Coords);
            var message = new WGSavingsTransferMessage(amount, toSavings) { Actor = bench.Body, UiKey = BankATMMenuUiKey.ATM };
            entMan.EventBus.RaiseLocalEvent(atm, message);
        });

        // The transfer waits on the savings balance, so give it time to land.
        for (var i = 0; i < 50 && (bench.Bank() != bankAfter || bench.Savings() != savingsAfter); i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(bench.Bank(), Is.EqualTo(bankAfter), "The bank holds the wrong amount.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(bench.Body).Balance, Is.EqualTo(bankAfter),
                    "The bank component shows the wrong balance.");
                Assert.That(bench.Savings(), Is.EqualTo(savingsAfter), "Savings hold the wrong amount.");
            });
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(60, false, true, TestName = "ASurvivorEarnsInterest")]
    [TestCase(5, false, false, TestName = "ALateJoinerEarnsNone")]
    [TestCase(60, true, false, TestName = "TheDeadEarnNone")]
    public async Task Interest(int minutesPlayed, bool dead, bool earns)
    {
        const long savings = 1_000_000;

        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair, 0, savings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var expected = savings;

        await server.WaitPost(() =>
        {
            var interest = server.ResolveDependency<IPrototypeManager>().Index(WGSavingsInterestPrototype.Default);
            if (earns)
                expected += interest.InterestOn(savings);

            if (dead)
                entMan.System<MobStateSystem>().ChangeMobState(bench.Body, MobState.Dead);

            var savingsSys = entMan.System<WGSavingsSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            savingsSys.RecordSpawn(bench.User, now - TimeSpan.FromMinutes(minutesPlayed));
            savingsSys.PayRoundInterest();
        });

        for (var i = 0; i < 50 && bench.Savings() != expected; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(5);
        Assert.That(bench.Savings(), Is.EqualTo(expected), earns ? "No interest was paid." : "Interest was paid.");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body the player owns, with a bank balance and savings set.
    /// </summary>
    private sealed class Bench
    {
        public EntityUid Body;
        public EntityCoordinates Coords;
        public Robust.Shared.Network.NetUserId User;
        private IServerPreferencesManager _prefs = default!;
        private MonoCoinsManager _coins = default!;

        public int Bank() => ((HumanoidCharacterProfile) _prefs.GetPreferences(User).SelectedCharacter).BankBalance;

        public long Savings() => _coins.GetMonoCoinsBalance(User) ?? 0;

        public static async Task<Bench> Create(TestPair pair, int bank, long savings)
        {
            var server = pair.Server;
            var entMan = server.EntMan;
            var map = await pair.CreateTestMap();
            var player = pair.Player!;
            var bench = new Bench
            {
                User = player.UserId,
                Coords = map.GridCoords,
                _prefs = server.ResolveDependency<IServerPreferencesManager>(),
                _coins = server.ResolveDependency<MonoCoinsManager>(),
            };

            Task setBank = Task.CompletedTask;
            Task setSavings = Task.CompletedTask;
            await server.WaitPost(() =>
            {
                var mindSys = entMan.System<SharedMindSystem>();
                bench.Body = entMan.SpawnEntity("MobHuman", map.GridCoords);
                mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, bench.Body);
                entMan.EnsureComponent<BankAccountComponent>(bench.Body);

                var current = bench._prefs.GetPreferences(player.UserId);
                var profile = (HumanoidCharacterProfile) current.SelectedCharacter;
                setBank = bench._prefs.SetProfile(player.UserId, current.SelectedCharacterIndex, profile.WithBankBalance(bank));
                entMan.System<BankSystem>().RefreshBalance(bench.Body);
                setSavings = bench._coins.SetMonoCoinsBalanceAsync(player.UserId, savings);
            });
            await setBank;
            await setSavings;
            await pair.RunTicksSync(2);

            return bench;
        }
    }
}
