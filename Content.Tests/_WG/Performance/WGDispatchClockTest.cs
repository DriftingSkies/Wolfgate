using System.Collections.Generic;
using Content.Server._WG.Performance;
using NUnit.Framework;

namespace Content.Tests._WG.Performance;

/// <summary>Checks each client message is charged from the end of the one before it, and the first from the tick's total.</summary>
[TestFixture]
[TestOf(typeof(WGDispatchClock))]
public sealed class WGDispatchClockTest
{
    // One timestamp tick is a millisecond.
    private const long Frequency = 1000;

    [Test]
    public void ChargesEachMessageItsShareOfTheTick()
    {
        var clock = new WGDispatchClock(Frequency);
        var charged = new Dictionary<string, double>();
        WGDispatchClock.Charge record = (label, seconds, _, _) => charged[label] = seconds * 1000;

        Run(clock, "A", 10, record);
        Run(clock, "B", 15, record);
        Run(clock, "C", 45, record);
        Assert.That(charged, Does.Not.ContainKey("A"), "The first message waits for the tick's total.");

        clock.EndTick(0.050, record);

        Assert.That(charged["B"], Is.EqualTo(5).Within(1e-9));
        Assert.That(charged["C"], Is.EqualTo(30).Within(1e-9));
        Assert.That(charged["A"], Is.EqualTo(15).Within(1e-9), "50 ms of EntityNet less the 35 the others took.");
    }

    [Test]
    public void EachTickStartsAfresh()
    {
        var clock = new WGDispatchClock(Frequency);
        var charged = new List<(string Label, double Ms)>();
        WGDispatchClock.Charge record = (label, seconds, _, _) => charged.Add((label, seconds * 1000));

        Run(clock, "A", 10, record);
        clock.EndTick(0.004, record);
        Run(clock, "B", 500, record);
        clock.EndTick(0.002, record);

        Assert.That(charged, Has.Count.EqualTo(2));
        Assert.That(charged[0].Ms, Is.EqualTo(4).Within(1e-9));
        Assert.That(charged[1].Ms, Is.EqualTo(2).Within(1e-9), "The gap between ticks is nobody's.");
    }

    [Test]
    public void UnknownTotalDropsOnlyTheFirst()
    {
        var clock = new WGDispatchClock(Frequency);
        var charged = new List<string>();
        WGDispatchClock.Charge record = (label, _, _, _) => charged.Add(label);

        Run(clock, "A", 10, record);
        Run(clock, "B", 20, record);
        clock.EndTick(null, record);

        Assert.That(charged, Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void FinishWithoutRaiseChargesNothing()
    {
        var clock = new WGDispatchClock(Frequency);
        var charged = new List<string>();
        WGDispatchClock.Charge record = (label, _, _, _) => charged.Add(label);

        clock.Finished(10, null, record);
        clock.EndTick(0.010, record);

        Assert.That(charged, Is.Empty);
    }

    private static void Run(WGDispatchClock clock, string label, long end, WGDispatchClock.Charge record)
    {
        clock.Raised(label, label);
        clock.Finished(end, null, record);
    }
}
