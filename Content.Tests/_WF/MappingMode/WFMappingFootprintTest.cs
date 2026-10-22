using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingFootprintTest
{
    private static HashSet<Vector2i> Cover(List<Box2i> rects)
    {
        var cells = new HashSet<Vector2i>();
        foreach (var rect in rects)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                for (var y = rect.Bottom; y < rect.Top; y++)
                {
                    Assert.That(cells.Add(new Vector2i(x, y)), Is.True, $"Rectangles overlap at {x},{y}.");
                }
            }
        }

        return cells;
    }

    [Test]
    public void MergeCoversExactlyTheCells([Values(1, 2, 3, 4, 5)] int seed)
    {
        var random = new Random(seed);
        var cells = new HashSet<Vector2i>();
        for (var i = 0; i < 300; i++)
        {
            cells.Add(new Vector2i(random.Next(-12, 12), random.Next(-12, 12)));
        }

        var rects = WFMappingFootprint.Merge(cells);
        Assert.That(Cover(rects).SetEquals(cells), Is.True);
        Assert.That(rects, Has.Count.LessThanOrEqualTo(cells.Count));
    }

    [Test]
    public void FilledRectanglesMergeToOne()
    {
        var cells = new List<Vector2i>();
        for (var x = -3; x < 5; x++)
        {
            for (var y = 2; y < 6; y++)
            {
                cells.Add(new Vector2i(x, y));
            }
        }

        // Duplicates change nothing.
        cells.AddRange(cells.Take(5).ToList());

        Assert.That(WFMappingFootprint.Merge(cells), Is.EqualTo(new List<Box2i> { new(-3, 2, 5, 6) }));
    }

    [Test]
    public void GapsSplitRectangles()
    {
        // Rows 0 and 2 have the same span but row 1 is empty, so they stay apart.
        var cells = new[] { new Vector2i(0, 0), new Vector2i(1, 0), new Vector2i(0, 2), new Vector2i(1, 2) };
        var rects = WFMappingFootprint.Merge(cells);
        Assert.That(rects, Has.Count.EqualTo(2));
        Assert.That(Cover(rects).SetEquals(cells), Is.True);
    }

    [Test]
    public void EmptyMergesToNothing()
    {
        Assert.That(WFMappingFootprint.Merge(Array.Empty<Vector2i>()), Is.Empty);
    }

    private static void AssertBox(Box2 actual, Box2 expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Left, Is.EqualTo(expected.Left).Within(1e-4));
            Assert.That(actual.Bottom, Is.EqualTo(expected.Bottom).Within(1e-4));
            Assert.That(actual.Right, Is.EqualTo(expected.Right).Within(1e-4));
            Assert.That(actual.Top, Is.EqualTo(expected.Top).Within(1e-4));
        });
    }

    [Test]
    public void BoundsTurnClockwise()
    {
        var grid = new WFMappingFootprintGrid(Vector2.Zero, Angle.Zero, new List<Box2i> { new(0, 0, 2, 1) });
        var grids = new[] { grid };

        AssertBox(WFMappingFootprint.Bounds(grids, 0), new Box2(0, 0, 2, 1));
        AssertBox(WFMappingFootprint.Bounds(grids, 1), new Box2(0, -2, 1, 0));
        AssertBox(WFMappingFootprint.Bounds(grids, 2), new Box2(-2, -1, 0, 0));
        AssertBox(WFMappingFootprint.Bounds(grids, 3), new Box2(-1, 0, 0, 2));
        AssertBox(WFMappingFootprint.Bounds(grids, 4), new Box2(0, 0, 2, 1));
    }

    [Test]
    public void BoundsIncludeGridOffsetAndRotation()
    {
        var turned = new WFMappingFootprintGrid(new Vector2(10, 0), Angle.FromDegrees(90),
            new List<Box2i> { new(0, 0, 3, 1) });
        var plain = new WFMappingFootprintGrid(new Vector2(-1, -1), Angle.Zero, new List<Box2i> { new(0, 0, 1, 1) });

        // A quarter turn anticlockwise stands the 3x1 strip up to the left of its origin.
        AssertBox(WFMappingFootprint.Bounds(new[] { turned, plain }, 0), new Box2(-1, -1, 10, 3));
    }

    [Test]
    public void PlaceTurnsAboutTheAnchor()
    {
        var grid = new WFMappingFootprintGrid(new Vector2(2, 0), Angle.Zero, new List<Box2i>());
        var (position, rotation) = WFMappingFootprint.Place(grid, new Vector2(5, 5), 1);
        Assert.Multiple(() =>
        {
            Assert.That(position.X, Is.EqualTo(5).Within(1e-4));
            Assert.That(position.Y, Is.EqualTo(3).Within(1e-4));
            Assert.That(rotation.Degrees, Is.EqualTo(-90).Within(1e-4));
        });
    }
}
