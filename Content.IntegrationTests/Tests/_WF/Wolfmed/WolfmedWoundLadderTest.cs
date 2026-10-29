#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Revival pass: wound ladders, the overflow a capped limb counts toward severing, and the pain falloff setting.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedWoundLadderSystem))]
public sealed class WolfmedWoundLadderTest : WolfmedGameTest
{
    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(part => part.Component.PartType == type).Id;

    private List<(string Id, float Severity)> Wounds(EntityUid part) =>
        SEntMan.System<WoundSystem>().GetWounds((part, SEntMan.GetComponent<WoundableComponent>(part)))
            .Where(wound => wound.Comp.State is not WoundState.Healed and not WoundState.Scarred)
            .Select(wound => (wound.Comp.Prototype.Id, wound.Comp.Severity.Float()))
            .ToList();

    private EntityUid? Create(EntityUid part, string wound, float severity) =>
        SEntMan.System<WoundSystem>().CreateOrMergeWound(part, wound, FixedPoint2.New(severity));

    /// <summary>
    /// A worse rung takes the wound over carrying the lesser one's share; a lesser one feeds the worse; a full rung
    /// opens the next at half its maximum plus the rescaled excess.
    /// </summary>
    [Test]
    public async Task WoundsOfAKindFoldAndClimbTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm);
            var leg = Part(body, BodyPartType.Leg);

            // Slash 40 of 200, then an arterial bleed (max 120) of 30 from a later hit: 30 + 40 x 0.6 = 54.
            Create(arm, "SlashWound", 40);
            Create(arm, "WFWolfmedArterialBleedWound", 30);
            Assert.That(Wounds(arm), Is.EqualTo(new List<(string, float)> { ("WFWolfmedArterialBleedWound", 54f) }));

            // A later slash of 50 feeds the artery at its scale: 54 + 50 x 0.6 = 84.
            Create(arm, "SlashWound", 50);
            Assert.That(Wounds(arm), Is.EqualTo(new List<(string, float)> { ("WFWolfmedArterialBleedWound", 84f) }));

            // A slash at 190 pushed 20 further: full at 200, the excess 10 opens the artery at 60 + 10 x 0.6 = 66.
            Create(leg, "SlashWound", 190);
            Create(leg, "SlashWound", 20);
            Assert.That(Wounds(leg), Is.EqualTo(new List<(string, float)> { ("WFWolfmedArterialBleedWound", 66f) }));
        });
    }

    /// <summary>Past its top rung a limb is armed for severing; a torso and a head never are.</summary>
    [Test]
    public async Task TopRungArmsALimbOnlyTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm);
            var torso = Part(body, BodyPartType.Torso);

            foreach (var part in new[] { arm, torso })
            {
                Create(part, "WFWolfmedAvulsionWound", 140);
                Create(part, "WFWolfmedAvulsionWound", 30);
            }

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WoundableComponent>(arm).Severable, Is.True, "a full top rung left the arm on.");
                Assert.That(SEntMan.GetComponent<WoundableComponent>(torso).Severable, Is.False, "a torso was armed.");
                Assert.That(Wounds(arm).Single().Severity, Is.EqualTo(150f), "the top rung passed its maximum.");
            });
        });
    }

    /// <summary>Whatever was lodged in a wound stays in the part when a worse rung takes it over.</summary>
    [Test]
    public async Task LodgedObjectsMoveWithTheWoundTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm);
            var shrapnel = Create(arm, "WFWolfmedShrapnelWound", 20)!.Value;
            SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(shrapnel, "WFWolfmedShrapnelFragment", 2, 5);

            var lodged = Create(arm, "WFWolfmedLodgedRoundWound", 20)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(Wounds(arm).Select(w => w.Id), Is.EqualTo(new[] { "WFWolfmedLodgedRoundWound" }));
                Assert.That(SEntMan.GetComponent<WolfmedEmbeddedObjectComponent>(lodged).Count, Is.EqualTo(2));
            });
        });
    }

    /// <summary>
    /// Bug report 234: a gunshot that only got worse climbed into shrapnel and a lodged round with nothing in them, which
    /// a hemostat can't pull and never close. It now stays a gunshot; a wound that does hold fragments still climbs.
    /// </summary>
    [Test]
    public async Task OnlyLodgedObjectsClimbIntoObjectRungsTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(body, BodyPartType.Torso);
            var arm = Part(body, BodyPartType.Arm);

            Create(torso, "WFWolfmedGunshotWound", 190);
            Create(torso, "WFWolfmedGunshotWound", 50);

            var shrapnel = Create(arm, "WFWolfmedShrapnelWound", 140)!.Value;
            SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(shrapnel, "WFWolfmedShrapnelFragment", 2, 5);
            Create(arm, "WFWolfmedShrapnelWound", 30);
            var climbed = SEntMan.System<WolfmedEmbeddedObjectSystem>().GetEmbeddedWound(arm);

            // A full gunshot on a limb held back from the object rungs is not the top of the ladder, so it arms nothing.
            var leg = Part(body, BodyPartType.Leg);
            Create(leg, "WFWolfmedGunshotWound", 190);
            Create(leg, "WFWolfmedGunshotWound", 50);

            // A fragment hit onto a nearly full gunshot overflows the shrapnel it makes; the fragment comes with it.
            var otherArm = SEntMan.System<SharedBodySystem>().GetBodyChildren(body)
                .First(p => p.Component.PartType == BodyPartType.Arm && p.Id != arm).Id;
            Create(otherArm, "WFWolfmedGunshotWound", 190);
            Create(otherArm, "WFWolfmedShrapnelWound", 140);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<WoundableComponent>(leg).Severable, Is.False,
                    "a gunshot kept from the object rungs armed the limb as if it topped the ladder.");
                Assert.That(Wounds(otherArm).Select(w => w.Id), Is.EqualTo(new[] { "WFWolfmedLodgedRoundWound" }),
                    "a fragment hit that overflowed its shrapnel wound did not climb.");
                Assert.That(Wounds(torso).Select(w => w.Id), Is.EqualTo(new[] { "WFWolfmedGunshotWound" }),
                    "a worsening gunshot became a wound that holds an object it doesn't have.");
                Assert.That(Wounds(arm).Select(w => w.Id), Is.EqualTo(new[] { "WFWolfmedLodgedRoundWound" }),
                    "a full shrapnel wound with fragments in it did not climb.");
                Assert.That(climbed?.Comp.Count, Is.EqualTo(2), "the fragments did not move with the wound.");
            });
        });
    }

    /// <summary><c>wolfmed.wound_ladders</c> off is the old stacking.</summary>
    [Test]
    public async Task LaddersOffStackTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.WoundLadders, false);
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm);
            Create(arm, "SlashWound", 40);
            Create(arm, "WFWolfmedArterialBleedWound", 30);
            Assert.That(Wounds(arm).Select(w => w.Id), Is.EquivalentTo(new[] { "SlashWound", "WFWolfmedArterialBleedWound" }));
        });
    }

    /// <summary>
    /// A capped arm stores 100 of a 130 Slash hit and counts the 30 over toward its Slash threshold of 130, so it is
    /// armed exactly as an uncapped arm was; a capped head counts none and is never armed; healing the arm back under
    /// its cap clears the count.
    /// </summary>
    [Test]
    public async Task CappedLimbSeversAtItsOldTotalsTest()
    {
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var attacker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var arm = Part(body, BodyPartType.Arm);
            var head = Part(body, BodyPartType.Head);
            var routing = SEntMan.System<WoundDamageRoutingSystem>();
            DamageSpecifier Slash(float amount) => new()
            {
                DamageDict = { [new ProtoId<DamageTypePrototype>("Slash")] = FixedPoint2.New(amount) },
            };

            routing.TryApplyPartDamage(body, arm, Slash(129), attacker, ignoreResistances: true);
            Assert.That(SEntMan.GetComponent<WoundableComponent>(arm).Severable, Is.False, "129 of 130 armed the arm.");
            routing.TryApplyPartDamage(body, arm, Slash(1), attacker, ignoreResistances: true);
            routing.TryApplyPartDamage(body, head, Slash(400), attacker, ignoreResistances: true);

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<DamageableComponent>(arm).TotalDamage, Is.EqualTo(FixedPoint2.New(100)));
                Assert.That(SEntMan.GetComponent<WolfmedPartOverflowComponent>(arm).Damage.DamageDict["Slash"],
                    Is.EqualTo(FixedPoint2.New(30)));
                Assert.That(SEntMan.GetComponent<WoundableComponent>(arm).Severable, Is.True, "130 Slash did not arm the arm.");
                Assert.That(SEntMan.GetComponent<WoundableComponent>(head).Severable, Is.False, "piled-up damage armed the head.");
            });

            // Healed back under its cap, the arm forgets what it took past it and is no longer armed.
            routing.TryApplyPartDamage(body, arm, Slash(-50), attacker, ignoreResistances: true);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<WolfmedPartOverflowComponent>(arm), Is.False, "healing kept the overflow.");
                Assert.That(SEntMan.GetComponent<WoundableComponent>(arm).AmputationOverflow, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(SEntMan.GetComponent<WoundableComponent>(arm).Severable, Is.False, "a healed arm stayed armed.");
            });
        });
    }

    /// <summary>
    /// <c>wolfmed.pain_spread_falloff</c>: parts combine worst first, each further one weighted by another factor.
    /// It ships at 1, the plain sum.
    /// </summary>
    [Test]
    public async Task PainSpreadFalloffTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.PainSpreadFalloff, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainLooseRecovery, 0f);
        var map = await CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var pain = SEntMan.System<PainSystem>();
            pain.SetPain(Part(body, BodyPartType.Leg), FixedPoint2.New(20));
            pain.SetPain(Part(body, BodyPartType.Torso), FixedPoint2.New(60));
            pain.SetPain(Part(body, BodyPartType.Arm), FixedPoint2.New(40));

            // 60 + 40 x 0.5 + 20 x 0.25 = 85.
            Assert.That(pain.GetRawPain(body).Float(), Is.EqualTo(85f).Within(0.011f));
        });
    }
}
