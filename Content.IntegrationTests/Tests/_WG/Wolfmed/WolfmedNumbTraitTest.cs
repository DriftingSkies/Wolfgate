#nullable enable
using System.Linq;
using Content.Server._WG.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Wolfmed.Consciousness;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Traits.Assorted;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// The Numb trait hides pain from the patient but still Downs and faints them; numbness without the trait, as a drug
/// gives it, keeps pain from putting the body down.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedConsciousnessSystem))]
public sealed class WolfmedNumbTraitTest : WolfmedGameTest
{
    [Test]
    public async Task NumbTraitStillGoesDownTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var pain = entities.System<PainSystem>();
            var bodies = entities.System<SharedBodySystem>();

            EntityUid Spawn(bool trait)
            {
                var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
                entities.EnsureComponent<PainNumbnessComponent>(mob);
                if (trait)
                    entities.EnsureComponent<WolfmedNumbTraitComponent>(mob);
                return mob;
            }

            EntityUid Part(EntityUid mob, BodyPartType type) =>
                bodies.GetBodyChildren(mob).First(part => part.Component.PartType == type).Id;

            WolfmedConsciousnessComponent State(EntityUid mob) =>
                entities.GetComponent<WolfmedConsciousnessComponent>(mob);

            var numb = Spawn(true);
            var drugged = Spawn(false);

            // 0.95 x the 135 soft cap is 128.25: 129 Downs a body that counts it.
            pain.SetPain(Part(numb, BodyPartType.Torso), FixedPoint2.New(129));
            pain.SetPain(Part(drugged, BodyPartType.Torso), FixedPoint2.New(129));

            Assert.That(pain.GetPain(numb), Is.EqualTo(FixedPoint2.Zero), "The trait body felt its pain.");
            Assert.That(State(numb).State, Is.EqualTo(WolfmedConsciousness.Downed), "The trait body shrugged off its pain.");
            Assert.That(State(numb).Cause, Is.EqualTo(WolfmedCause.Pain));
            Assert.That(State(drugged).State, Is.EqualTo(WolfmedConsciousness.Up),
                "Numbness without the trait no longer holds pain off.");

            // The faint line is 1.4 x 135 = 189 of summed pain.
            pain.SetPain(Part(numb, BodyPartType.Head), FixedPoint2.New(100));
            pain.SetPain(Part(drugged, BodyPartType.Head), FixedPoint2.New(100));

            Assert.That(State(numb).State, Is.EqualTo(WolfmedConsciousness.Unconscious), "The trait body didn't faint.");
            Assert.That(State(numb).Cause, Is.EqualTo(WolfmedCause.PainFaint));
            Assert.That(State(drugged).State, Is.EqualTo(WolfmedConsciousness.Up));

            entities.DeleteEntity(numb);
            entities.DeleteEntity(drugged);
        });
    }
}
