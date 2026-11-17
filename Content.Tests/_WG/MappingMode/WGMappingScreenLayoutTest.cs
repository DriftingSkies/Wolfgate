using System.Numerics;
using Content.Client._WG.MappingMode.Screen;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingScreenLayoutTest
{
    private static readonly Vector2 Area = new(800, 600);
    private static readonly Vector2 Bar = new(300, 40);

    [Test]
    public void ReadsBackWhatItWrites()
    {
        var layout = new WGMappingScreenLayout { Bar = new Vector2(0.25f, 1f), Vertical = true };
        var read = WGMappingScreenLayout.Read(layout.Write());
        Assert.Multiple(() =>
        {
            Assert.That(read.Bar, Is.EqualTo(new Vector2(0.25f, 1f)));
            Assert.That(read.Vertical, Is.True);
        });
    }

    [TestCase("")]
    [TestCase("not: [yaml")]
    [TestCase("barX: 0.5")]
    [TestCase("barX: nope\nbarY: 0.5")]
    [TestCase("- a list")]
    public void BadTextGivesTheDefaultSpot(string text)
    {
        Assert.That(WGMappingScreenLayout.Read(text).Bar, Is.Null);
    }

    [Test]
    public void FractionsAreClamped()
    {
        Assert.That(new WGMappingScreenLayout { Bar = new Vector2(-2f, 5f) }.Bar, Is.EqualTo(new Vector2(0f, 1f)));
        Assert.That(new WGMappingScreenLayout { Bar = new Vector2(float.NaN, 0.5f) }.Bar, Is.Null);
    }

    [Test]
    public void DropsNearAnEdgeSnap()
    {
        var free = Area - Bar - new Vector2(WGMappingScreenLayout.Margin * 2);
        Assert.Multiple(() =>
        {
            // Near the left and bottom edges.
            Assert.That(WGMappingScreenLayout.Fraction(new Vector2(20, free.Y), Area, Bar), Is.EqualTo(new Vector2(0f, 1f)));

            // In the middle it keeps where it was dropped.
            var middle = WGMappingScreenLayout.Fraction(new Vector2(260, 280), Area, Bar);
            Assert.That(middle.X, Is.EqualTo(250 / free.X).Within(0.0001f));
            Assert.That(middle.Y, Is.EqualTo(270 / free.Y).Within(0.0001f));

            // Placing a fraction gives back the dropped spot, and a snapped one sits at the margin.
            Assert.That(WGMappingScreenLayout.Place(middle, Area, Bar).X, Is.EqualTo(260).Within(0.01f));
            Assert.That(WGMappingScreenLayout.Place(new Vector2(1f, 0f), Area, Bar),
                Is.EqualTo(new Vector2(Area.X - Bar.X - WGMappingScreenLayout.Margin, WGMappingScreenLayout.Margin)));
        });
    }

    [Test]
    public void OrientationFollowsTheEdge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGMappingScreenLayout.VerticalAfterDrop(new Vector2(0f, 0.5f), false), Is.True, "Left edge.");
            Assert.That(WGMappingScreenLayout.VerticalAfterDrop(new Vector2(1f, 0.3f), false), Is.True, "Right edge.");
            Assert.That(WGMappingScreenLayout.VerticalAfterDrop(new Vector2(0.4f, 1f), true), Is.False, "Bottom edge.");
            Assert.That(WGMappingScreenLayout.VerticalAfterDrop(new Vector2(0f, 1f), true), Is.True, "A corner keeps it.");
            Assert.That(WGMappingScreenLayout.VerticalAfterDrop(new Vector2(0.5f, 0.5f), false), Is.False, "Mid view keeps it.");
        });
    }
}
