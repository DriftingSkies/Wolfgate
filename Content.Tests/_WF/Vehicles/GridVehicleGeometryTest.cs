using System.Numerics;
using Content.Shared._WF.Vehicles.Grid;
using NUnit.Framework;
using Robust.Shared.Maths;
using PhysicsTransform = Robust.Shared.Physics.Transform;

namespace Content.Tests._WF.Vehicles;

/// <summary>The tile and frame maths the grid mover uses to test the floor under a vehicle and to move between a grid's frame and the world.</summary>
[TestFixture]
public sealed class GridVehicleGeometryTest
{
    [Test]
    public void ABoxCoversTheTilesItOverlaps()
    {
        Assert.That(GridVehicleGeometry.TryGetTileRange(new Box2(1.3f, 2.075f, 3.7f, 2.925f), 1f, out var min, out var max), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(min, Is.EqualTo(new Vector2i(1, 2)));
            Assert.That(max, Is.EqualTo(new Vector2i(3, 2)));
        }
    }

    [Test]
    public void AnEdgeOnATileBoundaryDoesNotReachTheNextTile()
    {
        Assert.That(GridVehicleGeometry.TryGetTileRange(new Box2(1f, 2f, 4f, 3f), 1f, out var min, out var max), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(min, Is.EqualTo(new Vector2i(1, 2)));
            Assert.That(max, Is.EqualTo(new Vector2i(3, 2)));
        }
    }

    [Test]
    public void NegativeCoordinatesRoundDown()
    {
        Assert.That(GridVehicleGeometry.TryGetTileRange(new Box2(-1.5f, -0.2f, -0.5f, 0.3f), 1f, out var min, out var max), Is.True);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(min, Is.EqualTo(new Vector2i(-2, -1)));
            Assert.That(max, Is.EqualTo(new Vector2i(-1, 0)));
        }
    }

    [TestCase(0f, 1f)]
    [TestCase(1f, 0f)]
    public void AnEmptyBoxCoversNoTile(float width, float height)
    {
        Assert.That(GridVehicleGeometry.TryGetTileRange(new Box2(2f, 2f, 2f + width, 2f + height), 1f, out _, out _), Is.False);
    }

    [Test]
    public void TheOverlapIsTheAreaOfTheTileTheBoxCovers()
    {
        var box = new Box2(18.15f, 2.075f, 20.45f, 2.925f);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(GridVehicleGeometry.GetOverlapArea(box, new Vector2i(19, 2), 1f), Is.EqualTo(0.85f).Within(0.0001f), "The whole height of the box crosses the tile.");
            Assert.That(GridVehicleGeometry.GetOverlapArea(box, new Vector2i(20, 2), 1f), Is.EqualTo(0.45f * 0.85f).Within(0.0001f));
            Assert.That(GridVehicleGeometry.GetOverlapArea(box, new Vector2i(21, 2), 1f), Is.Zero);
            Assert.That(GridVehicleGeometry.GetOverlapArea(box, new Vector2i(20, 3), 1f), Is.Zero, "A tile the box does not reach.");
        }
    }

    [Test]
    public void ATileTheBoxOnlyTouchesHasNoOverlap()
    {
        Assert.That(GridVehicleGeometry.GetOverlapArea(new Box2(0f, 0f, 1f, 1f), new Vector2i(1, 0), 1f), Is.Zero);
    }

    [TestCase(0f, 0, -1)]
    [TestCase(90f, 1, 0)]
    [TestCase(180f, 0, 1)]
    [TestCase(270f, -1, 0)]
    [TestCase(-90f, -1, 0)]
    [TestCase(10f, 0, -1)]
    [TestCase(80f, 1, 0)]
    [TestCase(130f, 1, 0)]
    [TestCase(140f, 0, 1)]
    public void ARotationSnapsToTheNearestCardinal(float degrees, int x, int y)
    {
        Assert.That(GridVehicleGeometry.CardinalFromRotation(Angle.FromDegrees(degrees)), Is.EqualTo(new Vector2i(x, y)));
    }

    [TestCase(1, 0)]
    [TestCase(-1, 0)]
    [TestCase(0, 1)]
    [TestCase(0, -1)]
    public void ARotationBuiltFromAFacingSnapsBackToIt(int x, int y)
    {
        var facing = new Vector2i(x, y);
        var rotation = new Vector2(x, y).ToWorldAngle();

        Assert.That(GridVehicleGeometry.CardinalFromRotation(rotation), Is.EqualTo(facing));
    }

    [Test]
    public void ABoxInATurnedGridsFrameBecomesTheBoxAroundItsWorldShape()
    {
        var gridTransform = new PhysicsTransform(new Vector2(10f, 0f), Angle.FromDegrees(90f));
        var world = GridVehicleGeometry.ToWorldBounds(new Box2(0f, 0f, 4f, 1f), gridTransform);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(world.Left, Is.EqualTo(9f).Within(0.0001f));
            Assert.That(world.Right, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(world.Bottom, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(world.Top, Is.EqualTo(4f).Within(0.0001f));
        }
    }

    [Test]
    public void ABoxInATurnedGridsFrameStaysTheSameSizeWhenTheGridIsNotTurned()
    {
        var world = GridVehicleGeometry.ToWorldBounds(new Box2(2f, 3f, 4f, 5f), new PhysicsTransform(new Vector2(1f, 1f), Angle.Zero));

        Assert.That(world, Is.EqualTo(new Box2(3f, 4f, 5f, 6f)));
    }

    private static readonly Box2 Hull = new(-0.425f, -1.2f, 0.425f, 1.2f);

    private static Box2 Tile(float x, float y)
    {
        return new Box2(x, y, x + 1f, y + 1f);
    }

    /// <summary>Square to the tiles the turned box is the upright one: it meets what it covers and not what it only touches.</summary>
    [TestCase(0f, -1f, true)]
    [TestCase(0.425f, 0f, false)]
    [TestCase(0.4f, 0f, true)]
    [TestCase(0f, 1.2f, false)]
    public void TurnedBoxSquareToTheTiles(float x, float y, bool expected)
    {
        Assert.That(GridVehicleGeometry.Overlaps(Vector2.Zero, Angle.Zero, Hull, Tile(x, y)), Is.EqualTo(expected));
    }

    /// <summary>At 45 degrees the hull lies along one diagonal of the upright box round it and leaves the other diagonal's corners free.</summary>
    [Test]
    public void TurnedBoxLeavesItsBoundingCornersFree()
    {
        var rotation = Angle.FromDegrees(45);
        var centre = new Vector2(10f, 10f);
        var along = rotation.RotateVec(Vector2.UnitY);
        var across = rotation.RotateVec(Vector2.UnitX);
        var small = new Vector2(0.2f, 0.2f);

        Assert.Multiple(() =>
        {
            Assert.That(GridVehicleGeometry.Overlaps(centre, rotation, Hull, Box2.CenteredAround(centre + along * 1.1f, small)), Is.True,
                "What lies along the hull is met.");
            Assert.That(GridVehicleGeometry.Overlaps(centre, rotation, Hull, Box2.CenteredAround(centre + across * 1.1f, small)), Is.False,
                "What lies a full tile to the hull's side is not, though the upright box round the hull covers it.");
        });
    }
}
