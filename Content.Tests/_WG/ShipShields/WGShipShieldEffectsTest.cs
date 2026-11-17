using Content.Shared._WG.ShipShields;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._WG.ShipShields;

/// <summary>Checks shield condition and impact cues remain distinct.</summary>
[TestFixture]
public sealed class WGShipShieldEffectsTest
{
    [TestCase(1000f, 10000f, 0.5f, 1f, 1000f)]
    [TestCase(100000f, 10000f, 0.5f, 1f, 20000f)]
    [TestCase(100000f, 5000f, 0.5f, 2f, 100f)]
    [TestCase(1000f, 10000f, 0f, 1f, 1000f)]
    public void ImpactCapacityUsesTheFirstEmitterLimit(float limit, float draw, float modifier, float exponent, float expected)
    {
        Assert.That(WGShipShieldEffects.EffectiveCapacity(limit, draw, modifier, exponent), Is.EqualTo(expected).Within(0.01f));
    }

    [Test]
    public void DamageRelativeToCapacityControlsImpactStrength()
    {
        var small = WGShipShieldEffects.ImpactStrength(10f, 10000f);
        var medium = WGShipShieldEffects.ImpactStrength(100f, 10000f);
        var heavy = WGShipShieldEffects.ImpactStrength(500f, 10000f);
        Assert.That(small, Is.GreaterThan(0f));
        Assert.That(medium, Is.GreaterThan(small));
        Assert.That(heavy, Is.GreaterThan(medium));
        Assert.That(heavy, Is.EqualTo(1f));
        Assert.That(WGShipShieldEffects.ImpactStrength(50000f, 10000f), Is.EqualTo(heavy));
        Assert.That(WGShipShieldEffects.ImpactStrength(100f, 100000f), Is.LessThan(medium));
        Assert.That(WGShipShieldEffects.ImpactStrength(50f, 10000f), Is.LessThan(medium),
            "A reinforced sector consuming half the capacity should show a smaller impact.");
        Assert.That(WGShipShieldEffects.ImpactStrength(0f, 10000f), Is.Zero);
        Assert.That(WGShipShieldEffects.ImpactStrength(-1f, 10000f), Is.Zero);
        Assert.That(WGShipShieldEffects.ImpactStrength(float.NaN, 10000f), Is.Zero);
    }

    [Test]
    public void WeakHitsHaveSmallerDimmerPatchesAndShorterRipples()
    {
        var weak = WGShipShieldEffects.ImpactStrength(10f, 10000f);
        var scale = WGShipShieldEffects.ImpactRadiusScale(weak);
        Assert.That(scale, Is.InRange(0.2f, 0.5f));
        Assert.That(WGShipShieldEffects.ImpactRadiusScale(1f), Is.EqualTo(1f));
        Assert.That(WGShipShieldEffects.Flash(0f, 0f, weak), Is.LessThan(WGShipShieldEffects.Flash(0f, 0f, 1f)));
        Assert.That(WGShipShieldEffects.Heat(0f, 1f, weak), Is.LessThan(WGShipShieldEffects.Heat(0f, 1f, 1f)));
        Assert.That(WGShipShieldEffects.Flash(3f, 0f, weak), Is.LessThan(weak * WGShipShieldEffects.Flash(3f, 0f, 1f)));
        Assert.That(WGShipShieldEffects.Heat(3f, 1f, weak), Is.LessThan(weak * WGShipShieldEffects.Heat(3f, 1f, 1f)));
        var radius = WGShipShieldEffects.WaveSpeed;
        Assert.That(WGShipShieldEffects.Wave(radius * scale, 1f, weak), Is.GreaterThan(WGShipShieldEffects.Wave(radius, 1f, weak)));
        Assert.That(WGShipShieldEffects.Wave(radius, 1f, weak), Is.LessThan(0.001f));
        Assert.That(WGShipShieldEffects.WaveWake((radius - 3f) * scale, 1f, weak), Is.GreaterThan(WGShipShieldEffects.WaveWake(radius - 3f, 1f, weak)));
    }

