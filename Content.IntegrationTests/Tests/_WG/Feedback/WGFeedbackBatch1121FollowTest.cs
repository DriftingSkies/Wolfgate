#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Server._CE.ZLevels.Core;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Flight;
using Content.Shared._CE.ZLevels.Flight.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Follow-up reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121FollowTest
{
    /// <summary>
    /// #1222: a flyer who took off on the ground and rode a ship up holds the ship's level on stepping off instead of
    /// being flown straight back down to the ground; one who took off aboard does the same.
    /// </summary>
    [Test]
    public async Task FlyerLeavingAShipHoldsItsLevel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        EntityUid early = default, late = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                var maps = server.System<SharedMapSystem>();
                var transform = server.System<SharedTransformSystem>();
                var flight = server.System<CESharedZFlightSystem>();
                var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);

                var hull = maps.CreateGridEntity(em.GetComponent<MapComponent>(layers[0]).MapId);
                transform.SetCoordinates(hull, new EntityCoordinates(layers[0], new Vector2(0f, 200f)));
                for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    maps.SetTile(hull.Owner, hull.Comp, new Vector2i(x, y), plating);

                // Took off on the ground, then the ship carried it two levels up.
                early = em.SpawnEntity("MobAvali", new EntityCoordinates(hull.Owner, new Vector2(1.5f, 1.5f)));
                Assert.That(flight.TryActivateFlight(early), Is.True);
                Assert.That(em.GetComponent<CEZFlyerComponent>(early).TargetMapHeight, Is.EqualTo(0));

                var move = typeof(CEZLevelsSystem).GetMethod("MoveGridSetToMap", BindingFlags.Instance | BindingFlags.NonPublic)!;
                move.Invoke(server.System<CEZLevelsSystem>(), new object[] { new HashSet<EntityUid> { hull.Owner }, layers[2], 2, 2 });
                Assert.That(em.GetComponent<TransformComponent>(early).MapUid, Is.EqualTo(layers[2]));
                Assert.That(em.GetComponent<CEZPhysicsComponent>(early).CurrentZLevel, Is.EqualTo(2));

                // Took off aboard, up there.
                late = em.SpawnEntity("MobAvali", new EntityCoordinates(hull.Owner, new Vector2(3.5f, 3.5f)));
                Assert.That(flight.TryActivateFlight(late), Is.True);

                // Both step off the hull into open air.
                var hullPos = transform.GetWorldPosition(hull.Owner);
                transform.SetCoordinates(early, new EntityCoordinates(layers[2], hullPos + new Vector2(-20f, 2f)));
                transform.SetCoordinates(late, new EntityCoordinates(layers[2], hullPos + new Vector2(30f, 2f)));
                em.DeleteEntity(hull.Owner);
            });

            await pair.RunSeconds(4);

            await server.WaitAssertion(() =>
            {
                foreach (var flyer in new[] { early, late })
                {
                    Assert.That(em.GetComponent<CEZFlyerComponent>(flyer).Active, Is.True);
                    Assert.That(em.GetComponent<TransformComponent>(flyer).MapUid, Is.EqualTo(layers[2]),
                        $"{em.ToPrettyString(flyer)} dropped off the level the ship left it on.");
                    Assert.That(em.GetComponent<CEZFlyerComponent>(flyer).TargetMapHeight, Is.EqualTo(2));
                }
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await pair.CleanReturnAsync();
    }
}
