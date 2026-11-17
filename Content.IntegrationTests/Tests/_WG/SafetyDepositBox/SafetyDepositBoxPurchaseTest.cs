#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Mono.MonoCoins;
using Content.Server._WG.SafetyDepositBox;
using Content.Server._WG.ShipCommand;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared._WG.ShipCommand;
using Content.Shared._WG.SafetyDepositBox.Events;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Content.Server._NF.Bank;

namespace Content.IntegrationTests.Tests._WG.SafetyDepositBox;

/// <summary>
/// A purchase charges savings first and then the bank, and the bank component shows the new balance straight away.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxSystem))]
public sealed class SafetyDepositBoxPurchaseTest
{
    private const string BoxProto = "SafetyDepositBoxSmall";
    private const int Cost = 2000000;

    [TestCase(5000000, 0, 3000000, 0, TestName = "BankPaysWhenThereAreNoSavings")]
    [TestCase(1000000, 1500000, 500000, 0, TestName = "SavingsPayFirstAndBankPaysTheRest")]
    [TestCase(5000000, 3000000, 5000000, 1000000, TestName = "SavingsCoverItAndBankIsUntouched")]
    [TestCase(5000000, 0, 4000000, 0, 0.5f, TestName = "ACommandedShipsPriceRuleSetsTheCharge")]
    public async Task PurchaseCharges(int bank, int savings, int bankAfter, int savingsAfter, float multiplier = 1f)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var coins = server.ResolveDependency<MonoCoinsManager>();
        var db = server.ResolveDependency<IServerDbManager>();

        var console = EntityUid.Invalid;
        var body = EntityUid.Invalid;
        var character = 0;
        Task setSavings = Task.CompletedTask;
        Task setBank = Task.CompletedTask;

        await server.WaitPost(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            if (multiplier != 1f)
            {
                // The buyer is the test's admin session, so with admins served free the rule would never apply.
                var command = entMan.EnsureComponent<WGShipCommandComponent>(map.Grid.Owner);
                command.OwnerServedFree = command.AdminsServedFree = false;
                entMan.System<WGShipServiceSystem>().SetRule((map.Grid.Owner, command),
                    new WGShipPriceRule { Name = "rule", Services = WGShipServiceKind.Deposit, Multiplier = multiplier }, null);
            }

            console = entMan.SpawnEntity("SafetyDepositConsole", map.GridCoords);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, body);
            entMan.EnsureComponent<BankAccountComponent>(body);

            var current = prefs.GetPreferences(player.UserId);
            character = current.SelectedCharacterIndex;
            var profile = (HumanoidCharacterProfile) current.SelectedCharacter;
            setBank = prefs.SetProfile(player.UserId, character, profile.WithBankBalance(bank));
            entMan.System<BankSystem>().RefreshBalance(body);
            setSavings = coins.SetMonoCoinsBalanceAsync(player.UserId, savings);
        });
        await setBank;
        await setSavings;
        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            var message = new SafetyDepositPurchaseMessage(BoxProto) { Actor = body };
            entMan.EventBus.RaiseLocalEvent(console, message);
        });

        // The box comes from an async database call, so wait for it before the pair goes back.
        var boxSpawned = false;
        for (var i = 0; i < 100 && !boxSpawned; i++)
        {
            await pair.RunTicksSync(1);
            await server.WaitPost(() =>
            {
                var query = entMan.EntityQueryEnumerator<SafetyDepositBoxComponent>();
                while (query.MoveNext(out var comp))
                {
                    boxSpawned |= comp.OwnerId == player.UserId.UserId;
                }
            });
        }

        Assert.That(boxSpawned, "The purchase didn't hand out a box.");

        // Marking the record withdrawn is the purchase's last database call; it must be done before the record goes.
        var finished = false;
        for (var i = 0; i < 100 && !finished; i++)
        {
            await pair.RunTicksSync(1);
            finished = (await db.GetPlayerSafetyDepositBoxes(player.UserId.UserId, character)).Any(record => record.LastWithdrawn != null);
        }

        Assert.That(finished, "The purchase never finished.");
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var profile = (HumanoidCharacterProfile) prefs.GetPreferences(player.UserId).SelectedCharacter;
            Assert.Multiple(() =>
            {
                Assert.That(profile.BankBalance, Is.EqualTo(bankAfter), "The bank account was charged the wrong amount.");
                Assert.That(entMan.GetComponent<BankAccountComponent>(body).Balance, Is.EqualTo(bankAfter),
                    "The bank component still shows the old balance.");
                Assert.That(coins.GetMonoCoinsBalance(player.UserId), Is.EqualTo(savingsAfter),
                    "Savings were charged the wrong amount.");
                Assert.That(bank + savings - bankAfter - savingsAfter, Is.EqualTo((int) (Cost * multiplier)), "The test's numbers don't add up to the cost.");
            });
        });

        foreach (var record in await db.GetPlayerSafetyDepositBoxes(player.UserId.UserId, character))
        {
            await db.DeleteSafetyDepositBox(record.BoxId);
        }

        await pair.CleanReturnAsync();
    }
}
