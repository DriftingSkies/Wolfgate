using System;
using System.Linq;
using System.Numerics;
using Content.Shared._WG.ShipShields;
using NUnit.Framework;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks radar outlines retain hull shape and match directional shield coverage.</summary>
[TestFixture]
public sealed class WGShipShieldRadarGeometryTest
{
    private static readonly Vector2 Center = new(20f, -10f);

    private static Vector2[][] Hull(float radius = 5f) => new[]
    {
        new[] { Center + new Vector2(-radius, -radius), Center + new Vector2(radius, -radius),
            Center + new Vector2(radius, radius), Center + new Vector2(-radius, radius) },
    };

    [Test]
    public void BalancedAndFullCircleKeepTheWholeClosedHull()
    {
        var radar = new WGShipShieldRadarGeometry();
        radar.Update(Hull(), Center, 0.7f, 0f, MathF.PI / 2f);
        Assert.That(radar.Segments.Sum(s => Vector2.Distance(s.Start, s.End)), Is.EqualTo(40f).Within(0.001f));
        Assert.That(radar.Segments.All(s => s.Strength == 1f), Is.True);
        Assert.That(radar.Segments.Last().End, Is.EqualTo(radar.Segments.First().Start));
        radar.Update(Hull(), Center, 0.7f, 1f, MathF.Tau);
        Assert.That(radar.Segments.Sum(s => Vector2.Distance(s.Start, s.End)), Is.EqualTo(40f).Within(0.001f));
        Assert.That(radar.Segments.All(s => s.Strength == 1f), Is.True);
    }

    [TestCase(0f)]
    [TestCase(0.73f)]
    [TestCase(3.2f)]
    public void FullyShuntedMapLinesMatchActualProtectedIntervals(float direction)
    {
        var radar = new WGShipShieldRadarGeometry();
        radar.Update(Hull(), Center, direction, 1f, MathF.PI / 2f);
        Assert.That(radar.Segments, Is.Not.Empty);
        Assert.That(radar.Segments.Sum(s => Vector2.Distance(s.Start, s.End)), Is.LessThan(40f));
        foreach (var segment in radar.Segments)
        {
            var midpoint = (segment.Start + segment.End) / 2f;
            Assert.That(WGShipShieldShuntMath.StrengthMultiplier(midpoint, Center, direction, 1f, MathF.PI / 2f),
                Is.EqualTo(segment.Strength).Within(0.001f));
        }
    }

    [Test]
    public void PartialShuntSplitsBothStrengthsWithoutDroppingHullEdges()
    {
        var radar = new WGShipShieldRadarGeometry();
        radar.Update(Hull(), Center, 0.73f, 0.5f, MathF.PI / 2f);
        Assert.That(radar.Segments.Sum(s => Vector2.Distance(s.Start, s.End)), Is.EqualTo(40f).Within(0.001f));
        Assert.That(radar.Segments.Select(s => s.Strength).Distinct(), Is.EquivalentTo(new[] { WGShipShieldShuntMath.PeakStrength(0.5f, MathF.PI / 2f), 0.5f }));
        radar.Update(Hull(10f), Center, 0f, 0f, MathF.PI / 2f);
        Assert.That(radar.Segments.Sum(s => Vector2.Distance(s.Start, s.End)), Is.EqualTo(80f).Within(0.001f));
        radar.Update(Array.Empty<Vector2[]>(), Center, 0f, 0f, MathF.PI / 2f);
        Assert.That(radar.Segments, Is.Empty);
    }
}
