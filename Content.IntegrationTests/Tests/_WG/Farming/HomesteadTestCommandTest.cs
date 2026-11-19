using System.Linq;
using Content.Server._WG.Cooking;
using Content.Server._WG.Farming;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Farming;

/// <summary>The in-game test ground builds without errors, sends the admin there and lays out every section.</summary>
[TestFixture]
public sealed class HomesteadTestCommandTest
{
    [Test]
    public async Task TheTestGroundBuildsEverySection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;

        await server.WaitPost(() =>
        {
            // The test ground stands on a world of its own.
            server.CfgMan.SetCVar(Content.Shared._WG.CCVar.PlanetCVars.PlanetNetworks, true);
            body = entMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            server.ConsoleHost.ExecuteCommand(pair.Player, "wghomesteadtest");
        });
        // The test ground waits for its world to finish generating.
        await PoolManager.WaitUntil(server, () => entMan.GetComponent<TransformComponent>(body).GridUid != map.Grid.Owner, maxTicks: 9000);
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var grid = entMan.GetComponent<TransformComponent>(body).GridUid;
            Assert.That(grid, Is.Not.Null.And.Not.EqualTo(map.Grid.Owner), "The admin wasn't sent to the test ground.");

            int Count<T>() where T : IComponent
            {
                var n = 0;
                var query = entMan.EntityQueryEnumerator<T, TransformComponent>();
                while (query.MoveNext(out _, out _, out var xform))
                {
                    if (xform.GridUid == grid)
                        n++;
                }

                return n;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Count<WGHearthComponent>(), Is.GreaterThanOrEqualTo(3), "The kitchen's hearths are missing.");
                Assert.That(Count<WGProcessorComponent>(), Is.GreaterThanOrEqualTo(20), "The machines are missing.");
                Assert.That(Count<WGSprinklerComponent>(), Is.GreaterThanOrEqualTo(1), "The farm is missing.");
                Assert.That(Count<WGOrderBoardComponent>(), Is.EqualTo(1), "The order board is missing.");
            });

            var mobs = server.System<MobStateSystem>();
            var dead = 0;
            var query = entMan.EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var state, out var xform))
            {
                if (xform.GridUid == grid && mobs.IsDead(uid, state))
                    dead++;
            }

            Assert.That(dead, Is.GreaterThanOrEqualTo(12), "The carcasses to butcher are missing.");
        });

        await pair.CleanReturnAsync();
    }
}
