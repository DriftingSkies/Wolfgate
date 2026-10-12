#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.PlanetPois;
using Content.Shared._WF.PlanetPois;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.PlanetPois;

/// <summary>Checks connected routing, protected terrain and bounded deterministic failures.</summary>
[TestFixture]
[TestOf(typeof(WFPlanetRoadPlanner))]
public sealed class PlanetRoadPlannerTest
{
    private static WFPlanetRoadSite Site(int x, int y, WFPlanetPoiConnection mode = WFPlanetPoiConnection.Roadside)
    {
        var sockets = new List<WFPlanetRoadSocket> { new(new Vector2i(x + 2, y), WFPlanetPoiDirection.East) };
        if (mode == WFPlanetPoiConnection.Through)
            sockets.Add(new(new Vector2i(x - 2, y), WFPlanetPoiDirection.West));
        return new(new Box2i(x - 2, y - 2, x + 3, y + 3), mode, sockets);
    }

    private static WFPlanetRoadPlan Plan(IReadOnlyList<WFPlanetRoadSite> sites, HashSet<Vector2i>? blocked = null, int seed = 17) =>
        WFPlanetRoadPlanner.Plan(Vector2.Zero, 48, 5, sites, blocked ?? new(), 3, 1, seed);

    [Test]
    public void BranchesAndThroughEntrancesFormOnePhysicalNetwork()
    {
        var sites = new[] { Site(-20, -15), Site(17, -12, WFPlanetPoiConnection.Through), Site(-8, 22), Site(22, 22, WFPlanetPoiConnection.Isolated) };
        var plan = Plan(sites);
        Assert.That(plan.Success, Is.True, plan.Error);
        var reached = new HashSet<Vector2i>();
        var frontier = new Queue<Vector2i>();
        frontier.Enqueue(plan.RoadTiles.First());
        while (frontier.TryDequeue(out var current))
        {
            if (!reached.Add(current))
                continue;
            foreach (var delta in new[] { new Vector2i(1, 0), new Vector2i(-1, 0), new Vector2i(0, 1), new Vector2i(0, -1) })
                if (plan.RoadTiles.Contains(current + delta) && !reached.Contains(current + delta))
                    frontier.Enqueue(current + delta);
        }
        Assert.That(reached.Count, Is.EqualTo(plan.RoadTiles.Count));
        foreach (var site in sites.Where(s => s.Connection != WFPlanetPoiConnection.Isolated))
        foreach (var socket in site.Sockets)
            Assert.That(reached, Does.Contain(socket.Position));
        Assert.That(plan.RoadTiles, Does.Not.Contain(sites[3].Sockets[0].Position));
        foreach (var site in sites)
        for (var x = site.Bounds.Left + 1; x < site.Bounds.Right - 1; x++)
        for (var y = site.Bounds.Bottom + 1; y < site.Bounds.Top - 1; y++)
        {
            Assert.That(plan.RoadTiles, Does.Not.Contain(new Vector2i(x, y)));
            Assert.That(plan.ShoulderTiles, Does.Not.Contain(new Vector2i(x, y)));
        }
    }

    [Test]
    public void DetoursProtectPinnedTilesCircleAndCentre()
    {
        var blocked = new HashSet<Vector2i>();
        for (var y = -35; y <= 10; y++)
            blocked.Add(new Vector2i(0, y));
        var plan = Plan(new[] { Site(-20, -15), Site(20, -15) }, blocked);
        Assert.That(plan.Success, Is.True, plan.Error);
        foreach (var tile in plan.RoadTiles.Concat(plan.ShoulderTiles))
        {
            Assert.That(blocked, Does.Not.Contain(tile));
            var distance = new Vector2(tile.X + .5f, tile.Y + .5f).Length();
            Assert.That(distance + .707107f, Is.LessThanOrEqualTo(48));
            Assert.That(distance - .707107f, Is.GreaterThanOrEqualTo(5));
        }
        Assert.That(plan.RoadTiles.Any(t => t.X == 0 && (t.Y > 10 || t.Y < -35)), Is.True);
    }

