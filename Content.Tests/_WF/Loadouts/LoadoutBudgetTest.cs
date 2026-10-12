using Content.Client._WF.Loadouts;
using NUnit.Framework;

namespace Content.Tests._WF.Loadouts;

/// <summary>The window's cost walk matches how the server pays for a loadout at spawn.</summary>
[TestFixture]
[TestOf(typeof(LoadoutBudget))]
public sealed class LoadoutBudgetTest
{
    [Test]
    public void EverythingAffordable()
    {
        var dropped = LoadoutBudget.Unaffordable(new[] { 0, 2000, 500 }, 75000, out var cost);

        Assert.That(dropped, Is.Empty);
        Assert.That(cost, Is.EqualTo(2500));
    }

    /// <summary>Picks are paid for in order: a later cheap one still goes through after an earlier one was dropped.</summary>
    [Test]
    public void DropsWhatTheBalanceCannotCover()
    {
        var dropped = LoadoutBudget.Unaffordable(new[] { 3000, 9000, 0, 2000 }, 5000, out var cost);

        Assert.That(dropped, Is.EqualTo(new[] { 1 }), "3000 is paid, 9000 does not fit the 2000 left, 2000 does");
        Assert.That(cost, Is.EqualTo(14000), "the cost is what the whole selection asks for");
    }

    [Test]
    public void NegativePricesCostNothing()
    {
        var dropped = LoadoutBudget.Unaffordable(new[] { -500, 1000 }, 1000, out var cost);

        Assert.That(dropped, Is.Empty, "a negative price neither pays out nor uses up the balance");
        Assert.That(cost, Is.EqualTo(1000));
    }

    /// <summary>The server compares the price with the balance left, so a character in debt is refused even free gear.</summary>
    [Test]
    public void DebtDropsFreeGear()
    {
        var dropped = LoadoutBudget.Unaffordable(new[] { 0 }, -10, out _);

        Assert.That(dropped, Is.EqualTo(new[] { 0 }));
    }
}
