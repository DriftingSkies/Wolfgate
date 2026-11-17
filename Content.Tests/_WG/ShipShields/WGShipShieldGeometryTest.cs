using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WG.ShipShields;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks symmetric oval clearance and collision-safe edges.</summary>
[TestFixture]
[TestOf(typeof(WGShipShieldGeometry))]
public sealed class WGShipShieldGeometryTest
{
    [Test]
    public void EmptyHullHasNoContours()
    {
        Assert.That(WGShipShieldGeometry.CreateContours(Array.Empty<Vector2i>()), Is.Empty);
    }

    [Test]
    public void OvalIsSymmetricAndContainsAllTileCorners()
    {
        var tiles = new[] { new Vector2i(-18, -3), new Vector2i(7, 4), new Vector2i(2, -5) };
        var contour = WGShipShieldGeometry.CreateContours(tiles)[0];
        var center = new Vector2(-5f, 0f);
        for (var i = 0; i < contour.Length; i++)
        {
            Assert.That(Vector2.Distance(contour[i] + contour[(i + contour.Length / 2) % contour.Length], center * 2f),
                Is.LessThan(0.0001f));
        }
        foreach (var tile in tiles)
        foreach (var offset in new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One })
            Assert.That(Contains(contour, new Vector2(tile.X, tile.Y) + offset), Is.True);
        AssertValidContour(contour);
    }

    [Test]
    public void SquareShipGetsTighterCircleAndLongShipGetsOval()
    {
        var circle = WGShipShieldGeometry.CreateContours(new[] { Vector2i.Zero })[0];
        var center = new Vector2(0.5f);
        Assert.That(circle.Max(p => Vector2.Distance(p, center)), Is.LessThan(6f));
        Assert.That(circle.Max(p => Vector2.Distance(p, center)) - circle.Min(p => Vector2.Distance(p, center)), Is.LessThan(0.0001f));
        var oval = WGShipShieldGeometry.CreateContours(new[] { Vector2i.Zero, new Vector2i(30, 0) })[0];
        Assert.That(oval.Max(p => p.X) - oval.Min(p => p.X), Is.GreaterThan(3f * (oval.Max(p => p.Y) - oval.Min(p => p.Y))));
    }

    [Test]
    public void TileSizeScalesHullAndClearance()
    {
        var contour = WGShipShieldGeometry.CreateContours(new[] { new Vector2i(-2, 5) }, 2f)[0];
        var unit = WGShipShieldGeometry.CreateContours(new[] { new Vector2i(-2, 5) })[0];
        Assert.That(contour, Is.EqualTo(unit.Select(p => p * 2f).ToArray()));
        AssertValidContour(contour);
    }

    [Test]
    public void ConcaveHullGetsOneSymmetricConvexEnvelope()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 20; y++)
        {
            if (x < 4 || y < 4)
                tiles.Add(new Vector2i(x, y));
        }

        var contours = WGShipShieldGeometry.CreateContours(tiles);
        Assert.That(contours.Length, Is.EqualTo(1));
        var contour = contours[0];
        Assert.Multiple(() =>
        {
            Assert.That(Contains(contour, new Vector2(2f, 18f)), Is.True);
            Assert.That(Contains(contour, new Vector2(18f, 2f)), Is.True);
            Assert.That(Contains(contour, new Vector2(14f, 14f)), Is.True);
        });
        AssertValidContour(contour);
    }

    [Test]
    public void InputOrderAndDuplicatesDoNotChangeContours()
    {
        var tiles = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(1, 1), new Vector2i(30, -5) };
        var expected = WGShipShieldGeometry.CreateContours(tiles);
        var actual = WGShipShieldGeometry.CreateContours(tiles.Reverse().Concat(tiles));
        Assert.That(actual.Length, Is.EqualTo(expected.Length));
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual[i], Is.EqualTo(expected[i]));
            AssertValidContour(actual[i]);
        }
    }

    [Test]
    public void DisconnectedHullSectionsShareOneOval()
    {
        var contours = WGShipShieldGeometry.CreateContours(new[] { new Vector2i(0, 0), new Vector2i(30, 0) });
        Assert.That(contours.Length, Is.EqualTo(1));
        Assert.That(contours.Any(c => Contains(c, new Vector2(0.5f, 0.5f))), Is.True);
        Assert.That(contours.Any(c => Contains(c, new Vector2(30.5f, 0.5f))), Is.True);
        Assert.That(contours.Any(c => Contains(c, new Vector2(15f, 0.5f))), Is.True);
        foreach (var contour in contours)
            AssertValidContour(contour);
    }

    [Test]
    public void EnclosedHolesDoNotBecomeShieldPerimeters()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 20; y++)
        {
            if (x == 0 || y == 0 || x == 19 || y == 19)
                tiles.Add(new Vector2i(x, y));
        }

        var contours = WGShipShieldGeometry.CreateContours(tiles);
        Assert.That(contours.Length, Is.EqualTo(1));
        Assert.That(Contains(contours[0], new Vector2(10f, 10f)), Is.True);
        AssertValidContour(contours[0]);
    }

    [Test]
    public void ImpactProjectionIncludesClosingEdgeAndEndpoints()
    {
        var contours = new[] { new[] { new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 4f), new Vector2(0f, 4f) } };
        Assert.Multiple(() =>
        {
            Assert.That(WGShipShieldGeometry.ClosestPoint(contours, new Vector2(-2f, 2f)), Is.EqualTo(new Vector2(0f, 2f)));
            Assert.That(WGShipShieldGeometry.ClosestPoint(contours, new Vector2(5f, 5f)), Is.EqualTo(new Vector2(4f, 4f)));
            Assert.That(WGShipShieldGeometry.ClosestPoint(Array.Empty<Vector2[]>(), new Vector2(5f, 5f)), Is.EqualTo(new Vector2(5f, 5f)));
        });
    }
    [Test]
    public void RoundedHullKeepsClearanceAndAvoidsSharpRasterCorners()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 20; x++)
        for (var y = 0; y < 8; y++)
            tiles.Add(new Vector2i(x, y));
        var contour = WGShipShieldGeometry.CreateContours(tiles)[0];
        AssertValidContour(contour);
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            foreach (var point in new[] { a, (a + b) * 0.5f })
            {
                var distance = tiles.Min(tile => HullDistance(point, tile));
                Assert.That(distance, Is.GreaterThanOrEqualTo(4f), $"Rounded shield encroaches on hull at {point}.");
                Assert.That(distance, Is.LessThanOrEqualTo(10f), $"Rounded shield loses hull shape at {point}.");
            }
            var incoming = Vector2.Normalize(a - contour[(i + contour.Length - 1) % contour.Length]);
            var outgoing = Vector2.Normalize(b - a);
            Assert.That(Vector2.Dot(incoming, outgoing), Is.GreaterThan(0.4f), "Rounded corners should turn less than 66 degrees per segment.");
        }
    }

    private static float HullDistance(Vector2 point, Vector2i tile)
    {
        var x = Math.Max(0f, Math.Max(tile.X - point.X, point.X - tile.X - 1f));
        var y = Math.Max(0f, Math.Max(tile.Y - point.Y, point.Y - tile.Y - 1f));
        return MathF.Sqrt(x * x + y * y);
    }
    private static void AssertValidContour(Vector2[] contour)
    {
        Assert.That(contour.Length, Is.GreaterThanOrEqualTo(3));
        Assert.That(contour[0], Is.Not.EqualTo(contour[^1]), "Closing vertex must not be duplicated.");
        Assert.That(contour.Distinct().Count(), Is.EqualTo(contour.Length), "A contour must not touch itself.");
        var area = 0f;
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            Assert.That(Vector2.DistanceSquared(a, b), Is.GreaterThan(0.0001f), "Every edge must be usable for collision.");
            area += a.X * b.Y - b.X * a.Y;
        }
        Assert.That(area, Is.GreaterThan(0f), "Outer contours must face incoming projectiles.");
        for (var i = 0; i < contour.Length; i++)
        for (var j = i + 2; j < contour.Length; j++)
        {
            if (i == 0 && j == contour.Length - 1)
                continue;
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            var c = contour[j];
            var d = contour[(j + 1) % contour.Length];
            var ab = b - a;
            var cd = d - c;
            var crosses = Cross(ab, c - a) * Cross(ab, d - a) < -0.000001f &&
                Cross(cd, a - c) * Cross(cd, b - c) < -0.000001f;
            Assert.That(crosses, Is.False, $"Contour edges {i} and {j} must not cross.");
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static bool Contains(Vector2[] contour, Vector2 point)
    {
        var inside = false;
        for (var i = 0; i < contour.Length; i++)
        {
            var a = contour[i];
            var b = contour[(i + 1) % contour.Length];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
