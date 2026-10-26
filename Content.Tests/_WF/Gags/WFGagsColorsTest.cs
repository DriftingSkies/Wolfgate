#nullable enable
using System.Collections.Generic;
using Content.Shared._WF.Gags;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WF.Gags;

[TestFixture]
public sealed class WFGagsColorsTest
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
        Assert.That(WFGagsColors.SlotOf(state), Is.EqualTo(slot));
    }

    [Test]
    public void SavedColorsRoundTripTest()
    {
        var colors = new List<Color> { Color.FromHex("#12AB34"), Color.FromHex("#FFFFFF"), Color.FromHex("#000000") };
        var text = WFGagsColors.Format(colors);

        Assert.That(text, Is.EqualTo("#12AB34,#FFFFFF,#000000"));
        Assert.That(WFGagsColors.Parse(text), Is.EqualTo(colors));
        Assert.That(WFGagsColors.Format(null), Is.Empty);
        Assert.That(WFGagsColors.Parse(string.Empty), Is.Null);
    }

    [Test]
    public void BadSavedColorsAreDroppedTest()
    {
        Assert.That(WFGagsColors.Parse("#12AB34,banana,#FF0000"),
            Is.EqualTo(new List<Color> { Color.FromHex("#12AB34"), Color.FromHex("#FF0000") }));
        Assert.That(WFGagsColors.Parse("nothing"), Is.Null);
    }

    [Test]
    public void SanitizeCapsAndMakesOpaqueTest()
    {
        var sent = new List<Color>();
        for (var i = 0; i < WFGagsColors.MaxColors + 4; i++)
        {
            sent.Add(new Color(1f, 0f, 0f, 0.1f));
        }

        var safe = WFGagsColors.Sanitize(sent)!;
        Assert.That(safe, Has.Count.EqualTo(WFGagsColors.MaxColors));
        Assert.That(safe, Has.All.Matches<Color>(c => c.A == 1f));
        Assert.That(WFGagsColors.Sanitize(new List<Color>()), Is.Null);
    }
}
