using Content.Shared._WG.WolfChem.Narcotics;
using NUnit.Framework;

namespace Content.Tests._WG.WolfChem.Narcotics;

[TestFixture]
[TestOf(typeof(WGNarcoticHeatMath))]
public sealed class WGNarcoticHeatMathTest
{
    private const double Free = 100_000;
    private const double Scale = 100_000;
    private const double Floor = 0.05;

    [Test]
    public void AColdSellerIsPaidInFull()
    {
        Assert.That(WGNarcoticHeatMath.Payout(0, 100_000, Free, Scale, Floor), Is.EqualTo(100_000).Within(0.01));
        Assert.That(WGNarcoticHeatMath.Payout(40_000, 60_000, Free, Scale, Floor), Is.EqualTo(60_000).Within(0.01));
    }

    [Test]
    public void ADumpSellsForAFraction()
    {
        var dump = WGNarcoticHeatMath.Payout(0, 2_000_000, Free, Scale, Floor);
        Assert.Multiple(() =>
        {
            // 100,000 in full, 95,000 on the way down to the floor near 400,000 heat, then 5% of the rest.
            Assert.That(dump, Is.EqualTo(275_021).Within(1));
            Assert.That(WGNarcoticHeatMath.Payout(0, 150_000, Free, Scale, Floor), Is.GreaterThan(135_000),
                "A little over the line costs little.");
        });
    }

    [Test]
    public void OneSaleOrManyPayTheSame()
    {
        var whole = WGNarcoticHeatMath.Payout(50_000, 500_000, Free, Scale, Floor);
        var first = WGNarcoticHeatMath.Payout(50_000, 200_000, Free, Scale, Floor);
        var second = WGNarcoticHeatMath.Payout(250_000, 300_000, Free, Scale, Floor);
        Assert.That(first + second, Is.EqualTo(whole).Within(0.01), "Splitting a sale without waiting gains nothing.");
    }

    [Test]
    public void AHotSellerIsPaidTheFloor()
    {
        Assert.That(WGNarcoticHeatMath.Payout(600_000, 10_000, Free, Scale, Floor), Is.EqualTo(500).Within(0.01));
        Assert.That(WGNarcoticHeatMath.Payout(0, 0, Free, Scale, Floor), Is.Zero);
        Assert.That(WGNarcoticHeatMath.Payout(200_000, 10_000, Free, 0, Floor), Is.EqualTo(500).Within(0.01),
            "With no falloff the price drops straight to the floor.");
    }

    [Test]
    public void PastTheCapPaysTheGlutFloor()
    {
        const double cap = 500_000;
        const double glut = 0.01;
        var capped = WGNarcoticHeatMath.Payout(0, cap, Free, Scale, Floor, cap, glut);
        Assert.Multiple(() =>
        {
            Assert.That(WGNarcoticHeatMath.Payout(0, 100_000_000, Free, Scale, Floor, cap, glut),
                Is.EqualTo(capped + (100_000_000 - cap) * glut).Within(0.01),
                "What is sold past the cap pays the glut floor.");
            Assert.That(WGNarcoticHeatMath.Payout(cap, 10_000, Free, Scale, Floor, cap, glut), Is.EqualTo(100).Within(0.01));
            Assert.That(WGNarcoticHeatMath.Payout(cap, 10_000, Free, Scale, Floor, cap), Is.Zero, "No glut floor pays nothing.");
            Assert.That(WGNarcoticHeatMath.Payout(450_000, 100_000, Free, Scale, Floor, cap, glut),
                Is.EqualTo(WGNarcoticHeatMath.Payout(450_000, 50_000, Free, Scale, Floor) + 50_000 * glut).Within(0.01));
        });
    }

    [Test]
    public void OnlyWhatIsSoldPastALineCounts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGNarcoticHeatMath.Over(0, 200_000, 250_000), Is.Zero);
            Assert.That(WGNarcoticHeatMath.Over(200_000, 150_000, 250_000), Is.EqualTo(100_000));
            Assert.That(WGNarcoticHeatMath.Over(400_000, 150_000, 250_000), Is.EqualTo(150_000), "A seller already past it adds the whole sale.");
        });
    }

    [Test]
    public void HeatCoolsToNothing()
    {
        Assert.That(WGNarcoticHeatMath.Cool(150_000, 300, 250), Is.EqualTo(75_000));
        Assert.That(WGNarcoticHeatMath.Cool(150_000, 3_600, 250), Is.Zero);
        Assert.That(WGNarcoticHeatMath.Cool(150_000, -5, 250), Is.EqualTo(150_000));
    }

    [TestCase(0, ExpectedResult = WGNarcoticHeatTier.Cold)]
    [TestCase(100_000, ExpectedResult = WGNarcoticHeatTier.Cold)]
    [TestCase(100_001, ExpectedResult = WGNarcoticHeatTier.Warm)]
    [TestCase(250_000, ExpectedResult = WGNarcoticHeatTier.Warm)]
    [TestCase(250_001, ExpectedResult = WGNarcoticHeatTier.Hot)]
    public WGNarcoticHeatTier TiersFollowTheLines(double heat)
    {
        return WGNarcoticHeatMath.Tier(heat, Free, 250_000);
    }
}
