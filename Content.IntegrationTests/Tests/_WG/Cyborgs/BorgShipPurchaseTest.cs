#nullable enable
using System.Linq;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WG.Traders;
using Content.Shared._WG.Traders;
using Content.Server.Silicons.Borgs;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared._NF.Interaction.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cyborgs;

/// <summary>
/// A cyborg buys a ship with the ID card its tool module issues, and can take the deeded card back.
/// </summary>
[TestFixture]
public sealed class BorgShipPurchaseTest
{
    private const string Borg = "BorgChassisEngineer";
    private const string Cell = "PowerCellHigh";
    private const string Tool = "BorgModuleTool";
    private const string Card = "WGBorgIdCard";
    private const string Console = "ComputerShipyard";
    private const string Vessel = "Guppy";
    private const string Dealer = "WGTraderShipyard";
    private const string Table = "Table";

    /// <summary>Index of the dealer's first BuyShip option.</summary>
    private const int BuyShipOption = 0;

    [Test]
    public async Task BuysWithIssuedCardTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var shipyard = entMan.System<ShipyardSystem>();
        var bank = entMan.System<BankSystem>();
        var borgs = entMan.System<BorgSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var itemSlots = entMan.System<ItemSlotsSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var minds = entMan.System<SharedMindSystem>();

