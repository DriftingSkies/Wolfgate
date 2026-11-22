using System.Numerics;
using Content.Server._Mono.Radar;
using NUnit.Framework;

namespace Content.Tests._WG.Performance;

/// <summary>Checks a hitscan line goes to a radar when any of it passes within range, and not when none of it does.</summary>
[TestFixture]
[TestOf(typeof(RadarBlipSystem))]
public sealed class WGRadarLineTest
{
    private static readonly Vector2[] Radar = { Vector2.Zero };

    [TestCase(10, 0, 20, 0, ExpectedResult = true, TestName = "Both ends in range")]
    [TestCase(10, 0, 5000, 0, ExpectedResult = true, TestName = "Starts in range")]
    [TestCase(5000, 0, 10, 0, ExpectedResult = true, TestName = "Ends in range, which upstream dropped")]
    [TestCase(-3000, 50, 3000, 50, ExpectedResult = true, TestName = "Passes through with both ends out")]
    [TestCase(3000, 3000, 6000, 3000, ExpectedResult = false, TestName = "Far from the radar, which upstream sent")]
    [TestCase(150, 150, 150, 150, ExpectedResult = false, TestName = "A point just out of range")]
    public bool LineInReach(float x1, float y1, float x2, float y2)
    {
        return RadarBlipSystem.WGLineInReach(new Vector2(x1, y1), new Vector2(x2, y2), Radar, 200f);
    }
}
