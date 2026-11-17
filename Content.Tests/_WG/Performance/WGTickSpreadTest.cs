using Content.Shared._WG.Performance;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Tests._WG.Performance;

/// <summary>Checks a spread job still runs each entity exactly once per period.</summary>
[TestFixture]
[TestOf(typeof(WGTickSpread))]
public sealed class WGTickSpreadTest
{
    [TestCase(1)]
    [TestCase(30)]
    [TestCase(47)]
    public void EachEntityRunsOncePerPeriod(int period)
    {
        for (var id = 1; id < 200; id++)
        {
            var uid = new EntityUid(id);
            var runs = 0;
            for (uint tick = 1000; tick < 1000 + period; tick++)
            {
                if (WGTickSpread.Due(uid, new GameTick(tick), period))
                    runs++;
            }

            Assert.That(runs, Is.EqualTo(1), $"Entity {id} over a period of {period}");
        }
    }

    [Test]
    public void EntitiesSpreadAcrossTicks()
    {
        const int period = 30;
        var due = 0;
        for (var id = 1; id <= 300; id++)
        {
            if (WGTickSpread.Due(new EntityUid(id), new GameTick(1234), period))
                due++;
        }

        Assert.That(due, Is.EqualTo(10), "300 entities over 30 ticks should be 10 a tick.");
    }
}
