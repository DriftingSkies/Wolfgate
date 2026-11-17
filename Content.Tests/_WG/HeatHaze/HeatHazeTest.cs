using Content.Client._WG.HeatHaze;
using Content.Shared.Atmos.EntitySystems;
using NUnit.Framework;

namespace Content.Tests._WG.HeatHaze;

[TestFixture]
[TestOf(typeof(HeatHazeOverlay))]
public sealed class HeatHazeTest
{
    /// <summary>
    /// No haze at or below the threshold, full haze from the top of the range, and rising steadily in between.
    /// </summary>
    [Test]
    public void HeatCurve()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HeatHazeOverlay.GetHeat(293.15f), Is.Zero);
            Assert.That(HeatHazeOverlay.GetHeat(HeatHazeOverlay.MinTemperature), Is.Zero);
            Assert.That(HeatHazeOverlay.GetHeat(HeatHazeOverlay.FullTemperature), Is.EqualTo(1f));
            Assert.That(HeatHazeOverlay.GetHeat(5000f), Is.EqualTo(1f));
        });

        var last = 0f;

        for (var kelvin = HeatHazeOverlay.MinTemperature + 4f; kelvin <= HeatHazeOverlay.FullTemperature; kelvin += 4f)
        {
            var heat = HeatHazeOverlay.GetHeat(kelvin);
            Assert.That(heat, Is.GreaterThan(last), $"Heat fell at {kelvin} K.");
            last = heat;
        }
    }

    /// <summary>
    /// Networked tile temperatures: vacuum and tiles without air never shimmer, however the byte decodes.
    /// </summary>
    [Test]
    public void NetworkedTemperatures()
    {
        var vacuum = new ThermalByte();
        vacuum.SetVacuum();

        Assert.Multiple(() =>
        {
            Assert.That(HeatHazeOverlay.GetHeat(vacuum), Is.Zero);
            Assert.That(HeatHazeOverlay.GetHeat(new ThermalByte()), Is.Zero, "Walls and space have no air.");
            Assert.That(HeatHazeOverlay.GetHeat(new ThermalByte(293.15f)), Is.Zero);
            Assert.That(HeatHazeOverlay.GetHeat(new ThermalByte(1000f)), Is.EqualTo(1f));
        });
    }
}
