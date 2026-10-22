#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>A medical borg carries every Wolfmed instrument, and its single-use field items survive use.</summary>
[TestFixture]
public sealed class WolfmedBorgInstrumentsTest : WolfmedGameTest
{
    /// <summary>Every tool a Wolfmed surgery step asks for is held by some item of the medical borg's default modules.</summary>
    [Test]
    public async Task MedicalBorgHoldsEveryWolfmedSurgeryToolTest()
    {
        var prototypes = Server.ResolveDependency<IPrototypeManager>();
        var factory = Server.ResolveDependency<IComponentFactory>();

        await Server.WaitAssertion(() =>
        {
            var items = new List<EntityPrototype>();
            foreach (var module in prototypes.Index<BorgTypePrototype>("medical").DefaultModules)
            {
                if (prototypes.Index(module).TryGetComponent<ItemBorgModuleComponent>(out var provider, factory))
                    items.AddRange(provider.Items.Select(id => prototypes.Index<EntityPrototype>(id)));
            }

            var needed = new SortedSet<string>();
            foreach (var step in prototypes.EnumeratePrototypes<EntityPrototype>())
            {
                if (step.Abstract || !step.ID.StartsWith("WFSurgeryStep") ||
                    !step.TryGetComponent<SurgeryStepComponent>(out var comp, factory) || comp.Tool == null)
                    continue;

                needed.UnionWith(comp.Tool.Keys);
            }

            Assert.That(needed, Is.Not.Empty);
            var missing = needed.Where(tool => !items.Any(item => item.Components.ContainsKey(tool))).ToArray();
            Assert.That(missing, Is.Empty, "no default medical borg module holds these surgery tools.");
        });
    }

    /// <summary>The borg tourniquet stops a bleeding leg and is still in hand afterwards.</summary>
    [Test]
    public async Task BorgTourniquetIsNotUsedUpTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid patient = default, medic = default, strap = default, leg = default;
        var bleeding = SEntMan.System<WoundBleedingSystem>();

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            leg = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Left);
            SEntMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Slash", 40), medic);
            Assert.That(bleeding.GetPartRate(leg), Is.GreaterThan(0f));

            strap = SEntMan.SpawnEntity("WFWolfmedTourniquetBorg", map.GridCoords);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(medic, strap), Is.True);
            SEntMan.GetComponent<TargetingComponent>(medic).Target = TargetBodyPart.LeftLeg;
            var click = new AfterInteractEvent(medic, strap, patient, map.GridCoords, canReach: true);
            SEntMan.EventBus.RaiseLocalEvent(strap, click);
            Assert.That(click.Handled, Is.True);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(bleeding.GetPartRate(leg), Is.EqualTo(0f), "the borg tourniquet did not stop the leg.");
                Assert.That(SEntMan.Deleted(strap) || SEntMan.IsQueuedForDeletion(strap), Is.False,
                    "the borg tourniquet was used up.");
            });
        });
    }

    /// <summary>The borg splint sets a broken leg and is still in hand afterwards.</summary>
    [Test]
    public async Task BorgSplintIsNotUsedUpTest()
    {
        var map = await CreateTestMap();
        var s = new WolfmedScenario(SEntMan);

        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var leg = s.Part(patient, BodyPartType.Leg, BodyPartSymmetry.Left);
            SEntMan.System<WoundDamageRoutingSystem>().TryApplyPartDamage(patient, leg, WolfmedScenario.Spec("Blunt", 60), patient);
            Assert.That(SEntMan.System<WoundFractureSystem>().GetFracture(leg), Is.Not.Null);

            var splint = SEntMan.SpawnEntity("WFWolfmedSplintBorg", map.GridCoords);
            var comp = SEntMan.GetComponent<WolfmedSplintComponent>(splint);
            Assert.That(SEntMan.System<WolfmedSplintSystem>().TryApply((splint, comp), patient, leg, patient), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.System<WoundFractureSystem>().GetFracture(leg)!.Value.Comp2.Treatment,
                    Is.EqualTo(FractureTreatment.Reduced));
                Assert.That(SEntMan.Deleted(splint) || SEntMan.IsQueuedForDeletion(splint), Is.False,
                    "the borg splint was used up.");
            });
        });
    }
}
