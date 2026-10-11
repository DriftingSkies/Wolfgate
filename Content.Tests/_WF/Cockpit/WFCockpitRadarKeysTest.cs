using System.Numerics;
using Content.Client._WF.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

[TestFixture]
public sealed class WFCockpitRadarKeysTest
{
    private static readonly Vector2 Keys = new(164, 32);

    [Test]
    public void AWideScopePutsTheKeysInTheBottomRowAtTheRight()
    {
        var box = WFCockpitRadarKeys.Place(new Vector2(500, 400), Keys);
        Assert.That(box.Right, Is.EqualTo(500 - WFCockpitRadarKeys.Edge));
        Assert.That(box.Bottom, Is.EqualTo(400 - WFCockpitRadarKeys.Edge));
        Assert.That(box.Size, Is.EqualTo(Keys));
        Assert.That(box.Left, Is.GreaterThanOrEqualTo(WFCockpitRadarKeys.Edge + WFCockpitRadarKeys.Modes.X + WFCockpitRadarKeys.Gap),
            "The keys must clear the mode buttons at the bottom left.");
    }

    [Test]
    public void TheNarrowestScopeThatFitsBothStillUsesOneRow()
    {
        var width = WFCockpitRadarKeys.Edge + WFCockpitRadarKeys.Modes.X + WFCockpitRadarKeys.Gap + Keys.X + WFCockpitRadarKeys.Edge;
        var box = WFCockpitRadarKeys.Place(new Vector2(width, 300), Keys);
        Assert.That(box.Bottom, Is.EqualTo(300 - WFCockpitRadarKeys.Edge));
        Assert.That(box.Left, Is.EqualTo(WFCockpitRadarKeys.Edge + WFCockpitRadarKeys.Modes.X + WFCockpitRadarKeys.Gap));
    }

    [Test]
    public void ANarrowScopeStacksTheKeysAboveTheModeButtons()
    {
        var box = WFCockpitRadarKeys.Place(new Vector2(284, 300), Keys);
        Assert.That(box.Left, Is.EqualTo(WFCockpitRadarKeys.Edge));
        Assert.That(box.Bottom, Is.EqualTo(300 - WFCockpitRadarKeys.Edge - WFCockpitRadarKeys.Modes.Y - WFCockpitRadarKeys.Gap));
        Assert.That(box.Right, Is.LessThanOrEqualTo(284 - WFCockpitRadarKeys.Edge));
        Assert.That(box.Size, Is.EqualTo(Keys));
    }
}
