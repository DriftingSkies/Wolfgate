#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Wolfmed.Medical;
using Content.Server.EntityEffects.Effects;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// "Healing chems don't heal wounds": removed damage only took 0.15 off a wound and stopped once the part's damage was
/// gone. A healing reagent now closes wolfmed.reagent_wound_mend severity per unit healed, worst wound first.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedReagentWoundMendSystem))]
public sealed class WolfmedReagentWoundMendTest : WolfmedGameTest
{
    /// <summary>Bicaridine closes a cut whose part shows no damage left, at its brute heal per tick.</summary>
    [Test]
    public async Task BicaridineClosesAWoundWithNoDamageLeftTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var bicaridine = Effect<EvenHealthChange>("Bicaridine");
            var perTick = -bicaridine.Damage["Brute"] * Server.CfgMan.GetCVar(WolfmedCVars.ReagentWoundMend);

            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 10)!.Value;
            bicaridine.Effect(Args(body));
            Assert.That(SEntMan.GetComponent<WoundComponent>(cut).Severity,
                Is.EqualTo(FixedPoint2.New(10) - perTick), "one tick closes its heal off the wound.");

            for (var tick = 0; tick < 20 && IsOpen(torso, cut); tick++)
                bicaridine.Effect(Args(body));
            Assert.That(IsOpen(torso, cut), Is.False, "the cut closes.");
        });
    }

    /// <summary>The worst wound of the healed types closes first; a burn is left to burn medicine.</summary>
    [Test]
    public async Task WorstMatchingWoundFirstTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var leg = Part(body, BodyPartType.Leg, BodyPartSymmetry.Left);
            var mend = SEntMan.System<WolfmedReagentWoundMendSystem>();

            var bruise = wounds.CreateOrMergeWound(leg, "BluntWound", 10)!.Value;
            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 30)!.Value;
            var burn = wounds.CreateOrMergeWound(torso, "BurnWound", 30)!.Value;

            mend.Mend(body, new[] { "Blunt", "Slash", "Piercing" }, FixedPoint2.New(5),
                new HashSet<TreatmentCapability> { TreatmentCapability.Biological });

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WoundComponent>(cut).Severity, Is.EqualTo(FixedPoint2.New(25)));
                Assert.That(SEntMan.GetComponent<WoundComponent>(bruise).Severity, Is.EqualTo(FixedPoint2.New(10)));
                Assert.That(SEntMan.GetComponent<WoundComponent>(burn).Severity, Is.EqualTo(FixedPoint2.New(30)));
            });
        });
    }

    /// <summary>Bleeds and mechanical-only healing are not a brute chem's to close.</summary>
    [Test]
    public async Task LeavesBleedsAndOtherMaterialsTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var wounds = SEntMan.System<WoundSystem>();
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso, BodyPartSymmetry.None);
            var mend = SEntMan.System<WolfmedReagentWoundMendSystem>();

            var bleed = wounds.CreateOrMergeWound(torso, "SystemicBleedingWound", 10)!.Value;
            var cut = wounds.CreateOrMergeWound(torso, "SlashWound", 10)!.Value;

            mend.Mend(body, new[] { "Slash" }, FixedPoint2.New(5),
                new HashSet<TreatmentCapability> { TreatmentCapability.Mechanical });
            Assert.That(SEntMan.GetComponent<WoundComponent>(cut).Severity, Is.EqualTo(FixedPoint2.New(10)),
                "a mechanical-only heal leaves organic flesh alone.");

            mend.Mend(body, new[] { "Blunt", "Slash", "Piercing", "Bloodloss" }, FixedPoint2.New(50),
                new HashSet<TreatmentCapability> { TreatmentCapability.Biological });
            Assert.Multiple(() =>
            {
                Assert.That(IsOpen(torso, cut), Is.False);
                Assert.That(SEntMan.GetComponent<WoundComponent>(bleed).Severity, Is.EqualTo(FixedPoint2.New(10)),
                    "a bleed closes only under gauze, sutures or surgery.");
            });
        });
    }

    private T Effect<T>(string reagent) where T : EntityEffect =>
        SProtoMan.Index<ReagentPrototype>(reagent).Metabolisms!["Medicine"].Effects.OfType<T>().First();

    private bool IsOpen(EntityUid part, EntityUid wound) =>
        SEntMan.System<WoundSystem>().GetWounds(part).Any(open => open.Owner == wound);

    private EntityEffectReagentArgs Args(EntityUid body) =>
        new(body, SEntMan, null, null, FixedPoint2.New(1), null, null, FixedPoint2.New(1));

    private EntityUid Part(EntityUid body, BodyPartType type, BodyPartSymmetry symmetry) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry).Id;
}
