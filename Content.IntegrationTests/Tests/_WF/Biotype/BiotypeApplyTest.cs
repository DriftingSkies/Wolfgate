#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Biotype;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Components;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Biotype;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Biotype;

/// <summary>
/// A live mob loaded with a profile of the other biotype is rebuilt as one: body, organs, fluid, components and the
/// Wolfmed ladder. Items in its hands stay held. A profile of the mob's own biotype changes nothing.
/// </summary>
[TestFixture]
[TestOf(typeof(WFBiotypeSystem))]
public sealed class BiotypeApplyTest : WolfmedGameTest
{
    private const string Fluid = "WFWolfmedHydraulicFluid";

    [Test]
    public async Task SyntheticHumanTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var held = HoldTwoItems(body, map.GridCoords);
            var oldRoot = SEntMan.GetComponent<BodyComponent>(body).RootContainer.ContainedEntity;
            var before = ComponentNames(body);

            Load(body, "Human", WFBiotype.Synthetic);

            var bodyComp = SEntMan.GetComponent<BodyComponent>(body);
            var graph = SEntMan.System<SharedBodySystem>();
            var parts = graph.GetBodyChildren(body).ToList();
            var torso = parts.Single(p => p.Component.PartType == BodyPartType.Torso).Id;
            var organs = graph.GetBodyOrgans(body).Select(o => Proto(o.Id)).ToList();

            TestContext.Out.WriteLine($"Synthetic human lost: {string.Join(", ", before.Except(ComponentNames(body)).Order())}");
            TestContext.Out.WriteLine($"Synthetic human gained: {string.Join(", ", ComponentNames(body).Except(before).Order())}");
            TestContext.Out.WriteLine($"MobIPC has, synthetic human lacks: {string.Join(", ", Fresh("MobIPC", map.GridCoords).Except(ComponentNames(body)).Order())}");

