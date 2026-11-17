using System.Numerics;
using Content.Server._CE.ZLevels.Core;
using Content.Server._WG.Planets;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using static Content.IntegrationTests.Tests._WG.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WG.Planets;

/// <summary>Riding off a descending hull preserves the vehicle and its passengers when the transit map closes.</summary>
[TestFixture]
public sealed class CrashVehicleFallTest
{
    [TestCase("land")]
    [TestCase("hop")]
    [TestCase("destroy")]
    public async Task HoverbikeRiderSurvivesLeavingDescendingShip(string transition)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        await EnableFeature(pair);
        var layers = await BuildStandalone(pair);
        var hull = await BuildHull(pair, em.GetComponent<MapComponent>(layers[^1]).MapId);
        await MapInitHull(pair, hull);
        var bike = EntityUid.Invalid;
        var rider = EntityUid.Invalid;
        var transitMap = EntityUid.Invalid;
        var lower = EntityUid.Invalid;
        await server.WaitAssertion(() =>
        {
            bike = em.SpawnEntity("VehicleHoverbikeNFKeys", new EntityCoordinates(hull, 0.5f, 0.5f));
            rider = em.SpawnEntity("MobHuman", new EntityCoordinates(hull, 0.5f, 0.5f));
            Assert.That(server.System<SharedBuckleSystem>().TryBuckle(rider, rider, bike), Is.True);
            Assert.That(server.System<WGOrbitEntrySystem>().TryDropFromOrbit(hull, out var reason), Is.True, reason);
            transitMap = em.GetComponent<TransformComponent>(hull).MapUid!.Value;
            lower = em.GetComponent<CEZTransitMapComponent>(transitMap).LowerMap!.Value;
            server.System<SharedTransformSystem>().SetCoordinates(bike, new EntityCoordinates(transitMap, 100f, 100f));

            var levels = server.System<CEZLevelsSystem>();
            var grid = em.GetComponent<MapGridComponent>(hull);
            switch (transition)
            {
                case "land":
                    levels.SetZPosition(hull, 0f);
                    Assert.That(levels.TryExitTransit((hull, grid)), Is.True);
                    break;
                case "hop":
                    Assert.That(levels.SetTransitAltitude((hull, grid), -0.1f), Is.True);
                    break;
                case "destroy":
                    em.DeleteEntity(hull);
                    break;
            }
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(transitMap), Is.False, "The old transit map must actually be cleaned up.");
            Assert.That(em.EntityExists(bike), Is.True, "Leaving the deck must not delete the hoverbike.");
            Assert.That(em.EntityExists(rider), Is.True, "Transit cleanup must not delete a buckled player.");
            Assert.That(em.GetComponent<TransformComponent>(bike).MapUid, Is.EqualTo(lower));
            Assert.That(em.GetComponent<TransformComponent>(rider).ParentUid, Is.EqualTo(bike));
            Assert.That(em.GetComponent<BuckleComponent>(rider).Buckled, Is.True);
        });
        await Teardown(pair, layers);
        await pair.CleanReturnAsync();
    }
}
