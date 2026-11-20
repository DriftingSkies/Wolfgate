using System.Linq;
using System.Numerics;
using Content.Server.Nutrition.Components;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Server._WG.Fishing;
using Content.Server._WG.Hunting;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Power.EntitySystems;
using Content.Shared._WG.Farming;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Regression tests for the PR #740 review: each pins one bug that was found and fixed.</summary>
[TestFixture]
public sealed class ReviewFixesTest
{
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private static string? Proto(IEntityManager entMan, EntityUid uid)
    {
        return entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;
    }

    [Test]
    public async Task SecretRecipesOnlyBesideTheirMachine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var containers = entMan.System<SharedContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid plain = default, donk = default;

        await server.WaitAssertion(() =>
        {
            var cooking = entMan.System<WGCookingSystem>();
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var power = entMan.System<SharedPowerReceiverSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            plain = entMan.SpawnEntity("KitchenMicrowave", map.GridCoords);
            donk = entMan.SpawnEntity("SyndicateMicrowave", map.GridCoords);
            power.SetNeedsPower(plain, false);
            power.SetNeedsPower(donk, false);

            // The same ingredients on a board beside the Donk Co. microwave and on one far from any.
            EntityUid Assemble(EntityCoordinates at)
            {
                var board = entMan.SpawnEntity("WGPrepBoard", at);
                var beaker = entMan.SpawnEntity("Beaker", at);
                Assert.That(solutions.TryGetSolution(beaker, "beaker", out var soln), Is.True);
                solutions.TryAddReagent(soln!.Value, "TableSalt", FixedPoint2.New(5));
                solutions.TryAddReagent(soln.Value, "Blackpepper", FixedPoint2.New(5));
                foreach (var item in new[] { entMan.SpawnEntity("FoodDough", at), entMan.SpawnEntity("PartRodMetal1", at), beaker })
                    entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, item, board, at));

                var raw = cooking.Assemble((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cook);
                Assert.That(raw, Is.Not.Null, "The board put nothing together.");
                return raw!.Value;
            }

            var secret = Assemble(map.GridCoords);
            var ordinary = Assemble(new EntityCoordinates(map.MapUid, new Vector2(40, 40)));
            Assert.That(entMan.GetComponent<WGRawDishComponent>(secret).Recipe, Is.EqualTo("RecipeBaguetteSword"),
                "A board beside the Donk Co. microwave didn't offer its recipe.");
            Assert.That(entMan.GetComponent<WGRawDishComponent>(ordinary).Recipe, Is.EqualTo("RecipeBaguette"),
                "A board far from any Donk Co. machine made a secret recipe.");

            // A plain microwave refuses the secret dish, by hand or by any other insert, and takes the ordinary one.
            var refused = new InteractUsingEvent(cook, secret, plain, map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(plain, refused);
            Assert.That(refused.Handled, Is.True);
            var plainOven = containers.GetContainer(plain, WGHearthComponent.Oven);
            Assert.That(plainOven.ContainedEntities, Is.Empty, "A plain microwave took a secret dish.");
            Assert.That(containers.Insert(entMan.SpawnEntity("PartRodMetal1", map.GridCoords), plainOven), Is.False,
                "The oven took something it can't cook.");
            entMan.EventBus.RaiseLocalEvent(plain, new InteractUsingEvent(cook, ordinary, plain, map.GridCoords));
            entMan.EventBus.RaiseLocalEvent(donk, new InteractUsingEvent(cook, secret, donk, map.GridCoords));
            Assert.That(plainOven.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(containers.GetContainer(donk, WGHearthComponent.Oven).ContainedEntities, Has.Count.EqualTo(1));
        });

        // The Donk Co. oven bakes in (15 * 2 + 15) / 2 seconds.
        await pair.RunTicksSync(pair.SecondsToTicks(30));
        await server.WaitAssertion(() =>
        {
            Assert.That(containers.GetContainer(donk, WGHearthComponent.Oven).ContainedEntities.Select(e => Proto(entMan, e)),
                Is.EqualTo(new[] { "WeaponBaguette" }), "The Donk Co. microwave didn't bake its own recipe.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DishQualityIsTheUsedIngredients()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            for (var i = 0; i < 4; i++)
            {
                var slice = entMan.SpawnEntity("FoodDoughSlice", map.GridCoords);
                if (i == 0)
                    entMan.AddComponent<WGProduceQualityComponent>(slice).Quality = WGCropQuality.Gold;
                entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, slice, board, map.GridCoords));
            }

            // A bun takes one slice: the gold one, laid down first. The three left over don't water it down.
            var bun = entMan.System<WGCookingSystem>().Assemble((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cook);
            Assert.That(bun, Is.Not.Null);
            Assert.That(entMan.GetComponent<WGRawDishComponent>(bun!.Value).Recipe, Is.EqualTo("RecipeBun"));
            Assert.That(entMan.GetComponentOrNull<WGProduceQualityComponent>(bun.Value)?.Quality, Is.EqualTo(WGCropQuality.Gold),
                "Leftover ingredients lowered the dish's quality.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DryPotKeepsItsStewForMoreWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid cook = default, hearth = default, pot = default;

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(hearth, false);
            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(60));
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, pot, hearth, map.GridCoords));
        });

        // Two stews; then all the water but one portion is taken out, so the second finishes dry.
        await pair.RunTicksSync(pair.SecondsToTicks(25));
        await server.WaitAssertion(() =>
        {
            foreach (var id in new[] { "WheatBushel", "OatBushel" })
                entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, entMan.SpawnEntity(id, map.GridCoords), hearth, map.GridCoords));
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Has.Count.EqualTo(2));
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.RemoveReagent(soln!.Value, "Water", FixedPoint2.New(30));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(40));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Has.Count.EqualTo(1), "The stew left without water was lost or finished.");
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(30));
        });

        // Water back in: it boils again and the waiting stew finishes.
        await pair.RunTicksSync(pair.SecondsToTicks(30));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Is.Empty, "The waiting stew didn't finish once water was added.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChoppedPiecesShareWhatIsLeft()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var cooking = entMan.System<WGCookingSystem>();
            var solutions = entMan.System<SharedSolutionContainerSystem>();
            var containers = entMan.System<SharedContainerSystem>();
            var cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(cook, entMan.SpawnEntity("KitchenKnife", map.GridCoords)), Is.True);
            var board = entMan.SpawnEntity("WGPrepBoard", map.GridCoords);

            // A cake eaten down to nothing but poison.
            var cake = entMan.SpawnEntity("FoodCakePlain", map.GridCoords);
            Assert.That(solutions.TryGetSolution(cake, entMan.GetComponent<FoodComponent>(cake).Solution, out var soln), Is.True);
            solutions.RemoveAllSolution(soln!.Value);
            solutions.TryAddReagent(soln.Value, "Toxin", FixedPoint2.New(10));
            entMan.EventBus.RaiseLocalEvent(board, new InteractUsingEvent(cook, cake, board, map.GridCoords));

            var option = cooking.ChopOptions(cake).FindIndex(o => o.Result.Id == "FoodCakePlainSlice");
            Assert.That(option, Is.GreaterThanOrEqualTo(0));
            Assert.That(cooking.ChopItem((board, entMan.GetComponent<WGPrepBoardComponent>(board)), cake, cook, 1f, option), Is.True);

            var toxin = FixedPoint2.Zero;
            var other = FixedPoint2.Zero;
            foreach (var slice in containers.GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities)
            {
                Assert.That(solutions.TryGetSolution(slice, entMan.GetComponent<FoodComponent>(slice).Solution, out _, out var sliced), Is.True);
                toxin += sliced!.GetTotalPrototypeQuantity("Toxin");
                other += sliced.Volume - sliced.GetTotalPrototypeQuantity("Toxin");
            }

            Assert.That(toxin, Is.EqualTo(FixedPoint2.New(10)), "The slices lost or gained the cake's poison.");
            Assert.That(other, Is.EqualTo(FixedPoint2.Zero), "Chopping an eaten cake made fresh food.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StewMakesNoMoreSoupThanItsWater()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var solutions = entMan.System<SharedSolutionContainerSystem>();
        var map = await pair.CreateTestMap();
        EntityUid cook = default, hearth = default, pot = default;

        await server.WaitAssertion(() =>
        {
            cook = entMan.SpawnEntity("MobHuman", map.GridCoords);
            hearth = entMan.SpawnEntity("WGHearthStove", map.GridCoords);
            entMan.System<SharedPowerReceiverSystem>().SetNeedsPower(hearth, false);
            pot = entMan.SpawnEntity("WGCookPot", map.GridCoords);
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(60));
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, pot, hearth, map.GridCoords));
        });

        // Bring it to the boil, add the wheat, then take all but 10u of the water back out.
        await pair.RunTicksSync(pair.SecondsToTicks(25));
        await server.WaitAssertion(() =>
        {
            var wheat = entMan.SpawnEntity("WheatBushel", map.GridCoords);
            entMan.EventBus.RaiseLocalEvent(hearth, new InteractUsingEvent(cook, wheat, hearth, map.GridCoords));
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Has.Count.EqualTo(1), "The wheat didn't go in.");
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.RemoveReagent(soln!.Value, "Water", FixedPoint2.New(50));
        });

        await pair.RunTicksSync(pair.SecondsToTicks(40));
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGPotComponent>(pot).Jobs, Is.Empty, "The porridge never finished.");
            Assert.That(solutions.TryGetSolution(pot, "pot", out _, out var solution), Is.True);
            var porridge = solution!.GetTotalPrototypeQuantity("WGSoupPorridge");
            Assert.That(porridge, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(porridge, Is.LessThanOrEqualTo(FixedPoint2.New(10)), "10u of water became more soup than that.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KillGradeIsSetAtDeath()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var damage = entMan.System<DamageableSystem>();
            var goat = entMan.SpawnEntity("WGGameGoat", map.GridCoords);

            // A goat dies at 50; this blow is a clean kill.
            damage.TryChangeDamage(goat, new DamageSpecifier(server.ProtoMan.Index(Blunt), 55), true);
            Assert.That(entMan.System<MobStateSystem>().IsDead(goat), Is.True);
            Assert.That(entMan.System<WGHuntingSystem>().KillGrade(goat), Is.Zero);

            // Harm to the carcass afterwards doesn't spoil it.
            damage.TryChangeDamage(goat, new DamageSpecifier(server.ProtoMan.Index(Blunt), 100), true);
            Assert.That(entMan.System<WGHuntingSystem>().KillGrade(goat), Is.Zero, "Damage after death turned a clean kill messy.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EachBaitKeepsItsOwnQuality()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var angler = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var rod = entMan.SpawnEntity("WGFishingRod", map.GridCoords);
            foreach (var id in new[] { "WGBaitDoughball", "WGBaitDoughball", "WGBaitWorm" })
            {
                var bait = entMan.SpawnEntity(id, map.GridCoords);
                entMan.EventBus.RaiseLocalEvent(rod, new InteractUsingEvent(angler, bait, rod, map.GridCoords));
            }

            Assert.That(entMan.GetComponent<WGFishingRodComponent>(rod).Bait, Is.EqualTo(new[] { 1, 1, 2 }),
                "Loading a better bait changed the ones already on the hook.");
        });

        await pair.CleanReturnAsync();
    }
}
