using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WG.PlanetPois;
using Content.Shared._WG.PlanetPois;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.PlanetPois;

/// <summary>Checks the fallback can find large-city origins that random sampling may miss.</summary>
[TestFixture]
[TestOf(typeof(WGPlanetPoiPlacementPlanner))]
public sealed class PlanetPoiPlacementPlannerTest
{
    [TestCase(1977)]
    [TestCase(8231)]
    [TestCase(44119)]
    public void NarrowMerakCityBandContainsConnectedFallbackOrigin(int seed)
    {
        var footprint = new Box2i(-144, -72, 144, 72);
        var sockets = new[]
        {
            new WGPlanetRoadSocket(new Vector2i(-144, 0), WGPlanetPoiDirection.West),
            new WGPlanetRoadSocket(new Vector2i(143, 0), WGPlanetPoiDirection.East),
            new WGPlanetRoadSocket(new Vector2i(-72, 71), WGPlanetPoiDirection.North),
            new WGPlanetRoadSocket(new Vector2i(72, -72), WGPlanetPoiDirection.South),
        };
        var found = false;
        foreach (var origin in WGPlanetPoiPlacementPlanner.FallbackOrigins(Vector2.Zero, 224, footprint, seed))
        {
            var bounds = new Box2i(footprint.Left + origin.X, footprint.Bottom + origin.Y, footprint.Right + origin.X, footprint.Top + origin.Y);
            var clearance = WGPlanetPoiPlacementPlanner.WithRoadClearance(bounds, 5, 1);
            if (new[] { new Vector2(clearance.Left, clearance.Bottom), new Vector2(clearance.Right, clearance.Bottom), new Vector2(clearance.Left, clearance.Top), new Vector2(clearance.Right, clearance.Top) }
                .Any(corner => corner.Length() >= 319))
                continue;
            var nearest = new Vector2(System.Math.Clamp(0, clearance.Left, clearance.Right), System.Math.Clamp(0, clearance.Bottom, clearance.Top));
            if (nearest.Length() < 96)
                continue;
            var absolute = sockets.Select(socket => socket with { Position = socket.Position + origin }).ToArray();
            var site = new WGPlanetRoadSite(bounds, WGPlanetPoiConnection.Through, absolute);
            var plan = WGPlanetRoadPlanner.Plan(Vector2.Zero, 319, 96, new[] { site }, new HashSet<Vector2i>(), 5, 1, seed);
            if (!plan.Success)
                continue;
            Assert.That(absolute.All(socket => plan.RoadTiles.Contains(socket.Position)), Is.True);
            found = true;
            break;
        }
        Assert.That(found, Is.True, "The narrow city-placement region was missed by the fallback lattice.");
    }

    [Test]
    public void FallbackIsDeterministicDistinctAndBounded()
    {
        var footprint = new Box2i(-20, -16, 20, 16);
        var first = WGPlanetPoiPlacementPlanner.FallbackOrigins(new Vector2(23, -17), 4096, footprint, 1977).ToArray();
        var second = WGPlanetPoiPlacementPlanner.FallbackOrigins(new Vector2(23, -17), 4096, footprint, 1977).ToArray();
        Assert.That(first, Is.Not.Empty);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first.Distinct().Count(), Is.EqualTo(first.Length));
        Assert.That(first.Length, Is.LessThanOrEqualTo(129 * 129));
        Assert.That(first.All(point => Vector2.DistanceSquared(new Vector2(point.X, point.Y), new Vector2(23, -17)) <= 4096f * 4096f), Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(0)]
    public void InvalidSearchBoundsHaveNoCandidates(float reach)
    {
        Assert.That(WGPlanetPoiPlacementPlanner.FallbackOrigins(Vector2.Zero, reach, new Box2i(-20, -16, 20, 16), 1), Is.Empty);
    }
    [Test]
    public void ClearanceRingRejectsCityThatTrapsItsNorthernPort()
    {
        var city = new Box2i(-144, 140, 144, 284);
        Assert.That(new Vector2(city.Right, city.Top).Length(), Is.LessThan(319));
        var protectedBounds = WGPlanetPoiPlacementPlanner.WithRoadClearance(city, 5, 1);
        Assert.That(new Vector2(protectedBounds.Right, protectedBounds.Top).Length(), Is.GreaterThan(319));
    }
}
