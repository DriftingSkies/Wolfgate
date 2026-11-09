#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Shared._Onyx.Body.Systems;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-09 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1109Test : WolfmedGameTest
{
    /// <summary>Report 704: a spall liner keeps rounds and fragments out of a chassis as it does out of flesh.</summary>
    [Test]
    public async Task SpallLinerStopsChassisEmbedsTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var rules = SEntMan.System<WolfmedWoundRuleSystem>();
            var bare = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var lined = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var suit = SEntMan.SpawnEntity("ClothingOuterHardsuitAshen", map.GridCoords);
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(lined, suit, "outerClothing", silent: true, force: true), Is.True);

            rules.ForcedRoll = 0f;
            try
            {
                foreach (var body in new[] { bare, lined })
                {
                    var bullet = SEntMan.SpawnEntity("Bullet9x19mmFMJ", map.GridCoords);
                    var hit = new DamageSpecifier();
                    hit.DamageDict["Piercing"] = FixedPoint2.New(30);
                    SEntMan.System<DamageableSystem>().TryChangeDamage(body, hit, origin: bare, tool: bullet,
                        targetPart: TargetBodyPart.Torso);
                }
            }
            finally
            {
                rules.ForcedRoll = null;
            }

            Assert.Multiple(() =>
            {
                Assert.That(Embedded(bare), Is.GreaterThan(0), "a bare chassis kept nothing, so the test proves nothing.");
                Assert.That(Embedded(lined), Is.Zero, "the spall liner let a round lodge in the chassis.");
            });
        });
    }

    /// <summary>
    /// Report 701: a chassis's eyes and pump are repaired with tools, and the flesh organ surgeries don't list on it.
    /// </summary>
    [TestCase("MobIPC", "eyes", BodyPartType.Head, "WFSurgeryRepairChassisEyes", "WFSurgeryHealEyes")]
    [TestCase("MobSynth", "eyes", BodyPartType.Head, "WFSurgeryRepairChassisEyes", "WFSurgeryHealEyes")]
    [TestCase("MobIPC", "pump", BodyPartType.Torso, "WFSurgeryRepairChassisPump", null)]
    [TestCase("MobSynth", "heart", BodyPartType.Torso, "WFSurgeryRepairSynthPump", "WFSurgeryHealHeart")]
    [TestCase("MobHuman", "eyes", BodyPartType.Head, "WFSurgeryHealEyes", "WFSurgeryRepairChassisEyes")]
    public async Task ChassisOrganRepairTest(string mob, string slot, BodyPartType partType, string listed, string? hidden)
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity(mob, map.GridCoords);
            var bodies = SEntMan.System<SharedBodySystem>();
            var organ = bodies.GetBodyOrgans(body).First(o => o.Component.SlotId == slot).Id;
            var health = SEntMan.GetComponent<WolfmedOrganComponent>(organ);
            SEntMan.System<OrganHealthSystem>().ChangeHealth((organ, health), -health.MaxHealth / 2);
            var part = bodies.GetBodyChildrenOfType(body, partType).First().Id;
            var surgery = SEntMan.System<SurgerySystem>();

            Assert.Multiple(() =>
            {
                Assert.That(surgery.WolfmedSurgeryValid(body, part, listed), Is.True, $"{listed} does not list on a damaged {slot}.");
                if (hidden != null)
                    Assert.That(surgery.WolfmedSurgeryValid(body, part, hidden), Is.False, $"{hidden} lists on {mob}.");
            });

            var step = listed.Replace("WFSurgery", "WFSurgeryStep");
            var singleton = surgery.GetSingleton(step);
            Assert.That(singleton, Is.Not.Null, $"{step} has no singleton.");
            var user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            for (var i = 0; i < 40 && health.Health < health.MaxHealth; i++)
            {
                var ev = new SurgeryStepEvent(user, body, part, new List<EntityUid>(), singleton!.Value);
                SEntMan.EventBus.RaiseLocalEvent(singleton.Value, ref ev);
            }

            Assert.That(health.Health, Is.EqualTo(health.MaxHealth), $"{step} did not bring the {slot} back to full.");
        });
    }

    private int Embedded(EntityUid body)
    {
        var embedded = SEntMan.System<WolfmedEmbeddedObjectSystem>();
        return SEntMan.System<SharedBodySystem>().GetBodyChildren(body).Sum(p => embedded.GetPartCount(p.Id));
    }
}