        Assert.That(pair.Client.Session, Is.Not.Null, "This test needs a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        var console = EntityUid.Invalid;
        var borg = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var gridUid = map.Grid.Owner;
            entMan.System<SharedMapSystem>().SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

            // A purchase docks the ship to the console's station and syncs its records.
            var station = entMan.Spawn();
            entMan.EnsureComponent<StationDataComponent>(station);
            entMan.EnsureComponent<StationRecordsComponent>(station);
            entMan.EnsureComponent<StationSectorServiceHostComponent>(station);
            entMan.System<StationSystem>().AddGridToStation(station, gridUid);

            console = entMan.SpawnEntity(Console, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            borg = entMan.SpawnEntity(Borg, new EntityCoordinates(gridUid, 1.5f, 0.5f));

            var cell = entMan.SpawnEntity(Cell, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true);

            // The account is read through the player's preferences, so the cyborg needs a real session.
            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, borg);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(session.AttachedEntity, Is.EqualTo(borg), "The cyborg should be the player's body.");
            var vessel = protoMan.Index<VesselPrototype>(Vessel);
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            borgs.InstallAllModules(borg);
            borgs.UnselectModule(borg);
            borgs.SelectModule(borg, modules.Single(m => Proto(m) == Tool));

            var card = hands.EnumerateHeld(borg).SingleOrDefault(held => Proto(held) == Card);
            Assert.That(card.IsValid(), "The tool module issued no ID card.");

            var account = entMan.GetComponent<BankAccountComponent>(borg);
            if (account.Balance < vessel.Price)
                Assert.That(bank.TryBankDeposit(borg, vessel.Price, tax: false), "Could not fund the cyborg's account.");

            Assert.That(hands.TryDrop(borg, card), "The cyborg could not let go of its card.");
            Assert.That(itemSlots.TryInsert(console, ShipyardConsoleComponent.TargetIdCardSlotId, card, null),
                "The card should go in the console's slot.");

            Assert.That(entMan.System<UserInterfaceSystem>().TryOpenUi(console, ShipyardConsoleUiKey.Shipyard, borg),
                "The cyborg should be able to open the console.");

            shipyard.LastConsolePopup = null;
            entMan.EventBus.RaiseLocalEvent(console, new ShipyardConsolePurchaseMessage(Vessel)
            {
                Actor = borg,
                UiKey = ShipyardConsoleUiKey.Shipyard,
            });

            Assert.That(entMan.TryGetComponent<ShuttleDeedComponent>(card, out var deed),
                $"The cyborg was refused the ship; the console said: {shipyard.LastConsolePopup}");
            Assert.That(entMan.EntityExists(deed!.ShuttleUid), "The deed should point at the bought ship.");

            Assert.That(itemSlots.TryEject(console, ShipyardConsoleComponent.TargetIdCardSlotId, null, out _),
                "The console kept the card.");

            // An emptied gripper hand holds a placeholder, and clicking an item with it picks the item up.
            var interaction = entMan.System<SharedInteractionSystem>();
            var coords = entMan.GetComponent<TransformComponent>(card).Coordinates;
            foreach (var placeholder in hands.EnumerateHeld(borg).Where(entMan.HasComponent<HandPlaceholderComponent>).ToArray())
            {
                interaction.RangedInteractDoBefore(borg, placeholder, card, coords, canReach: true);
            }

            Assert.That(hands.IsHolding(borg, card), "The cyborg could not take its deeded card back.");
        });

        // A client still attached to a cyborg throws while the pair's cleanup deletes it.
        await server.WaitPost(() => minds.WipeMind(session.ContentData()?.Mind));
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }

    /// <summary>A ship dealer takes the card off its counter as the cyborg's own and opens its listing on it.</summary>
    [Test]
    public async Task TraderTakesIssuedCardTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        var entMan = server.EntMan;
        var borgs = entMan.System<BorgSystem>();
        var hands = entMan.System<SharedHandsSystem>();
        var containers = entMan.System<SharedContainerSystem>();
        var minds = entMan.System<SharedMindSystem>();
        var traders = entMan.System<TraderSystem>();

        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);
        var gridUid = map.Grid.Owner;

        var trader = EntityUid.Invalid;
        var borg = EntityUid.Invalid;
        var card = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var mapSys = entMan.System<SharedMapSystem>();
            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(0, -1), map.Tile.Tile);
            mapSys.SetTile(gridUid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);

            entMan.EnsureComponent<StationSectorServiceHostComponent>(entMan.Spawn());

            trader = entMan.SpawnEntity(Dealer, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            entMan.SpawnEntity(Table, new EntityCoordinates(gridUid, 0.5f, -0.5f));
            borg = entMan.SpawnEntity(Borg, new EntityCoordinates(gridUid, 1.5f, 0.5f));

            var cell = entMan.SpawnEntity(Cell, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            containers.Insert(cell, containers.GetContainer(borg, "cell_slot"), force: true);

            minds.WipeMind(session.ContentData()?.Mind);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, borg);
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            string? Proto(EntityUid uid) => entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            var modules = entMan.GetComponent<BorgChassisComponent>(borg).ModuleContainer.ContainedEntities;
            borgs.InstallAllModules(borg);
            borgs.UnselectModule(borg);
            borgs.SelectModule(borg, modules.Single(m => Proto(m) == Tool));

            card = hands.EnumerateHeld(borg).SingleOrDefault(held => Proto(held) == Card);
            Assert.That(card.IsValid(), "The tool module issued no ID card.");

            Assert.That(hands.TryDrop(borg, card), "The cyborg could not let go of its card.");
            entMan.System<SharedTransformSystem>().SetCoordinates(card, new EntityCoordinates(gridUid, 0.5f, -0.5f));
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            traders.RefreshTable((trader, comp));
            Assert.That(traders.TryStartConversation((trader, comp), borg), "The dealer should talk to a cyborg.");
            entMan.EventBus.RaiseLocalEvent(trader, new TraderDialogueSelectMessage(BuyShipOption)
            {
                Actor = borg,
                UiKey = TraderUiKey.Dialogue,
            });
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<TraderComponent>(trader);
            Assert.That(entMan.TryGetComponent<ShipyardConsoleComponent>(trader, out var console),
                $"The dealer refused the cyborg's card; it said: {comp.CurrentLine}");
            Assert.That(console!.TargetIdSlot.Item, Is.EqualTo(card), "The cyborg's card should be in the listing's slot.");

            traders.EndConversation((trader, comp), farewell: false);
        });

        await server.WaitPost(() => minds.WipeMind(session.ContentData()?.Mind));
        await pair.RunTicksSync(5);

        await pair.CleanReturnAsync();
    }
}
