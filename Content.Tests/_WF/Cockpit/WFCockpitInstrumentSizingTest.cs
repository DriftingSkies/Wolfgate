using Content.Client._WF.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks when the dial column swaps to compact faces and how small they get.</summary>
[TestFixture]
public sealed class WFCockpitInstrumentSizingTest
{
    [Test]
    public void FullDialsNeedAColumnThatShowsThemWhole()
    {
        var fits = WFCockpitInstrumentSizing.FullColumnHeight + WFCockpitInstrumentSizing.ColumnChrome;
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits), Is.False);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits + 200), Is.False);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(fits - 1), Is.True);
        Assert.That(WFCockpitInstrumentSizing.UseCompact(80), Is.True);
    }

    [Test]
    public void DialScaleFillsShortColumns()
    {
        var fits = WFCockpitInstrumentSizing.FullColumnHeight + WFCockpitInstrumentSizing.ColumnChrome;
        Assert.That(WFCockpitInstrumentSizing.DialScale(fits), Is.EqualTo(1f));
        Assert.That(WFCockpitInstrumentSizing.DialScale(80), Is.EqualTo(WFCockpitInstrumentSizing.MinScale));

        var shortBy = fits - 60;
        var scale = WFCockpitInstrumentSizing.DialScale(shortBy);
        Assert.That(scale, Is.GreaterThan(WFCockpitInstrumentSizing.MinScale).And.LessThan(1f));
        var used = WFCockpitInstrumentSizing.FullColumnHeight - WFCockpitInstrumentSizing.DialStackHeight
                   + WFCockpitInstrumentSizing.DialStackHeight * scale;
        Assert.That(used, Is.LessThanOrEqualTo(shortBy - WFCockpitInstrumentSizing.ColumnChrome));
        Assert.That(used, Is.GreaterThan(shortBy - WFCockpitInstrumentSizing.ColumnChrome - 10));
    }

    [TestCase(176, 110)]
    [TestCase(160, 100)]
    public void CompactFacesKeepTheEarlierReadableSizes(float full, float compact)
    {
        Assert.That(WFCockpitInstrumentSizing.CompactHeight(full), Is.EqualTo(compact));
        Assert.That(WFCockpitInstrumentSizing.CompactHeight(full), Is.LessThan(full));
    }
}
