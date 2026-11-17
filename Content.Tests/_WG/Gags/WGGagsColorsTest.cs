#nullable enable
using System.Collections.Generic;
using Content.Shared._WG.Gags;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.Gags;

[TestFixture]
public sealed class WGGagsColorsTest
{
    [TestCase("icon-1-c1", 0)]
    [TestCase("equipped-INNERCLOTHING-3-c2", 1)]
    [TestCase("inhand-left-2-c16", 15)]
    [TestCase("icon-2", null)]
    [TestCase("icon", null)]
    [TestCase("icon-1-c0", null)]
    [TestCase("icon-1-c17", null)]
    [TestCase(null, null)]
    public void SlotOfTest(string? state, int? slot)
    {
        Assert.That(WGGagsColors.SlotOf(state), Is.EqualTo(slot));
    }

    [Test]
    public void SavedColorsRoundTripTest()
    {
        var colors = new List<Color> { Color.FromHex("#12AB34"), Color.FromHex("#FFFFFF"), Color.FromHex("#000000") };
        var text = WGGagsColors.Format(colors);

        Assert.That(text, Is.EqualTo("#12AB34,#FFFFFF,#000000"));
        Assert.That(WGGagsColors.Parse(text), Is.EqualTo(colors));
        Assert.That(WGGagsColors.Format(null), Is.Empty);
        Assert.That(WGGagsColors.Parse(string.Empty), Is.Null);
    }

    [Test]
    public void BadSavedColorsAreDroppedTest()
    {
        Assert.That(WGGagsColors.Parse("#12AB34,banana,#FF0000"),
            Is.EqualTo(new List<Color> { Color.FromHex("#12AB34"), Color.FromHex("#FF0000") }));
        Assert.That(WGGagsColors.Parse("nothing"), Is.Null);
    }

    [Test]
    public void SanitizeCapsAndMakesOpaqueTest()
    {
        var sent = new List<Color>();
        for (var i = 0; i < WGGagsColors.MaxColors + 4; i++)
        {
            sent.Add(new Color(1f, 0f, 0f, 0.1f));
        }

        var safe = WGGagsColors.Sanitize(sent)!;
        Assert.That(safe, Has.Count.EqualTo(WGGagsColors.MaxColors));
        Assert.That(safe, Has.All.Matches<Color>(c => c.A == 1f));
        Assert.That(WGGagsColors.Sanitize(new List<Color>()), Is.Null);
    }
}
