using System;
using Content.Shared._WG.ShipPa;
using NUnit.Framework;

namespace Content.Tests._WG.ShipPa;

[TestFixture]
public sealed class ShipPaPlaybackPolicyTest
{
    [Test]
    public void TimelineAndHysteresisDoNotRestartAtSpeakerBoundaries()
    {
        var track = new ShipPaBroadcast { Start = TimeSpan.FromSeconds(10), Length = 7 };
        Assert.That(track.Position(TimeSpan.FromSeconds(13)), Is.EqualTo(3));
        Assert.That(track.IsPlaying(TimeSpan.FromSeconds(9)), Is.False);
        Assert.That(track.IsPlaying(TimeSpan.FromSeconds(17)), Is.False);
        track.Loop = true;
        Assert.That(track.Position(TimeSpan.FromSeconds(20)), Is.EqualTo(3));
        Assert.That(ShipPaPlaybackPolicy.ShouldSwitch(1f, 0.95f), Is.False);
        Assert.That(ShipPaPlaybackPolicy.ShouldSwitch(1f, 0.7f), Is.True);
        Assert.That(ShipPaPlaybackPolicy.Score(3, 14, 4), Is.GreaterThan(ShipPaPlaybackPolicy.Score(6, 14, 0)));
    }
}

