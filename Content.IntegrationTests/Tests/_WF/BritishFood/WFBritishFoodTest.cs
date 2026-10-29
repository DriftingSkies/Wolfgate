#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server._WF.BritishFood;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared._WF.BritishFood;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Traits.Assorted;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.BritishFood;

/// <summary>Piping hot vendor food, taking food out of its pack, and the deep fried Mars bar's fat.</summary>
[TestFixture]
[TestOf(typeof(WFPipingHotSystem))]
public sealed class WFBritishFoodTest : WolfmedGameTest
{
    /// <summary>A hot bite burns and puts the eater off the next, so eating stops after one. Then it cools.</summary>
    [Test]
    public async Task PipingHotStopsEatingTest()
    {
        var map = await CreateTestMap();
        EntityUid eater = default;
        EntityUid food = default;

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            eater = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            food = SEntMan.SpawnEntity("WFFoodPackHuntersChicken", map.GridCoords);
            Assert.That(SEntMan.HasComponent<WFPipingHotComponent>(food), "vendor food spawns hot.");
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(eater, food));
            Assert.That(Feed(eater, food).Success);
        });
        await RunSeconds(4);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Volume(food), Is.EqualTo(FixedPoint2.New(11)),
                    "one 5u bite went down and the next was refused.");
                Assert.That(Heat(eater), Is.GreaterThan(FixedPoint2.Zero), "the bite burned.");
            });

            SEntMan.GetComponent<WFPipingHotComponent>(food).CoolAt = Server.ResolveDependency<IGameTiming>().CurTime;
        });
        await RunSeconds(0.5f);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.HasComponent<WFPipingHotComponent>(food), Is.False, "it cooled."));
    }

    /// <summary>A bite refused for a full stomach doesn't burn or put the eater off.</summary>
    [Test]
    public async Task RefusedBiteDoesNotBurnTest()
    {
        var map = await CreateTestMap();
        EntityUid eater = default;
        EntityUid food = default;

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            eater = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var stomach = SEntMan.System<SharedBodySystem>().GetBodyOrgans(eater)
                .First(organ => SEntMan.HasComponent<StomachComponent>(organ.Id)).Id;
            var stomachSolution = StomachSystem.DefaultSolutionName;
            Assert.That(solutions.TryGetSolution(stomach, stomachSolution, out var full, out var held));
            Assert.That(solutions.TryAddReagent(full!.Value, "Fat", held!.AvailableVolume),
                "the stomach filled up.");

            food = SEntMan.SpawnEntity("WFFoodPackHuntersChicken", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(eater, food));
            Assert.That(Feed(eater, food).Success);
        });
        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(Volume(food), Is.EqualTo(FixedPoint2.New(16)), "nothing was eaten.");
                Assert.That(Heat(eater), Is.EqualTo(FixedPoint2.Zero), "a refused bite doesn't burn.");
                Assert.That(SEntMan.GetComponent<WFPipingHotComponent>(food).PausedUntil, Is.EqualTo(TimeSpan.Zero));
            });
        });
    }

    /// <summary>An eater numb to pain eats hot food straight through without a burn.</summary>
    [Test]
    public async Task PainNumbEaterIsNotScaldedTest()
    {
        var map = await CreateTestMap();
        EntityUid eater = default;
        EntityUid food = default;

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            eater = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.EnsureComponent<PainNumbnessComponent>(eater);
            food = SEntMan.SpawnEntity("WFFoodPackHuntersChicken", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(eater, food));
            Feed(eater, food);
        });
        await RunSeconds(3.5f);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.Deleted(food) || Volume(food) < FixedPoint2.New(11),
                    "eating carried on past the first bite.");
                Assert.That(Heat(eater), Is.EqualTo(FixedPoint2.Zero));
            });
        });
    }

    /// <summary>Unwrapping a bitten hot pack keeps the bites and the heat, and leaves the container.</summary>
    [Test]
    [TestOf(typeof(WFUnwrapFoodSystem))]
    public async Task UnwrapKeepsBitesAndHeatTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var hands = SEntMan.System<SharedHandsSystem>();
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var pack = SEntMan.SpawnEntity("WFFoodPackCheesyBeanMelt", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, pack));

            solutions.TryGetSolution(pack, "food", out var packSoln, out _);
            solutions.SplitSolution(packSoln!.Value, FixedPoint2.New(5));
            var coolAt = SEntMan.GetComponent<WFPipingHotComponent>(pack).CoolAt;

            var food = Unwrap(pack, user)!.Value;
            var held = hands.EnumerateHeld(user).Select(Proto).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(Proto(food), Is.EqualTo("WFFoodCheesyBeanMeltVendor"));
                Assert.That(Volume(food), Is.EqualTo(FixedPoint2.New(7)), "the bite taken from the pack stays taken.");
                Assert.That(SEntMan.GetComponent<WFPipingHotComponent>(food).CoolAt, Is.EqualTo(coolAt),
                    "the heat came out with it.");
                Assert.That(held, Is.EquivalentTo(new[] { "WFFoodCheesyBeanMeltVendor", "WFTrashPackCheesyBeanMelt" }));
                Assert.That(SEntMan.Deleted(pack) || SEntMan.IsQueuedForDeletion(pack));
            });
        });
    }

    /// <summary>
    /// A fried pack holds more than the bare food was made for. Unwrapping keeps all of it, oil included, and the
    /// capacity the fryer gave the pack.
    /// </summary>
    [Test]
    [TestOf(typeof(WFUnwrapFoodSystem))]
    public async Task UnwrapFriedPackKeepsEverythingTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var pack = SEntMan.SpawnEntity("WFFoodPackHuntersChicken", map.GridCoords);

            // What the deep fryer does to food: room for twice the oil, then the oil.
            solutions.TryGetSolution(pack, "food", out var packSoln, out var packFood);
            solutions.SetCapacity(packSoln!.Value, packFood!.Volume + FixedPoint2.New(30));
            Assert.That(solutions.TryAddReagent(packSoln.Value, "Cornoil", FixedPoint2.New(10)));
            var before = packFood.Contents.ToDictionary(r => r.Reagent.Prototype, r => r.Quantity);
            var capacity = packFood.MaxVolume;

            var food = Unwrap(pack, user)!.Value;
            solutions.TryGetSolution(food, "food", out _, out var bare);

            Assert.Multiple(() =>
            {
                Assert.That(bare!.Contents.ToDictionary(r => r.Reagent.Prototype, r => r.Quantity),
                    Is.EquivalentTo(before), "every reagent came out, the oil too.");
                Assert.That(bare.Volume, Is.EqualTo(FixedPoint2.New(26)));
                Assert.That(bare.MaxVolume, Is.GreaterThanOrEqualTo(capacity));
            });
        });
    }

    /// <summary>When the food can't take the pack's contents, the pack stays as it was and nothing spawns.</summary>
    [Test]
    [TestOf(typeof(WFUnwrapFoodSystem))]
    public async Task FailedUnwrapLeavesThePackTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var pack = SEntMan.SpawnEntity("WFFoodPackHuntersChicken", map.GridCoords);
            // Food with no food solution to fill.
            SEntMan.GetComponent<WFUnwrapFoodComponent>(pack).Food = "Crowbar";
            var crowbars = Count("Crowbar");

            Assert.Multiple(() =>
            {
                Assert.That(Unwrap(pack, user), Is.Null);
                Assert.That(SEntMan.Deleted(pack) || SEntMan.IsQueuedForDeletion(pack), Is.False);
                Assert.That(Volume(pack), Is.EqualTo(FixedPoint2.New(16)));
                Assert.That(Count("Crowbar"), Is.EqualTo(crowbars), "the food spawned for it was removed.");
                Assert.That(Count("WFTrashPackHuntersChicken"), Is.Zero, "no empty container was handed out.");
            });
        });
    }

    /// <summary>Slices of hot food keep its cooling time; a cold pie's slices are cold.</summary>
    [Test]
    public async Task SlicesKeepTheHeatTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var slicer = SEntMan.System<SliceableFoodSystem>();
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var hot = SEntMan.SpawnEntity("WFFoodPieSteakAleVendor", map.GridCoords);
            var cold = SEntMan.SpawnEntity("WFFoodPieSteakAleVendor", map.GridCoords);
            var coolAt = Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromSeconds(20);
            SEntMan.AddComponent(hot, new WFPipingHotComponent { CoolAt = coolAt });

            var hotSlice = slicer.Slice(hot, user);
            var coldSlice = slicer.Slice(cold, user);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.TryGetComponent<WFPipingHotComponent>(hotSlice, out var heat),
                    "the slice is hot.");
                Assert.That(heat?.CoolAt, Is.EqualTo(coolAt), "it cools when the pie would have.");
                Assert.That(SEntMan.HasComponent<WFPipingHotComponent>(coldSlice), Is.False);
            });
        });
    }

    /// <summary>Saturated fat over the threshold strains the heart; under it, nothing happens.</summary>
    [Test]
    [TestOf(typeof(WFStrainHeartSystem))]
    public async Task SaturatedFatStrainsTheHeartTest()
    {
        var map = await CreateTestMap();
        EntityUid over = default;
        EntityUid under = default;

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            over = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            under = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var bloodstream = SEntMan.System<BloodstreamSystem>();
            Assert.That(bloodstream.TryAddToChemicals(over, new Solution("WFSaturatedFat", FixedPoint2.New(12))));
            Assert.That(bloodstream.TryAddToChemicals(under, new Solution("WFSaturatedFat", FixedPoint2.New(8))));
        });
        await RunSeconds(20);

        await Server.WaitAssertion(() =>
        {
            var overHeart = Heart(over);
            var underHeart = Heart(under);
            Assert.Multiple(() =>
            {
                Assert.That(overHeart.Health, Is.LessThan(overHeart.MaxHealth - FixedPoint2.New(0.2)),
                    "12u strained the heart.");
                Assert.That(underHeart.Health, Is.EqualTo(underHeart.MaxHealth), "8u is under the threshold.");
            });
        });
    }

    private (bool Success, bool Handled) Feed(EntityUid eater, EntityUid food)
    {
        return SEntMan.System<FoodSystem>().TryFeed(eater, eater, food, SEntMan.GetComponent<FoodComponent>(food));
    }

    private EntityUid? Unwrap(EntityUid pack, EntityUid user)
    {
        var unwrap = SEntMan.GetComponent<WFUnwrapFoodComponent>(pack);
        return SEntMan.System<WFUnwrapFoodSystem>().Unwrap((pack, unwrap), user);
    }

    private FixedPoint2 Volume(EntityUid food)
    {
        SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(food, "food", out _, out var solution);
        return solution!.Volume;
    }

    private FixedPoint2 Heat(EntityUid mob)
    {
        return SEntMan.GetComponent<DamageableComponent>(mob).Damage.DamageDict.GetValueOrDefault("Heat");
    }

    private string Proto(EntityUid uid)
    {
        return SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
    }

    private int Count(string proto)
    {
        var count = 0;
        var query = SEntMan.EntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (meta.EntityPrototype?.ID == proto && !SEntMan.IsQueuedForDeletion(uid))
                count++;
        }

        return count;
    }

    private WolfmedOrganComponent Heart(EntityUid body)
    {
        var heart = SEntMan.System<SharedBodySystem>().GetBodyOrgans(body)
            .First(organ => organ.Component.SlotId == "heart").Id;
        return SEntMan.GetComponent<WolfmedOrganComponent>(heart);
    }
}
