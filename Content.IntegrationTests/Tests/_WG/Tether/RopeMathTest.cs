using System.Numerics;
using Content.Client._WG.Tether;
using Content.Shared._WG.Tether;

namespace Content.IntegrationTests.Tests._WG.Tether;

/// <summary>Pure maths for the rope spring and the drawn verlet chain; no server pair needed.</summary>
[TestFixture]
public sealed class RopeMathTest
{
    private const float Step = 1f / 60f;

    [Test]
    public void SlackRopeAppliesNothingAndDampingNeverPulls()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeMath.Extension(4f, 5f), Is.LessThan(0f));
            Assert.That(RopeMath.Strain(4f, 5f, 0.1f), Is.Zero);
            Assert.That(RopeMath.SpringImpulse(-1f, 5f, 3f, 100f, 4000f, 0.7f, 0.1f, Step), Is.Zero,
                "A slack rope must not pull even while its ends separate quickly.");
            Assert.That(RopeMath.SpringImpulse(0.1f, 5f, -20f, 100f, 4000f, 0.7f, 0.1f, Step), Is.GreaterThanOrEqualTo(0f),
                "Damping on a closing rope must never flip the impulse into a push.");
        });
    }

    [Test]
    public void StrainReportsTheHardLimitTravel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeMath.Strain(10f, 10f, 0.1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(RopeMath.Strain(10.5f, 10f, 0.1f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(RopeMath.Strain(11f, 10f, 0.1f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(RopeMath.Strain(100f, 10f, 0.1f), Is.EqualTo(2f).Within(0.001f), "Strain is clamped for the network.");
            Assert.That(RopeMath.Strain(float.NaN, 10f, 0.1f), Is.Zero);
        });
    }

    [Test]
    public void ResamplingKeepsTheChainsShape()
    {
        var old = new[] { Vector2.Zero, new Vector2(1f, 1f), new Vector2(2f, 0f) };
        var points = RopeVerlet.Resample(old, 3, 5);
        Assert.Multiple(() =>
        {
            Assert.That(points, Has.Length.EqualTo(5));
            Assert.That(points[0], Is.EqualTo(old[0]));
            Assert.That(points[2], Is.EqualTo(old[1]), "The bend stays where it was.");
            Assert.That(points[4], Is.EqualTo(old[2]));
        });
    }

    [Test]
    public void SparksHoldTheirPaceAndSpacing()
    {
        const float speed = 8f;
        const float spacing = 10f;
        var start = RopeVerlet.SparkDistance(0f, 0, speed, spacing, 0f);
        var later = RopeVerlet.SparkDistance(0.5f, 0, speed, spacing, 0f);
        Assert.Multiple(() =>
        {
            Assert.That(later - start, Is.EqualTo(4f).Within(0.001f), "Half a second at 8 m/s.");
            Assert.That(RopeVerlet.SparkDistance(0.5f, 3, speed, spacing, 0f) - later, Is.EqualTo(30f).Within(0.001f));
            Assert.That(RopeVerlet.SparkDistance(1.25f, 0, speed, spacing, 0f), Is.LessThan(spacing),
                "A spark hands over to the one behind it instead of running on.");
        });
    }

    [Test]
    public void BendStiffnessOpensUpAFold()
    {
        float Fold(float bend)
        {
            var points = new[] { Vector2.Zero, new Vector2(3f, 0f), new Vector2(0.5f, 0f), new Vector2(3.5f, 0f), new Vector2(1f, 0f) };
            var previous = (Vector2[]) points.Clone();
            for (var i = 0; i < 30; i++)
            {
                RopeVerlet.Step(points, previous, 5, points[0], points[4], 3f, 1f / 60f, 0f, 0f, 1, bend);
            }

            return MathF.Abs(points[1].X - points[2].X);
        }

        Assert.That(Fold(0.25f), Is.LessThan(Fold(0f)), "A stiff cable doesn't stay doubled back on itself.");
    }

    [Test]
    public void StiffRopeOnALightBodyCannotExplode()
    {
        // A very light body on a very stiff rope: the impulse may never do more than remove the
        // separating motion and the extension itself within one step.
        const float mass = 0.5f;
        const float extension = 0.4f;
        const float separating = 2f;
        var impulse = RopeMath.SpringImpulse(extension, 5f, separating, mass, 5_000_000f, 0.7f, 0.1f, Step);
        var ceiling = mass * (separating + extension / Step);
        Assert.That(impulse, Is.LessThanOrEqualTo(ceiling + 0.0001f));
        Assert.That(impulse, Is.GreaterThan(0f));
    }

    [Test]
    public void EffectiveMassAccountsForAnchorArms()
    {
        var direction = Vector2.UnitX;
        var straight = RopeMath.EffectiveMass(direction, Vector2.Zero, Vector2.Zero, 0.5f, 0.5f, 1f, 1f);
        var levered = RopeMath.EffectiveMass(direction, new Vector2(0f, 2f), Vector2.Zero, 0.5f, 0.5f, 1f, 1f);
        Assert.That(straight, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(levered, Is.LessThan(straight), "An anchor on a lever arm is easier to move.");
        Assert.That(RopeMath.EffectiveMass(direction, Vector2.Zero, Vector2.Zero, 0f, 0f, 0f, 0f), Is.Zero,
            "Two static bodies have no effective mass and must produce no impulse.");
    }

    [Test]
    public void VerletChainStaysFiniteAndRespectsItsMaximumLength()
    {
        var count = RopeVerlet.PointCount(10f, 3f);
        Assert.That(count, Is.InRange(RopeVerlet.MinPoints, RopeVerlet.MaxPoints));

        var points = new Vector2[count];
        var previous = new Vector2[count];
        var endA = Vector2.Zero;
        var endB = new Vector2(4f, 0f);
        var rest = 10f / (count - 1);
        RopeVerlet.Seed(points, previous, count, endA, endB, 0.5f);

        for (var i = 0; i < 400; i++)
        {
            // An end whipping about with an over-long step must not destabilise the chain.
            endB = new Vector2(4f + MathF.Sin(i * 0.4f) * 3f, MathF.Cos(i * 0.31f) * 3f);
            RopeVerlet.Step(points, previous, count, endA, endB, rest, 0.5f, 1.5f, i * 0.016f, 7);
        }

        Assert.Multiple(() =>
        {
            Assert.That(points[0], Is.EqualTo(endA));
            Assert.That(points[count - 1], Is.EqualTo(endB));
            for (var i = 0; i < count; i++)
            {
                Assert.That(float.IsFinite(points[i].X) && float.IsFinite(points[i].Y), Is.True, $"Point {i} is finite.");
            }

            for (var i = 0; i < count - 2; i++)
            {
                // Relaxation is iterative, so a violently whipping end leaves a little overshoot;
                // what matters is that it stays bounded instead of growing every frame.
                Assert.That(Vector2.Distance(points[i], points[i + 1]), Is.LessThan(rest * 1.6f));
            }
        });
    }

    [Test]
    public void TeleportingAnEndRequestsAReseed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, Vector2.Zero, Vector2.One), Is.False);
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, new Vector2(500f, 0f), Vector2.One), Is.True);
            Assert.That(RopeVerlet.NeedsReseed(Vector2.Zero, Vector2.One, Vector2.Zero, new Vector2(float.NaN, 0f)), Is.True);
        });
    }
}
