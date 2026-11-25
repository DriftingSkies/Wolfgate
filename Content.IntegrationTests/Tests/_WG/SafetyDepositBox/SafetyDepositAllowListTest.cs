#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WG.SafetyDepositBox;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.SafetyDepositBox;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared._WG.SafetyDepositBox.Events;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.SafetyDepositBox;

/// <summary>
/// The allow list lets named suits and thalers past the restricted list, and an allowed suit is stored with its
/// helmet but nothing in its pockets.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositAllowListSystem))]
public sealed class SafetyDepositAllowListTest
{
    private const string BoxProto = "SafetyDepositBoxLarge";
    private const string SuitProto = "ClothingOuterHardsuitM82c";
    private const string TankProto = "EmergencyOxygenTankFilled";

    [TestCase("ClothingOuterHardsuitM82c", true, TestName = "AnAllowedFactionSuitGoesIn")]
    [TestCase("ClothingOuterHardsuitUsspL10", true, TestName = "AnAllowedSuitWithAHelmetGoesIn")]
    [TestCase("ClothingOuterArmorBasic", true, TestName = "AHugeArmourVestGoesIn")]
    [TestCase("SpaceCash1000", true, TestName = "ThalersGoIn")]
    [TestCase("TreasureCoinGold", true, TestName = "CoinsGoIn")]
    [TestCase("ClothingOuterHardsuitM86", false, TestName = "AHigherTierFactionSuitStaysOut")]
    [TestCase("FederationMilitaryCredit", false, TestName = "FactionScripStaysOut")]
    [TestCase("ClothingBackpack", false, TestName = "ABagStaysOut")]
    [TestCase("PassengerIDCard", false, TestName = "TheBlacklistStillWins")]
    public async Task BoxTakes(string proto, bool takes)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var item = entMan.SpawnEntity(proto, map.GridCoords);

            Assert.That(entMan.System<SharedStorageSystem>().CanInsert(box, item, out var reason), Is.EqualTo(takes),
                $"{BoxProto} {(takes ? "refused" : "took")} {proto} ({reason}).");

            entMan.DeleteEntity(item);
            entMan.DeleteEntity(box);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AllowedSuitIsStoredWithItsHelmetOnly()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var db = server.ResolveDependency<IServerDbManager>();
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        var userId = player.UserId.UserId;

        var console = EntityUid.Invalid;
        var body = EntityUid.Invalid;
        var tank = EntityUid.Invalid;
        var character = 0;
        var round = 0;

        await server.WaitAssertion(() =>
        {
            var mindSys = entMan.System<SharedMindSystem>();
            console = entMan.SpawnEntity("SafetyDepositConsole", map.GridCoords);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, body);
            character = server.ResolveDependency<IServerPreferencesManager>().GetPreferences(player.UserId).SelectedCharacterIndex;
            round = entMan.System<GameTicker>().RoundId;
        });

        var record = await db.PurchaseSafetyDepositBox(userId, character, "Tester", BoxProto);
        await db.ClearSafetyDepositBoxItems(record.BoxId, round);

        await server.WaitAssertion(() =>
        {
            var storage = entMan.System<SharedStorageSystem>();
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var comp = entMan.EnsureComponent<SafetyDepositBoxComponent>(box);
            comp.BoxId = record.BoxId;
            comp.OwnerId = userId;
            comp.CharacterIndex = character;

            var suit = entMan.SpawnEntity(SuitProto, map.GridCoords);
            Assert.That(entMan.GetComponent<ToggleableClothingComponent>(suit).ClothingUids, Is.Not.Empty,
                "The suit spawned without its helmet.");

            tank = entMan.SpawnEntity(TankProto, map.GridCoords);
            Assert.That(storage.Insert(suit, tank, out _, playSound: false), "The tank didn't go in the suit's storage.");
            Assert.That(storage.Insert(box, suit, out _, playSound: false), "The box didn't take the suit.");

            var slot = entMan.GetComponent<SafetyDepositConsoleComponent>(console).BoxSlot;
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(console, slot, box, null), "The console didn't take the box.");
        });

        await Send(pair, console, body, new SafetyDepositDepositMessage());

        var saved = await db.GetSafetyDepositBox(record.BoxId);
        Assert.That(saved!.Items, Has.Count.EqualTo(1), "The deposit didn't save the suit.");

        // The deposited box is deleted with all it holds, so a tank left in the suit would be gone.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(tank) && !entMan.IsQueuedForDeletion(tank),
                "The tank in the suit's storage wasn't handed back.");
        });

        await Send(pair, console, body, new SafetyDepositWithdrawMessage(record.BoxId));

        await server.WaitAssertion(() =>
        {
            var suits = new List<EntityUid>();
            var query = entMan.AllEntityQueryEnumerator<SafetyDepositStoredComponent, MetaDataComponent>();
            while (query.MoveNext(out var uid, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == SuitProto)
                    suits.Add(uid);
            }

            Assert.That(suits, Has.Count.EqualTo(1), "The suit didn't come back out exactly once.");
            var toggle = entMan.GetComponent<ToggleableClothingComponent>(suits[0]);
            Assert.That(toggle.ClothingUids, Is.Not.Empty, "The withdrawn suit lost track of its helmet.");
            Assert.That(toggle.ActionEntity is { } action && entMan.EntityExists(action),
                "The withdrawn suit has no helmet toggle action.");
            var containers = entMan.System<SharedContainerSystem>();
            Assert.That(toggle.ClothingUids.Keys.All(part =>
                    containers.TryGetContainingContainer(part, out var held) && held.Owner == suits[0] && held.ID == toggle.ContainerId),
                "The withdrawn suit's helmet isn't in it.");
            Assert.That(entMan.GetComponent<StorageComponent>(suits[0]).Container.ContainedEntities, Is.Empty,
                "Something came back in the suit's storage.");
        });

        await db.DeleteSafetyDepositBox(record.BoxId);
        await pair.CleanReturnAsync();
    }

    private static async Task Send<T>(TestPair pair, EntityUid console, EntityUid body, T message) where T : BoundUserInterfaceMessage
    {
        await pair.Server.WaitPost(() =>
        {
            message.Actor = body;
            pair.Server.EntMan.EventBus.RaiseLocalEvent(console, message);
        });

        await pair.RunTicksSync(5);
    }
}