    [Test]
    public void SeededPlanIsRepeatable()
    {
        var sites = new[] { Site(-20, -15), Site(20, 15), Site(-15, 20) };
        var first = Plan(sites);
        var second = Plan(sites);
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(second.RoadTiles.SetEquals(first.RoadTiles), Is.True);
        Assert.That(second.ShoulderTiles.SetEquals(first.ShoulderTiles), Is.True);
    }

    [Test]
    public void ImpossibleRouteReturnsNoPartialNetwork()
    {
        var blocked = new HashSet<Vector2i>();
        for (var y = -48; y <= 48; y++)
            blocked.Add(new Vector2i(0, y));
        var plan = Plan(new[] { Site(-20, -15), Site(20, -15) }, blocked);
        Assert.That(plan.Success, Is.False);
        Assert.That(plan.Error, Is.Not.Empty);
        Assert.That(plan.RoadTiles, Is.Empty);
        Assert.That(plan.ShoulderTiles, Is.Empty);
    }

    [Test]
    public void ThroughRequiresDifferentEntranceSides()
    {
        var site = Site(20, 10, WFPlanetPoiConnection.Through);
        var invalid = site with { Sockets = new[] { site.Sockets[0], site.Sockets[0] with { Position = new Vector2i(22, 11) } } };
        Assert.That(Plan(new[] { invalid }).Success, Is.False);
    }
    [Test]
    public void EntranceAtWorldEdgeFailsWithFullWidthClearance()
    {
        Assert.That(Plan(new[] { Site(44, 0) }).Success, Is.False);
    }

    [Test]
    public void PinnedEntranceCannotBeOverwritten()
    {
        var site = Site(20, 10);
        Assert.That(Plan(new[] { site }, new HashSet<Vector2i> { site.Sockets[0].Position }).Success, Is.False);
    }
    [Test]
    public void SocketApproachCannotExcuseAnotherBuilding()
    {
        var first = Site(20, 10);
        var neighbor = new WFPlanetRoadSite(new Box2i(23, 8, 28, 13), WFPlanetPoiConnection.Isolated, new List<WFPlanetRoadSocket>());
        Assert.That(Plan(new[] { first, neighbor }).Success, Is.False);
    }

    [Test]
    public void SocketMustBeOnItsOwnBoundary()
    {
        var site = Site(20, 10);
        var invalid = site with { Sockets = new[] { new WFPlanetRoadSocket(new Vector2i(30, 10), WFPlanetPoiDirection.East) } };
        Assert.That(Plan(new[] { invalid }).Success, Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void NonFiniteWorldIsRejected(float radius)
    {
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, radius, 0, new[] { Site(20, 10) }, new HashSet<Vector2i>(), 3, 0, 1);
        Assert.That(plan.Success, Is.False);
    }

    [Test]
    public void EvenWidthIsRejected()
    {
        var plan = WFPlanetRoadPlanner.Plan(Vector2.Zero, 48, 0, new[] { Site(20, 10) }, new HashSet<Vector2i>(), 2, 0, 1);
        Assert.That(plan.Success, Is.False);
    }
    [Test]
    public void TranslatingWorldAndSitesTranslatesTheNetwork()
    {
        var sites = new[] { Site(-20, -15), Site(20, 15), Site(-15, 20) };
        var first = Plan(sites);
        var offset = new Vector2i(93, -71);
        var translated = sites.Select(s => s with
        {
            Bounds = new Box2i(s.Bounds.Left + offset.X, s.Bounds.Bottom + offset.Y, s.Bounds.Right + offset.X, s.Bounds.Top + offset.Y),
            Sockets = s.Sockets.Select(socket => socket with { Position = socket.Position + offset }).ToArray(),
        }).ToArray();
        var second = WFPlanetRoadPlanner.Plan(new Vector2(offset.X, offset.Y), 48, 5, translated, new HashSet<Vector2i>(), 3, 1, 17);
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(second.Success, Is.True, second.Error);
        Assert.That(second.RoadTiles.SetEquals(first.RoadTiles.Select(tile => tile + offset)), Is.True);
        Assert.That(second.ShoulderTiles.SetEquals(first.ShoulderTiles.Select(tile => tile + offset)), Is.True);
    }
}
