#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.Server._NF.Bank;
using Content.Server._WG.ScratchTickets;
using Content.Server._WG.Traders;
using Content.Shared.Cargo.Components;
using Content.Server.Power.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WG.ScratchTickets;
using Content.Shared._WG.Traders;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.ScratchTickets;

/// <summary>
/// A spawned ticket is rolled, and an ATM pays a scratched winner into the bank and nothing else.
/// </summary>
[TestFixture]
[TestOf(typeof(WGScratchTicketSystem))]
public sealed class WGScratchTicketTest
{
    private const string TicketProto = "WGScratchTicket";
    private const int Balance = 3000;

    private static readonly string[] Tickets = { TicketProto, "WGScratchTicketFoundry", "WGScratchTicketTycoon" };
    private static readonly int[] Prices = { 500, 2500, 10000 };

    /// <summary>
    /// No tier pays back more than it costs on average, and each dearer tier wins less often for a bigger top prize.
    /// </summary>
    [Test]
    public async Task DearerTiersRiskMoreForMore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var lastChance = 1f;
            var lastTop = 0;

            for (var i = 0; i < Tickets.Length; i++)
            {
                var uid = entMan.SpawnEntity(Tickets[i], map.GridCoords);
                var ticket = entMan.GetComponent<WGScratchTicketComponent>(uid);
                var total = ticket.LoseWeight + ticket.Symbols.Sum(symbol => symbol.Weight);
                var chance = ticket.Symbols.Sum(symbol => symbol.Weight) / total;
                var payback = ticket.Symbols.Sum(symbol => symbol.Weight * symbol.Prize) / total;
                var top = ticket.Symbols.Max(symbol => symbol.Prize);

                Assert.Multiple(() =>
                {
                    Assert.That(entMan.GetComponent<StaticPriceComponent>(uid).VendPrice, Is.EqualTo(Prices[i]));
                    Assert.That(payback / Prices[i], Is.InRange(0.5f, 0.8f), $"{Tickets[i]} pays back the wrong share of its price.");
                    Assert.That(chance, Is.LessThan(lastChance), $"{Tickets[i]} wins as often as a cheaper ticket.");
                    Assert.That(top, Is.GreaterThan(lastTop), $"{Tickets[i]} has no bigger prize than a cheaper ticket.");
                    Assert.That(ticket.Symbols, Has.Count.GreaterThanOrEqualTo(5), "Too few symbols to fill a losing ticket.");
                });

                lastChance = chance;
                lastTop = top;
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnedTicketsAreRolled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 300; i++)
            {
                var ticket = entMan.GetComponent<WGScratchTicketComponent>(entMan.SpawnEntity(Tickets[i % Tickets.Length], map.GridCoords));
                var winner = WGScratchTicketRules.Winner(ticket.Format, ticket.Cells);

                Assert.Multiple(() =>
                {
                    Assert.That(ticket.Cells, Has.Count.EqualTo(WGScratchTicketRules.CellCount(ticket.Format)));
                    Assert.That(ticket.Scratched, Is.False);
                    Assert.That(ticket.Prize, Is.EqualTo(winner < 0 ? 0 : ticket.Symbols[winner].Prize));
                });
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SellerSellsTicketsAtTheirVendPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var seller = EntityUid.Invalid;

        await server.WaitPost(() => seller = entMan.SpawnEntity("WGScratchTicketSeller", map.GridCoords));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var stock = entMan.System<TraderShopSystem>().GetStock((seller, entMan.GetComponent<TraderShopComponent>(seller)));
            Assert.Multiple(() =>
            {
                // Each ticket singly and in packs of 10 and 20, at 10 and 20 times the price.
                var packs = new[] { 1, 10, 20 };
                Assert.That(stock.Select(entry => entry.Item),
                    Is.EquivalentTo(Tickets.SelectMany(ticket => packs.Select(pack => pack == 1 ? ticket : ticket + pack))));
                Assert.That(stock.Select(entry => entry.Price),
                    Is.EquivalentTo(Prices.SelectMany(price => packs.Select(pack => price * pack))));
                Assert.That(entMan.GetComponent<HumanoidAppearanceComponent>(seller).Species.Id, Is.EqualTo("Reptilian"));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Using a pack pulls one ticket into the other hand, and a ticket that has been scratched no longer stacks.
    /// </summary>
    [Test]
    public async Task UsingAPilePullsOneTicket()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var hands = entMan.System<SharedHandsSystem>();
        var stacks = entMan.System<SharedStackSystem>();
        var body = EntityUid.Invalid;
        var pile = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            pile = entMan.SpawnEntity(TicketProto + "20", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(body, pile, checkActionBlocker: false));
            Assert.That(entMan.System<SharedInteractionSystem>().UseInHandInteraction(body, pile, checkCanUse: false, checkCanInteract: false));
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var pulled = hands.EnumerateHeld(body).Where(held => held != pile).ToList();
            Assert.That(stacks.GetCount(pile), Is.EqualTo(19));
            Assert.That(pulled, Has.Count.EqualTo(1), "No ticket came out of the pile.");
            Assert.That(stacks.GetCount(pulled[0]), Is.EqualTo(1));
            Assert.That(entMan.GetComponent<WGScratchTicketComponent>(pulled[0]).Cells, Is.Not.Empty);

            entMan.System<WGScratchTicketSystem>().Reveal((pulled[0], entMan.GetComponent<WGScratchTicketComponent>(pulled[0])), body);
            Assert.That(entMan.HasComponent<StackComponent>(pulled[0]), Is.False, "A scratched ticket can go back in a pile.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AtmPaysOnlyAScratchedWinner()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var tickets = entMan.System<WGScratchTicketSystem>();

        var atm = EntityUid.Invalid;
        var body = EntityUid.Invalid;
        var loser = EntityUid.Invalid;
        var winner = EntityUid.Invalid;
        Task setBank = Task.CompletedTask;

        await server.WaitPost(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            atm = entMan.SpawnEntity("ComputerBankATM", map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(atm, false);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, body);
            entMan.EnsureComponent<BankAccountComponent>(body);

            var current = prefs.GetPreferences(player.UserId);
            var profile = (HumanoidCharacterProfile) current.SelectedCharacter;
            setBank = prefs.SetProfile(player.UserId, current.SelectedCharacterIndex, profile.WithBankBalance(Balance));
            entMan.System<BankSystem>().RefreshBalance(body);

            loser = entMan.SpawnEntity(TicketProto, map.GridCoords);
            entMan.GetComponent<WGScratchTicketComponent>(loser).Prize = 0;
            winner = entMan.SpawnEntity(TicketProto, map.GridCoords);
            entMan.GetComponent<WGScratchTicketComponent>(winner).Prize = 1000;
        });
        await setBank;
        await pair.RunTicksSync(5);
        // The power net only updates every half second, so the ATM may not count as powered yet.
        await PoolManager.WaitUntil(server, () => entMan.GetComponent<ApcPowerReceiverComponent>(atm).Powered);

        void Use(EntityUid ticket)
        {
            entMan.EventBus.RaiseLocalEvent(atm, new InteractUsingEvent(body, ticket, atm, map.GridCoords));
        }

        int Bank()
        {
            return ((HumanoidCharacterProfile) prefs.GetPreferences(player.UserId).SelectedCharacter).BankBalance;
        }

        await server.WaitPost(() => Use(winner));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(winner), Is.False, "The ATM took a ticket with its foil on.");
            Assert.That(Bank(), Is.EqualTo(Balance), "An unscratched ticket paid out.");
        });

        await server.WaitPost(() =>
        {
            tickets.Reveal((loser, entMan.GetComponent<WGScratchTicketComponent>(loser)), body);
            tickets.Reveal((winner, entMan.GetComponent<WGScratchTicketComponent>(winner)), body);
            Use(loser);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(loser), Is.False, "The ATM took a losing ticket.");
            Assert.That(Bank(), Is.EqualTo(Balance), "A losing ticket paid out.");
        });

        // Two uses in one tick, before the redeemed ticket is gone, pay once.
        await server.WaitPost(() =>
        {
            Use(winner);
            Use(winner);
        });
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.Deleted(winner), "The ATM kept the winning ticket.");
            Assert.That(Bank(), Is.EqualTo(Balance + 1000), "The prize didn't reach the bank.");
            Assert.That(entMan.GetComponent<BankAccountComponent>(body).Balance, Is.EqualTo(Balance + 1000));
        });

        await pair.CleanReturnAsync();
    }
}
