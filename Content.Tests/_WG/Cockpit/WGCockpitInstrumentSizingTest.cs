using Content.Client._WG.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WG.Cockpit;

/// <summary>Checks when the dial column swaps to compact faces and how small they get.</summary>
[TestFixture]
public sealed class WGCockpitInstrumentSizingTest
{
    [Test]
    public void FullDialsNeedAColumnThatShowsThemWhole()
    {
        var fits = WGCockpitInstrumentSizing.FullColumnHeight + WGCockpitInstrumentSizing.ColumnChrome;
        Assert.That(WGCockpitInstrumentSizing.UseCompact(fits), Is.False);
        Assert.That(WGCockpitInstrumentSizing.UseCompact(fits + 200), Is.False);
        Assert.That(WGCockpitInstrumentSizing.UseCompact(fits - 1), Is.True);
        Assert.That(WGCockpitInstrumentSizing.UseCompact(80), Is.True);
    }

    [Test]
    public void DialScaleFillsShortColumns()
    {
        var fits = WGCockpitInstrumentSizing.FullColumnHeight + WGCockpitInstrumentSizing.ColumnChrome;
        Assert.That(WGCockpitInstrumentSizing.DialScale(fits), Is.EqualTo(1f));
        Assert.That(WGCockpitInstrumentSizing.DialScale(80), Is.EqualTo(WGCockpitInstrumentSizing.MinScale));

        var shortBy = fits - 60;
        var scale = WGCockpitInstrumentSizing.DialScale(shortBy);
        Assert.That(scale, Is.GreaterThan(WGCockpitInstrumentSizing.MinScale).And.LessThan(1f));
        var used = WGCockpitInstrumentSizing.FullColumnHeight - WGCockpitInstrumentSizing.DialStackHeight
                   + WGCockpitInstrumentSizing.DialStackHeight * scale;
        Assert.That(used, Is.LessThanOrEqualTo(shortBy - WGCockpitInstrumentSizing.ColumnChrome));
        Assert.That(used, Is.GreaterThan(shortBy - WGCockpitInstrumentSizing.ColumnChrome - 10));
    }

    [TestCase(176, 110)]
    [TestCase(160, 100)]
    public void CompactFacesKeepTheEarlierReadableSizes(float full, float compact)
    {
        Assert.That(WGCockpitInstrumentSizing.CompactHeight(full), Is.EqualTo(compact));
        Assert.That(WGCockpitInstrumentSizing.CompactHeight(full), Is.LessThan(full));
    }
}
