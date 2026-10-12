using System.Numerics;
using Content.Client._WF.BlackHole;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.BlackHole;

/// <summary>Checks that offscreen effects avoid screen capture without clipping visible lensing.</summary>
[TestFixture]
public sealed class BlackHoleVisualsTest
{
    /// <summary>A center can be outside the viewport while its surrounding lensing remains visible.</summary>
    [TestCase(0f, true)]
    [TestCase(70f, true)]
    [TestCase(73f, false)]
    [TestCase(-70f, true)]
    [TestCase(-73f, false)]
    public void IncludesPartlyVisibleEffects(float x, bool expected)
    {
        var center = new Vector2(x, 0f);
        Assert.That(BlackHoleVisuals.IsVisible(center, 1f, center, new Box2(-10, -10, 10, 10)),
            Is.EqualTo(expected));
    }

    /// <summary>Corner overlap uses the circular effect instead of its larger bounding square.</summary>
    [TestCase(50f, true)]
    [TestCase(60f, false)]
    [TestCase(-50f, true)]
    [TestCase(-60f, false)]
    public void UsesDistanceToViewportCorners(float coordinate, bool expected)
    {
        var center = new Vector2(coordinate);
        Assert.That(BlackHoleVisuals.IsVisible(center, 1f, center, new Box2(-10, -10, 10, 10)),
            Is.EqualTo(expected));
    }

    /// <summary>The last touching pixel stays eligible for drawing.</summary>
    [TestCase(322f, true)]
    [TestCase(322.1f, false)]
    public void KeepsTangentEffects(float x, bool expected)
    {
        var center = new Vector2(x, 0f);
        Assert.That(BlackHoleVisuals.IsVisible(center, 5f, center, new Box2(-10, -10, 10, 10)),
            Is.EqualTo(expected));
    }

    /// <summary>Visibility follows the apparent position, including translated and negative coordinates.</summary>
    [TestCase(0f, 0f)]
    [TestCase(-1000f, -2000f)]
    public void ProjectsTheStarWithParallax(float offsetX, float offsetY)
    {
        var translation = new Vector2(offsetX, offsetY);
        var star = new Vector2(100, -40) + translation;
        var eye = new Vector2(300, 160) + translation;
        var viewport = new Box2(new Vector2(287, 147) + translation, new Vector2(293, 153) + translation);
        Assert.That(BlackHoleVisuals.IsVisible(star, 0.01f, eye, viewport), Is.True);
        Assert.That(BlackHoleVisuals.IsVisible(star, 0.01f, eye,
            new Box2(eye - Vector2.One, eye + Vector2.One)), Is.False);
    }

    /// <summary>Zooming out can reveal a field beyond the narrower viewport.</summary>
    [Test]
    public void ZoomedOutViewportCanSeeTheEffect()
    {
        var star = new Vector2(1800, 0);
        Assert.That(BlackHoleVisuals.IsVisible(star, 1f, Vector2.Zero, new Box2(-20, -20, 20, 20)), Is.False);
        Assert.That(BlackHoleVisuals.IsVisible(star, 1f, Vector2.Zero, new Box2(-40, -40, 40, 40)), Is.True);
    }

    /// <summary>The field scales with the selected star's radius.</summary>
    [Test]
    public void LargerStarsHaveLargerVisibleFields()
    {
        var center = new Vector2(80, 0);
        var viewport = new Box2(-10, -10, 10, 10);
        Assert.That(BlackHoleVisuals.IsVisible(center, 1f, center, viewport), Is.False);
        Assert.That(BlackHoleVisuals.IsVisible(center, 2f, center, viewport), Is.True);
    }
}
