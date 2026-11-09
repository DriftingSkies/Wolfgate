using System.Numerics;
using Content.Shared._WF.ShipMissiles;
using NUnit.Framework;

namespace Content.Tests._WF.ShipMissiles;

/// <summary>The proportional-navigation maths: every case is a closed-form geometry the flight model relies on.</summary>
[TestFixture]
[TestOf(typeof(MissileGuidance))]
public sealed class MissileGuidanceTest
{
    /// <summary>A missile 200 m/s down +x and a target 100 m ahead moving 10 m/s across it.</summary>
    private static readonly Vector2 Crossing = new Vector2(0f, 10f) - new Vector2(200f, 0f);

    /// <summary>Dead ahead at 200 m/s: the line of sight does not move, so neither should the missile.</summary>
    [Test]
    public void LevelsOffOnACollisionCourse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.LosRate(new Vector2(100f, 0f), new Vector2(-200f, 0f)), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(MissileGuidance.ProportionalNavigation(0f, 200f, 4f, 110f), Is.EqualTo(0f).Within(1e-6f));
        });
    }

    /// <summary>A crossing target turns the line of sight, and the command leads the way it is turning.</summary>
    [Test]
    public void LeadsACrossingTarget()
    {
        var relPos = new Vector2(100f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.LosRate(relPos, Crossing), Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(MissileGuidance.ClosingRate(relPos, Crossing), Is.EqualTo(200f).Within(1e-4f));
            // N * Vc * losRate = 4 * 200 * 0.1, well inside the authority.
            Assert.That(MissileGuidance.ProportionalNavigation(0.1f, 200f, 4f, 500f), Is.EqualTo(80f).Within(1e-4f));
            // The target drifts to +y, so the normal and the turn both point at +y.
            var normal = MissileGuidance.LosNormal(relPos);
            Assert.That(normal.X, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(normal.Y, Is.EqualTo(1f).Within(1e-6f));
        });
    }

    /// <summary>Closure is positive head-on, negative while opening and zero on a pure beam.</summary>
    [Test]
    public void SignsTheClosingRate()
    {
        var relPos = new Vector2(100f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.ClosingRate(relPos, new Vector2(-50f, 0f)), Is.EqualTo(50f).Within(1e-4f));
            Assert.That(MissileGuidance.ClosingRate(relPos, new Vector2(50f, 0f)), Is.EqualTo(-50f).Within(1e-4f));
            Assert.That(MissileGuidance.ClosingRate(relPos, new Vector2(0f, 20f)), Is.EqualTo(0f).Within(1e-4f));
        });
    }

    /// <summary>The command never exceeds the diverters, in either direction.</summary>
    [Test]
    public void ClampsToTheDiverters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.ProportionalNavigation(1f, 200f, 4f, 110f), Is.EqualTo(110f));
            Assert.That(MissileGuidance.ProportionalNavigation(-1f, 200f, 4f, 110f), Is.EqualTo(-110f));
            // A non-finite command is dropped rather than flown.
            Assert.That(MissileGuidance.ProportionalNavigation(float.NaN, 200f, 4f, 110f), Is.EqualTo(0f));
        });
    }

    /// <summary>Beaming is fast and square to the seeker; head-on and slow flight are neither.</summary>
    [Test]
    public void FindsTheNotch()
    {
        var relPos = new Vector2(100f, 0f);
        const float band = 4f;

        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.IsBeaming(relPos, new Vector2(0f, 20f), band), Is.True, "Beam is a notch.");
            Assert.That(MissileGuidance.IsBeaming(relPos, Crossing, band), Is.False, "Head-on is not a notch.");
            Assert.That(MissileGuidance.IsBeaming(relPos, new Vector2(0f, 5f), band), Is.False, "Too slow to notch.");
        });
    }

    /// <summary>Time to go counts down on closure and does not explode on a receding target.</summary>
    [Test]
    public void EstimatesTimeToGo()
    {
        var relPos = new Vector2(100f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.TimeToGo(relPos, new Vector2(-200f, 0f)), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(MissileGuidance.TimeToGo(relPos, new Vector2(100f, 0f)), Is.EqualTo(100f).Within(1e-3f));
        });
    }

    /// <summary>A launch cone is checked with vectors, so no angle convention can get in the way.</summary>
    [Test]
    public void TestsTheLaunchCone()
    {
        var nose = new Vector2(1f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(MissileGuidance.WithinCone(new Vector2(100f, 0f), nose, 45f), Is.True, "Dead ahead.");
            Assert.That(MissileGuidance.WithinCone(new Vector2(100f, 100f), nose, 60f), Is.True, "45 off, inside 60.");
            Assert.That(MissileGuidance.WithinCone(new Vector2(100f, 100f), nose, 30f), Is.False, "45 off, outside 30.");
            Assert.That(MissileGuidance.WithinCone(Vector2.Zero, nose, 45f), Is.False, "No bearing, no target.");
        });
    }

    /// <summary>A handoff is clamped to what the missile can take off the rail, in whichever direction it is off.</summary>
    [Test]
    public void ClampsAHandoffToTheRail()
    {
        var nose = new Vector2(1f, 0f);

        var goal = new Vector2(1f, 0.2f);
        var inside = MissileGuidance.ClampBearing(nose, goal, 90f);
        Assert.That(Vector2.Dot(Vector2.Normalize(goal), inside), Is.EqualTo(1f).Within(1e-5f),
            "A bearing inside the limit is kept.");

        var clamped = MissileGuidance.ClampBearing(nose, new Vector2(-1f, 0f), 45f);
        // 45 degrees off the nose is a dot of cos(45) = 0.707, no further.
        Assert.That(Vector2.Dot(nose, clamped), Is.EqualTo(0.7071068f).Within(1e-4f));

        var mirror = MissileGuidance.ClampBearing(nose, new Vector2(-1f, -1f), 45f);
        Assert.That(Vector2.Dot(nose, mirror), Is.EqualTo(0.7071068f).Within(1e-4f), "Clamped either way round.");
        Assert.That(mirror.Y, Is.LessThan(0f), "And on the side it was aimed at.");
    }

    /// <summary>Zero leaves the nose alone, one hands the whole bearing over, and a half-turn still resolves.</summary>
    [Test]
    public void BlendsTheReleaseBearing()
    {
        var nose = new Vector2(1f, 0f);
        var goal = new Vector2(0f, 1f);

        var none = MissileGuidance.BlendDirection(nose, goal, 0f);
        Assert.That(Vector2.Dot(nose, none), Is.EqualTo(1f).Within(1e-5f), "Nothing moves at zero.");

        var all = MissileGuidance.BlendDirection(nose, goal, 1f);
        Assert.That(Vector2.Dot(goal, all), Is.EqualTo(1f).Within(1e-5f), "The goal is taken in full at one.");

        var half = MissileGuidance.BlendDirection(nose, -nose, 0.5f);
        Assert.That(half.Length(), Is.EqualTo(1f).Within(1e-4f), "A half turn still gives a unit vector.");
    }
}
