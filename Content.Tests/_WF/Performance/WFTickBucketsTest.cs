using System.Collections.Generic;
using Content.Shared._WF.Performance;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Tests._WF.Performance;

/// <summary>Checks the buckets hand out the same turns as <see cref="WFTickSpread.Due"/> and fill once a period.</summary>
[TestFixture]
[TestOf(typeof(WFTickBuckets))]
public sealed class WFTickBucketsTest
{
    [TestCase(1)]
    [TestCase(30)]
    [TestCase(47)]
    public void MatchesTickSpread(int period)
    {
        var buckets = new WFTickBuckets();
        var fills = 0;
        const uint start = 1000;
        for (var tick = start; tick < start + period * 3; tick++)
        {
            var gameTick = new GameTick(tick);
            if (buckets.Begin(gameTick, period))
            {
                fills++;
                for (var id = 1; id < 200; id++)
                {
                    buckets.Add(new EntityUid(id));
                }
            }

            var expected = new List<EntityUid>();
            for (var id = 1; id < 200; id++)
            {
                if (WFTickSpread.Due(new EntityUid(id), gameTick, period))
                    expected.Add(new EntityUid(id));
            }

            Assert.That(buckets.Due, Is.EqualTo(expected));
        }

        // One fill on the first call, then one at the start of each later period.
        Assert.That(fills, Is.LessThanOrEqualTo(4));
    }
}