            Assert.Multiple(() =>
            {
                Assert.That(bodyComp.Prototype?.Id, Is.EqualTo("IPC"));
                Assert.That(oldRoot is { } root && SEntMan.Deleted(root), Is.True, "The old torso is still around.");
                Assert.That(parts, Has.Count.EqualTo(10));
                Assert.That(SEntMan.GetComponent<WoundableComponent>(torso).Profile.Id, Is.EqualTo("IpcBodyPartProfile"));
                Assert.That(organs, Does.Contain("PositronicBrain"));
                Assert.That(organs, Does.Contain("OrganIPCPump"));
                Assert.That(SEntMan.HasComponent<SiliconComponent>(body));
                Assert.That(SEntMan.HasComponent<RespiratorComponent>(body), Is.False);
                Assert.That(SEntMan.HasComponent<HungerComponent>(body), Is.False);
                Assert.That(SEntMan.HasComponent<ThirstComponent>(body), Is.False);
                AssertBlood(body, Fluid, 250, 0);
                Assert.That(SEntMan.System<ItemSlotsSystem>().TryGetSlot(body, "cell_slot", out var cell) && cell.HasItem,
                    "No power cell in the cell slot.");
                Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsMechanical(body), Is.True);
                Assert.That(SEntMan.System<WolfmedSyntheticHudSystem>().IsMechanicalBody(body), Is.True);
                AssertStamped(body, WFBiotype.Synthetic, "WFBiotypeSynthetic");
                AssertUnhurt(body);
                AssertStillHeld(body, held);
            });

            // The same profile again is a no-op.
            var rebuilt = bodyComp.RootContainer.ContainedEntity;
            var after = ComponentNames(body);
            Load(body, "Human", WFBiotype.Synthetic);
            Assert.That(SEntMan.GetComponent<BodyComponent>(body).RootContainer.ContainedEntity, Is.EqualTo(rebuilt));
            Assert.That(ComponentNames(body), Is.EquivalentTo(after));
        });
    }

    [Test]
    public async Task OrganicIpcTest()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var body = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            var held = HoldTwoItems(body, map.GridCoords);
            var before = ComponentNames(body);

            Load(body, "IPC", WFBiotype.Organic);

            var graph = SEntMan.System<SharedBodySystem>();
            var organs = graph.GetBodyOrgans(body).Select(o => o.Id).ToList();

            TestContext.Out.WriteLine($"Organic IPC lost: {string.Join(", ", before.Except(ComponentNames(body)).Order())}");
            TestContext.Out.WriteLine($"Organic IPC gained: {string.Join(", ", ComponentNames(body).Except(before).Order())}");
            TestContext.Out.WriteLine($"MobHuman has, organic IPC lacks: {string.Join(", ", Fresh("MobHuman", map.GridCoords).Except(ComponentNames(body)).Order())}");

            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<BodyComponent>(body).Prototype?.Id, Is.EqualTo("Human"));
                Assert.That(organs.Any(o => SEntMan.HasComponent<BrainComponent>(o) && SEntMan.HasComponent<WolfmedBrainComponent>(o)),
                    "No organic brain.");
                Assert.That(organs.Any(SEntMan.HasComponent<HeartComponent>), "No heart.");
                Assert.That(organs.Any(SEntMan.HasComponent<LungComponent>), "No lungs.");
                Assert.That(SEntMan.HasComponent<RespiratorComponent>(body));
                Assert.That(SEntMan.HasComponent<SiliconComponent>(body), Is.False);
                Assert.That(SEntMan.HasComponent<HungerComponent>(body));
                Assert.That(SEntMan.HasComponent<InjectableSolutionComponent>(body));
                Assert.That(SEntMan.System<ItemSlotsSystem>().TryGetSlot(body, "cell_slot", out _), Is.False,
                    "The cell slot is still there.");
                AssertBlood(body, "Blood", 300, 250);
                Assert.That(SEntMan.System<WolfmedShutdownSystem>().IsMechanical(body), Is.False);
                Assert.That(SEntMan.System<WolfmedSyntheticHudSystem>().IsMechanicalBody(body), Is.False);
                AssertStamped(body, WFBiotype.Organic, "WFBiotypeOrganic");
                AssertUnhurt(body);
                AssertStillHeld(body, held);
            });
        });
    }

    /// <summary>A mob given its own biotype keeps every component and its body; at most the marker is added.</summary>
    [Test]
    public async Task NativeUnchangedTest()
    {
        var map = await CreateTestMap();

        await Server.WaitAssertion(() =>
        {
            var biotypes = SEntMan.System<WFBiotypeSystem>();
            foreach (var (mob, species, biotype) in new[] { ("MobHuman", "Human", WFBiotype.Organic), ("MobIPC", "IPC", WFBiotype.Synthetic) })
            {
                foreach (var stored in new[] { biotype, WFBiotype.Default })
                {
                    var body = SEntMan.SpawnEntity(mob, map.GridCoords);
                    var root = SEntMan.GetComponent<BodyComponent>(body).RootContainer.ContainedEntity;
                    var before = ComponentNames(body);

                    biotypes.Apply(body, HumanoidCharacterProfile.RandomWithSpecies(species).WithBiotype(stored));

                    Assert.Multiple(() =>
                    {
                        var after = ComponentNames(body);
                        Assert.That(after, Is.EquivalentTo(before.Union(new[] { "WFBiotype" })),
                            $"{mob} {stored} lost {string.Join(", ", before.Except(after))}, gained {string.Join(", ", after.Except(before))}.");
                        Assert.That(SEntMan.GetComponent<BodyComponent>(body).RootContainer.ContainedEntity, Is.EqualTo(root));
                        AssertStamped(body, biotype, null);
                    });
                    SEntMan.DeleteEntity(body);
                }
            }
        });
    }

    private void Load(EntityUid body, string species, WFBiotype biotype)
    {
        SEntMan.System<SharedHumanoidAppearanceSystem>()
            .LoadProfile(body, HumanoidCharacterProfile.RandomWithSpecies(species).WithBiotype(biotype));
    }

    private HashSet<string> ComponentNames(EntityUid uid)
    {
        var factory = SEntMan.ComponentFactory;
        return SEntMan.GetComponents(uid).Select(c => factory.GetComponentName(c.GetType())).ToHashSet();
    }

    private HashSet<string> Fresh(EntProtoId prototype, Robust.Shared.Map.EntityCoordinates coords)
    {
        var mob = SEntMan.SpawnEntity(prototype, coords);
        var names = ComponentNames(mob);
        SEntMan.DeleteEntity(mob);
        return names;
    }

    private string? Proto(EntityUid uid) => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

    private Dictionary<string, EntityUid> HoldTwoItems(EntityUid body, Robust.Shared.Map.EntityCoordinates coords)
    {
        var hands = SEntMan.System<SharedHandsSystem>();
        var held = new Dictionary<string, EntityUid>();
        foreach (var hand in SEntMan.GetComponent<HandsComponent>(body).Hands.Keys.ToList())
        {
            var item = SEntMan.SpawnEntity("Crowbar", coords);
            Assert.That(hands.TryPickup(body, item, hand, checkActionBlocker: false), $"Could not put a crowbar in {hand}.");
            held[hand] = item;
        }

        Assert.That(held, Has.Count.EqualTo(2));
        return held;
    }

    private void AssertStillHeld(EntityUid body, Dictionary<string, EntityUid> held)
    {
        var hands = SEntMan.GetComponent<HandsComponent>(body).Hands;
        foreach (var (hand, item) in held)
        {
            Assert.That(SEntMan.Deleted(item), Is.False, $"The item in {hand} was deleted.");
            Assert.That(hands.TryGetValue(hand, out var now) && now.HeldEntity == item, $"{hand} let go of its item.");
        }
    }

    private void AssertBlood(EntityUid body, string reagent, int volume, int chemicals)
    {
        var blood = SEntMan.GetComponent<BloodstreamComponent>(body);
        var solutions = SEntMan.System<SharedSolutionContainerSystem>();
        Assert.That(blood.BloodReagent.Id, Is.EqualTo(reagent));
        Assert.That(solutions.TryGetSolution(body, blood.BloodSolutionName, out _, out var bloodSolution), "No blood solution.");
        Assert.That(bloodSolution!.Volume.Int(), Is.EqualTo(volume), "Blood volume.");
        Assert.That(bloodSolution.Contents.Select(r => r.Reagent.Prototype), Is.EquivalentTo(new[] { reagent }));
        Assert.That(solutions.TryGetSolution(body, blood.ChemicalSolutionName, out _, out var chemicalSolution), "No chemicals.");
        Assert.That(chemicalSolution!.MaxVolume.Int(), Is.EqualTo(chemicals), "Chemical capacity.");
    }

    private void AssertStamped(EntityUid body, WFBiotype biotype, string? applied)
    {
        var marker = SEntMan.GetComponent<WFBiotypeComponent>(body);
        Assert.That(marker.Biotype, Is.EqualTo(biotype));
        Assert.That(marker.Applied?.Id, Is.EqualTo(applied));
        Assert.That(SEntMan.GetComponent<HumanoidAppearanceComponent>(body).Biotype, Is.EqualTo(biotype));
    }

    private void AssertUnhurt(EntityUid body)
    {
        Assert.That(SEntMan.System<MobStateSystem>().IsAlive(body), "The conversion hurt the mob.");
        Assert.That(SEntMan.HasComponent<DebrainedComponent>(body), Is.False, "The conversion took the brain out.");
        Assert.That(SEntMan.HasComponent<WolfmedCardiacArrestComponent>(body), Is.False, "The conversion stopped the heart.");
        Assert.That(SEntMan.GetComponent<Content.Shared.Damage.DamageableComponent>(body).TotalDamage.Int(), Is.Zero);
        Assert.That(SEntMan.GetComponent<MobThresholdsComponent>(body).TriggersAlerts, Is.False,
            "Wolfmed no longer owns the health alerts.");
    }
}
