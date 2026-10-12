#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Verbs;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Monolith's edible limbs made every arm, leg, hand and foot food. A severed limb has to last until it is sewn back
/// on, so only the Eat verb eats one: not a use in hand, not a click on the patient, not a hungry animal. An IPC or
/// synth limb grinds and tastes like metal, as a cybernetic one does.
/// </summary>
[TestFixture]
[TestOf(typeof(FoodSystem))]
public sealed class WolfmedEdibleLimbTest : WolfmedGameTest
{
    private static readonly string[] MechanicalLimbs =
    {
        "LeftArmIPC", "RightArmIPC", "LeftHandIPC", "RightHandIPC",
        "LeftLegIPC", "RightLegIPC", "LeftFootIPC", "RightFootIPC",
        "LeftArmSynth", "RightArmSynth", "LeftHandSynth", "RightHandSynth",
        "LeftLegSynth", "RightLegSynth", "LeftFootSynth", "RightFootSynth",
    };

    /// <summary>Use in hand and a click on the patient leave the arm alone; the Eat verb still starts eating it.</summary>
    [Test]
    public async Task SeveredArmIsOnlyEatenThroughTheVerbTest()
    {
        var map = await CreateTestMap();
        EntityUid user = default, patient = default, arm = default;

        await Server.WaitAssertion(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arm = SEntMan.SpawnEntity("LeftArmHuman", map.GridCoords);
            Assert.That(SEntMan.HasComponent<FoodComponent>(arm), Is.True, "the arm is not food; this test guards nothing.");
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(user, arm), Is.True);

            var use = new UseInHandEvent(user);
            SEntMan.EventBus.RaiseLocalEvent(arm, use);
            Assert.Multiple(() =>
            {
                Assert.That(use.Handled, Is.False, "using the arm in hand was taken as eating it.");
                Assert.That(ActiveDoAfters(user), Is.Zero, "using the arm in hand started a do-after.");
            });

            var click = new AfterInteractEvent(user, arm, patient, map.GridCoords, canReach: true);
            SEntMan.EventBus.RaiseLocalEvent(arm, click);
            Assert.Multiple(() =>
            {
                Assert.That(click.Handled, Is.False, "clicking the arm on the patient was taken as feeding it to them.");
                Assert.That(ActiveDoAfters(user), Is.Zero, "clicking the arm on the patient started a do-after.");
            });
        });

        // Longer than the eat delay and the force-feed delay.
        await RunSeconds(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(arm), Is.False, "the arm was eaten.");
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(arm, "food", out _, out var food), Is.True);
            Assert.That(food!.Volume, Is.EqualTo(FixedPoint2.New(5)), "a bite was taken out of the arm.");

            var verbs = SEntMan.System<SharedVerbSystem>();
            var verbEat = SEntMan.GetComponent<FoodComponent>(arm).VerbEat;
            var text = Loc.GetString(verbEat);
            var eat = verbs.GetLocalVerbs(arm, user, typeof(AlternativeVerb)).FirstOrDefault(verb => verb.Text == text);
            Assert.That(eat, Is.Not.Null, "the Eat verb is gone from the arm.");
            verbs.ExecuteVerb(eat!, user, arm);
            Assert.That(ActiveDoAfters(user), Is.EqualTo(1), "the Eat verb did not start eating the arm.");
        });
    }

    /// <summary>An IPC or synth limb carries the cybernetic food data, not the organic data its slot base brings.</summary>
    [Test]
    public async Task MechanicalLimbsShareTheCyberneticProfileTest()
    {
        var factory = Server.ResolveDependency<IComponentFactory>();

        await Server.WaitAssertion(() =>
        {
            var cybernetic = Profile("LeftHandCybernetic", factory);
            Assert.That(cybernetic, Is.Not.EqualTo(Profile("LeftHandHuman", factory)),
                "cybernetic and organic limbs share a profile; this test tells nothing apart.");

            Assert.Multiple(() =>
            {
                foreach (var limb in MechanicalLimbs)
                {
                    Assert.That(Profile(limb, factory), Is.EqualTo(cybernetic),
                        $"{limb} does not grind and taste like a cybernetic limb.");
                }
            });
        });
    }

    /// <summary>The query a hungry animal picks its food with finds the donut and skips the arm lying beside it.</summary>
    [Test]
    public async Task HungryAnimalsDoNotPickLimbsTest()
    {
        var map = await CreateTestMap();
        EntityUid eater = default, arm = default, donut = default;

        await Server.WaitPost(() =>
        {
            eater = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arm = SEntMan.SpawnEntity("LeftArmHuman", map.GridCoords);
            donut = SEntMan.SpawnEntity("FoodDonutPlain", map.GridCoords);
        });

        await RunTicksSync(5);

        await Server.WaitAssertion(() =>
        {
            var blackboard = new NPCBlackboard();
            blackboard.SetValue(NPCBlackboard.Owner, eater);
            var found = SEntMan.System<NPCUtilitySystem>().GetEntities(blackboard, "NearbyFood", bestOnly: false).Entities;

            Assert.Multiple(() =>
            {
                Assert.That(found.ContainsKey(donut), Is.True, "the query does not find ordinary food; this test guards nothing.");
                Assert.That(found.ContainsKey(arm), Is.False, "a hungry animal would walk up and eat the severed arm.");
            });
        });
    }

    /// <summary>The do-afters the user has running.</summary>
    private int ActiveDoAfters(EntityUid user)
    {
        if (!SEntMan.TryGetComponent(user, out DoAfterComponent? comp))
            return 0;

        var doAfters = comp.DoAfters;
        return doAfters.Values.Count(doAfter => !doAfter.Cancelled && !doAfter.Completed);
    }

    /// <summary>What a limb prototype grinds into, feeds and tastes of.</summary>
    private string Profile(string id, IComponentFactory factory)
    {
        var proto = SProtoMan.Index<EntityPrototype>(id);
        Assert.That(proto.TryGetComponent(out SolutionContainerManagerComponent? manager, factory), Is.True, $"{id} has no solutions.");
        Assert.That(proto.TryGetComponent(out FlavorProfileComponent? flavor, factory), Is.True, $"{id} has no flavour.");

        var solutions = manager!.Solutions;
        Assert.That(solutions, Is.Not.Null, $"{id} declares no solutions.");

        var parts = new List<string>();
        foreach (var name in new[] { "limb", "food" })
        {
            Assert.That(solutions!.TryGetValue(name, out var solution), Is.True, $"{id} has no {name} solution.");
            parts.Add($"{name}: " + string.Join(", ", solution!.Contents.Select(reagent => $"{reagent.Reagent.Prototype} {reagent.Quantity}")));
        }

        parts.Add("flavors: " + string.Join(", ", flavor!.Flavors.OrderBy(taste => taste)));
        return string.Join("; ", parts);
    }
}
