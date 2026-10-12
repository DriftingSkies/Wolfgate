#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server._WF.SafetyDepositBox;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.SafetyDepositBox;

/// <summary>
/// A withdrawal hands back all it can and always clears the record: an item the box no longer takes is put down at
/// the box, an item that won't load is skipped, and neither lets the box be withdrawn a second time.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositBoxSystem))]
public sealed class SafetyDepositBoxWithdrawTest
{
    private const string ConsoleProto = "SafetyDepositConsole";
    private const string BoxProto = "WFSafetyDepositWithdrawTestBox";
    private const string PlayerProto = "MobHuman";
    private const string ChangedProto = "Crowbar";
    private const string KeptProto = "Wrench";
    private const string RefusedProto = "WFSafetyDepositWithdrawTestRefused";
    private const string MissingProto = "WFSafetyDepositWithdrawTestRemoved";

    private static readonly string[] ItemProtos = { ChangedProto, KeptProto };

    // The box's blacklist is the test's own. It has no SafetyDepositBox component, which the system adds on
    // withdrawal, so sweeps over the real boxes skip it. StorageSizeArbitrageTest sweeps test prototypes too, so the
    // box is as big as the items it takes.
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFSafetyDepositWithdrawTestBox
  parent: BaseStorageItem
  components:
  - type: Item
    size: Normal
  - type: Storage
    maxItemSize: Normal
    grid:
    - 0,0,3,3
    blacklist:
      tags:
      - SafetyDepositBlacklist
  - type: Tag
    tags:
    - SafetyDepositBox

- type: entity
  id: WFSafetyDepositWithdrawTestRefused
  parent: Crowbar
  components:
  - type: Tag
    tags:
    - SafetyDepositBlacklist
";

