#nullable enable
using System.Collections.Generic;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.Outposts;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>
/// The ground round an outpost is its owner's: another player can't found or build on the raw ground there, and an
/// outpost is only founded with clear ground round it, away from what the world has reserved.
/// </summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostClaimTest
{
    [Test]
    public async Task ClaimsKeepOthersOff()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var (layers, user) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        var outposts = server.System<WFOutpostSystem>();
        await LayTiles(pair, ground, new Vector2i(20, -6), new Vector2i(42, 6));

        try
        {
            await server.WaitAssertion(() =>
            {
                ClearAnchored(entMan, ground, new Vector2i(20, -6), new Vector2i(42, 6));

                // A stranger's outpost: founded by the session, then handed over.
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var theirs, out var reason), Is.True, $"Founding was refused: {reason}.");
                outposts.TransferOwnership((theirs!.Value, entMan.GetComponent<WFOutpostComponent>(theirs.Value)), new NetUserId(Guid.NewGuid()), "Stranger");

                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, 12.5f, 0.5f), out _, out reason), Is.False, "An outpost was founded in another's claim.");
                Assert.That(reason, Is.EqualTo("wf-outpost-found-claimed"));

                Assert.That(Builds(entMan, user, new EntityCoordinates(ground, 10.5f, 4.5f)), Is.False,
                    "Someone built on the raw ground of another's claim.");
                Assert.That(Builds(entMan, user, new EntityCoordinates(ground, -6.5f, 0.5f)), Is.True,
                    "Building was refused outside any claim.");

                // A road five tiles from where the next outpost would stand keeps it away.
                var roads = entMan.EnsureComponent<WFPlanetPoiComponent>(ground).RoadTiles;
                var road = new Vector2i(25, 0);
                var hadRoad = !roads.Add(road);
                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, 31.5f, 0.5f), out _, out reason), Is.False, "An outpost was founded beside reserved ground.");
                Assert.That(reason, Is.EqualTo("wf-outpost-found-too-close"));
                if (!hadRoad)
                    roads.Remove(road);

                Assert.That(outposts.TryFound(user, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, 31.5f, 0.5f), out _, out reason), Is.True, $"Founding on clear ground was refused: {reason}.");
                Assert.That(Builds(entMan, user, new EntityCoordinates(ground, 34.5f, 4.5f)), Is.True,
                    "The owner was refused building on the raw ground of their own claim.");
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }

    private static bool Builds(IEntityManager entMan, EntityUid user, EntityCoordinates at)
    {
        var ev = new WFGroundBuildAttemptEvent(user, at);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
        return !ev.Cancelled;
    }

    private static void ClearAnchored(IEntityManager entMan, EntityUid ground, Vector2i from, Vector2i to)
    {
        var maps = entMan.System<SharedMapSystem>();
        var grid = entMan.GetComponent<MapGridComponent>(ground);
        var clear = new List<EntityUid>();
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            var anchored = maps.GetAnchoredEntitiesEnumerator(ground, grid, new Vector2i(x, y));
            while (anchored.MoveNext(out var ent))
                clear.Add(ent.Value);
        }

        foreach (var ent in clear)
            entMan.DeleteEntity(ent);
    }
}
