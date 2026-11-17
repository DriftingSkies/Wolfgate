#nullable enable
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;
using Content.Server._WG.Wolfmed.Life;
using Content.Server._WG.Wolfmed.Medical;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WG.Wolfmed.EntityEffects;
using Content.Shared.Chemistry.Components;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// Mannitol restores a damaged brain: a metabolised dose raises the brain organ's health, never past its maximum,
/// and a brain at zero stays dead.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedBrainMendSystem))]
public sealed class WolfmedMannitolTest : WolfmedGameTest
{
    [Test]
    public async Task MannitolRestoresADamagedBrainTest()
    {
        var map = await CreateTestMap();
        var life = SEntMan.System<WolfmedLifeSystem>();
        var organs = SEntMan.System<OrganHealthSystem>();
        EntityUid patient = default;

        await Server.WaitPost(() =>
        {
            new WolfmedScenario(SEntMan).SetAir(map.MapUid, true);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var brain = life.GetBrainOrgan(patient)!.Value;
            organs.SetHealth(brain, FixedPoint2.New(6));
            Assert.That(SEntMan.System<BloodstreamSystem>()
                .TryAddToChemicals(patient, new Solution("Mannitol", FixedPoint2.New(10))), Is.True);
        });
        await RunSeconds(40);

        await Server.WaitAssertion(() =>
        {
            var brain = life.GetBrainOrgan(patient)!.Value;
            Assert.That(brain.Comp.Health, Is.GreaterThan(FixedPoint2.New(8)), "a 10u dose did not restore the brain.");
            Assert.That(brain.Comp.Health, Is.LessThanOrEqualTo(brain.Comp.MaxHealth));

            // Never past the maximum.
            organs.SetHealth(brain, brain.Comp.MaxHealth - FixedPoint2.New(0.1));
            var effect = new WolfmedMendBrain();
            effect.Effect(new EntityEffectBaseArgs(patient, SEntMan));
            Assert.That(brain.Comp.Health, Is.EqualTo(brain.Comp.MaxHealth));

            // A dead brain stays dead.
            organs.SetHealth(brain, FixedPoint2.Zero);
            effect.Effect(new EntityEffectBaseArgs(patient, SEntMan));
            Assert.That(brain.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "mannitol brought a dead brain back.");
        });
    }
}
