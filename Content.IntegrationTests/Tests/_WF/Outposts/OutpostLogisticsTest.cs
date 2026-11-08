#nullable enable
using Content.Server._NF.Bank;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Power.Components;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Bank.Events;
using Content.Shared._WF.Outposts;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>The F10.5 logistics extras: the deposit terminal and the broadcast console's distress call.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostLogisticsSystem))]
public sealed class OutpostLogisticsTest
{
    /// <summary>Cash put in the terminal goes to the bank less the 5% fee, and a withdrawal is refused.</summary>
    [Test]
    public async Task DepositTerminalBanksCashAndPaysNothingOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var bank = server.System<BankSystem>();
        var slots = server.System<ItemSlotsSystem>();

        try
        {
            await server.WaitAssertion(() =>
            {
                var terminal = entMan.SpawnEntity("WFOutpostDepositTerminal", new EntityCoordinates(ground, 2.5f, 2.5f));
                entMan.EnsureComponent<BankAccountComponent>(user);
                Assert.That(bank.TryGetBalance(user, out var before), Is.True);

                var cash = entMan.SpawnEntity("SpaceCash1000", new EntityCoordinates(ground, 2.5f, 2.5f));
                Assert.That(slots.TryInsert(terminal, BankATMComponent.CashSlotId, cash, user), Is.True, "The cash didn't go in.");
                entMan.EventBus.RaiseLocalEvent(terminal, new BankDepositMessage { Actor = user, UiKey = BankATMMenuUiKey.ATM });
                Assert.That(bank.TryGetBalance(user, out var after), Is.True);
                // The fee takes 5%; the bank may put part of the rest into savings, as at any ATM.
                Assert.That(after - before, Is.InRange(1, 950), "The deposit, less 5%, reached the bank.");
                Assert.That(entMan.Deleted(cash), "The deposited cash is gone.");

                var withdraw = new BoundUserInterfaceMessageAttempt(user, terminal, BankATMMenuUiKey.ATM, new BankWithdrawMessage(100));
                entMan.EventBus.RaiseLocalEvent(terminal, withdraw);
                Assert.That(withdraw.Cancelled, "The terminal let a withdrawal through.");

                entMan.DeleteEntity(terminal);
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A call for help goes out only while the outpost is raided, and then not again until the cooldown is over.</summary>
    [Test]
    public async Task DistressOnlyUnderRaid()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        var raids = server.System<WFRaidSystem>();
        var maps = server.System<SharedMapSystem>();

        var raid = 0;
        try
        {
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out var reason), Is.True, $"Founding was refused: {reason}.");
                var outpost = grid!.Value;
                var gridComp = entMan.GetComponent<MapGridComponent>(outpost);
                var console = entMan.SpawnEntity("WFOutpostBroadcastConsole", maps.GridTileToLocal(outpost, gridComp, new Vector2i(2, 2)));
                server.System<SharedPowerReceiverSystem>().SetNeedsPower(console, false);
                entMan.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
                var comp = entMan.GetComponent<WFPlanetBroadcastConsoleComponent>(console);

                entMan.EventBus.RaiseLocalEvent(console, new WFPlanetDistressMessage { Actor = user });
                Assert.That(comp.NextDistress, Is.EqualTo(TimeSpan.Zero), "A call went out with no raid.");

                raid = raids.ScheduleRaid(outpost, server.Timing.CurTime + TimeSpan.FromHours(1));
                entMan.EventBus.RaiseLocalEvent(console, new WFPlanetDistressMessage { Actor = user });
                var next = comp.NextDistress;
                Assert.That(next, Is.GreaterThan(server.Timing.CurTime), "The call under raid didn't go out.");

                entMan.EventBus.RaiseLocalEvent(console, new WFPlanetDistressMessage { Actor = user });
                Assert.That(comp.NextDistress, Is.EqualTo(next), "A second call went out inside the cooldown.");
            });
        }
        finally
        {
            await server.WaitPost(() => raids.CancelRaid(raid));
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
