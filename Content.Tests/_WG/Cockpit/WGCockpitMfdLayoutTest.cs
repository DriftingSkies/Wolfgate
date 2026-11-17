using Content.Client._WG.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WG.Cockpit;

/// <summary>Checks how much of an MFD page's height the plot gets.</summary>
[TestFixture]
public sealed class WGCockpitMfdLayoutTest
{
    [Test]
    public void PlotShareEasesFromShortToTall()
    {
        Assert.That(WGCockpitMfdSplit.PlotShare(0.7f, 200), Is.EqualTo(WGCockpitMfdSplit.ShortShare));
        Assert.That(WGCockpitMfdSplit.PlotShare(0.7f, WGCockpitMfdSplit.ShortHeight), Is.EqualTo(WGCockpitMfdSplit.ShortShare));
        Assert.That(WGCockpitMfdSplit.PlotShare(0.7f, WGCockpitMfdSplit.TallHeight), Is.EqualTo(0.7f).Within(0.0001f));
        Assert.That(WGCockpitMfdSplit.PlotShare(0.9f, 200), Is.EqualTo(0.9f));
    }

    [Test]
    public void PlotNeverShrinksAsThePageGrows()
    {
        var previous = 0f;
        for (var height = 200f; height <= 900f; height += 4)
        {
            var plot = WGCockpitMfdSplit.Boxes(new System.Numerics.Vector2(400, height), 0.8f).Plot.Height;
            Assert.That(plot, Is.GreaterThanOrEqualTo(previous), $"page height {height}");
            previous = plot;
        }
    }
}
