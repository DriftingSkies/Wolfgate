using Content.Client._WG.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WG.CombatConsole;

/// <summary>Keeps missile warnings quiet during repeated state refreshes and hidden-window updates.</summary>
[TestFixture]
public sealed class WGThreatAnnunciatorTest
{
    [Test]
    public void WarnsOnNewLockButDoesNotLoopWhileTracked()
    {
        var annunciator = new WGThreatAnnunciator();
        Assert.That(annunciator.Update(0, true), Is.False);
        Assert.That(annunciator.Update(1, true), Is.True);
        Assert.That(annunciator.Update(1, true), Is.False);
        Assert.That(annunciator.Update(3, true), Is.False);
        Assert.That(annunciator.Update(0, true), Is.False);
        Assert.That(annunciator.Update(2, true), Is.True);
    }

    [Test]
    public void HiddenUpdatesDoNotPlayOrQueueWarnings()
    {
        var annunciator = new WGThreatAnnunciator();
        Assert.That(annunciator.Update(1, false), Is.False);
        Assert.That(annunciator.Update(1, true), Is.False);
        Assert.That(annunciator.Update(0, false), Is.False);
        Assert.That(annunciator.Update(1, true), Is.True);
    }
}
