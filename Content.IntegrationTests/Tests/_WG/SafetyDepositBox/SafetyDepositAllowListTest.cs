#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WG.SafetyDepositBox;
using Content.Shared._WG.SafetyDepositBox.Components;
using Content.Shared._WG.SafetyDepositBox.Events;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mind;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.SafetyDepositBox;

/// <summary>
/// A box takes everyday gear, bags included, but holds everything inside to the same rules: restricted items only
/// through the allow list, reagents only of food and drink or as the item spawns with them, and the blacklist always.
/// </summary>
[TestFixture]
[TestOf(typeof(SafetyDepositAllowListSystem))]
public sealed class SafetyDepositAllowListTest
{
    private const string BoxProto = "SafetyDepositBoxLarge";
    private const string SuitProto = "ClothingOuterHardsuitM82c";
    private const string TankProto = "EmergencyOxygenTankFilled";
    private const string BagProto = "BoxCardboard";
    private const string KeptProto = "Screwdriver";
    private const string RefusedProto = "PassengerIDCard";
    private const string LimitedProto = "AdvancedCapacitorStockPart";

    [TestCase("ClothingOuterHardsuitM82c", true, TestName = "AnAllowedFactionSuitGoesIn")]
    [TestCase("ClothingOuterHardsuitUsspL10", true, TestName = "AnAllowedSuitWithAHelmetGoesIn")]
    [TestCase("ClothingOuterArmorBasic", true, TestName = "AHugeArmourVestGoesIn")]
    [TestCase("SpaceCash1000", true, TestName = "ThalersGoIn")]
    [TestCase("TreasureCoinGold", true, TestName = "CoinsGoIn")]
    [TestCase("FoodDonkpocket", true, TestName = "FoodGoesIn")]
    [TestCase("DrinkColaCan", true, TestName = "ADrinkGoesIn")]
    [TestCase("CigPackGreen", true, TestName = "CigarettesGoIn")]
    [TestCase("Welder", true, TestName = "AFuelledToolGoesIn")]
    [TestCase("PowerDrill", true, TestName = "APoweredToolGoesIn")]
    [TestCase("WeaponPistolMk58", true, TestName = "AGunGoesIn")]
    [TestCase("WeaponLaserCarbine", true, TestName = "AnEnergyGunGoesIn")]
    [TestCase("WeaponRifleLecter", true, TestName = "ATier1FactionGunGoesIn")]
    [TestCase("WeaponRifleAnnie", true, TestName = "ATier2FactionGunGoesIn")]
    [TestCase("EnergySword", true, TestName = "AFactionMeleeWeaponGoesIn")]
    [TestCase("WeaponHFKatana", true, TestName = "AHighFrequencyBladeGoesIn")]
    [TestCase("ClothingShoesBootsMagNfsd", true, TestName = "Tier1FactionGearGoesIn")]
    [TestCase("PowerCellMedium", true, TestName = "APowerCellGoesIn")]
    [TestCase("BoxCardboard", true, TestName = "ABoxGoesIn")]
    [TestCase("MedkitFilled", true, TestName = "AFilledMedkitGoesIn")]
    [TestCase("ClothingBeltUtilityFilled", true, TestName = "AToolBeltGoesIn")]
    [TestCase("ClothingBackpack", true, TestName = "ABackpackGoesIn")]
    [TestCase("ClothingBackpackDuffel", true, TestName = "ADuffelBagGoesIn")]
    [TestCase("PillDexalin", true, TestName = "APillGoesIn")]
    [TestCase("ClothingHeadsetGrey", true, TestName = "AHeadsetGoesIn")]
    [TestCase("CapacitorStockPart", true, TestName = "ATier1StockPartGoesIn")]
    [TestCase("AdvancedCapacitorStockPart", true, TestName = "ATier2StockPartGoesIn")]
    [TestCase("ClothingOuterHardsuitM86", false, TestName = "ATier3FactionSuitStaysOut")]
    [TestCase("WeaponPulseRifle", true, TestName = "ATier3FactionGunGoesIn")]
    [TestCase("WeaponEnergyShotgunOverseer", false, TestName = "ATaggedGunStaysOut")]
    [TestCase("WeaponRifleXlr556", true, TestName = "AResearchGunGoesIn")]
    [TestCase("WeaponDEWSmilodon", true, TestName = "TheSmilodonGoesIn")]
    [TestCase("UllmanWeaponPulseRifle", true, TestName = "AnUllmanPulseRifleGoesIn")]
    [TestCase("WeaponLMGPKMS", true, TestName = "AMachineGunGoesIn")]
    [TestCase("WeaponSniperHristovEmpty", true, TestName = "TheHristovGoesIn")]
    [TestCase("WeaponMurasama", true, TestName = "ATopTierBladeGoesIn")]
    [TestCase("WeaponLauncherTSFAnubis", false, TestName = "AVariantOfARefusedLauncherStaysOut")]
    [TestCase("WeaponLauncherPanzero", false, TestName = "ARocketLauncherStaysOut")]
    [TestCase("WeaponWandFireball", false, TestName = "AWandStaysOut")]
    [TestCase("SuperCapacitorStockPart", false, TestName = "ATier3StockPartStaysOut")]
    [TestCase("CapacitorEconomy2", false, TestName = "AnIndustrialCapacitorStaysOut")]
    [TestCase("AlertsComputerCircuitboard", false, TestName = "AComputerBoardStaysOut")]
    [TestCase("BorgModuleTool", false, TestName = "ACyborgModuleStaysOut")]
    [TestCase("Emag", false, TestName = "AnEmagStaysOut")]
    [TestCase("FederationMilitaryCredit", false, TestName = "FactionScripStaysOut")]
    [TestCase("ClothingBackpackHolding", false, TestName = "ABagOfHoldingStaysOut")]
    [TestCase("ClothingBackpackMessengerHolding", false, TestName = "AVariantOfABagOfHoldingStaysOut")]
    [TestCase("OreBagOfHolding", false, TestName = "AnOreBagOfHoldingStaysOut")]
    [TestCase("PassengerIDCard", false, TestName = "TheBlacklistStillWins")]
    [TestCase("Implanter", false, TestName = "ImplantersStayOut")]
    [TestCase("PlantAnalyzer", false, TestName = "APlantAnalyzerStaysOut")]
    [TestCase("TomatoSeeds", false, TestName = "SeedsStayOut")]
    [TestCase("MobMouse", false, TestName = "AMouseStaysOut")]
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