    [TestCase("#FF3333")]
    [TestCase("#9933FF")]
    [TestCase("#3399FF")]
    [TestCase("#FF9933")]
    [TestCase("#50C878")]
    [TestCase("#FF0A70")]
    public void GeneratorColourSurvivesAtFullHealthAndBlendsWithDamage(string hex)
    {
        var generator = Color.FromHex(hex);
        Assert.That(WGShipShieldEffects.HealthColor(1f, generator), Is.EqualTo(generator));
        Assert.That(WGShipShieldEffects.HealthColor(2f, generator), Is.EqualTo(generator));
        var amber = WGShipShieldEffects.HealthColor(0.45f);
        var midpoint = WGShipShieldEffects.HealthColor(0.725f, generator);
        var expected = Color.InterpolateBetween(amber, generator, 0.5f);
        Assert.That(midpoint.R, Is.EqualTo(expected.R).Within(0.00001f));
        Assert.That(midpoint.G, Is.EqualTo(expected.G).Within(0.00001f));
        Assert.That(midpoint.B, Is.EqualTo(expected.B).Within(0.00001f));
        Assert.That(WGShipShieldEffects.HealthColor(0.45f, generator), Is.EqualTo(amber));
        Assert.That(WGShipShieldEffects.HealthColor(0f, generator), Is.EqualTo(WGShipShieldEffects.HealthColor(0f)));
        var idle = WGShipShieldEffects.Appearance(generator, 0f, 0f, 0f);
        Assert.That(idle.Tint, Is.EqualTo(Color.FromSrgb(generator)));
        var hit = WGShipShieldEffects.Appearance(generator, 0f, 0f, 1f);
        Assert.That(hit.Tint, Is.Not.EqualTo(idle.Tint));
    }

    [Test]
    public void LocalHeatReddensTheSurfaceAndLeavesUntouchedAreasAtHealthColour()
    {
        var health = WGShipShieldEffects.HealthColor(1f, Color.FromHex("#3399FF"));
        var idle = WGShipShieldEffects.Appearance(health, 0f, 0f, 0f);
        var hit = WGShipShieldEffects.Appearance(health, 1f, 0f, 0f);
        Assert.That(idle.SurfaceTint, Is.EqualTo(Color.FromSrgb(health)));
        Assert.That(hit.SurfaceTint.R, Is.GreaterThan(idle.SurfaceTint.R));
        Assert.That(hit.SurfaceTint.B, Is.LessThan(idle.SurfaceTint.B));
        Assert.That(hit.Surface, Is.GreaterThan(idle.Surface));
        var lingering = WGShipShieldEffects.Appearance(health, WGShipShieldEffects.Heat(0f, 9f, 1f) * 4f, 0f, 0f);
        Assert.That(lingering.SurfaceTint.R, Is.GreaterThan(idle.SurfaceTint.R));
        Assert.That(lingering.SurfaceTint.B, Is.LessThan(idle.SurfaceTint.B));
    }

    [Test]
    public void WhiteFlashStaysAtTheHitWhileTheColouredWaveTravels()
    {
        var health = WGShipShieldEffects.HealthColor(1f);
        var crest = WGShipShieldEffects.Appearance(health, 0f, 0f, WGShipShieldEffects.Flash(0f, 0f, 1f));
        var idle = WGShipShieldEffects.Appearance(health, 0f, 0f, 0f);
        var traveling = WGShipShieldEffects.Appearance(health, 0f,
            WGShipShieldEffects.Wave(11f, 0.5f, 1f), WGShipShieldEffects.Flash(11f, 0.5f, 1f));
        var wake = WGShipShieldEffects.Appearance(health, 0f, 0f, 0f, wake: 1f);
        foreach (var tint in new[] { crest.Tint, crest.SurfaceTint })
        {
            Assert.That(tint.R, Is.GreaterThan(0.75f));
            Assert.That(tint.G, Is.GreaterThan(0.75f));
            Assert.That(tint.B, Is.GreaterThan(0.75f));
        }
        Assert.That(traveling.Tint.R, Is.EqualTo(idle.Tint.R).Within(0.00001f));
        Assert.That(traveling.SurfaceTint.R, Is.EqualTo(idle.SurfaceTint.R).Within(0.00001f));
        Assert.That(traveling.Surface, Is.GreaterThan(idle.Surface));
        Assert.That(traveling.Hexes, Is.GreaterThan(idle.Hexes));
        foreach (var tint in new[] { wake.Tint, wake.SurfaceTint })
            Assert.That(tint.R, Is.GreaterThan(tint.B * 3f));
    }

