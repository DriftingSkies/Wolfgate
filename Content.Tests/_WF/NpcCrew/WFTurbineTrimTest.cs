using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using NUnit.Framework;

namespace Content.Tests._WF.NpcCrew;

/// <summary>The engineer's turbine trim: the stator load follows the speed error, in bigger steps while the blades suffer.</summary>
[TestFixture]
public sealed class WFTurbineTrimTest
{
    private static TurbineComponent Turbine(float rpm, float load = 35000f) => new() { RPM = rpm, StatorLoad = load, BestRPM = 600f };

    [Test]
    public void RunningFastRaisesTheLoadByTheSpeedError()
    {
        var turbine = Turbine(660f);
        Assert.That(WFCrewWorkSystem.Trim(turbine), Is.True);
        Assert.That(turbine.StatorLoad, Is.EqualTo(38500f).Within(0.5f));
    }

    [Test]
    public void OverspeedRaisesTheLoadByHalfAtLeast()
    {
        var turbine = Turbine(740f);
        turbine.Overspeed = true;
        Assert.That(WFCrewWorkSystem.Trim(turbine), Is.True);
        Assert.That(turbine.StatorLoad, Is.EqualTo(52500f).Within(0.5f), "A 23% overspeed still gets the 50% step.");

        var runaway = Turbine(1800f, 400000f);
        runaway.Overspeed = true;
        Assert.That(WFCrewWorkSystem.Trim(runaway), Is.True);
        Assert.That(runaway.StatorLoad, Is.EqualTo(runaway.StatorLoadMax), "Never past the turbine's own limit.");
    }

    [Test]
    public void RunningSlowLowersTheLoadAndAStallHalvesIt()
    {
        var slow = Turbine(300f);
        Assert.That(WFCrewWorkSystem.Trim(slow), Is.True);
        Assert.That(slow.StatorLoad, Is.EqualTo(17500f).Within(0.5f));

        var stalled = Turbine(0f);
        stalled.Stalling = true;
        Assert.That(WFCrewWorkSystem.Trim(stalled), Is.True);
        Assert.That(stalled.StatorLoad, Is.EqualTo(17500f).Within(0.5f));

        var floor = Turbine(0f, 1200f);
        floor.Stalling = true;
        Assert.That(WFCrewWorkSystem.Trim(floor), Is.True);
        Assert.That(floor.StatorLoad, Is.EqualTo(1000f), "Cut no further than the least worth having.");
    }

    [Test]
    public void InTheBandStandingOrColdTheLoadIsLeftAlone()
    {
        Assert.That(WFCrewWorkSystem.Trim(Turbine(600f)), Is.False, "On speed.");
        Assert.That(WFCrewWorkSystem.Trim(Turbine(585f)), Is.False, "Inside the band.");
        Assert.That(WFCrewWorkSystem.Trim(Turbine(5f)), Is.False, "Standing still, not stalled.");

        var cold = Turbine(900f);
        cold.Undertemp = true;
        Assert.That(WFCrewWorkSystem.Trim(cold), Is.False, "Cold gas tells the engineer nothing.");

        var ruined = Turbine(900f);
        ruined.Ruined = true;
        Assert.That(WFCrewWorkSystem.Trim(ruined), Is.False);
    }
}
