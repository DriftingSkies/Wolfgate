using System;
using System.Numerics;
using Content.Shared._WG.ShipFacing;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.ShipFacing;

/// <summary>Checks the facing key's turn input against a hull that turns the way the helm drives it.</summary>
[TestFixture]
public sealed class WGShipFacingSteeringTest
{
    private const float FrameTime = 1f / 30f;
    private const float MaxSpeed = 4f;

    [Test]
    public void HeadingPointsTheNoseAtThePoint()
    {
        Assert.That(WGShipFacingSteering.Heading(Vector2.Zero, new Vector2(0, 5)).Theta, Is.EqualTo(0).Within(1e-5));
        Assert.That(WGShipFacingSteering.Heading(Vector2.Zero, new Vector2(5, 0)).Theta, Is.EqualTo(-Math.PI / 2).Within(1e-5));
        Assert.That(new Angle(WGShipFacingSteering.Heading(Vector2.Zero, new Vector2(-5, 0))).RotateVec(Vector2.UnitY).X,
            Is.EqualTo(-1).Within(1e-5));
    }

    [Test]
    public void APointToTheRightTurnsClockwise()
    {
        // Positive input is the rotate-right key.
        var input = WGShipFacingSteering.TurnInput(Angle.Zero, 0f, Vector2.Zero, new Vector2(10, 0), 2f, MaxSpeed, FrameTime);
        Assert.That(input, Is.EqualTo(1f));
        input = WGShipFacingSteering.TurnInput(Angle.Zero, 0f, Vector2.Zero, new Vector2(-10, 0), 2f, MaxSpeed, FrameTime);
        Assert.That(input, Is.EqualTo(-1f));
    }

    [Test]
    public void APointOnTheHullGivesNoInput()
    {
        var input = WGShipFacingSteering.TurnInput(Angle.Zero, 0f, Vector2.Zero, new Vector2(0.5f, 0), 2f, MaxSpeed, FrameTime);
        Assert.That(input, Is.Zero);
    }

    [TestCase(0.5f, 10f, 0f)]
    [TestCase(2f, -10f, 0f)]
    [TestCase(8f, 0f, -10f)]
    [TestCase(2f, -1f, -10f)]
    public void TheHullSettlesOnTheHeadingWithoutOvershooting(float accel, float x, float y)
    {
        var target = new Vector2(x, y);
        var wanted = WGShipFacingSteering.Heading(Vector2.Zero, target);
        var rotation = 0f;
        var speed = 0f;
        var start = Math.Abs(Angle.ShortestDistance(Angle.Zero, wanted).Theta);
        var worst = 0.0;

        for (var t = 0f; t < 10f; t += FrameTime)
        {
            var input = WGShipFacingSteering.TurnInput(new Angle(rotation), speed, Vector2.Zero, target, accel, MaxSpeed, FrameTime);
            Assert.That(input, Is.InRange(-1f, 1f));

            // As the helm applies it: torque against the input, capped at the hull's top turn rate.
            speed = Math.Clamp(speed - input * accel * FrameTime, -MaxSpeed, MaxSpeed);
            rotation += speed * FrameTime;

            var error = Angle.ShortestDistance(new Angle(rotation), wanted).Theta;
            if (Math.Sign(error) != Math.Sign(Angle.ShortestDistance(Angle.Zero, wanted).Theta))
                worst = Math.Max(worst, Math.Abs(error));
        }

        var left = Math.Abs(Angle.ShortestDistance(new Angle(rotation), wanted).Degrees);
        Assert.That(left, Is.LessThan(0.5), $"Settled {left:F2}° off, from {start:F2} rad.");
        Assert.That(speed, Is.EqualTo(0f).Within(WGShipFacingSteering.DeadbandSpeed));
        Assert.That(worst, Is.LessThan(0.02), $"Overshot by {worst:F3} rad.");
    }
}
