using System.Linq;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server.EntityEffects.Effects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Drunk;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// Ethanol holds the drunk timer at its units in the blood, and a smaller hold never cuts a longer timer short.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAlcoholSystem))]
public sealed class AlcoholTest
{
    [Test]
    public async Task DrunkFollowsBloodTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = server.System<StatusEffectsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var ethanol = server.ProtoMan.Index<ReagentPrototype>("Ethanol");
            var drunk = ethanol.Metabolisms!["Alcohol"].Effects.OfType<Drunk>().Single();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            double Left()
            {
                return status.TryGetTime(mob, SharedDrunkSystem.DrunkKey, out var time)
                    ? (time.Value.Item2 - timing.CurTime).TotalSeconds
                    : 0;
            }

            void Metabolise(float units)
            {
                var blood = new Solution();
                blood.AddReagent(ethanol.ID, FixedPoint2.New(units));
                drunk.Effect(new EntityEffectReagentArgs(mob, entMan, null, blood, FixedPoint2.New(0.025), ethanol, null,
                    FixedPoint2.New(1)));
            }

            Metabolise(5f);
            Assert.That(Left(), Is.EqualTo(100).Within(0.5), "5 u holds 100 s: booze power 2 times 10 a unit.");

            Metabolise(2f);
            Assert.That(Left(), Is.EqualTo(100).Within(0.5), "Less ethanol doesn't cut a longer timer short.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An overdose drunkenness counts only the units above its threshold: Bicaridine from 16 u.</summary>
    [Test]
    public async Task DrunkAboveThresholdTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = server.System<StatusEffectsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var bicaridine = server.ProtoMan.Index<ReagentPrototype>("Bicaridine");
            var drunk = bicaridine.Metabolisms!["Medicine"].Effects.OfType<Drunk>().Single();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            double Left()
            {
                return status.TryGetTime(mob, SharedDrunkSystem.DrunkKey, out var time)
                    ? (time.Value.Item2 - timing.CurTime).TotalSeconds
                    : 0;
            }

            void Metabolise(float units)
            {
                var blood = new Solution();
                blood.AddReagent(bicaridine.ID, FixedPoint2.New(units));
                drunk.Effect(new EntityEffectReagentArgs(mob, entMan, null, blood, FixedPoint2.New(0.2), bicaridine,
                    null, FixedPoint2.New(1)));
            }

            Metabolise(15f);
            Assert.That(Left(), Is.EqualTo(0), "Below the threshold there is no drunkenness.");

            Metabolise(20f);
            Assert.That(Left(), Is.EqualTo(3 * drunk.PerUnitInBlood * 4).Within(0.5), "Only the 4 u above 16 count.");
        });

        await pair.CleanReturnAsync();
    }
}
