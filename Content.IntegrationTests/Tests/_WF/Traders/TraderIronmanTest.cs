#nullable enable
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Traders;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Traders;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traders;

/// <summary>
/// An Ironman's account is frozen, so a trader can only be paid with the cash on its counter: the
/// shipyard dealer's hosted console counts it as its cash slot, and no trade reaches for the bank.
/// </summary>
[TestFixture]
[TestOf(typeof(TraderShipyardSystem))]
public sealed class TraderIronmanTest
{
    private const string DealerProto = "WFTraderShipyard";
    private const string SalesmanProto = "WFTraderUsedShips";
    private const string TableProto = "Table";
    private const string IdProto = "PassengerIDCard";
    private const string CustomerProto = "MobHuman";
    private const string Cash = "SpaceCash100000";
    private const int CashAmount = 100000;

    /// <summary>
    /// Index of the dealer's first BuyShip option, which fronts the civilian shipyard console.
    /// </summary>
    private const int BuyShipOption = 0;

    /// <summary>
    /// Any stock hull on the civilian listing; these tests are about the money.
    /// </summary>
    private const string VesselProto = "Guppy";

    /// <summary>
    /// What the cash on the counter falls short by, and what a purchase it covers costs.
    /// </summary>
    private const int Shortfall = 1000;
    private const int Price = 40000;

    /// <summary>
    /// The dealer refuses an Ironman with nothing on the counter, then sells for the cash put there:
    /// the price is taken, the rest stays as change and the account is left alone.
    /// </summary>
    [Test]
    public async Task DealerSellsForCounterCash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var traderSys = entMan.System<TraderSystem>();
        var shipyardSys = entMan.System<ShipyardSystem>();
        var uiSys = entMan.System<UserInterfaceSystem>();

        var gridUid = map.Grid.Owner;
        var counter = new EntityCoordinates(gridUid, 0.5f, -0.5f);

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var trader = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var idCard = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            (trader, customer) = SpawnCounter(entMan, map, session, DealerProto);

