using Content.Client._WF.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks how much of an MFD page's height the plot gets.</summary>
[TestFixture]
public sealed class WFCockpitMfdLayoutTest
{
    [Test]
    public void PlotShareEasesFromShortToTall()
    {
        Assert.That(WFCockpitMfdSplit.PlotShare(0.7f, 200), Is.EqualTo(WFCockpitMfdSplit.ShortShare));
        Assert.That(WFCockpitMfdSplit.PlotShare(0.7f, WFCockpitMfdSplit.ShortHeight), Is.EqualTo(WFCockpitMfdSplit.ShortShare));
        Assert.That(WFCockpitMfdSplit.PlotShare(0.7f, WFCockpitMfdSplit.TallHeight), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(WFCockpitMfdSplit.PlotShare(0.9f, 200), Is.EqualTo(0.9f));
    }

    [Test]
    public void PlotNeverShrinksAsThePageGrows()
    {
        var previous = 0f;
        for (var height = 200f; height <= 900f; height += 4)
        {
            var plot = WFCockpitMfdSplit.Boxes(new System.Numerics.Vector2(400, height), 0.8f).Plot.Height;
            Assert.That(plot, Is.GreaterThanOrEqualTo(previous), $"page height {height}");
            previous = plot;
        }
    }
}