    [Test]
    public void ImpactRevealsHexesMoreStronglyThanTheIdleSurface()
    {
        var health = WGShipShieldEffects.HealthColor(1f);
        var idle = WGShipShieldEffects.Appearance(health, 0f, 0f, 0f);
        var hit = WGShipShieldEffects.Appearance(health, WGShipShieldEffects.Heat(0f, 0f, 1f),
            WGShipShieldEffects.Wave(0f, 0f, 1f), WGShipShieldEffects.Flash(0f, 0f, 1f));
        Assert.That(idle.Surface, Is.LessThan(0.1f));
        Assert.That(idle.Hexes * WGShipShieldMesh.HexOpacity(0f), Is.InRange(0.1f, 0.15f));
        Assert.That(hit.Surface, Is.GreaterThan(idle.Surface * 5f));
        Assert.That(hit.Hexes, Is.GreaterThan(idle.Hexes * 10f));
    }

    [Test]
    public void DamagedShieldsBecomeMoreVisibleWithoutAnActiveImpact()
    {
        var healthy = WGShipShieldEffects.Appearance(WGShipShieldEffects.HealthColor(1f), 0f, 0f, 0f, 1f);
        var damaged = WGShipShieldEffects.Appearance(WGShipShieldEffects.HealthColor(0.5f), 0f, 0f, 0f, 0.5f);
        var critical = WGShipShieldEffects.Appearance(WGShipShieldEffects.HealthColor(0f), 0f, 0f, 0f, 0f);
        Assert.That(healthy.Surface, Is.LessThan(0.2f));
        Assert.That(damaged.Surface, Is.GreaterThan(healthy.Surface));
        Assert.That(critical.Surface, Is.GreaterThan(damaged.Surface));
        Assert.That(damaged.Hexes, Is.GreaterThan(healthy.Hexes));
        Assert.That(critical.Hexes, Is.GreaterThan(damaged.Hexes));
    }

    [Test]
    public void FlashStaysLocalAndHeatOutlastsTheRipple()
    {
        Assert.That(WGShipShieldEffects.Flash(8f, 0f, 1f), Is.LessThan(0.01f));
        Assert.That(WGShipShieldEffects.Flash(0f, -1f, 1f), Is.Zero);
        Assert.That(WGShipShieldEffects.Flash(0f, 0.65f, 1f), Is.Zero);
        Assert.That(WGShipShieldEffects.Wave(0f, WGShipShieldEffects.WaveLifetime, 1f), Is.Zero);
        Assert.That(WGShipShieldEffects.Heat(0f, 3f, 1f), Is.GreaterThan(0f));
        Assert.That(WGShipShieldEffects.Heat(0f, WGShipShieldEffects.HeatLifetime - 0.01f, 1f), Is.LessThan(0.001f));
        Assert.That(WGShipShieldEffects.Heat(0f, WGShipShieldEffects.HeatLifetime, 1f), Is.Zero);
    }

