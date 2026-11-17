using System;
using Content.Client._WG.Cockpit;
using Content.Shared._WG.Shuttles;
using NUnit.Framework;

namespace Content.Tests._WG.Cockpit;

/// <summary>Prevents missing or disabled collision telemetry from lighting a clear-status indication.</summary>
[TestFixture]
public sealed class WGCockpitTcasReadingTest
{
    [TestCase(false, false, WGCockpitTcasState.NoSignal)]
    [TestCase(false, true, WGCockpitTcasState.NoSignal)]
    [TestCase(true, false, WGCockpitTcasState.Off)]
    [TestCase(true, true, WGCockpitTcasState.Clear)]
    public void ClearRequiresAvailableEnabledTelemetry(bool available, bool enabled, WGCockpitTcasState expected)
    {
        Assert.That(WGCockpitTcasReading.From(available, enabled, null).State, Is.EqualTo(expected));
    }

    [TestCase(CollisionWarningLevel.Advisory, WGCockpitTcasState.Caution)]
    [TestCase(CollisionWarningLevel.Imminent, WGCockpitTcasState.Warning)]
    public void WarningsRetainTheirSeverityAndTargetDetails(CollisionWarningLevel level, WGCockpitTcasState expected)
    {
        var warning = new CollisionWarningComponent
        {
            Level = level,
            ImpactTime = TimeSpan.FromSeconds(8),
            ThreatName = "Approaching vessel",
            Bearing = 315,
            ClosingSpeed = 24,
        };
        var reading = WGCockpitTcasReading.From(true, true, warning);
        Assert.Multiple(() =>
        {
            Assert.That(reading.State, Is.EqualTo(expected));
            Assert.That(reading.ImpactTime, Is.EqualTo(warning.ImpactTime));
            Assert.That(reading.ThreatName, Is.EqualTo(warning.ThreatName));
            Assert.That(reading.Bearing, Is.EqualTo(warning.Bearing));
            Assert.That(reading.ClosingSpeed, Is.EqualTo(warning.ClosingSpeed));
            Assert.That(WGCockpitTcasReading.From(true, false, warning).State, Is.EqualTo(WGCockpitTcasState.Off));
            Assert.That(WGCockpitTcasReading.From(false, true, warning).State, Is.EqualTo(WGCockpitTcasState.NoSignal));
        });
    }

    [TestCase(0, true)]
    [TestCase(0.999, true)]
    [TestCase(1, false)]
    [TestCase(1.999, false)]
    [TestCase(2, true)]
    public void CautionFlashesOnASeparateTwoSecondCycle(double seconds, bool illuminated)
    {
        var lamps = new WGCockpitTcasReading(WGCockpitTcasState.Caution).LampsAt(TimeSpan.FromSeconds(seconds));
        Assert.That(lamps, Is.EqualTo(new WGCockpitTcasLamps(illuminated, false, false, false)));
    }

    [TestCase(0, true, true)]
    [TestCase(0.249, true, true)]
    [TestCase(0.25, true, false)]
    [TestCase(0.499, true, false)]
    [TestCase(0.5, true, true)]
    [TestCase(1, false, true)]
    [TestCase(1.25, false, false)]
    [TestCase(1.5, false, true)]
    [TestCase(2, true, true)]
    public void WarningFlashesFasterWithoutReplacingCaution(double seconds, bool caution, bool warning)
    {
        var lamps = new WGCockpitTcasReading(WGCockpitTcasState.Warning).LampsAt(TimeSpan.FromSeconds(seconds));
        Assert.That(lamps, Is.EqualTo(new WGCockpitTcasLamps(caution, warning, false, false)));
    }

    [TestCase(WGCockpitTcasState.Clear, true, false)]
    [TestCase(WGCockpitTcasState.Off, false, true)]
    [TestCase(WGCockpitTcasState.NoSignal, false, true)]
    public void OkAndFaultStaySteadyDuringEveryFlashPhase(WGCockpitTcasState state, bool ok, bool fault)
    {
        foreach (var seconds in new[] { 0, 0.25, 0.5, 1, 1.25, 2 })
            Assert.That(new WGCockpitTcasReading(state).LampsAt(TimeSpan.FromSeconds(seconds)),
                Is.EqualTo(new WGCockpitTcasLamps(false, false, ok, fault)));
    }

}