    [TestCase(null, true, TestName = "AnEmptyBeakerGoesIn")]
    [TestCase("Milk", true, TestName = "ABeakerOfMilkGoesIn")]
    [TestCase("Letoferol", false, TestName = "ABeakerOfLetoferolStaysOut")]
    [TestCase("Bicaridine", true, TestName = "ABeakerOfMedicineGoesIn")]
    public async Task BoxTakesReagents(string? reagent, bool takes)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var beaker = entMan.SpawnEntity("Beaker", map.GridCoords);

            if (reagent != null)
            {
                var solutions = entMan.System<SharedSolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(beaker, "beaker", out var solution), "The beaker has no solution.");
                Assert.That(solutions.TryAddReagent(solution!.Value, reagent, 10, out _), $"Couldn't put {reagent} in the beaker.");
            }

            Assert.That(entMan.System<SharedStorageSystem>().CanInsert(box, beaker, out var reason), Is.EqualTo(takes),
                $"{BoxProto} {(takes ? "refused" : "took")} a beaker of {reagent ?? "nothing"} ({reason}).");

            entMan.DeleteEntity(beaker);
            entMan.DeleteEntity(box);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A limited allowance and a reagent limit count everything in the box, what is in its bags included.
    /// </summary>
    [Test]
    public async Task LimitsCountAcrossTheBox()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var storage = entMan.System<SharedStorageSystem>();
            var stacks = entMan.System<SharedStackSystem>();
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var allowList = entMan.GetComponent<SafetyDepositAllowListComponent>(box);
            var limit = allowList.Allowances.Single(a => a.Limit != null).Limit!.Value;

            var bag = entMan.SpawnEntity(BagProto, map.GridCoords);
            Assert.That(storage.Insert(bag, entMan.SpawnEntity(LimitedProto, map.GridCoords), out _, playSound: false),
                $"{LimitedProto} didn't go in the {BagProto}.");
            Assert.That(storage.Insert(box, bag, out _, playSound: false), $"The box didn't take the {BagProto}.");

            var parts = entMan.SpawnEntity(LimitedProto, map.GridCoords);
            stacks.SetCount(parts, limit);
            Assert.That(entMan.GetComponent<StackComponent>(parts).Count, Is.EqualTo(limit), $"A {LimitedProto} stack can't hold {limit}.");
            Assert.That(storage.CanInsert(box, parts, out _), Is.False,
                $"The box took a stack of {limit} {LimitedProto} besides the one in the {BagProto}.");
            stacks.SetCount(parts, limit - 1);
            Assert.That(storage.Insert(box, parts, out _, playSound: false),
                $"The box refused a stack of {limit - 1} {LimitedProto} besides the one in the {BagProto}.");

            var cap = allowList.ReagentLimits["Medicine"];
            var full = (int) (cap.Float() / 100);
            for (var i = 0; i <= full; i++)
            {
                var beaker = entMan.SpawnEntity("LargeBeaker", map.GridCoords);
                Assert.That(solutions.TryGetSolution(beaker, "beaker", out var solution), "The beaker has no solution.");
                Assert.That(solutions.TryAddReagent(solution!.Value, "Bicaridine", 100, out _), "Couldn't fill the beaker.");
                Assert.That(storage.Insert(box, beaker, out _, playSound: false), Is.EqualTo(i < full),
                    $"With a limit of {cap}u, the box {(i < full ? "refused" : "took")} {(i + 1) * 100}u of medicine.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// On deposit, what goes over a limit comes out of the bags, a single item whole and a stack split, and what was
    /// stored directly stays.
    /// </summary>
    [Test]
    public async Task WhatGoesOverALimitComesOutOfTheBags()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var storage = entMan.System<SharedStorageSystem>();
            var stacks = entMan.System<SharedStackSystem>();
            var allowLists = entMan.System<SafetyDepositAllowListSystem>();
            var box = entMan.SpawnEntity(BoxProto, map.GridCoords);
            var allowList = entMan.GetComponent<SafetyDepositAllowListComponent>(box);
            var limit = allowList.Allowances.Single(a => a.Limit != null).Limit!.Value;

            var bag = entMan.SpawnEntity(BagProto, map.GridCoords);
            Assert.That(storage.Insert(box, bag, out _, playSound: false), $"The box didn't take the {BagProto}.");
            var stored = entMan.SpawnEntity(LimitedProto, map.GridCoords);
            stacks.SetCount(stored, limit - 1);
            Assert.That(storage.Insert(box, stored, out _, playSound: false), $"The box refused {limit - 1} {LimitedProto}.");

            // Put in the bag once it is in the box, which the box's own check doesn't see.
            var tucked = entMan.SpawnEntity(LimitedProto, map.GridCoords);
            stacks.SetCount(tucked, 3);
            Assert.That(storage.Insert(bag, tucked, out _, playSound: false), $"{LimitedProto} didn't go in the {BagProto}.");

            var refused = new List<EntityUid>();
            var over = new Dictionary<EntityUid, int>();
            allowLists.FindRefused((box, entMan.GetComponent<StorageComponent>(box), allowList), refused, over);
            Assert.That(refused, Is.Empty, "Nothing should come out whole while the stack in the bag has room for one.");
            Assert.That(over, Is.EqualTo(new Dictionary<EntityUid, int> { [tucked] = 2 }),
                $"Two of the {LimitedProto} in the {BagProto} should be split off, and none of the stored stack.");

            stacks.SetCount(stored, limit);
            refused.Clear();
            over.Clear();
            allowLists.FindRefused((box, entMan.GetComponent<StorageComponent>(box), allowList), refused, over);
            Assert.That(refused, Is.EqualTo(new[] { tucked }), $"The {LimitedProto} in the {BagProto} should come out whole.");
            Assert.That(over, Is.Empty, "Nothing should be split off a stack that fits.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A suit keeps its helmet, toggle and what is in its pocket; a refused item inside a bag is handed back, a stack
    /// over a limit is split, and the rest of the bag is stored.
    /// </summary>
    [Test]
    public async Task ContentsAreHeldToTheSameRules()
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
        var card = EntityUid.Invalid;
        var limit = 0;
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
            var tank = entMan.SpawnEntity(TankProto, map.GridCoords);
            Assert.That(storage.Insert(suit, tank, out _, playSound: false), "The tank didn't go in the suit's storage.");
            Assert.That(storage.Insert(box, suit, out _, playSound: false), "The box didn't take the suit.");

            var bag = entMan.SpawnEntity(BagProto, map.GridCoords);
            card = entMan.SpawnEntity(RefusedProto, map.GridCoords);
            Assert.That(storage.Insert(bag, entMan.SpawnEntity(KeptProto, map.GridCoords), out _, playSound: false),
                $"{KeptProto} didn't go in the {BagProto}.");
            Assert.That(storage.Insert(bag, card, out _, playSound: false), $"{RefusedProto} didn't go in the {BagProto}.");
            limit = entMan.GetComponent<SafetyDepositAllowListComponent>(box).Allowances.Single(a => a.Limit != null).Limit!.Value;
            var parts = entMan.SpawnEntity(LimitedProto, map.GridCoords);
            entMan.System<SharedStackSystem>().SetCount(parts, limit + 2);
            Assert.That(storage.Insert(bag, parts, out _, playSound: false), $"{LimitedProto} didn't go in the {BagProto}.");
            Assert.That(storage.Insert(box, bag, out _, playSound: false), $"The box didn't take the {BagProto}.");

            var slot = entMan.GetComponent<SafetyDepositConsoleComponent>(console).BoxSlot;
            Assert.That(entMan.System<ItemSlotsSystem>().TryInsert(console, slot, box, null), "The console didn't take the box.");
        });

        await Send(pair, console, body, new SafetyDepositDepositMessage());

        var saved = await db.GetSafetyDepositBox(record.BoxId);
        Assert.That(saved!.Items, Has.Count.EqualTo(2), "The deposit didn't save the suit and the bag.");

        // The deposited box is deleted with all it holds, so a card left in the bag would be gone.
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(card) && !entMan.IsQueuedForDeletion(card),
                "The ID card in the bag wasn't handed back.");
        });

        await Send(pair, console, body, new SafetyDepositWithdrawMessage(record.BoxId));

        await server.WaitAssertion(() =>
        {
            var containers = entMan.System<SharedContainerSystem>();

            var suits = Withdrawn(entMan, SuitProto);
            Assert.That(suits, Has.Count.EqualTo(1), "The suit didn't come back out exactly once.");
            var toggle = entMan.GetComponent<ToggleableClothingComponent>(suits[0]);
            Assert.That(toggle.ClothingUids, Is.Not.Empty, "The withdrawn suit lost track of its helmet.");
            Assert.That(toggle.ClothingUids.Keys.All(part =>
                    containers.TryGetContainingContainer(part, out var held) && held.Owner == suits[0] && held.ID == toggle.ContainerId),
                "The withdrawn suit's helmet isn't in it.");
            Assert.That(toggle.ActionEntity is { } action && entMan.EntityExists(action),
                "The withdrawn suit has no helmet toggle action.");
            Assert.That(entMan.GetComponent<StorageComponent>(suits[0]).Container.ContainedEntities
                    .Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID),
                Is.EqualTo(new[] { TankProto }), "The suit didn't keep the tank in its pocket.");

            var bags = Withdrawn(entMan, BagProto);
            Assert.That(bags, Has.Count.EqualTo(1), $"The {BagProto} didn't come back out exactly once.");
            var inBag = entMan.GetComponent<StorageComponent>(bags[0]).Container.ContainedEntities;
            Assert.That(inBag.Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID),
                Is.EquivalentTo(new[] { KeptProto, LimitedProto }),
                $"The {BagProto} didn't come back holding just the {KeptProto} and the {LimitedProto}.");
            var kept = inBag.Single(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == LimitedProto);
            Assert.That(entMan.GetComponent<StackComponent>(kept).Count, Is.EqualTo(limit),
                $"The {LimitedProto} stack wasn't cut down to the limit of {limit}.");
        });

        await db.DeleteSafetyDepositBox(record.BoxId);
        await pair.CleanReturnAsync();
    }

    /// <summary>Items of a prototype that came out of a box.</summary>
    private static List<EntityUid> Withdrawn(IEntityManager entMan, string proto)
    {
        var found = new List<EntityUid>();
        var query = entMan.AllEntityQueryEnumerator<SafetyDepositStoredComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
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
