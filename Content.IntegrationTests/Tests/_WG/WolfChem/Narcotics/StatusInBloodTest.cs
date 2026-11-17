using System.Linq;
using Content.Server.EntityEffects.Effects.StatusEffects;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffect;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.WolfChem.Narcotics;

/// <summary>
/// A status effect with a per-unit time is held at the units left in the blood, as long as it used to pile up to.
/// </summary>
[TestFixture]
[TestOf(typeof(GenericStatusEffect))]
public sealed class StatusInBloodTest
{
    [Test]
    public async Task StatusFollowsBloodTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var status = server.System<StatusEffectsSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var chloral = server.ProtoMan.Index<ReagentPrototype>("ChloralHydrate");
            var drowsy = chloral.Metabolisms!["Poison"].Effects.OfType<GenericStatusEffect>()
                .Single(e => e.Key == "Drowsiness");
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            double Left()
            {
                return status.TryGetTime(mob, "Drowsiness", out var time)
                    ? (time.Value.Item2 - timing.CurTime).TotalSeconds
                    : 0;
            }

            void Metabolise(float units)
            {
                var blood = new Solution();
                blood.AddReagent(chloral.ID, FixedPoint2.New(units));
                drowsy.Effect(new EntityEffectReagentArgs(mob, entMan, null, blood, FixedPoint2.New(0.5), chloral,
                    null, FixedPoint2.New(1)));
            }

            Metabolise(10f);
            Assert.That(Left(), Is.EqualTo(80).Within(0.5), "10 u holds 80 s: 4 s a tick over 20 ticks, as before.");

            Metabolise(4f);
            Assert.That(Left(), Is.EqualTo(80).Within(0.5), "Fewer units left don't cut the timer short.");
        });

        await pair.CleanReturnAsync();
    }
}