            // A purchase docks the ship to the dealer's station, syncs its records and files a shuttle record.
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<StationRecordsComponent>(station);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, gridUid);
        });

        await pair.RunTicksSync(5);

        // The customer's own card on the counter gets the civilian listing open.
        await server.WaitPost(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(customer), "The customer should be the player's body.");

            entMan.EnsureComponent<BankAccountComponent>(customer);
            entMan.EnsureComponent<IronmanComponent>(customer);

            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.RefreshTable((trader, comp));
            Assert.That(comp.Table, Is.Not.Null, "The dealer should have found its table.");

            idCard = entMan.SpawnEntity(IdProto, counter);
            entMan.EnsureComponent<IdCardOwnerComponent>(idCard).UserId = session.UserId;

            Assert.That(traderSys.TryStartConversation((trader, comp), customer), Is.True,
                "The dealer should have started talking.");
            SelectOption(entMan, trader, customer, BuyShipOption);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(vessel.Price, Is.InRange(1, CashAmount - 1), $"{VesselProto} should cost less than the cash on hand.");

            Assert.That(entMan.TryGetComponent<ShipyardConsoleComponent>(trader, out var console), Is.True,
                "The dealer should be hosting the listing.");
            Assert.That(console!.TargetIdSlot.Item, Is.EqualTo(idCard), "The customer's card should be in the hosted slot.");
            Assert.That(uiSys.IsUiOpen(trader, ShipyardConsoleUiKey.Shipyard, customer), Is.True,
                "The listing should be open on the customer.");

            var frozen = entMan.GetComponent<BankAccountComponent>(customer).Balance;

            // Nothing on the counter: the account pays for nothing, and the loaded hull is deleted again.
            var ships = entMan.Count<ShuttleComponent>();
            shipyardSys.LastConsolePopup = null;
            Purchase(entMan, trader, customer);

            Assert.That(shipyardSys.LastConsolePopup,
                Is.EqualTo(loc.GetString("cargo-console-insufficient-funds", ("cost", vessel.Price))),
                "The dealer should have refused the sale for want of funds.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False,
                "A frozen balance must not put a deed on the card.");
            Assert.That(entMan.Count<ShuttleComponent>(), Is.EqualTo(ships),
                "The hull loaded for the refused sale should have been deleted.");

            // Cash on the counter is the hosted console's cash slot.
            entMan.SpawnEntity(Cash, counter);
            Assert.That(traderSys.GetZoneCash((trader, comp)), Is.EqualTo(CashAmount), "The cash should be on the counter.");

            shipyardSys.LastConsolePopup = null;
            Purchase(entMan, trader, customer);

            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.True,
                $"The cash covered the ship, but the console said: {shipyardSys.LastConsolePopup}");
            Assert.That(traderSys.GetZoneCash((trader, comp)), Is.EqualTo(CashAmount - vessel.Price),
                "The price should have come off the counter, the rest left as change.");
            Assert.That(entMan.GetComponent<BankAccountComponent>(customer).Balance, Is.EqualTo(frozen),
                "The frozen account must not be touched.");

            traderSys.EndConversation((trader, comp), farewell: false);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The payment every counter trade goes through, the used ship lot included: cash that falls short
    /// is refused and left lying even with the customer's card beside it, and cash that covers the
    /// price is taken with change. The account is never charged.
    /// </summary>
    [Test]
    public async Task IronmanPaysInCashOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var traderSys = entMan.System<TraderSystem>();
        var bankSys = entMan.System<BankSystem>();

        var counter = new EntityCoordinates(map.Grid.Owner, 0.5f, -0.5f);

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var trader = EntityUid.Invalid;
        var customer = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            (trader, customer) = SpawnCounter(entMan, map, session, SalesmanProto);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(customer), "The customer should be the player's body.");

            var comp = entMan.GetComponent<TraderComponent>(trader);
            traderSys.RefreshTable((trader, comp));
            Assert.That(comp.Table, Is.Not.Null, "The salesman should have found its table.");

            // An account that would have covered the shortfall, read before the trait froze it.
            var bank = entMan.EnsureComponent<BankAccountComponent>(customer);
            if (bank.Balance < Shortfall)
                Assert.That(bankSys.TryBankDeposit(customer, Shortfall, tax: false), Is.True, "Could not fund the account.");

            entMan.EnsureComponent<IronmanComponent>(customer);
            var frozen = bank.Balance;
            Assert.That(frozen, Is.GreaterThanOrEqualTo(Shortfall), "The account should cover the shortfall on paper.");

            var idCard = entMan.SpawnEntity(IdProto, counter);
            entMan.EnsureComponent<IdCardOwnerComponent>(idCard).UserId = session.UserId;
            entMan.SpawnEntity(Cash, counter);
            Assert.That(traderSys.GetZoneCash((trader, comp)), Is.EqualTo(CashAmount), "The cash should be on the counter.");

            // Card on the counter and cash a little short: a working account would be charged the rest.
            Assert.That(traderSys.TryTakePayment((trader, comp), customer, CashAmount + Shortfall), Is.False,
                "A frozen account must not make up what the cash is short by.");
            Assert.That(traderSys.GetZoneCash((trader, comp)), Is.EqualTo(CashAmount),
                "A refused payment must leave the cash on the counter.");
            Assert.That(bank.Balance, Is.EqualTo(frozen), "The frozen account must not be touched.");

            // The cash covers it: taken whole, the difference handed back.
            Assert.That(traderSys.TryTakePayment((trader, comp), customer, Price, out var tendered, out var change), Is.True,
                "Cash that covers the price should be accepted.");
            Assert.That(tendered, Is.EqualTo(CashAmount));
            Assert.That(change, Is.EqualTo(CashAmount - Price));
            Assert.That(traderSys.GetZoneCash((trader, comp)), Is.EqualTo(CashAmount - Price),
                "Only the change should be left on the counter.");
            Assert.That(bank.Balance, Is.EqualTo(frozen), "The frozen account must not be touched.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A trader behind its table, and the player's body standing beside it.
    /// </summary>
    private static (EntityUid Trader, EntityUid Customer) SpawnCounter(
        IEntityManager entMan,
        TestMapData map,
        ICommonSession session,
        string traderProto)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var gridUid = map.Grid.Owner;

        mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(0, -1), map.Tile.Tile);
        mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

        var trader = entMan.SpawnEntity(traderProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
        entMan.SpawnEntity(TableProto, new EntityCoordinates(gridUid, 0.5f, -0.5f));

        // A customer with a real session: an ID only counts as theirs against a player.
        mindSys.WipeMind(session.ContentData()?.Mind);
        var customer = entMan.SpawnEntity(CustomerProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));
        var mind = mindSys.CreateMind(session.UserId).Owner;
        mindSys.TransferTo(mind, customer);

        return (trader, customer);
    }

    /// <summary>
    /// Sends the dialogue message a customer's window would send, without a client behind it.
    /// </summary>
    private static void SelectOption(IEntityManager entMan, EntityUid trader, EntityUid customer, int index)
    {
        var msg = new TraderDialogueSelectMessage(index)
        {
            Actor = customer,
            UiKey = TraderUiKey.Dialogue,
        };

        entMan.EventBus.RaiseLocalEvent(trader, msg);
    }

    /// <summary>
    /// Sends the purchase message the hosted listing's window would send, without a client behind it.
    /// </summary>
    private static void Purchase(IEntityManager entMan, EntityUid trader, EntityUid customer)
    {
        var msg = new ShipyardConsolePurchaseMessage(VesselProto)
        {
            Actor = customer,
            UiKey = ShipyardConsoleUiKey.Shipyard,
        };

        entMan.EventBus.RaiseLocalEvent(trader, msg);
    }
}
