using Content.Shared._WG.Bench;
using NUnit.Framework;

namespace Content.Tests._WG.Bench;

[TestFixture]
[TestOf(typeof(WGBenchMath))]
public sealed class WGBenchMathTest
{
    [Test]
    public void SumAddsFinesAndTakesHighestLevel()
    {
        var (total, level) = WGBenchMath.Sum(new[]
        {
            (10_000, WGPenaltyLevel.Citation),
            (60_000, WGPenaltyLevel.Impound),
            (0, WGPenaltyLevel.Citation),
            (-5, WGPenaltyLevel.Citation),
        });
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(70_000));
            Assert.That(level, Is.EqualTo(WGPenaltyLevel.Impound));
        });
    }

    [Test]
    public void SumOfNothingIsAFreeCitation()
    {
        Assert.That(WGBenchMath.Sum(System.Array.Empty<(int, WGPenaltyLevel)>()), Is.EqualTo((0, WGPenaltyLevel.Citation)));
    }

    [TestCase(15_000, 5_000, 20_000, ExpectedResult = 15_000)]
    [TestCase(99_000, 5_000, 20_000, ExpectedResult = 20_000)]
    [TestCase(10, 5_000, 20_000, ExpectedResult = 5_000)]
    [TestCase(10, 5_000, 0, ExpectedResult = 5_000)]
    public int ClampKeepsTheLawsRange(int fine, int min, int max)
    {
        return WGBenchMath.Clamp(fine, min, max);
    }

    [TestCase(150_000, 200_000, 0, 400_000, ExpectedResult = 150_000)]
    [TestCase(20_000_000, 200_000, 0, 400_000, ExpectedResult = 200_000)]
    [TestCase(150_000, 200_000, 300_000, 400_000, ExpectedResult = 100_000)]
    [TestCase(150_000, 200_000, 500_000, 400_000, ExpectedResult = 0)]
    [TestCase(150_000, -5, 0, 400_000, ExpectedResult = 0)]
    public int CapAppliesTicketAndRoundLimits(int total, int ticketCap, int alreadyFined, int roundCap)
    {
        return WGBenchMath.Cap(total, ticketCap, alreadyFined, roundCap);
    }

    [TestCase(20_000, 25_000, ExpectedResult = 20_000)]
    [TestCase(90_000, 25_000, ExpectedResult = 25_000)]
    [TestCase(-100, 25_000, ExpectedResult = 0)]
    public int CollectTakesWhatTheBalanceCovers(int balance, int fine)
    {
        return WGBenchMath.Collect(balance, fine);
    }

    [TestCase(4_000, 0.5f, 5_000, ExpectedResult = 2_000)]
    [TestCase(4_000, 0.5f, 500, ExpectedResult = 500)]
    [TestCase(1, 0.5f, 500, ExpectedResult = 1)]
    [TestCase(4_000, 0.5f, 0, ExpectedResult = 0)]
    [TestCase(0, 0.5f, 500, ExpectedResult = 0)]
    [TestCase(4_000, 0f, 500, ExpectedResult = 0)]
    public int GarnishTakesAShareOfGains(int gained, float share, int owed)
    {
        return WGBenchMath.Garnish(gained, share, owed);
    }
}
