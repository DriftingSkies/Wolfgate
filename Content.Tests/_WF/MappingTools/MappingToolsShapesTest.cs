#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingTools;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.MappingTools;

[TestFixture]
public sealed class MappingToolsShapesTest
{
    private static Vector2i V(int x, int y) => new(x, y);

    [Test]
    public void LineAlongAnAxis()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsShapes.Line(V(0, 0), V(3, 0)), Is.EqualTo(new[] { V(0, 0), V(1, 0), V(2, 0), V(3, 0) }));
            Assert.That(MappingToolsShapes.Line(V(2, 1), V(2, 4)), Is.EqualTo(new[] { V(2, 1), V(2, 2), V(2, 3), V(2, 4) }));
        });
    }

    [Test]
    public void LineOnTheDiagonal()
    {
        Assert.That(MappingToolsShapes.Line(V(0, 0), V(3, -3)), Is.EqualTo(new[] { V(0, 0), V(1, -1), V(2, -2), V(3, -3) }));
    }

    [Test]
    public void LineIsConnectedWithoutRepeats()
    {
        var cells = MappingToolsShapes.Line(V(-2, 1), V(7, 5));
        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(10), "One cell per step of the longer axis.");
            Assert.That(cells, Is.Unique);
            Assert.That(cells[0], Is.EqualTo(V(-2, 1)));
            Assert.That(cells[^1], Is.EqualTo(V(7, 5)));
            for (var i = 1; i < cells.Count; i++)
            {
                var step = cells[i] - cells[i - 1];
                Assert.That(Math.Abs(step.X) <= 1 && Math.Abs(step.Y) <= 1, Is.True, $"Gap between {cells[i - 1]} and {cells[i]}.");
            }
        });
    }

    [Test]
    public void LineOfOneCell()
    {
        Assert.That(MappingToolsShapes.Line(V(4, -2), V(4, -2)), Is.EqualTo(new[] { V(4, -2) }));
    }

    [Test]
    public void LineReversedHasTheSameCells()
    {
        var forward = MappingToolsShapes.Line(V(0, 0), V(5, 2));
        var backward = MappingToolsShapes.Line(V(5, 2), V(0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(backward[0], Is.EqualTo(V(5, 2)));
            Assert.That(backward[^1], Is.EqualTo(V(0, 0)));
            Assert.That(backward, Has.Count.EqualTo(forward.Count));
        });
    }

    [Test]
    public void FilledRect()
    {
        var cells = MappingToolsShapes.Rect(V(2, 3), V(0, 1), false);
        var expected = new List<Vector2i>();
        for (var y = 1; y <= 3; y++)
        {
            for (var x = 0; x <= 2; x++)
            {
                expected.Add(V(x, y));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.EquivalentTo(expected));
            Assert.That(MappingToolsShapes.RectCount(V(2, 3), V(0, 1), false), Is.EqualTo(9));
        });
    }

    [Test]
    public void HollowRect()
    {
        var cells = MappingToolsShapes.Rect(V(0, 0), V(3, 2), true);
        Assert.Multiple(() =>
        {
            Assert.That(cells, Has.Count.EqualTo(10));
            Assert.That(cells, Is.Unique);
            Assert.That(cells, Does.Not.Contain(V(1, 1)));
            Assert.That(cells, Does.Not.Contain(V(2, 1)));
            Assert.That(cells.All(c => c.X is 0 or 3 || c.Y is 0 or 2), Is.True);
            Assert.That(MappingToolsShapes.RectCount(V(0, 0), V(3, 2), true), Is.EqualTo(10));
        });
    }

    /// <summary>Boxes of every shape class: square, wide, tall, odd and even sides, off the origin.</summary>
    private static readonly (Vector2i A, Vector2i B)[] EllipseBoxes =
    {
        (V(0, 0), V(9, 9)),
        (V(0, 0), V(10, 10)),
        (V(-7, 3), V(12, 8)),
        (V(4, -20), V(6, 9)),
        (V(0, 0), V(1, 11)),
        (V(0, 0), V(30, 2)),
        (V(-50, -40), V(49, 39)),
        (V(0, 0), V(2, 2)),
        (V(0, 0), V(3, 1)),
    };

    private static (Vector2i Min, Vector2i Max) BoxOf(Vector2i a, Vector2i b)
    {
        return (Vector2i.ComponentMin(a, b), Vector2i.ComponentMax(a, b));
    }

    [Test]
    public void EllipseIsSymmetricAndTouchesEverySide()
    {
        Assert.Multiple(() =>
        {
            foreach (var (a, b) in EllipseBoxes)
            {
                var (min, max) = BoxOf(a, b);
                foreach (var hollow in new[] { false, true })
                {
                    var cells = MappingToolsShapes.Ellipse(a, b, hollow);
                    var set = cells.ToHashSet();
                    var name = $"{a} {b} hollow {hollow}";
                    Assert.That(cells, Is.Unique, name);
                    Assert.That(cells.All(c => c.X >= min.X && c.X <= max.X && c.Y >= min.Y && c.Y <= max.Y), Is.True,
                        $"{name}: inside the box");
                    Assert.That(cells.All(c => set.Contains(V(min.X + max.X - c.X, c.Y)) &&
                                               set.Contains(V(c.X, min.Y + max.Y - c.Y))),
                        Is.True, $"{name}: mirror symmetric both ways");
                    Assert.That(cells.Any(c => c.X == min.X) && cells.Any(c => c.X == max.X) &&
                                cells.Any(c => c.Y == min.Y) && cells.Any(c => c.Y == max.Y),
                        Is.True, $"{name}: touches all four sides");
                    Assert.That(MappingToolsShapes.Ellipse(b, a, hollow), Is.EquivalentTo(cells), $"{name}: corner order");
                }
            }
        });
    }

    [Test]
    public void EllipseRowsAndColumnsHaveNoGaps()
    {
        Assert.Multiple(() =>
        {
            foreach (var (a, b) in EllipseBoxes)
            {
                var (min, max) = BoxOf(a, b);
                var cells = MappingToolsShapes.Ellipse(a, b, false);
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var row = cells.Where(c => c.Y == y).Select(c => c.X).ToList();
                    Assert.That(row, Is.Not.Empty, $"{a} {b}: row {y}");
                    Assert.That(row.Max() - row.Min() + 1, Is.EqualTo(row.Count), $"{a} {b}: row {y} is one run");
                }

                for (var x = min.X; x <= max.X; x++)
                {
                    var column = cells.Where(c => c.X == x).Select(c => c.Y).ToList();
                    Assert.That(column, Is.Not.Empty, $"{a} {b}: column {x}");
                    Assert.That(column.Max() - column.Min() + 1, Is.EqualTo(column.Count),
                        $"{a} {b}: column {x} is one run");
                }
            }
        });
    }

    [Test]
    public void HollowEllipseIsAClosedRingOneCellThick()
    {
        Assert.Multiple(() =>
        {
            foreach (var (a, b) in EllipseBoxes)
            {
                var (min, max) = BoxOf(a, b);
                var filled = MappingToolsShapes.Ellipse(a, b, false).ToHashSet();
                var ring = MappingToolsShapes.Ellipse(a, b, true).ToHashSet();
                var name = $"{a} {b}";
                var sides = new[] { V(1, 0), V(-1, 0), V(0, 1), V(0, -1) };

                Assert.That(ring.IsSubsetOf(filled), Is.True, $"{name}: the ring is the filled shape's edge");
                Assert.That(ring.All(c => sides.Any(s => !filled.Contains(c + s))), Is.True,
                    $"{name}: every ring cell borders the outside, so it is one cell thick");
                Assert.That(filled.Except(ring).All(c => sides.All(s => filled.Contains(c + s))), Is.True,
                    $"{name}: every cell left out has all four sides inside");

                // Closed: walking side to side from outside the box never reaches a cell inside the ring.
                var outside = new HashSet<Vector2i>();
                var queue = new Queue<Vector2i>();
                queue.Enqueue(min - V(1, 1));
                outside.Add(min - V(1, 1));
                while (queue.TryDequeue(out var cell))
                {
                    foreach (var side in sides)
                    {
                        var next = cell + side;
                        if (next.X < min.X - 1 || next.X > max.X + 1 || next.Y < min.Y - 1 || next.Y > max.Y + 1 ||
                            ring.Contains(next) || !outside.Add(next))
                        {
                            continue;
                        }

                        queue.Enqueue(next);
                    }
                }

                Assert.That(filled.Except(ring).Any(outside.Contains), Is.False, $"{name}: the ring has no gap");

                // The ring hangs together corner to corner.
                var start = ring.First();
                var reached = new HashSet<Vector2i> { start };
                var walk = new Queue<Vector2i>();
                walk.Enqueue(start);
                while (walk.TryDequeue(out var cell))
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            var next = cell + V(dx, dy);
                            if (ring.Contains(next) && reached.Add(next))
                                walk.Enqueue(next);
                        }
                    }
                }

                Assert.That(reached, Has.Count.EqualTo(ring.Count), $"{name}: the ring is one piece");
            }
        });
    }

    [Test]
    public void EllipseDegenerateBoxes()
    {
        Assert.Multiple(() =>
        {
            foreach (var hollow in new[] { false, true })
            {
                Assert.That(MappingToolsShapes.Ellipse(V(4, -2), V(4, -2), hollow), Is.EqualTo(new[] { V(4, -2) }),
                    $"1x1, hollow {hollow}");

                var column = MappingToolsShapes.Ellipse(V(0, 0), V(0, 6), hollow);
                Assert.That(column, Is.EquivalentTo(Enumerable.Range(0, 7).Select(y => V(0, y))),
                    $"1x7 is the whole column, hollow {hollow}");

                var row = MappingToolsShapes.Ellipse(V(-3, 2), V(5, 2), hollow);
                Assert.That(row, Is.EquivalentTo(Enumerable.Range(-3, 9).Select(x => V(x, 2))),
                    $"9x1 is the whole row, hollow {hollow}");

                Assert.That(MappingToolsShapes.Ellipse(V(0, 0), V(1, 1), hollow), Has.Count.EqualTo(4),
                    $"2x2 fills its box, hollow {hollow}");
            }
        });
    }

    [Test]
    public void EllipseCellCountBounds()
    {
        Assert.Multiple(() =>
        {
            foreach (var (a, b) in EllipseBoxes)
            {
                var (min, max) = BoxOf(a, b);
                long w = max.X - min.X + 1;
                long h = max.Y - min.Y + 1;
                var filled = MappingToolsShapes.Ellipse(a, b, false).Count;
                var ring = MappingToolsShapes.Ellipse(a, b, true).Count;
                var name = $"{a} {b}";

                Assert.That(filled, Is.LessThanOrEqualTo(w * h), $"{name}: within the box");
                Assert.That(ring, Is.LessThanOrEqualTo(filled), $"{name}: the ring is part of the fill");
                Assert.That(ring, Is.LessThanOrEqualTo(2 * (w + h)), $"{name}: the ring is about the perimeter");
                if (w >= 8 && h >= 8)
                {
                    // Close to the area of the inscribed ellipse, pi/4 of the box.
                    Assert.That(filled, Is.InRange(0.7 * w * h, 0.86 * w * h), $"{name}: about pi/4 of the box");
                    Assert.That(ring, Is.LessThan(filled), $"{name}: a big ring leaves an inside");
                }
            }
        });
    }

    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 0, 1, 5)]
    [TestCase(-3, -3, 3, 3)]
    [TestCase(5, 0, 0, 1)]
    public void RectCountMatchesCells(int ax, int ay, int bx, int by)
    {
        Assert.Multiple(() =>
        {
            foreach (var hollow in new[] { false, true })
            {
                var cells = MappingToolsShapes.Rect(V(ax, ay), V(bx, by), hollow);
                Assert.That(MappingToolsShapes.RectCount(V(ax, ay), V(bx, by), hollow), Is.EqualTo(cells.Count),
                    $"hollow {hollow}");
            }
        });
    }

    [Test]
    public void RectCountWithoutBuildingIt()
    {
        Assert.That(MappingToolsShapes.RectCount(V(0, 0), V(99999, 99999), false), Is.EqualTo(10_000_000_000L));
    }

    /// <summary>A 7x7 grid: a ring of walls (type 2) round floor (type 1), with space (0) outside.</summary>
    private static int? Room(Vector2i cell)
    {
        if (cell.X is < 0 or > 6 || cell.Y is < 0 or > 6)
            return 0;

        return cell.X is 0 or 6 || cell.Y is 0 or 6 ? 2 : 1;
    }

    [Test]
    public void FloodFillsAnEnclosedRoom()
    {
        var cells = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(25));
            Assert.That(cells, Is.Unique);
            Assert.That(cells.All(c => Room(c) == 1), Is.True);
        });
    }

    [Test]
    public void FloodLeakStopsAtTheBounds()
    {
        // A gap in the wall lets the fill out into space; the bounds hold it.
        int? Leaky(Vector2i cell) => cell == V(6, 3) ? 1 : Room(cell) == 0 ? 1 : Room(cell);

        var bounds = new Box2i(-2, -2, 9, 9);
        var cells = MappingToolsShapes.Flood(V(3, 3), Leaky, bounds, 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(11 * 11 - 23), "Every non-wall cell in the bounds.");
            Assert.That(cells.All(c => c.X >= -2 && c.X < 9 && c.Y >= -2 && c.Y < 9), Is.True);
        });
    }

    [Test]
    public void FloodFillsEmptyCells()
    {
        var cells = MappingToolsShapes.Flood(V(-1, -1), Room, new Box2i(-1, -1, 8, 8), 1000, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.False);
            Assert.That(cells, Has.Count.EqualTo(9 * 9 - 49), "The space ring round the room, inside the bounds.");
            Assert.That(cells.All(c => Room(c) == 0), Is.True);
        });
    }

    [Test]
    public void FloodStopsAtTheCap()
    {
        var cells = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 10, out var truncated);
        Assert.Multiple(() =>
        {
            Assert.That(truncated, Is.True);
            Assert.That(cells, Has.Count.EqualTo(10));
            Assert.That(cells[0], Is.EqualTo(V(3, 3)));
        });

        var exact = MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(-10, -10, 10, 10), 25, out var exactTruncated);
        Assert.Multiple(() =>
        {
            Assert.That(exact, Has.Count.EqualTo(25));
            Assert.That(exactTruncated, Is.False, "A cap the fill just fits isn't truncation.");
        });
    }

    [Test]
    public void FloodStartingOutsideTheBoundsFillsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(4, 4, 6, 6), 100, out var truncated), Is.Empty);
            Assert.That(truncated, Is.False);
            Assert.That(MappingToolsShapes.Flood(V(6, 6), Room, new Box2i(0, 0, 6, 6), 100, out _), Is.Empty,
                "Right and top are exclusive.");
            Assert.That(MappingToolsShapes.Flood(V(3, 3), _ => null, new Box2i(0, 0, 6, 6), 100, out _), Is.Empty,
                "A cell that can't be filled.");
            Assert.That(MappingToolsShapes.Flood(V(3, 3), Room, new Box2i(0, 0, 6, 6), 0, out _), Is.Empty,
                "No cap, no cells.");
        });
    }
}
