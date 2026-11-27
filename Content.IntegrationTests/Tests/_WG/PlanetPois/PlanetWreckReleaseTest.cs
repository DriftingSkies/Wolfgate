#nullable enable
using System.Numerics;
using Content.Server.Shuttles.Components;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.PlanetCities;
using Content.Shared._WG.Planets;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>
/// A settled wreck stays ground while it moves between its world's layers, and is a flyable vessel again once it is
/// on a map that is no planet layer.
/// </summary>
[TestFixture]
public sealed class PlanetWreckReleaseTest
{
    [Test]
    public async Task SettledHullIsReleasedOffThePlanet()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;

        var ground = await pair.CreateTestMap();
        var air = await pair.CreateTestMap();
        var space = await pair.CreateTestMap();
        var gap = await pair.CreateTestMap();
        var hull = ground.Grid.Owner;

        await server.WaitAssertion(() =>
        {
            var network = em.GetNetEntity(ground.MapUid);
            em.AddComponent<WGPlanetLayerComponent>(ground.MapUid).Network = network;
            em.AddComponent<WGPlanetLayerComponent>(air.MapUid).Network = network;
            var transit = em.AddComponent<CEZTransitMapComponent>(gap.MapUid);
            transit.LowerMap = ground.MapUid;
            transit.UpperMap = air.MapUid;

            // As WGPlanetPoiSystem.SettleWreck leaves a hull.
            em.RemoveComponent<ShuttleComponent>(hull);
            em.EnsureComponent<WGPlanetCityGridComponent>(hull);
            em.EnsureComponent<CEZMappingAnchorGridComponent>(hull);
            em.EnsureComponent<RoofComponent>(hull);
            var gravity = em.EnsureComponent<GravityComponent>(hull);
#pragma warning disable RA0002 // The test settles the hull directly, as the wreck placement does.
            gravity.Enabled = true;
            gravity.Inherent = true;
#pragma warning restore RA0002
            server.System<SharedPhysicsSystem>().SetBodyType(hull, BodyType.Static);

            var xforms = server.System<SharedTransformSystem>();
            xforms.SetCoordinates(hull, new EntityCoordinates(gap.MapUid, Vector2.Zero));
            Assert.That(em.HasComponent<WGPlanetCityGridComponent>(hull), Is.True, "The hull stopped being ground in the gap between two layers of its world.");

            xforms.SetCoordinates(hull, new EntityCoordinates(air.MapUid, Vector2.Zero));
            Assert.That(em.HasComponent<WGPlanetCityGridComponent>(hull), Is.True, "The hull stopped being ground on another layer of its world.");

            xforms.SetCoordinates(hull, new EntityCoordinates(space.MapUid, Vector2.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<WGPlanetCityGridComponent>(hull), Is.False, "The hull is still a planet city grid in space.");
                Assert.That(em.HasComponent<CEZMappingAnchorGridComponent>(hull), Is.False, "The hull is still a z-level anchor in space.");
                Assert.That(em.HasComponent<RoofComponent>(hull), Is.False, "The hull still has the planet's roof.");
                Assert.That(em.GetComponent<GravityComponent>(hull).Inherent, Is.False, "The hull's gravity is still the planet's.");
                Assert.That(em.GetComponent<PhysicsComponent>(hull).BodyType, Is.EqualTo(BodyType.Dynamic), "The hull is still static.");
                Assert.That(em.HasComponent<ShuttleComponent>(hull), Is.True, "The hull is not a shuttle again.");
            });

            // The gap is a stand-in with no grid in transit; it goes before the z-level systems tick over it.
            em.DeleteEntity(gap.MapUid);
        });

        await pair.CleanReturnAsync();
    }
}
