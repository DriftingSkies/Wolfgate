using Content.Shared._WG.Lattice;
using NUnit.Framework;

namespace Content.Tests._WG.Lattice;

/// <summary>Checks Kyphrus's implosion timeline: swell, fall to a point, then waves leaving in order.</summary>
[TestFixture]
public sealed class WGLatticeImplosionTest
{
    [Test]
    public void SwellsThenFallsToNothing()
    {
        Assert.That(WGLatticeImplosion.Scale(0f), Is.EqualTo(1f));
        Assert.That(WGLatticeImplosion.Scale(WGLatticeImplosion.Swell - 0.01f), Is.EqualTo(WGLatticeImplosion.SwellScale).Within(0.01f));
        Assert.That(WGLatticeImplosion.Scale(WGLatticeImplosion.Swell * 0.5f), Is.GreaterThan(1f));
        Assert.That(WGLatticeImplosion.Scale(WGLatticeImplosion.Collapse - 0.01f), Is.LessThan(0.01f));
        Assert.That(WGLatticeImplosion.Scale(WGLatticeImplosion.Burst), Is.EqualTo(0f));
    }

    [Test]
    public void WavesLeaveAtTheBurstInTurn()
    {
        Assert.That(WGLatticeImplosion.WaveRadius(WGLatticeImplosion.Burst - 1f, 0), Is.LessThanOrEqualTo(0f));
        Assert.That(WGLatticeImplosion.WaveRadius(WGLatticeImplosion.Burst + 1f, 0), Is.EqualTo(WGLatticeImplosion.WaveSpeed));
        Assert.That(WGLatticeImplosion.WaveRadius(WGLatticeImplosion.Burst + 1f, 1), Is.LessThanOrEqualTo(0f));
        Assert.That(WGLatticeImplosion.WaveRadius(WGLatticeImplosion.Burst + WGLatticeImplosion.WaveGap + 1f, 1),
            Is.EqualTo(WGLatticeImplosion.WaveSpeed));
    }
}
