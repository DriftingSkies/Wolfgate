using Content.Shared._WG.Savings;
using NUnit.Framework;

namespace Content.Tests._WG.Savings;

/// <summary>Checks what a savings balance earns in a round.</summary>
[TestFixture]
[TestOf(typeof(WGSavingsInterestPrototype))]
public sealed class WGSavingsInterestTest
{
    private static readonly WGSavingsInterestPrototype Interest = new() { Rate = 0.02, BalanceCap = 10_000_000, MaxBalance = 20_000_000 };

    [TestCase(0, 0, TestName = "NothingEarnsNothing")]
    [TestCase(-500, 0, TestName = "ANegativeBalanceEarnsNothing")]
    [TestCase(49, 0, TestName = "ATinyBalanceRoundsDownToNothing")]
    [TestCase(1_000_000, 20_000, TestName = "ABalanceEarnsTwoPercent")]
    [TestCase(10_000_000, 200_000, TestName = "TheCapItselfEarnsInFull")]
    [TestCase(15_000_000, 200_000, TestName = "EarningsStopAtTheCap")]
    [TestCase(19_900_000, 100_000, TestName = "InterestStopsAtTheMaxBalance")]
    [TestCase(20_000_000, 0, TestName = "AFullAccountEarnsNothing")]
    [TestCase(25_000_000, 0, TestName = "AnAccountOverTheMaxEarnsNothing")]
    public void InterestOn(long balance, long expected)
    {
        Assert.That(Interest.InterestOn(balance), Is.EqualTo(expected));
    }

    [TestCase(0, 20_000_000, TestName = "AnEmptyAccountHasRoomForTheMax")]
    [TestCase(19_000_000, 1_000_000, TestName = "RoomIsWhatIsLeftToTheMax")]
    [TestCase(25_000_000, 0, TestName = "AnAccountOverTheMaxHasNoRoom")]
    public void Room(long balance, long expected)
    {
        Assert.That(Interest.Room(balance), Is.EqualTo(expected));
    }

    [Test]
    public void InterestCompounds()
    {
        long balance = 1_000_000;
        for (var round = 0; round < 3; round++)
        {
            balance += Interest.InterestOn(balance);
        }

        Assert.That(balance, Is.EqualTo(1_061_208), "Three rounds at 2% didn't compound.");
    }
}
