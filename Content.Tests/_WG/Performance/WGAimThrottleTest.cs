using System;
using System.Numerics;
using Content.Shared._WG.Performance;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests._WG.Performance;

/// <summary>Checks a held laser pointer's aim is sent on a change, no more than 10 times a second, and kept alive.</summary>
[TestFixture]
[TestOf(typeof(WGAimThrottle))]
public sealed class WGAimThrottleTest
{
    private static readonly Vector2 Right = new(1, 0);

    [Test]
    public void FirstAimIsSent()
    {
        Assert.That(WGAimThrottle.Due(null, null, Right, Vector2.Zero, TimeSpan.FromSeconds(1)), Is.True);
    }

    [Test]
    public void NothingSoonerThanTheInterval()
    {
        var sent = new WGAimThrottle.Sent(null, Right, Vector2.Zero, TimeSpan.FromSeconds(1));
        var soon = TimeSpan.FromSeconds(1.05);

        Assert.That(WGAimThrottle.Due(sent, new EntityUid(5), new Vector2(0, 1), new Vector2(3, 3), soon), Is.False,
            "Even a new target waits out the interval.");
    }

    [Test]
    public void ChangesAreSentAfterTheInterval()
    {
        var sent = new WGAimThrottle.Sent(null, Right, Vector2.Zero, TimeSpan.FromSeconds(1));
        var later = TimeSpan.FromSeconds(1.15);

        Assert.That(WGAimThrottle.Due(sent, null, Right, Vector2.Zero, later), Is.False, "An unchanged aim waits for the keepalive.");
        Assert.That(WGAimThrottle.Due(sent, new EntityUid(5), Right, Vector2.Zero, later), Is.True, "A new target.");
        Assert.That(WGAimThrottle.Due(sent, null, new Vector2(1, 0.1f), Vector2.Zero, later), Is.True, "A turn of about 6 degrees.");
        Assert.That(WGAimThrottle.Due(sent, null, new Vector2(1, 0.01f), Vector2.Zero, later), Is.False, "A turn under 2 degrees.");
        Assert.That(WGAimThrottle.Due(sent, null, Right, new Vector2(0.5f, 0), later), Is.True, "The holder moved half a metre.");
        Assert.That(WGAimThrottle.Due(sent, null, null, Vector2.Zero, later), Is.True, "Leaving combat mode drops the aim.");
    }

    [Test]
    public void UnchangedAimIsKeptAlive()
    {
        var sent = new WGAimThrottle.Sent(null, Right, Vector2.Zero, TimeSpan.FromSeconds(1));

        Assert.That(WGAimThrottle.Due(sent, null, Right, Vector2.Zero, TimeSpan.FromSeconds(1.3)), Is.True,
            "The server falls back to the holder's facing after 0.5 s without one.");
        Assert.That(WGAimThrottle.KeepAlive, Is.LessThan(TimeSpan.FromSeconds(0.5)));
    }

    [Test]
    public void LineMovesOnlyPastTheSlack()
    {
        Assert.That(WGAimThrottle.LineMoved(Vector2.Zero, Right, new Vector2(0.01f, 0), Right), Is.False);
        Assert.That(WGAimThrottle.LineMoved(Vector2.Zero, Right, Vector2.Zero, new Vector2(1, 0.2f)), Is.True);
    }
}
