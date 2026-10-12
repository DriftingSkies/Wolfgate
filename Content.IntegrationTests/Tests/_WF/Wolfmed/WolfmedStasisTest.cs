#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Stasis;
using Content.Server.Atmos.Components;
using Content.Server.Body.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Starlight.Actions.Stasis;
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
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Avali stasis on a wound host: it holds every bleed while it lasts, a hit in it keeps only the factor's share before
/// it becomes a wound, it closes wounds at topical strength and thins the parts' stored damage, and it never touches a
/// fracture (owner's rule). The stock system did none of this on a wound host: its bleed stop was refused, its healing
/// took 15% off a wound, and its "resistance" healed back a total nothing reads. The left cut carries a lodged round,
/// which refuses every closing, so its bleed is stopped by the hold alone and returns when stasis ends. As shipped the
/// factor is 1 and stasis ends by itself (wolfmed.stasis_max_seconds).
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedStasisSystem))]
public sealed class WolfmedStasisTest : WolfmedGameTest
{
    [Test]
    public async Task StasisHoldsBleedsClosesWoundsScalesHitsAndLeavesBonesTest()
    {
        // The factor ships at 1; half pins the knob.
        await OverrideCVar(Side.Server, WolfmedCVars.StasisDamageFactor, 0.5f);
        var map = await CreateTestMap();
        EntityUid body = default, leftArm = default, rightArm = default, fracture = default;
        FixedPoint2 controlCut = default, leftCut = default, rightCut = default, fractureSeverity = default;
        FractureGrade grade = default;
        var armSlashBefore = 0f;

        await Server.WaitAssertion(() =>
        {
            body = Avali(map.GridCoords);
            var control = Avali(map.GridCoords);
            var routing = SEntMan.System<WoundDamageRoutingSystem>();
            var fractures = SEntMan.System<WoundFractureSystem>();
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            var blunt = SProtoMan.Index<DamageTypePrototype>("Blunt");
            leftArm = Arm(body, BodyPartSymmetry.Left);
            rightArm = Arm(body, BodyPartSymmetry.Right);

            // A bleeding cut in the left arm, and a hairline fracture: two 10 Blunt hits on the certain profile.
            Assert.That(routing.TryApplyPartDamage(body, leftArm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.That(Bleed(body), Is.GreaterThan(0f), "the cut does not bleed.");
            foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(leftArm).ToArray())
                Assert.That(SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(wound.Owner, "WFWolfmedSpentRound", 1, 1), Is.EqualTo(1));
            leftCut = SlashSeverity(leftArm);
            SEntMan.GetComponent<WolfmedBodyPartComponent>(leftArm).FractureProfile = "WolfmedTestFractureProfileCertain";
            for (var hit = 0; hit < 2; hit++)
                Assert.That(routing.TryApplyPartDamage(body, leftArm, new DamageSpecifier(blunt, 10), ignoreResistances: true), Is.True);
            var broken = fractures.GetFracture(leftArm);
            Assert.That(broken, Is.Not.Null, "no fracture to leave alone.");
            fracture = broken!.Value.Owner;
            grade = broken.Value.Comp2.Grade;
            fractureSeverity = broken.Value.Comp1.Severity;

            // The control takes the right-arm cut out of stasis.
            var controlArm = Arm(control, BodyPartSymmetry.Right);
            Assert.That(routing.TryApplyPartDamage(control, controlArm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            controlCut = SlashSeverity(controlArm);
            Assert.That(controlCut, Is.GreaterThan(FixedPoint2.Zero), "the control has no cut.");

            SEntMan.EventBus.RaiseLocalEvent(body, new EnterStasisActionEvent());
            Assert.That(SEntMan.GetComponent<StasisComponent>(body).IsInStasis, Is.True, "the Avali did not enter stasis.");
        });

        // The hold lands on the next update.
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Bleed(body), Is.Zero, "stasis did not hold the bleed.");

            // The same hit in stasis makes the factor's share of the control's wound.
            var factor = Server.CfgMan.GetCVar(WolfmedCVars.StasisDamageFactor);
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, rightArm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.That(SlashSeverity(rightArm).Float(), Is.EqualTo(controlCut.Float() * factor).Within(0.6f),
                "a hit in stasis was not cut down before it became a wound.");

            rightCut = SlashSeverity(rightArm);
            armSlashBefore = StoredSlash(leftArm);
        });

        await RunSeconds(5);

        await Server.WaitAssertion(() =>
        {
            var broken = SEntMan.System<WoundFractureSystem>().GetFracture(leftArm);
            Assert.Multiple(() =>
            {
                // Half a point a second off the cut; the cut with the round in it is refused.
                Assert.That(SlashSeverity(rightArm), Is.LessThan(rightCut), "stasis did not close the cut at topical strength.");
                Assert.That(SlashSeverity(leftArm), Is.EqualTo(leftCut), "stasis closed a cut with a round still in it.");
                Assert.That(StoredSlash(leftArm), Is.LessThan(armSlashBefore), "stasis left the arm's stored damage alone.");
                Assert.That(Bleed(body), Is.Zero, "the hold slipped.");
                Assert.That(broken, Is.Not.Null, "stasis set the bone.");
                Assert.That(broken?.Owner, Is.EqualTo(fracture), "stasis replaced the fracture.");
                Assert.That(broken?.Comp2.Grade, Is.EqualTo(grade), "stasis changed the fracture's grade.");
                Assert.That(broken?.Comp1.Severity, Is.EqualTo(fractureSeverity), "stasis changed the fracture's severity.");
                Assert.That(broken?.Comp2.Treatment, Is.EqualTo(FractureTreatment.None), "stasis treated the fracture.");
            });

            SEntMan.EventBus.RaiseLocalEvent(body, new ExitStasisActionEvent());
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
            Assert.That(Bleed(body), Is.GreaterThan(0f), "the bleed did not come back after stasis."));
    }

    /// <summary>
    /// As shipped a hit in stasis lands whole, and stasis ends by itself when its time is up. The held cut carries a
    /// lodged round: a closing stabilises a cut, which would stop its bleed for good.
    /// </summary>
    [Test]
    public async Task StasisTakesTheWholeHitAndEndsByItselfTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.StasisMaxSeconds, 3f);
        var map = await CreateTestMap();
        EntityUid body = default, arm = default;
        FixedPoint2 controlCut = default;

