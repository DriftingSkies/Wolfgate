#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Tests._WG.Wolfmed.Scenarios;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server.Body.Systems;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WG.Wolfmed.Autodoc;
using Content.Shared._WG.Wolfmed.Life;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Wolfmed;

/// <summary>
/// "People can't figure out how to heal slime brains." There was no way: a slime's brain is the core in its torso, and
/// both brain surgeries list on a head with a brain in it. The same two list on a slime's torso now, the pod's neuro
/// disk carries them, and mannitol reaches the core like any other brain.
/// </summary>
[TestFixture]
public sealed class WolfmedSlimeCoreTest : WolfmedGameTest
{
    private static readonly ProtoId<AutodocProgramPrototype> WGWolfmedAutodocProgramNeuroProto = "WGWolfmedAutodocProgramNeuro";
    private static readonly ProtoId<AutodocCategoryPrototype> WGWolfmedAutodocCategoryNeuroProto = "WGWolfmedAutodocCategoryNeuro";

    private const string Heal = "WGSurgeryHealSlimeCore";
    private const string Repair = "WGSurgeryRepairSlimeCore";

    private EntityUid Part(EntityUid body, BodyPartType type) =>
        SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(part => part.Component.PartType == type).Id;

    /// <summary>Runs one surgery step on the part the way the surgery system does once its do-after is over.</summary>
    private void Step(EntityUid body, EntityUid part, string step)
    {
        var surgery = SEntMan.System<SurgerySystem>();
        var singleton = surgery.GetSingleton(step);
        Assert.That(singleton, Is.Not.Null, $"{step} has no singleton.");
        var ev = new SurgeryStepEvent(body, body, part, new List<EntityUid>(), singleton!.Value);
        SEntMan.EventBus.RaiseLocalEvent(singleton.Value, ref ev);
    }

    [Test]
    public async Task SlimeCoreIsOperableTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var organs = SEntMan.System<OrganHealthSystem>();
        var surgery = SEntMan.System<SurgerySystem>();
        EntityUid slime = default, human = default;

        await Server.WaitPost(() =>
        {
            slime = SEntMan.SpawnEntity("MobSlimePerson", map.GridCoords);
            human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var torso = Part(slime, BodyPartType.Torso);
            var core = s.Life.GetBrainOrgan(slime)!.Value;
            Assert.That(surgery.WolfmedSurgeryValid(slime, torso, Heal), Is.False, "a whole core lists for healing.");

            organs.SetHealth(core, FixedPoint2.New(6));
            organs.SetHealth(s.Life.GetBrainOrgan(human)!.Value, FixedPoint2.New(6));
            Assert.Multiple(() =>
            {
                Assert.That(surgery.WolfmedSurgeryValid(slime, torso, Heal), Is.True, "no core healing on a slime's torso.");
                Assert.That(surgery.WolfmedSurgeryValid(slime, torso, Repair), Is.True, "no core repair on a slime's torso.");
                // Why nobody could find it: nothing lists on the head, which is where a brain is looked for.
                Assert.That(surgery.WolfmedSurgeryValid(slime, Part(slime, BodyPartType.Head), "WGSurgeryHealBrain"), Is.False);
                Assert.That(surgery.WolfmedSurgeryValid(human, Part(human, BodyPartType.Torso), Heal), Is.False,
                    "core healing is offered on a human's chest.");
                Assert.That(surgery.WolfmedSurgeryValid(human, Part(human, BodyPartType.Head), "WGSurgeryHealBrain"), Is.True);
            });

            Step(slime, torso, "WGSurgeryStepHealSlimeCore");
            Assert.That(core.Comp.Health, Is.EqualTo(FixedPoint2.New(9)), "the heal step did not reach the core.");
        });

        // A destroyed core is a dead slime; the repair puts the whole core back and leaves the trauma, as on a brain.
        await Server.WaitPost(() => organs.SetHealth(s.Life.GetBrainOrgan(slime)!.Value, FixedPoint2.Zero));
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            var torso = Part(slime, BodyPartType.Torso);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(slime), Is.True, "a destroyed core did not kill the slime.");
            Assert.That(surgery.WolfmedSurgeryValid(slime, torso, Repair), Is.True, "no repair for a destroyed core.");

            Step(slime, torso, "WGSurgeryStepRepairSlimeCore");
            var core = s.Life.GetBrainOrgan(slime)!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(core.Comp.Health, Is.EqualTo(core.Comp.MaxHealth), "the core was not repaired.");
                Assert.That(SEntMan.HasComponent<WolfmedBrainTraumaComponent>(slime), Is.True,
                    "a repaired core carries no trauma.");
            });
        });
    }

    /// <summary>The pod's neuro disk, its category and its triage plan all carry the two core surgeries.</summary>
    [Test]
    public async Task PodKnowsTheSlimeCoreTest()
    {
        await Server.WaitAssertion(() =>
        {
            var program = SProtoMan.Index<AutodocProgramPrototype>(WGWolfmedAutodocProgramNeuroProto);
            var category = SProtoMan.Index<AutodocCategoryPrototype>(WGWolfmedAutodocCategoryNeuroProto);
            var planned = SProtoMan.EnumeratePrototypes<AutodocTriagePrototype>()
                .SelectMany(triage => triage.Steps)
                .SelectMany(step => step.Surgeries)
                .Select(id => id.Id)
                .ToHashSet();

            foreach (var id in new[] { Heal, Repair })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(program.Surgeries.Any(entry => entry.Id == id), Is.True, $"the neuro disk lacks {id}.");
                    Assert.That(category.Surgeries.Any(entry => entry.Id == id), Is.True, $"the neuro category lacks {id}.");
                    Assert.That(planned, Does.Contain(id), $"triage never plans {id}.");
                });
            }
        });
    }

    /// <summary>Mannitol reaches a slime's core: the core is what metabolises it.</summary>
    [Test]
    public async Task MannitolReachesTheCoreTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid slime = default;

        await Server.WaitPost(() => slime = SEntMan.SpawnEntity("MobSlimePerson", map.GridCoords));
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            SEntMan.System<OrganHealthSystem>().SetHealth(s.Life.GetBrainOrgan(slime)!.Value, FixedPoint2.New(6));
            Assert.That(SEntMan.System<BloodstreamSystem>()
                .TryAddToChemicals(slime, new Solution("Mannitol", FixedPoint2.New(10))), Is.True);
        });
        await RunSeconds(40);

        await Server.WaitAssertion(() =>
            Assert.That(s.Life.GetBrainOrgan(slime)!.Value.Comp.Health, Is.GreaterThan(FixedPoint2.New(8)),
                "a 10u dose of mannitol did not restore the slime's core."));
    }
}