    [Test]
    public void RepeatedHitsLeaveBrighterRedderSurfaceAndHexesThenCool()
    {
        var health = WGShipShieldEffects.HealthColor(1f);
        var heat = WGShipShieldEffects.Heat(0f, 1f, 1f);
        var single = WGShipShieldEffects.Appearance(health, heat, 0f, 0f);
        var repeated = WGShipShieldEffects.Appearance(health, heat * 3f, 0f, 0f);
        Assert.That(repeated.Tint.R, Is.GreaterThan(single.Tint.R));
        Assert.That(repeated.Tint.B, Is.LessThan(single.Tint.B));
        Assert.That(repeated.Hexes, Is.GreaterThan(single.Hexes));
        Assert.That(repeated.SurfaceTint.R, Is.GreaterThan(single.SurfaceTint.R));
        Assert.That(repeated.SurfaceTint.B, Is.LessThan(single.SurfaceTint.B));
        var cooled = WGShipShieldEffects.Appearance(health, WGShipShieldEffects.Heat(0f, WGShipShieldEffects.HeatLifetime, 1f), 0f, 0f);
        Assert.That(cooled, Is.EqualTo(WGShipShieldEffects.Appearance(health, 0f, 0f, 0f)));
        var damaged = WGShipShieldEffects.Appearance(WGShipShieldEffects.HealthColor(0.2f), 0f, 0f, 0f);
        Assert.That(damaged.Tint.R, Is.GreaterThan(damaged.Tint.B));
    }

    [Test]
    public void ShieldHealthChangesFromBlueThroughAmberToRed()
    {
        var healthy = WGShipShieldEffects.HealthColor(1f);
        var damaged = WGShipShieldEffects.HealthColor(0.45f);
        var critical = WGShipShieldEffects.HealthColor(0f);
        Assert.Multiple(() =>
        {
            Assert.That(healthy.B, Is.GreaterThan(healthy.R));
            Assert.That(damaged.R, Is.GreaterThan(damaged.B));
            Assert.That(damaged.G, Is.GreaterThan(critical.G));
            Assert.That(critical.R, Is.GreaterThan(critical.G * 5f));
            Assert.That(WGShipShieldEffects.HealthColor(2f), Is.EqualTo(healthy));
            Assert.That(WGShipShieldEffects.HealthColor(-1f), Is.EqualTo(critical));
            Assert.That(WGShipShieldEffects.HealthColor(float.NaN), Is.EqualTo(critical));
        });
    }

    [Test]
    public void RepeatedImpactsAccumulateLocalHeatAndCool()
    {
        var initial = WGShipShieldEffects.Heat(0f, 0f, 1f);
        var previous = WGShipShieldEffects.Heat(0f, 1f, 1f);
        Assert.Multiple(() =>
        {
            Assert.That(initial + previous, Is.GreaterThan(initial));
            Assert.That(previous, Is.LessThan(initial));
            Assert.That(WGShipShieldEffects.Heat(8f, 0f, 1f), Is.LessThan(initial * 0.02f));
            Assert.That(WGShipShieldEffects.Heat(0f, 17f, 1f), Is.Zero);
            Assert.That(WGShipShieldEffects.Heat(0f, -1f, 1f), Is.Zero);
        });
    }

    [Test]
    public void ImpactWaveTravelsAwayFromItsOriginAndExpires()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WGShipShieldEffects.Wave(11f, 0.5f, 1f), Is.GreaterThan(WGShipShieldEffects.Wave(0f, 0.5f, 1f)));
            Assert.That(WGShipShieldEffects.Wave(11f, 0f, 1f), Is.LessThan(WGShipShieldEffects.Wave(11f, 0.5f, 1f)));
            Assert.That(WGShipShieldEffects.Wave(66f, 3f, 1f), Is.GreaterThan(0.05f), "Late waves must still be visible beyond the old forty-tile reach.");
            Assert.That(WGShipShieldEffects.Wave(88f, 4f, 1f), Is.Zero);
            Assert.That(WGShipShieldEffects.WaveWake(88f, 4f, 1f), Is.Zero);
        });
    }
}