        await Server.WaitAssertion(() =>
        {
            body = Avali(map.GridCoords);
            var control = Avali(map.GridCoords);
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            var controlArm = Arm(control, BodyPartSymmetry.Right);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(control, controlArm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            controlCut = SlashSeverity(controlArm);

            var leftArm = Arm(body, BodyPartSymmetry.Left);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, leftArm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(leftArm).ToArray())
                Assert.That(SEntMan.System<WolfmedEmbeddedObjectSystem>().Add(wound.Owner, "WFWolfmedSpentRound", 1, 1), Is.EqualTo(1));
            Assert.That(Bleed(body), Is.GreaterThan(0f), "the cut does not bleed.");

            SEntMan.EventBus.RaiseLocalEvent(body, new EnterStasisActionEvent());
            Assert.That(SEntMan.GetComponent<StasisComponent>(body).IsInStasis, Is.True, "the Avali did not enter stasis.");
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var slash = SProtoMan.Index<DamageTypePrototype>("Slash");
            arm = Arm(body, BodyPartSymmetry.Right);
            Assert.That(SEntMan.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(body, arm, new DamageSpecifier(slash, 15), ignoreResistances: true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(SlashSeverity(arm).Float(), Is.EqualTo(controlCut.Float()).Within(0.6f), "stasis softened a hit.");
                Assert.That(SEntMan.HasComponent<WolfmedStasisHoldComponent>(body), Is.True, "the hold is not on.");
                Assert.That(Bleed(body), Is.Zero, "stasis did not hold the bleed.");
            });
        });

        await RunSeconds(3);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<StasisComponent>(body).IsInStasis, Is.False, "stasis outlasted its limit.");
                Assert.That(SEntMan.HasComponent<StasisFrozenComponent>(body), Is.False, "the Avali is still frozen.");
                Assert.That(SEntMan.HasComponent<WolfmedStasisHoldComponent>(body), Is.False, "the hold outlasted stasis.");
                Assert.That(Bleed(body), Is.GreaterThan(0f), "the bleed did not come back after stasis.");
            });
        });
    }

    private EntityUid Avali(EntityCoordinates coordinates)
    {
        var avali = SEntMan.SpawnEntity("MobAvali", coordinates);
        SEntMan.RemoveComponent<BarotraumaComponent>(avali);
        SEntMan.RemoveComponent<TemperatureComponent>(avali);
        return avali;
    }

    private EntityUid Arm(EntityUid body, BodyPartSymmetry side) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Arm, symmetry: side).Single().Id;

    private float Bleed(EntityUid body) => SEntMan.GetComponent<BloodstreamComponent>(body).BleedAmount;

    private float StoredSlash(EntityUid part) =>
        SEntMan.GetComponent<DamageableComponent>(part).Damage.DamageDict.GetValueOrDefault("Slash").Float();

    /// <summary>Severity of every wound on the part a Slash topical would close.</summary>
    private FixedPoint2 SlashSeverity(EntityUid part)
    {
        var total = FixedPoint2.Zero;
        var slash = new ProtoId<DamageTypePrototype>("Slash");
        foreach (var wound in SEntMan.System<WoundSystem>().GetWounds(part))
        {
            if (SProtoMan.TryIndex(wound.Comp.Prototype, out var prototype) && prototype.DamageTypes.ContainsKey(slash))
                total += wound.Comp.Severity;
        }

        return total;
    }
}
