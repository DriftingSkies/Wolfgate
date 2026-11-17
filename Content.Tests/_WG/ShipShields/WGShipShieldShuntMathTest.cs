using System;
using System.Numerics;
using NUnit.Framework;
using Content.Shared._WG.ShipShields;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks directional shield capacity and collision coverage agree.</summary>
[TestFixture]
public sealed class WGShipShieldShuntMathTest
{
    [Test]
    public void LiveAllocationTakesTheShortWayAndSettlesWithoutOvershoot()
    {
        var direction = 179f * MathF.PI / 180f;
        var target = -179f * MathF.PI / 180f;
        var first = WGShipShieldShuntMath.StepAngle(direction, target, 0.1f);
        Assert.That(WGShipShieldShuntMath.NormalizeAngle(first - direction), Is.InRange(0f, 2f * MathF.PI / 180f));
        var concentration = 0f;
        for (var step = 0; step < 100; step++)
        {
            var next = WGShipShieldShuntMath.Step(concentration, 1f, 0.1f, WGShipShieldShuntMath.TransferSpeed);
            Assert.That(next, Is.InRange(concentration, MathF.Min(1f, concentration + WGShipShieldShuntMath.TransferSpeed * 0.1f + 0.001f)));
            concentration = next;
            direction = WGShipShieldShuntMath.StepAngle(direction, target, 0.1f);
        }
        Assert.That(concentration, Is.EqualTo(1f), "Full diversion must eventually open the unprotected sectors exactly.");
        Assert.That(WGShipShieldShuntMath.NormalizeAngle(direction - target), Is.EqualTo(0f).Within(0.00001f));
        Assert.That(WGShipShieldShuntMath.Step(0.5f, 0f, 0.1f, 0.5f), Is.InRange(0.45f, 0.5f));
        Assert.That(MathF.Abs(WGShipShieldShuntMath.StepAngle(0f, MathF.PI, 0.1f)), Is.LessThanOrEqualTo(WGShipShieldShuntMath.TurnSpeed * 0.1f + 0.00001f));
    }

    [TestCase(0f, 90f)]
    [TestCase(90f, 0f)]
    [TestCase(180f, -90f)]
    [TestCase(270f, -180f)]
    public void HelmBearingsAreClockwiseAndRelativeToConsole(float bearing, float gridDegrees)
    {
        const float helmRotation = 0.63f;
        var direction = WGShipShieldHelmAngles.GridDirection(bearing, helmRotation);
        Assert.That(direction, Is.EqualTo(helmRotation + gridDegrees * MathF.PI / 180f).Within(0.0001f));
        var roundTrip = WGShipShieldHelmAngles.Bearing(direction + 4f * MathF.PI, helmRotation);
        Assert.That(MathF.Abs(WGShipShieldShuntMath.NormalizeAngle((roundTrip - bearing) * MathF.PI / 180f)), Is.LessThan(0.0001f));
    }
    [TestCase(0f, 1f, 1f)]
    [TestCase(0.5f, 1.2937f, 0.5f)]
    [TestCase(1f, 1.5874f, 0f)]
    public void QuarterArcTransfersPowerAndResetRestoresCoverage(float amount, float reinforced, float remainder)
    {
        Assert.That(WGShipShieldShuntMath.StrengthMultiplier(Vector2.UnitX, Vector2.Zero, 0f, amount, MathF.PI / 2f), Is.EqualTo(reinforced).Within(0.0001f));
        Assert.That(WGShipShieldShuntMath.StrengthMultiplier(-Vector2.UnitX, Vector2.Zero, 0f, amount, MathF.PI / 2f), Is.EqualTo(remainder).Within(0.0001f));
    }

    [TestCase(31f, 30f)]
    [TestCase(179f, 90f)]
    [TestCase(-191f, 120f)]
    [TestCase(721f, 270f)]
    public void ArbitraryDirectionsNeverAddCapacityAndWrap(float directionDegrees, float arcDegrees)
    {
        var direction = directionDegrees * MathF.PI / 180f;
        var arc = arcDegrees * MathF.PI / 180f;
        const int samples = 3600;
        var total = 0f;
        for (var i = 0; i < samples; i++)
        {
            var angle = (i + 0.37f) * WGShipShieldShuntMath.FullArc / samples;
            var point = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var value = WGShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, direction, 0.73f, arc);
            Assert.That(value, Is.EqualTo(WGShipShieldShuntMath.StrengthMultiplier(point, Vector2.Zero, direction + WGShipShieldShuntMath.FullArc, 0.73f, arc)).Within(0.0001f));
            total += value;
        }
        Assert.That(total / samples, Is.LessThanOrEqualTo(1.002f), "Shunting must never add total capacity.");
    }

    [TestCase(30f, 2.2894f)]
    [TestCase(90f, 1.5874f)]
    [TestCase(180f, 1.2599f)]
    [TestCase(360f, 1f)]
    public void FullShuntStrengthIsCappedByCubeRoot(float arcDegrees, float expected)
    {
        Assert.That(WGShipShieldShuntMath.PeakStrength(1f, arcDegrees * MathF.PI / 180f), Is.EqualTo(expected).Within(0.001f));
        Assert.That(WGShipShieldShuntMath.PeakStrength(0f, arcDegrees * MathF.PI / 180f), Is.EqualTo(1f));
    }

    [Test]
    public void FullDiversionSplitsCoverageAtSectorRays()
    {
        var segments = WGShipShieldShuntMath.ProtectedSegments(new Vector2(2f, -4f), new Vector2(2f, 4f), Vector2.Zero, 0f, 1f, MathF.PI / 2f);
        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(Vector2.Distance(segments[0].Start, new Vector2(2f, -2f)), Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(segments[0].End, new Vector2(2f, 2f)), Is.LessThan(0.0001f));
        Assert.That(WGShipShieldShuntMath.ProtectedSegments(new Vector2(-2f, -4f), new Vector2(-2f, 4f), Vector2.Zero, 0f, 1f, MathF.PI / 2f), Is.Empty);
        Assert.That(WGShipShieldShuntMath.ProtectedSegments(new Vector2(-2f, -4f), new Vector2(-2f, 4f), Vector2.Zero, 0f, 0f, MathF.PI / 2f), Has.Count.EqualTo(1));
    }

    [Test]
    public void RotatedOffOriginHullRetainsItsLocalAllocation()
    {
        var center = new Vector2(103f, -47f);
        var angle = 0.731f;
        Vector2 Rotate(Vector2 point) => new(MathF.Cos(angle) * point.X - MathF.Sin(angle) * point.Y, MathF.Sin(angle) * point.X + MathF.Cos(angle) * point.Y);
        var segments = WGShipShieldShuntMath.ProtectedSegments(center + Rotate(new Vector2(2f, -4f)), center + Rotate(new Vector2(2f, 4f)), center, angle, 1f, MathF.PI / 2f);
        Assert.That(segments, Has.Count.EqualTo(1));
        Assert.That(Vector2.Distance(segments[0].Start, center + Rotate(new Vector2(2f, -2f))), Is.LessThan(0.0001f));
        Assert.That(Vector2.Distance(segments[0].End, center + Rotate(new Vector2(2f, 2f))), Is.LessThan(0.0001f));
        Assert.That(WGShipShieldShuntMath.StrengthMultiplier(center - Rotate(Vector2.UnitX), center, angle, 1f, MathF.PI / 2f), Is.Zero);
    }
}