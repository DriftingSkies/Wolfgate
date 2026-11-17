using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Content.Client._WG.ShipShields;
using Content.Shared._WG.ShipShields;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks cached shield ribbons remain valid and cheap at a distant camera scale.</summary>
[TestFixture]
public sealed class WGShipShieldMeshTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ImpactSamplingReproducesSmoothGradientAtEveryVertex(bool distant)
    {
        var mesh = new WGShipShieldMesh(ShipContours(), distant);
        var colors = mesh.Samples.Select(p => new Color(p.X / 100f + 0.2f, p.Y / 100f + 0.2f, 0.5f, 1f)).ToArray();
        foreach (var vertex in mesh.Triangles.Concat(mesh.HexLines))
        {
            var actual = WGShipShieldMesh.Interpolate(vertex, colors);
            Assert.That(actual.R, Is.EqualTo(vertex.Position.X / 100f + 0.2f).Within(0.00001f));
            Assert.That(actual.G, Is.EqualTo(vertex.Position.Y / 100f + 0.2f).Within(0.00001f));
        }
    }

    [Test]
    public void ShortConcaveCornersDoNotInvertRibbonTriangles()
    {
        var contours = new[] { new[]
        {
            new Vector2(0f, 0f), new Vector2(8f, 0f), new Vector2(8f, 2f),
            new Vector2(2.35f, 2f), new Vector2(2.175f, 2.175f), new Vector2(2f, 2.35f),
            new Vector2(2f, 8f), new Vector2(0f, 8f),
        } };
        AssertValidMesh(new WGShipShieldMesh(contours, false));
        AssertValidMesh(new WGShipShieldMesh(contours, true));
    }

    [Test]
    public void RoundedHullHasVisibleHexesAndDistantMeshDropsDetail()
    {
        var contours = ShipContours();
        var detailed = new WGShipShieldMesh(contours, false);
        var distant = new WGShipShieldMesh(contours, true);
        AssertValidMesh(detailed);
        AssertValidMesh(distant);
        Assert.Multiple(() =>
        {
            Assert.That(detailed.HexLines.Count, Is.GreaterThan(0));
            Assert.That(detailed.HexLines.Max(v => v.Alpha), Is.GreaterThanOrEqualTo(0.1f), "Idle hex cells need visible contrast.");
            Assert.That(distant.HexLines, Is.Empty);
            Assert.That(distant.Triangles.Count, Is.LessThan(detailed.Triangles.Count));
            Assert.That(distant.Samples.Count, Is.LessThan(detailed.Samples.Count));
            Assert.That(distant.Triangles.Count, Is.LessThanOrEqualTo(contours.Sum(c => c.Length) * 24));
        });
    }

    [Test]
    public void StationSizedDistantOvalsHaveBoundedGeometry()
    {
        foreach (var radius in new[] { 100f, 1000f, 10000f })
        {
            var points = new Vector2[512];
            var legacyVertices = 0;
            for (var i = 0; i < points.Length; i++)
            {
                var angle = i * MathF.Tau / points.Length;
                points[i] = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius * 0.5f);
            }
            for (var i = 0; i < points.Length; i++)
                legacyVertices += Math.Clamp((int) MathF.Ceiling(Vector2.Distance(points[i], points[(i + 1) % points.Length]) / 5f), 1, 128) * 18;
            var mesh = new WGShipShieldMesh(new[] { points }, true);
            AssertValidMesh(mesh);
            Assert.That(mesh.Triangles.Count, Is.EqualTo(96 * 18));
            Assert.That(mesh.Samples.Count, Is.LessThanOrEqualTo(96 * 4));
            Assert.That(mesh.HexLines, Is.Empty);
            foreach (var vertex in mesh.Triangles)
            {
                var normalized = new Vector2(vertex.Position.X / radius, vertex.Position.Y / (radius * 0.5f)).Length();
                Assert.That(normalized, Is.InRange(1f - 0.31f / (radius * 0.5f), 1f + 0.13f / (radius * 0.5f)));
            }
            var hitAngle = MathF.PI / 96f;
            var hit = new Vector2(MathF.Cos(hitAngle) * radius, MathF.Sin(hitAngle) * radius * 0.5f);
            var distance = mesh.Samples.Min(p => Vector2.Distance(p, hit));
            Assert.That(WGShipShieldEffects.Flash(MathF.Max(0f, distance - mesh.ImpactSampleRadius), 0.02f, 1f),
                Is.GreaterThan(0.5f), "Hits between distant vertices must retain their flash.");
            TestContext.Progress.WriteLine($"Distant oval radius {radius}: legacy {legacyVertices} vertices; bounded {mesh.Triangles.Count} vertices / {mesh.Samples.Count} impact samples.");
        }
    }

    [Test]
    public void HealthSnapshotsKeepIdenticalContourGeometry()
    {
        var contours = ShipContours();
        var received = contours.Select(c => c.ToArray()).ToArray();
        Assert.That(WGShipShieldMesh.ContoursEqual(contours, received), Is.True);
        received[0][0] += new Vector2(0.25f, 0f);
        Assert.That(WGShipShieldMesh.ContoursEqual(contours, received), Is.False);
        Assert.That(WGShipShieldMesh.ContoursEqual(contours, Array.Empty<Vector2[]>()), Is.False);
    }
    [Test]
    public void GeometryPreparationBenchmark()
    {
        const int ships = 3;
        const int frames = 12;
        var contours = ShipContours();
        _ = new WGShipShieldMesh(contours, true);
        var rebuiltVertices = 0L;
        var watch = Stopwatch.StartNew();
        var allocations = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < frames; frame++)
        for (var ship = 0; ship < ships; ship++)
            rebuiltVertices += new WGShipShieldMesh(contours, true).Triangles.Count;
        var rebuiltBytes = GC.GetAllocatedBytesForCurrentThread() - allocations;
        var rebuiltMs = watch.Elapsed.TotalMilliseconds;
        var cached = new WGShipShieldMesh[ships];
        watch.Restart();
        allocations = GC.GetAllocatedBytesForCurrentThread();
        for (var ship = 0; ship < ships; ship++)
            cached[ship] = new WGShipShieldMesh(contours, true);
        var cachedVertices = 0L;
        for (var frame = 0; frame < frames; frame++)
        for (var ship = 0; ship < ships; ship++)
            cachedVertices += cached[ship].Triangles.Count;
        var cachedBytes = GC.GetAllocatedBytesForCurrentThread() - allocations;
        Assert.That(cachedVertices, Is.EqualTo(rebuiltVertices));
        Assert.That(cachedBytes, Is.LessThan(rebuiltBytes));
        TestContext.Progress.WriteLine($"Shield geometry preparation, {ships} ships x {frames} frames: " +
            $"rebuild {rebuiltMs:F2} ms / {rebuiltBytes} bytes / {ships * frames} builds; " +
            $"cached {watch.Elapsed.TotalMilliseconds:F2} ms / {cachedBytes} bytes / {ships} builds. " +
            "This measures CPU mesh preparation, not full rendering frame time.");
    }

    [Test]
    public void LongStraightEdgesKeepFullRibbonWidthBetweenCorners()
    {
        var midpoint = WGShipShieldMesh.EdgeNormal(Vector2.Zero, new Vector2(20f, 0f),
            new Vector2(0f, 0.08f), new Vector2(0f, 0.08f), 0.5f);
        Assert.That(midpoint.Length(), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(midpoint.Y, Is.GreaterThan(0.99f));
    }
    [Test]
    public void HexOpacityFadesSmoothlyThroughTheFullFieldDepth()
    {
        Assert.That(WGShipShieldMesh.HexOpacity(0f), Is.GreaterThan(0.2f));
        Assert.That(WGShipShieldMesh.HexOpacity(WGShipShieldMesh.InwardDepth), Is.Zero);
        Assert.That(WGShipShieldMesh.HexOpacity(WGShipShieldMesh.InwardDepth + 1f), Is.Zero);
        var previous = WGShipShieldMesh.HexOpacity(0f);
        for (var step = 1; step <= 16; step++)
        {
            var opacity = WGShipShieldMesh.HexOpacity(WGShipShieldMesh.InwardDepth * step / 16f);
            Assert.That(opacity, Is.LessThan(previous));
            previous = opacity;
        }
    }

    [Test]
    public void HexLatticeReachesBeyondTwoTilesAndFadesBeforeTheHull()
    {
        var contours = new[] { new[]
        {
            Vector2.Zero, new Vector2(20f, 0f), new Vector2(20f, 12f), new Vector2(0f, 12f),
        } };
        var mesh = new WGShipShieldMesh(contours, false);
        var deepest = mesh.HexLines.Max(v => MathF.Min(MathF.Min(v.Position.X, 20f - v.Position.X),
            MathF.Min(v.Position.Y, 12f - v.Position.Y)));
        Assert.That(deepest, Is.GreaterThan(2f));
        Assert.That(mesh.HexLines.Any(v => v.Alpha < 0.05f &&
            MathF.Min(MathF.Min(v.Position.X, 20f - v.Position.X), MathF.Min(v.Position.Y, 12f - v.Position.Y)) > 3f), Is.True);
        AssertValidMesh(mesh);
    }
    [Test]
    public void ConcaveJoinKeepsHexEdgesOnRegularCartesianAxes()
    {
        var contours = new[] { new[]
        {
            Vector2.Zero, new Vector2(20f, 0f), new Vector2(20f, 6f),
            new Vector2(6f, 6f), new Vector2(6f, 20f), new Vector2(0f, 20f),
        } };
        var mesh = new WGShipShieldMesh(contours, false);
        var checkedLines = 0;
        var nearJoin = 0;
        for (var i = 0; i < mesh.HexLines.Count; i += 2)
        {
            var a = mesh.HexLines[i].Position;
            var b = mesh.HexLines[i + 1].Position;
            var delta = b - a;
            if (delta.LengthSquared() < 0.0025f)
                continue;
            checkedLines++;
            if (Vector2.Distance((a + b) * 0.5f, new Vector2(6f, 6f)) < 4f)
                nearJoin++;
            var angle = MathF.Atan2(delta.Y, delta.X);
            Assert.That(MathF.Abs(MathF.Sin(angle * 3f)), Is.LessThan(0.001f),
                "Concave joins must clip regular hex cells without compressing or fanning their edges.");
            foreach (var point in new[] { a, (a + b) * 0.5f, b })
                Assert.That(WGShipShieldMesh.SignedDistance(contours, point), Is.InRange(-0.06f, WGShipShieldMesh.InwardDepth + 0.06f));
        }
        Assert.That(checkedLines, Is.GreaterThan(100));
        Assert.That(nearJoin, Is.GreaterThan(5));
        AssertValidMesh(mesh);
    }

    [Test]
    public void DistanceFieldRespectsConcaveNotchesAndDisconnectedSections()
    {
        var contours = new[]
        {
            new[] { Vector2.Zero, new Vector2(20f, 0f), new Vector2(20f, 6f), new Vector2(6f, 6f), new Vector2(6f, 20f), new Vector2(0f, 20f) },
            new[] { new Vector2(30f, 0f), new Vector2(34f, 0f), new Vector2(34f, 4f), new Vector2(30f, 4f) },
        };
        Assert.Multiple(() =>
        {
            Assert.That(WGShipShieldMesh.SignedDistance(contours, new Vector2(3f, 10f)), Is.EqualTo(3f).Within(0.0001f));
            Assert.That(WGShipShieldMesh.SignedDistance(contours, new Vector2(10f, 10f)), Is.EqualTo(-4f).Within(0.0001f));
            Assert.That(WGShipShieldMesh.SignedDistance(contours, new Vector2(32f, 2f)), Is.EqualTo(2f).Within(0.0001f));
            Assert.That(WGShipShieldMesh.SignedDistance(contours, new Vector2(26f, 2f)), Is.EqualTo(-4f).Within(0.0001f));
        });
    }
    private static Vector2[][] ShipContours()
    {
        var tiles = new List<Vector2i>();
        for (var x = 0; x < 48; x++)
        for (var y = 0; y < 24; y++)
        {
            if (x < 12 || y < 8 || x > 32)
                tiles.Add(new Vector2i(x, y));
        }
        return WGShipShieldGeometry.CreateContours(tiles);
    }

    private static void AssertValidMesh(WGShipShieldMesh mesh)
    {
        Assert.That(mesh.Triangles.Count % 3, Is.Zero);
        Assert.That(mesh.HexLines.Count % 2, Is.Zero);
        Assert.That(mesh.Triangles, Is.Not.Empty);
        foreach (var vertex in mesh.Triangles.Concat(mesh.HexLines))
        {
            Assert.That(float.IsFinite(vertex.Position.X) && float.IsFinite(vertex.Position.Y), Is.True);
            Assert.That(vertex.Alpha, Is.InRange(0f, 1f));
            Assert.That(vertex.Sample, Is.InRange(0, mesh.Samples.Count - 1));
        }
        for (var i = 0; i < mesh.Triangles.Count; i += 3)
        {
            var a = mesh.Triangles[i].Position;
            var b = mesh.Triangles[i + 1].Position;
            var c = mesh.Triangles[i + 2].Position;
            var ab = b - a;
            var ac = c - a;
            Assert.That(ab.X * ac.Y - ab.Y * ac.X, Is.GreaterThan(0.0000001f), $"Triangle {i / 3} folds or collapses.");
        }
    }
}
