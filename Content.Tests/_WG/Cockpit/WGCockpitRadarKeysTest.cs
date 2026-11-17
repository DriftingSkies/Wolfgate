using System.Numerics;
using Content.Client._WG.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WG.Cockpit;

[TestFixture]
public sealed class WGCockpitRadarKeysTest
{
    private static readonly Vector2 Keys = new(164, 32);

    [Test]
    public void AWideScopePutsTheKeysInTheBottomRowAtTheRight()
    {
        var box = WGCockpitRadarKeys.Place(new Vector2(500, 400), Keys);
        Assert.That(box.Right, Is.EqualTo(500 - WGCockpitRadarKeys.Edge));
        Assert.That(box.Bottom, Is.EqualTo(400 - WGCockpitRadarKeys.Edge));
        Assert.That(box.Size, Is.EqualTo(Keys));
        Assert.That(box.Left, Is.GreaterThanOrEqualTo(WGCockpitRadarKeys.Edge + WGCockpitRadarKeys.Modes.X + WGCockpitRadarKeys.Gap),
            "The keys must clear the mode buttons at the bottom left.");
    }

    [Test]
    public void TheNarrowestScopeThatFitsBothStillUsesOneRow()
    {
        var width = WGCockpitRadarKeys.Edge + WGCockpitRadarKeys.Modes.X + WGCockpitRadarKeys.Gap + Keys.X + WGCockpitRadarKeys.Edge;
        var box = WGCockpitRadarKeys.Place(new Vector2(width, 300), Keys);
        Assert.That(box.Bottom, Is.EqualTo(300 - WGCockpitRadarKeys.Edge));
        Assert.That(box.Left, Is.EqualTo(WGCockpitRadarKeys.Edge + WGCockpitRadarKeys.Modes.X + WGCockpitRadarKeys.Gap));
    }

    [Test]
    public void ANarrowScopeStacksTheKeysAboveTheModeButtons()
    {
        var box = WGCockpitRadarKeys.Place(new Vector2(284, 300), Keys);
        Assert.That(box.Left, Is.EqualTo(WGCockpitRadarKeys.Edge));
        Assert.That(box.Bottom, Is.EqualTo(300 - WGCockpitRadarKeys.Edge - WGCockpitRadarKeys.Modes.Y - WGCockpitRadarKeys.Gap));
        Assert.That(box.Right, Is.LessThanOrEqualTo(284 - WGCockpitRadarKeys.Edge));
        Assert.That(box.Size, Is.EqualTo(Keys));
    }
}