    [Test]
    public async Task RefusedItemIsLeftAtTheBox()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair);
        var server = pair.Server;
        var entMan = bench.EntMan;

        // The stored crowbar's prototype is one the box refuses by the time it comes out, as after a port that makes
        // a stored item contraband.
        var boxId = await bench.DepositBox();
        await bench.ChangeSavedProto(boxId, RefusedProto);

        await bench.Send(new SafetyDepositWithdrawMessage(boxId));

        var boxes = await bench.BoxesInWorld(boxId);
        Assert.That(boxes, Has.Count.EqualTo(1), "The withdrawal didn't hand out exactly one box.");
        Assert.That(await bench.ItemCount(boxes[0]), Is.EqualTo(ItemProtos.Length - 1),
            "The item the box still takes didn't come out in it.");

        var left = await bench.StoredItems(RefusedProto);
        Assert.That(left, Has.Count.EqualTo(1), "The item the box refuses was deleted.");

        await server.WaitAssertion(() =>
        {
            var xformSys = entMan.System<SharedTransformSystem>();
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(left[0]), Is.False,
                "The refused item isn't on the floor.");
            Assert.That(xformSys.GetMapCoordinates(left[0]).InRange(xformSys.GetMapCoordinates(boxes[0]), 0.5f), Is.True,
                "The refused item isn't at the box.");
        });

        var after = await bench.Db.GetSafetyDepositBox(boxId);
        Assert.That(after!.Items, Is.Empty, "The database still holds the items of a box that is in the world.");
        Assert.That(after.LastWithdrawn, Is.Not.Null, "The box isn't marked as withdrawn.");

        await bench.Send(new SafetyDepositWithdrawMessage(boxId));
        Assert.That(await bench.BoxesInWorld(boxId), Has.Count.EqualTo(1), "The box came out a second time.");
        Assert.That(await bench.StoredItems(RefusedProto), Has.Count.EqualTo(1), "The refused item came out a second time.");

        await bench.Db.DeleteSafetyDepositBox(boxId);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnloadableItemAllowsNoSecondWithdrawal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var bench = await Bench.Create(pair);

        // The stored crowbar's prototype no longer exists, and it is saved ahead of an item that still loads.
        var boxId = await bench.DepositBox();
        await bench.ChangeSavedProto(boxId, MissingProto);

        // The loader and the system both log the item that can't load as an error, which would fail the test.
        var failureLevel = pair.ServerLogHandler.FailureLevel;
        pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
        try
        {
            await bench.Send(new SafetyDepositWithdrawMessage(boxId));
        }
        finally
        {
            pair.ServerLogHandler.FailureLevel = failureLevel;
        }

        var boxes = await bench.BoxesInWorld(boxId);
        Assert.That(boxes, Has.Count.EqualTo(1), "The withdrawal didn't hand out exactly one box.");
        Assert.That(await bench.ItemCount(boxes[0]), Is.EqualTo(ItemProtos.Length - 1),
            "The item after the broken one didn't come out.");

        var after = await bench.Db.GetSafetyDepositBox(boxId);
        Assert.That(after!.Items, Is.Empty, "The database still holds the items of a box that is in the world.");
        Assert.That(after.LastWithdrawn, Is.Not.Null, "The box isn't marked as withdrawn.");

        await bench.Send(new SafetyDepositWithdrawMessage(boxId));
        Assert.That(await bench.BoxesInWorld(boxId), Has.Count.EqualTo(1), "The box came out a second time.");

        await bench.Db.DeleteSafetyDepositBox(boxId);
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A console, a body the player owns and the calls the tests share.
    /// </summary>
    private sealed class Bench
    {
        public TestPair Pair = default!;
        public IEntityManager EntMan = default!;
        public IServerDbManager Db = default!;
        public EntityCoordinates Coords;
        public MapId TestMap;
        public EntityUid Console;
        public EntityUid Body;
        public Guid UserId;
        public int Character;
        public int Round;

        public static async Task<Bench> Create(TestPair pair)
        {
            var server = pair.Server;
            var map = await pair.CreateTestMap();
            var player = pair.Player!;

            var bench = new Bench
            {
                Pair = pair,
                EntMan = server.EntMan,
                Db = server.ResolveDependency<IServerDbManager>(),
                Coords = map.GridCoords,
                TestMap = map.MapId,
                UserId = player.UserId.UserId,
            };

            await server.WaitAssertion(() =>
            {
                var entMan = bench.EntMan;
                var mindSys = entMan.System<SharedMindSystem>();

                bench.Console = entMan.SpawnEntity(ConsoleProto, bench.Coords);
                bench.Body = entMan.SpawnEntity(PlayerProto, bench.Coords);
                mindSys.TransferTo(mindSys.CreateMind(player.UserId).Owner, bench.Body);
                Assert.That(player.AttachedEntity, Is.EqualTo(bench.Body), "The player didn't get the body.");

                bench.Character = server.ResolveDependency<IServerPreferencesManager>()
                    .GetPreferences(player.UserId)
                    .SelectedCharacterIndex;
                bench.Round = entMan.System<GameTicker>().RoundId;
            });

            return bench;
        }

        /// <summary>
        /// Sends a console message from the player and lets it finish.
        /// </summary>
        public async Task Send<T>(T message) where T : BoundUserInterfaceMessage
        {
            await Pair.Server.WaitPost(() =>
            {
                message.Actor = Body;
                EntMan.EventBus.RaiseLocalEvent(Console, message);
            });

            await Pair.RunTicksSync(5);
        }

        /// <summary>
        /// Buys a box, fills it and deposits it through the console, so the saved data is what the game writes.
        /// </summary>
        public async Task<Guid> DepositBox()
        {
            var record = await Db.PurchaseSafetyDepositBox(UserId, Character, "Tester", BoxProto);
            var boxId = record.BoxId;
            await Db.ClearSafetyDepositBoxItems(boxId, Round);

            await Pair.Server.WaitAssertion(() =>
            {
                var box = EntMan.SpawnEntity(BoxProto, Coords);
                var comp = EntMan.EnsureComponent<SafetyDepositBoxComponent>(box);
                comp.BoxId = boxId;
                comp.OwnerId = UserId;
                comp.CharacterIndex = Character;

                var storage = EntMan.System<SharedStorageSystem>();
                foreach (var proto in ItemProtos)
                {
                    var item = EntMan.SpawnEntity(proto, Coords);
                    Assert.That(storage.Insert(box, item, out _, playSound: false), Is.True,
                        $"{proto} doesn't go in {BoxProto}; pick an item that does.");
                }

                var console = EntMan.GetComponent<SafetyDepositConsoleComponent>(Console);
                Assert.That(EntMan.System<ItemSlotsSystem>().TryInsert(Console, console.BoxSlot, box, null), Is.True,
                    "The console didn't take the box.");
            });

            await Send(new SafetyDepositDepositMessage());

            var saved = await Db.GetSafetyDepositBox(boxId);
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.Items, Has.Count.EqualTo(ItemProtos.Length), "The deposit didn't save the box's items.");
            Assert.That(saved.LastWithdrawn, Is.Null, "The deposit didn't mark the box as stored.");
            Assert.That(await BoxesInWorld(boxId), Is.Empty, "The deposited box is still in the world.");

            return boxId;
        }

        /// <summary>
        /// Rewrites the saved crowbar to another prototype and moves it ahead of the other items.
        /// </summary>
        public async Task ChangeSavedProto(Guid boxId, string proto)
        {
            var saved = await Db.GetSafetyDepositBox(boxId);
            var data = saved!.Items.Select(item => item.EntityData).ToList();

            var index = data.FindIndex(entry => entry.Contains($"proto: {ChangedProto}"));
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "The saved item data no longer names its prototype as 'proto: X'.");

            var changed = data[index].Replace($"proto: {ChangedProto}", $"proto: {proto}");
            data.RemoveAt(index);
            data.Insert(0, changed);
            await Db.DepositSafetyDepositBoxItems(boxId, data);
        }

        public async Task<List<EntityUid>> BoxesInWorld(Guid boxId)
        {
            var found = new List<EntityUid>();
            await Pair.Server.WaitPost(() =>
            {
                var query = EntMan.AllEntityQueryEnumerator<SafetyDepositBoxComponent>();
                while (query.MoveNext(out var uid, out var comp))
                {
                    if (comp.BoxId == boxId)
                        found.Add(uid);
                }
            });

            return found;
        }

        public async Task<int> ItemCount(EntityUid box)
        {
            var count = 0;
            await Pair.Server.WaitPost(() => count = EntMan.GetComponent<StorageComponent>(box).Container.ContainedEntities.Count);
            return count;
        }

        /// <summary>
        /// The items of a prototype on the test map that came out of a box.
        /// </summary>
        public async Task<List<EntityUid>> StoredItems(string proto)
        {
            var found = new List<EntityUid>();
            await Pair.Server.WaitPost(() =>
            {
                var query = EntMan.AllEntityQueryEnumerator<SafetyDepositStoredComponent, MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == proto && xform.MapID == TestMap)
                        found.Add(uid);
                }
            });

            return found;
        }
    }
}
