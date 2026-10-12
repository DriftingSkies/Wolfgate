#nullable enable
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Shared._Mono.Economy;
using Content.Shared._Mono.Economy.Component;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Stacks;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Shipyard;

/// <summary>
/// An Ironman's account is frozen: a shipyard console sells them a ship for the cash in its slot and
/// for nothing else.
/// </summary>
[TestFixture]
[TestOf(typeof(ShipyardSystem))]
public sealed class IronmanShipPurchaseTest
{
    private const string ConsoleProto = "ComputerShipyard";
    private const string IdProto = "PassengerIDCard";
    private const string CustomerProto = "MobHuman";
    private const string Cash = "SpaceCash100000";
    private const int CashAmount = 100000;

    /// <summary>
    /// Any stock hull on the civilian listing; these tests are about the money.
    /// </summary>
    private const string VesselProto = "Guppy";

    /// <summary>
    /// The balance on an Ironman's account covers the ship on paper, but none of it can be withdrawn:
    /// with no cash in the slot the sale is refused and the loaded hull is deleted again.
    /// </summary>
    [Test]
    public async Task FrozenBalanceBuysNothing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var shipyardSys = entMan.System<ShipyardSystem>();
        var bankSys = entMan.System<BankSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var console = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var idCard = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            (console, customer, idCard) = SpawnShipyard(entMan, map, session);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(customer), "The customer should be the player's body.");
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);

            // The account is read while it still works; the trait arrives afterwards, as it does at spawn.
            var bank = entMan.EnsureComponent<BankAccountComponent>(customer);
            if (bank.Balance < vessel.Price)
                Assert.That(bankSys.TryBankDeposit(customer, vessel.Price, tax: false), Is.True, "Could not fund the account.");

            entMan.EnsureComponent<IronmanComponent>(customer);
            Assert.That(bank.Balance, Is.GreaterThanOrEqualTo(vessel.Price),
                "The frozen balance should cover the ship on paper, or the old check would have refused this sale too.");
            Assert.That(credit.GetCashBalance(console), Is.Zero, "The cash slot should be empty.");

            OpenListing(entMan, console, customer);

            var ships = entMan.Count<ShuttleComponent>();
            shipyardSys.LastConsolePopup = null;
            Purchase(entMan, console, customer);

            Assert.That(shipyardSys.LastConsolePopup,
                Is.EqualTo(loc.GetString("cargo-console-insufficient-funds", ("cost", vessel.Price))),
                "The console should have refused the sale for want of funds.");
            Assert.That(entMan.HasComponent<ShuttleDeedComponent>(idCard), Is.False,
                "A frozen balance must not put a deed on the card.");
            Assert.That(entMan.Count<ShuttleComponent>(), Is.EqualTo(ships),
                "The hull loaded for the refused sale should have been deleted.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Cash in the console's slot buys an Ironman a ship: the price comes out of the stack and the
    /// account is left alone.
    /// </summary>
    [Test]
    public async Task CashInSlotBuys()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var shipyardSys = entMan.System<ShipyardSystem>();
        var credit = entMan.System<SharedCreditReceiverSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var console = EntityUid.Invalid;
        var customer = EntityUid.Invalid;
        var idCard = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            (console, customer, idCard) = SpawnShipyard(entMan, map, session);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(customer), "The customer should be the player's body.");
            var vessel = protoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(vessel.Price, Is.InRange(1, CashAmount - 1), $"{VesselProto} should cost less than the cash on hand.");

            var bank = entMan.EnsureComponent<BankAccountComponent>(customer);
            entMan.EnsureComponent<IronmanComponent>(customer);
            var frozen = bank.Balance;

            var cash = entMan.SpawnEntity(Cash, map.GridCoords);
            var receiver = entMan.GetComponent<CreditReceiverComponent>(console);
            Assert.That(itemSlots.TryInsert(console, receiver.CashSlotName, cash, null), Is.True,
                "The cash should go in the console's slot.");
            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount));

            OpenListing(entMan, console, customer);

            shipyardSys.LastConsolePopup = null;
            Purchase(entMan, console, customer);

            Assert.That(entMan.TryGetComponent<ShuttleDeedComponent>(idCard, out var deed), Is.True,
                $"The cash covered the ship, but the console said: {shipyardSys.LastConsolePopup}");
            Assert.That(entMan.EntityExists(deed!.ShuttleUid), Is.True, "The deed should point at the bought ship.");

            Assert.That(credit.GetCashBalance(console), Is.EqualTo(CashAmount - vessel.Price),
                "The price should have come out of the cash slot.");
            Assert.That(entMan.GetComponent<StackComponent>(cash).Count, Is.EqualTo(CashAmount - vessel.Price));
            Assert.That(bank.Balance, Is.EqualTo(frozen), "The frozen account must not be touched.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A console on a station's grid with a card in its slot, and the player's body standing next to it.
    /// </summary>
    private static (EntityUid Console, EntityUid Customer, EntityUid IdCard) SpawnShipyard(
        IEntityManager entMan,
        TestMapData map,
        ICommonSession session)
    {
        var mapSys = entMan.System<SharedMapSystem>();
        var mindSys = entMan.System<SharedMindSystem>();
        var gridUid = map.Grid.Owner;

        mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

        // A purchase docks the ship to the console's station, syncs its records and files a shuttle record.
        var station = entMan.Spawn();
        entMan.EnsureComponent<StationDataComponent>(station);
        entMan.EnsureComponent<StationRecordsComponent>(station);
        entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
        entMan.System<StationSystem>().AddGridToStation(station, gridUid);

        var console = entMan.SpawnEntity(ConsoleProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));

        // A customer with a real session: the account is read through the player's preferences.
        mindSys.WipeMind(session.ContentData()?.Mind);
        var customer = entMan.SpawnEntity(CustomerProto, new EntityCoordinates(gridUid, 1.5f, 0.5f));
        var mind = mindSys.CreateMind(session.UserId).Owner;
        mindSys.TransferTo(mind, customer);

        var idCard = entMan.SpawnEntity(IdProto, new EntityCoordinates(gridUid, 0.5f, 0.5f));
        Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(console, ShipyardConsoleComponent.TargetIdCardSlotId, idCard, null),
            Is.True, "The card should go in the console's slot.");

        return (console, customer, idCard);
    }

    /// <summary>
    /// Opens the console on the customer; the listing it sells from is the one its open window is keyed to.
    /// </summary>
    private static void OpenListing(IEntityManager entMan, EntityUid console, EntityUid customer)
    {
        Assert.That(entMan.System<UserInterfaceSystem>().TryOpenUi(console, ShipyardConsoleUiKey.Shipyard, customer), Is.True,
            "The customer should be able to open the console.");
    }

    /// <summary>
    /// Sends the purchase message the console's window would send, without a client behind it.
    /// </summary>
    private static void Purchase(IEntityManager entMan, EntityUid console, EntityUid customer)
    {
        var msg = new ShipyardConsolePurchaseMessage(VesselProto)
        {
            Actor = customer,
            UiKey = ShipyardConsoleUiKey.Shipyard,
        };

        entMan.EventBus.RaiseLocalEvent(console, msg);
    }
}
