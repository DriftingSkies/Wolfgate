#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Atmos.Components;
using Content.Server.Temperature.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Surgery;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Standing;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// A necrotic torso or head cannot be amputated, so Remove Necrotic Flesh cuts the dead tissue out and grafts over it.
/// Driven the way a surgeon drives it. A dead limb is still an amputation and does not list the surgery.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedNecrosisSystem))]
public sealed class WolfmedNecrosisSurgeryTest : WolfmedGameTest
{
    private const string Surgery = "WFSurgeryRemoveNecroticFlesh";
    private const string DeadTissue = "WFWolfmedNecrosisWound";

    private static readonly (string Surgery, string Step, string Tool)[] OpenIncision =
    {
        ("SurgeryOpenIncision", "SurgeryStepOpenIncisionScalpel", "Scalpel"),
        ("SurgeryOpenIncision", "SurgeryStepRetractSkin", "Retractor"),
        ("SurgeryOpenIncision", "SurgeryStepClampBleeders", "Hemostat"),
    };

    [Test]
    [TestCase(BodyPartType.Torso)]
    [TestCase(BodyPartType.Head)]
    public async Task DeadTissueIsCutOutTest(BodyPartType type)
    {
        var map = await CreateTestMap();
        EntityUid body = default, part = default, surgeon = default;

        await Server.WaitAssertion(() =>
        {
            (body, surgeon) = Setup(map.GridCoords);
            part = Part(body, type);
            Assert.That(Valid(body, part), Is.False, "a healthy part offers the surgery.");
            Assert.That(SEntMan.System<WolfmedNecrosisSystem>().MakeNecrotic(part), Is.Not.Null, "the part did not die.");
            Assert.That(Valid(body, part), Is.True, "a necrotic part does not offer the surgery.");
        });

        foreach (var (surgery, step, tool) in OpenIncision)
            await Perform(body, part, surgeon, surgery, step, tool);
        await Perform(body, part, surgeon, Surgery, "WFSurgeryStepRemoveNecroticFlesh", "Scalpel");

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<WolfmedNecrosisSystem>().IsNecrotic(part), Is.True, "the cut alone cured the part, with no graft."));

        await Perform(body, part, surgeon, Surgery, "WFSurgeryStepGraftNecroticFlesh", "WFWolfmedSkinGraft");

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<WolfmedNecrosisSystem>().IsNecrotic(part), Is.False, "the part is still necrotic.");
                Assert.That(Wound(part, DeadTissue), Is.False, "the necrosis wound is still there.");
                Assert.That(SEntMan.HasComponent<WolfmedNecrosisComponent>(part), Is.False, "the part is still on a necrosis clock.");
                Assert.That(Valid(body, part), Is.True, "the surgery cannot be sealed once the dead tissue is out.");
            });
        });

        await Perform(body, part, surgeon, Surgery, "SurgeryStepSealTendWound", "Cautery");

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.HasComponent<IncisionOpenComponent>(part), Is.False, "the part is still open.");
                Assert.That(SEntMan.HasComponent<WolfmedNecrosisExcisedComponent>(part), Is.False, "the closed part still reads as cut and waiting for a graft.");
                Assert.That(Valid(body, part), Is.False, "the surgery is still offered after it was sealed.");
            });
        });
    }

    /// <summary>What killed the tissue and is still on the part puts it back on the clock.</summary>
    [Test]
    public async Task ARiskStillOnThePartRestartsTheClockTest()
    {
        var map = await CreateTestMap();
        EntityUid torso = default;

        await Server.WaitAssertion(() =>
        {
            var (body, _) = Setup(map.GridCoords);
            torso = Part(body, BodyPartType.Torso);
            var necrosis = SEntMan.System<WolfmedNecrosisSystem>();
            Assert.That(SEntMan.System<WoundSystem>().CreateOrMergeWound(torso, "WFWolfmedCharringWound", 100), Is.Not.Null);
            Assert.That(necrosis.IsAtRisk(torso), Is.True, "critical charring starts no clock.");
            Assert.That(necrosis.MakeNecrotic(torso), Is.Not.Null);

            Assert.That(necrosis.RemoveNecrosis(torso), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(necrosis.IsNecrotic(torso), Is.False);
                Assert.That(necrosis.IsAtRisk(torso), Is.True, "the charring left on the torso no longer threatens it.");
            });
        });
    }

    [Test]
    public async Task ADeadLimbIsStillAnAmputationTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var (body, _) = Setup(map.GridCoords);
            var arm = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, BodyPartType.Arm).First().Id;
            Assert.That(SEntMan.System<WolfmedNecrosisSystem>().MakeNecrotic(arm), Is.Not.Null, "the arm did not die.");
            Assert.That(Valid(body, arm), Is.False, "a dead limb offers the surgery instead of an amputation.");
        });
    }

    /// <summary>The pod plans it for itself on a dead torso, and plans nothing of the kind for a dead arm.</summary>
    [Test]
    [TestCase(BodyPartType.Torso, true)]
    [TestCase(BodyPartType.Arm, false)]
    public async Task PodPlansTheSurgeryTest(BodyPartType type, bool planned)
    {
        var map = await CreateTestMap();
        Entity<AutodocComponent> pod = default;

        await Server.WaitAssertion(() =>
        {
            var (body, _) = Setup(map.GridCoords);
            var part = SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type).First().Id;
            Assert.That(SEntMan.System<WolfmedNecrosisSystem>().MakeNecrotic(part), Is.Not.Null, "the part did not die.");

            var machine = SEntMan.SpawnEntity("WolfmedTestAutodoc", map.GridCoords);
            pod = (machine, SEntMan.GetComponent<AutodocComponent>(machine));
            Assert.That(SEntMan.System<AutodocSystem>().TryInsert(pod, body), Is.True);
        });

        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<AutodocSystem>().TryPlan(pod);
            Assert.That(pod.Comp!.Queue.Any(queued => queued.Surgery.Id == Surgery), Is.EqualTo(planned));
        });
    }

    private (EntityUid Body, EntityUid Surgeon) Setup(EntityCoordinates coordinates)
    {
        var body = SEntMan.SpawnEntity("MobVulpkanin", coordinates);
        var surgeon = SEntMan.SpawnEntity("MobHuman", coordinates);
        // The test map is vacuum; the patient and the surgeon are not what is being tested.
        foreach (var mob in new[] { body, surgeon })
        {
            SEntMan.RemoveComponent<BarotraumaComponent>(mob);
            SEntMan.RemoveComponent<TemperatureComponent>(mob);
        }

        SEntMan.System<StandingStateSystem>().Down(body);
        return (body, surgeon);
    }

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildrenOfType(body, type).Single().Id;

    /// <summary>One step as the surgery window sends it, with the tool in hand, then the do-after run out.</summary>
    private async Task Perform(EntityUid body, EntityUid part, EntityUid surgeon, string surgery, string step, string tool)
    {
        await Server.WaitAssertion(() =>
        {
            var hands = SEntMan.System<SharedHandsSystem>();
            foreach (var held in hands.EnumerateHeld(surgeon).ToList())
                SEntMan.DeleteEntity(held);
            var item = SEntMan.SpawnEntity(tool, SEntMan.GetComponent<TransformComponent>(surgeon).Coordinates);
            Assert.That(hands.TryPickupAnyHand(surgeon, item, checkActionBlocker: false), Is.True, $"the surgeon cannot hold the {tool}.");

            var chosen = new SurgeryStepChosenBuiMsg(SEntMan.GetNetEntity(part), surgery, step, false)
            {
                UiKey = SurgeryUIKey.Key,
                Actor = surgeon,
                Entity = SEntMan.GetNetEntity(body),
            };
            SEntMan.EventBus.RaiseLocalEvent(body, chosen);
        });

        await RunSeconds(8);

        await Server.WaitAssertion(() =>
        {
            var surgerySystem = SEntMan.System<SurgerySystem>();
            Assert.That(surgerySystem.IsStepComplete(body, part, step, surgerySystem.GetSingleton(surgery)!.Value), Is.True,
                $"{step} of {surgery} did not complete when chosen from the surgery window.");
        });
    }

    private bool Valid(EntityUid body, EntityUid part) =>
        SEntMan.System<SurgerySystem>().WolfmedSurgeryValid(body, part, Surgery);

    private bool Wound(EntityUid part, string prototype) =>
        SEntMan.System<WoundSystem>().GetWounds(part).Any(wound => wound.Comp.Prototype == prototype);
}
