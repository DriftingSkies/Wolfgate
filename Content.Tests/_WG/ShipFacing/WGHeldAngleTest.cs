using System;
using Content.Shared._WG.ShipFacing;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.ShipFacing;

/// <summary>Checks that the view stands still while the facing key is held and swings back after.</summary>
[TestFixture]
public sealed class WGHeldAngleTest
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Test]
    public void FollowsTheLiveRotationWhenNotHeld()
    {
        var angle = new WGHeldAngle();
        Assert.That(angle.Apply(new Angle(1.2), false, At(0)).Theta, Is.EqualTo(1.2).Within(1e-6));
        Assert.That(angle.Apply(new Angle(-0.4), false, At(1)).Theta, Is.EqualTo(-0.4).Within(1e-6));
    }

    [Test]
    public void StandsStillWhileHeldAsTheShipTurns()
    {
        var angle = new WGHeldAngle();
        Assert.That(angle.Apply(new Angle(0.5), true, At(0)).Theta, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(angle.Apply(new Angle(1.5), true, At(0.5)).Theta, Is.EqualTo(0.5).Within(1e-6));
        Assert.That(angle.Apply(new Angle(3.0), true, At(2)).Theta, Is.EqualTo(0.5).Within(1e-6));
    }

    [Test]
    public void SwingsBackSmoothlyAfterRelease()
    {
        var angle = new WGHeldAngle();
        angle.Apply(Angle.Zero, true, At(0));

        var live = new Angle(1.0);
        var start = angle.Apply(live, false, At(1)).Theta;
        var middle = angle.Apply(live, false, At(1) + WGHeldAngle.ReturnTime / 2).Theta;
        var end = angle.Apply(live, false, At(1) + WGHeldAngle.ReturnTime).Theta;

        Assert.That(start, Is.EqualTo(0).Within(1e-6), "The view must not jump on release.");
        Assert.That(middle, Is.EqualTo(0.5).Within(1e-3));
        Assert.That(end, Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void SwingsTheShortWayRound()
    {
        var angle = new WGHeldAngle();
        angle.Apply(new Angle(3.0), true, At(0));

        // Live is just past -pi, 0.28 rad clockwise from 3.0 the short way.
        var live = new Angle(-3.0);
        var middle = angle.Apply(live, false, At(0) + WGHeldAngle.ReturnTime / 2);
        Assert.That(Math.Abs(Angle.ShortestDistance(new Angle(3.0), middle).Theta), Is.LessThan(0.2));
    }

    [Test]
    public void PressedMidSwingHoldsWhereTheViewIs()
    {
        var angle = new WGHeldAngle();
        angle.Apply(Angle.Zero, true, At(0));
        angle.Apply(new Angle(1.0), false, At(1));

        var now = angle.Apply(new Angle(1.0), true, At(1) + WGHeldAngle.ReturnTime / 2).Theta;
        Assert.That(now, Is.EqualTo(0.5).Within(1e-3));
        Assert.That(angle.Apply(new Angle(2.0), true, At(3)).Theta, Is.EqualTo(now).Within(1e-6));
    }
}
