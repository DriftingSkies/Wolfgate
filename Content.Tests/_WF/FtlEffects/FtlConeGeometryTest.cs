using System;
using System.Numerics;
using Content.Client._WF.FtlEffects;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.FtlEffects;

/// <summary>Checks that the cone and rendered rush follow the hull rather than the grid origin.</summary>
[TestFixture]
public sealed class FtlConeGeometryTest
{
    [TestCase(-12f, -20f, -6f, -8f)]
    [TestCase(0f, 0f, 1f, 1f)]
    [TestCase(-30f, -60f, 30f, 60f)]
    public void ConeExtendsFromMidshipToAheadOfBow(float left, float bottom, float right, float top)
    {
        var hull = new Box2(left, bottom, right, top);
        var cone = FtlConeGeometry.Bounds(hull);
        Assert.That(cone.Bottom, Is.EqualTo(hull.Center.Y));
        Assert.That(cone.Top, Is.GreaterThan(hull.Top));
        Assert.That(cone.Top - hull.Top, Is.LessThanOrEqualTo(MathF.Max(0.75f, hull.Width * 0.25f)));
        Assert.That(cone.Top - hull.Top, Is.LessThanOrEqualTo(6f));
        Assert.That(cone.Center.X, Is.EqualTo(hull.Center.X));
        Assert.That(cone.Width, Is.GreaterThan(hull.Width * 1.6f));
        Assert.That(hull.Left - cone.Left, Is.EqualTo(cone.Right - hull.Right).Within(0.001f));
    }

    [Test]
    public void RestingHullIsNeverDisplacedOrStretched()
    {
        var hull = new Box2(-12, -20, -6, -8);
        var transform = FtlConeGeometry.MotionTransform(hull, 0f);
        Assert.That(Vector2.Transform(hull.BottomLeft, transform), Is.EqualTo(hull.BottomLeft));
        Assert.That(Vector2.Transform(hull.TopRight, transform), Is.EqualTo(hull.TopRight));
    }

    [TestCase(1f)]
    [TestCase(-1f)]
    public void RushStretchesAlongForwardAxisAroundTheActualHull(float motion)
    {
        var hull = new Box2(-12, -20, -6, -8);
        var transform = FtlConeGeometry.MotionTransform(hull, motion);
        var center = Vector2.Transform(hull.Center, transform);
        Assert.That(center.X, Is.EqualTo(hull.Center.X));
        Assert.That((center.Y - hull.Center.Y) * motion, Is.GreaterThan(hull.Height));
        var moved = transform.TransformBox(hull);
        Assert.That(moved.Width, Is.EqualTo(hull.Width));
        Assert.That(moved.Height, Is.GreaterThan(hull.Height * 2f));
    }

    [Test]
    public void DockedGridAtRestIsNeverDisplaced()
    {
        var lead = Matrix3x2.CreateRotation(0.7f) * Matrix3x2.CreateTranslation(40f, -15f);
        var follower = Matrix3x2.CreateRotation(-1.2f) * Matrix3x2.CreateTranslation(52f, -9f);
        var transform = FtlConeGeometry.FollowerTransform(new Box2(-4, -10, 4, 10), 0f, follower, lead);
        var point = new Vector2(3f, -2f);
        Assert.That(Vector2.Distance(Vector2.Transform(point, transform), Vector2.Transform(point, follower)),
            Is.LessThan(0.001f));
    }

    [TestCase(1f)]
    [TestCase(-0.4f)]
    public void DockedGridRidesTheLeadShipsRush(float motion)
    {
        var hull = new Box2(-4, -10, 4, 10);
        var lead = Matrix3x2.CreateRotation(0.7f) * Matrix3x2.CreateTranslation(40f, -15f);
        var follower = Matrix3x2.CreateRotation(-1.2f) * Matrix3x2.CreateTranslation(52f, -9f);
        Assert.That(Matrix3x2.Invert(lead, out var leadInverse), Is.True);
        Assert.That(Matrix3x2.Invert(follower, out var followerInverse), Is.True);

        // A point of space shared by both grids must land in the same place whichever grid draws it.
        var world = new Vector2(47f, -11f);
        var viaLead = Vector2.Transform(Vector2.Transform(world, leadInverse), FtlConeGeometry.MotionTransform(hull, motion) * lead);
        var viaFollower = Vector2.Transform(Vector2.Transform(world, followerInverse),
            FtlConeGeometry.FollowerTransform(hull, motion, follower, lead));
        Assert.That(Vector2.Distance(viaLead, viaFollower), Is.LessThan(0.01f));
        Assert.That(Vector2.Distance(viaLead, world), Is.GreaterThan(5f));
    }
}
