using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Cooking;
using Content.Shared._WG.Cooking;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Power.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Chops and stirs through the real windows, as a player's client sends them.</summary>
public sealed class KitchenUiTest : InteractionTest
{
    private List<string> BoardIds(EntityUid board)
    {
        return SEntMan.System<SharedContainerSystem>().GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities
            .Select(e => SEntMan.GetComponent<MetaDataComponent>(e).EntityPrototype?.ID ?? "").ToList();
    }

    [Test]
    public async Task ChopPotatoThroughTheWindow()
    {
        await SpawnTarget("WGPrepBoard");
        var board = SEntMan.GetEntity(Target!.Value);
        await InteractUsing("FoodPotato");
        Assert.That(BoardIds(board), Does.Contain("FoodPotato"), "The potato didn't go on the board.");

        await PlaceInHands("KitchenKnife");
        await Interact();
        var potato = SEntMan.System<SharedContainerSystem>().GetContainer(board, WGPrepBoardComponent.Contents).ContainedEntities[0];
        await SendBui(WGPrepBoardUiKey.Key, new WGPrepBoardChopMessage(SEntMan.GetNetEntity(potato), 1f));

        Assert.That(BoardIds(board), Does.Not.Contain("FoodPotato"), "The potato wasn't chopped.");
        Assert.That(BoardIds(board).Count(i => i == "WGFoodPotatoSliced"), Is.EqualTo(2));
    }

    [Test]
    public async Task StirThroughTheWindow()
    {
        await SpawnTarget("KitchenElectricRange");
        var hearth = SEntMan.GetEntity(Target!.Value);
        var pot = SEntMan.GetEntity(await PlaceInHands("WGCookPot"));
        await Server.WaitPost(() =>
        {
            SEntMan.System<SharedPowerReceiverSystem>().SetNeedsPower(hearth, false);
            Assert.That(SEntMan.GetComponent<TransformComponent>(hearth).Anchored, Is.True);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(pot, "pot", out var soln), Is.True);
            solutions.TryAddReagent(soln!.Value, "Water", FixedPoint2.New(200));
        });

        await Interact();
        await RunSeconds(25);
        await InteractUsing("WGFoodPotatoSliced");
        var comp = SEntMan.GetComponent<WGPotComponent>(pot);
        Assert.That(comp.Jobs, Has.Count.EqualTo(1), "The potato didn't go in the boiling pot.");
        var before = comp.Jobs[0].Left;

        // The "Stir the pot" verb opens the window on the hearth.
        await Server.WaitPost(() => SEntMan.System<UserInterfaceSystem>().OpenUi(hearth, WGStirUiKey.Key, SPlayer));
        await RunTicks(15);
        Assert.That(TryGetBui(WGStirUiKey.Key, out _), Is.True, "The stirring window didn't open.");
        await SendBui(WGStirUiKey.Key, new WGStirMessage(1f));

        Assert.That(comp.Stirred, Is.EqualTo(1f), "The stir didn't count.");
        Assert.That(comp.Jobs[0].Left, Is.LessThan(before - 10f));
    }
}
