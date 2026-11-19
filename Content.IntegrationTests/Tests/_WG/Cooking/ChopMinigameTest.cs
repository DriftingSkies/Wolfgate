using System.Collections.Generic;
using Content.Client._WG.Cooking;
using Content.Server._WG.Cooking;
using Content.Shared._WG.Cooking;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Cooking;

/// <summary>Opens the chopping minigame on the client over raw meat, as the prep board's Chop button does.</summary>
[TestFixture]
public sealed class ChopMinigameTest
{
    [Test]
    public async Task ChopOpensOverRawMeat()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.ResolveDependency<IEntityManager>();
        var cEntMan = client.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();
        var meats = new[] { "FoodMeat", "WGMeatSaiga" };
        var nets = new List<NetEntity>();

        await server.WaitAssertion(() =>
        {
            var board = sEntMan.SpawnEntity("WGPrepBoard", map.GridCoords);
            var cooking = sEntMan.System<WGCookingSystem>();
            foreach (var id in meats)
            {
                var meat = sEntMan.SpawnEntity(id, map.GridCoords);
                var ev = new InteractUsingEvent(meat, meat, board, map.GridCoords);
                sEntMan.EventBus.RaiseLocalEvent(board, ev);
                Assert.That(cooking.TryGetChop(meat, out _, out _), Is.True, $"{id} doesn't chop.");
                nets.Add(sEntMan.GetNetEntity(meat));
            }
        });

        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            for (var i = 0; i < meats.Length; i++)
            {
                var window = new WGPrepBoardWindow();
                var view = new WGPrepItemView(nets[i], meats[i], meats[i], 3, new List<string> { "a", "b" }, new List<int> { 2, 3 });
                window.Update(new WGPrepBoardState(new List<WGPrepItemView> { view }, new List<WGPrepRecipeView>()), cEntMan);
                window.StartChop(view, cEntMan, 1);
                Assert.That(window.Chopping, Is.True, $"The chopping minigame didn't open over {meats[i]}.");
                window.Dispose();
            }
        });

        await pair.CleanReturnAsync();
    }
}
