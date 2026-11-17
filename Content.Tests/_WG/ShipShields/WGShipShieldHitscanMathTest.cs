using System;
using System.Numerics;
using Content.Shared._WG.ShipShields;
using NUnit.Framework;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks beam interception against shield range, perimeter and directional gaps.</summary>
[TestFixture]
public sealed class WGShipShieldHitscanMathTest
{
    private static readonly Vector2[][] Hull = { new[]
    {
        new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(2f, 2f), new Vector2(-2f, 2f),
    } };

    [TestCase(0f, true)]
    [TestCase(-10f, false)]
    public void FriendlyOriginMustBeInsidePerimeter(float x, bool inside)
    {
        Assert.That(WGShipShieldHitscanMath.Contains(Hull, new Vector2(x, 0f)), Is.EqualTo(inside));
    }

    [Test]
    public void IncomingBeamStopsAtNearPerimeter()
    {
        Assert.That(WGShipShieldHitscanMath.TryIntersect(Hull, new Vector2(-10f, 0f), Vector2.UnitX,
            20f, null, out var distance, out var point, out var strength), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(distance, Is.EqualTo(8f).Within(0.0001f));
            Assert.That(point, Is.EqualTo(new Vector2(-2f, 0f)));
            Assert.That(strength, Is.EqualTo(1f));
        });
    }

    [TestCase(7f, 0f)]
    [TestCase(20f, 3f)]
    public void ObstacleBeforeShieldAndMissDoNotIntercept(float maximum, float height)
    {
        Assert.That(WGShipShieldHitscanMath.TryIntersect(Hull, new Vector2(-10f, height), Vector2.UnitX,
            maximum, null, out _, out _, out _), Is.False);
    }

    [TestCase(1f, false, 0f)]
    [TestCase(0.5f, true, 0.5f)]
    public void FullyDivertedGapPassesAndPartialGapRetainsCapacity(float concentration, bool intercepted, float expected)
    {
        var allocation = new WGShipShieldShuntComponent
        {
            Center = Vector2.Zero,
            DirectionRadians = 0f,
            Concentration = concentration,
            ArcRadians = MathF.PI / 2f,
        };
        Assert.That(WGShipShieldHitscanMath.TryIntersect(Hull, new Vector2(-10f, 0f), Vector2.UnitX,
            10f, allocation, out _, out _, out var strength), Is.EqualTo(intercepted));
        Assert.That(strength, Is.EqualTo(expected));
    }

    [Test]
    public void ReinforcedSectorUsesAllocationStrength()
    {
        var allocation = new WGShipShieldShuntComponent
        {
            DirectionRadians = MathF.PI,
            Concentration = 1f,
            ArcRadians = MathF.PI / 2f,
        };
        Assert.That(WGShipShieldHitscanMath.TryIntersect(Hull, new Vector2(-10f, 0f), Vector2.UnitX,
            20f, allocation, out _, out _, out var strength), Is.True);
        Assert.That(strength, Is.EqualTo(1.5874f).Within(0.0001f));
    }

    [Test]
    public void LocalRayPreservesWorldDistanceUnderRotation()
    {
        var world = Matrix3x2.CreateRotation(0.71f) * Matrix3x2.CreateTranslation(35f, -12f);
        Matrix3x2.Invert(world, out var inverse);
        var origin = Vector2.Transform(new Vector2(-10f, 0f), world);
        var direction = Vector2.TransformNormal(Vector2.UnitX, world);
        Assert.That(WGShipShieldHitscanMath.TryIntersect(Hull, Vector2.Transform(origin, inverse),
            Vector2.TransformNormal(direction, inverse), 20f, null, out var distance, out _, out _), Is.True);
        Assert.That(distance, Is.EqualTo(8f).Within(0.0001f));
    }
}
