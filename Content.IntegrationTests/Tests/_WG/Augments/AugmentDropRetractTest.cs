using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Augments;
using Content.Shared._Goobstation.MantisBlades;
using Content.Shared._WG.Augments;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>Pressing drop with an arm augment's item in hand folds it back in; anything else is left alone.</summary>
[TestFixture]
[TestOf(typeof(WGAugmentDropRetractSystem))]
public sealed class AugmentDropRetractTest
{
    [Test]
    public async Task DropRetractsArmAugments()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var bodies = server.System<SharedBodySystem>();
        var hands = server.System<SharedHandsSystem>();
        var actions = server.System<SharedActionsSystem>();
        var humanoids = server.System<SharedHumanoidAppearanceSystem>();
        var retract = server.System<WGAugmentDropRetractSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            humanoids.LoadProfile(mob, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithAugments(new Dictionary<WGAugmentSlot, WGAugmentChoice>())
                .WithAugmentImplants(new ProtoId<WGAugmentPrototype>[] { "WGAugmentLeftFingerLighter" }));
            EntityUid Part(BodyPartType type, BodyPartSymmetry side) =>
                bodies.GetBodyChildrenOfType(mob, type, symmetry: side).Single().Id;
            EntityUid? Held(HandLocation side) => hands.EnumerateHands(mob).Single(h => h.Location == side).HeldEntity;

            // A deployable: the finger lighter's flame.
            var lighter = entities.GetComponent<WGFingerLighterComponent>(Part(BodyPartType.Hand, BodyPartSymmetry.Left));
            var action = entities.GetComponent<InstantActionComponent>(lighter.ActionEntity!.Value);
            actions.PerformAction(mob, null, lighter.ActionEntity.Value, action, action.BaseEvent, timing.CurTime, false);
            Assert.That(Held(HandLocation.Left), Is.Not.Null, "The lighter didn't fold out.");
            var flame = Held(HandLocation.Left)!.Value;

            var wrench = entities.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(retract.TryRetract(mob, wrench), Is.False, "An ordinary item isn't retracted.");
            Assert.That(retract.TryRetract(mob, flame), Is.True);
            Assert.That(lighter.Deployed, Is.Null, "Drop didn't fold the flame in.");
            Assert.That(entities.Deleted(flame) || entities.IsQueuedForDeletion(flame));

            // A mantis blade, through its arm's own toggle.
            var arm = Part(BodyPartType.Arm, BodyPartSymmetry.Right);
            var mantis = entities.ComponentFactory.GetComponent<MantisBladeArmComponent>();
            mantis.ActionProto = "ActionToggleRightMantisBlade";
            entities.AddComponent(arm, mantis);
            entities.EventBus.RaiseLocalEvent(arm, new ToggleMantisBladeEvent());
            Assert.That(mantis.BladeUid, Is.Not.Null, "The blade didn't extend.");
            var blade = mantis.BladeUid!.Value;
            Assert.That(Held(HandLocation.Right), Is.EqualTo(blade));
            Assert.That(retract.TryRetract(mob, blade), Is.True);
            Assert.That(mantis.BladeUid, Is.Null, "Drop didn't retract the blade.");
            Assert.That(Held(HandLocation.Right), Is.Null.Or.Not.EqualTo(blade));
        });
        await pair.CleanReturnAsync();
    }
}
