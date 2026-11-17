using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Shitmed.Body.Part;
using Content.Shared._WG.Augments;
using Content.Shared._WG.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A human loaded with augments gets the real parts and organs without an injury, keeps what its hands hold, draws
/// the picked styles (digitigrade on digitigrade legs) and takes them back off when loaded without them.
/// </summary>
[TestOf(typeof(WGAugmentRules))]
public sealed class AugmentBodyTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    // Kinds no shipped augment uses yet, so the system stays tested for whoever adds them.
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgAugment
  id: WGTestAugmentHeadPlating
  name: wg-augment-cyber-arm
  description: wg-augment-cyber-arm-desc
  slot: Head
  kind: Plating
  styled: true

- type: wgAugment
  id: WGTestAugmentMissingLeftArm
  name: wg-augment-cyber-arm
  description: wg-augment-cyber-arm-desc
  slot: LeftArm
  kind: Missing

- type: wgAugment
  id: WGTestAugmentMissingRightArm
  name: wg-augment-cyber-arm
  description: wg-augment-cyber-arm-desc
  slot: RightArm
  kind: Missing

- type: wgAugment
  id: WGTestAugmentSpeedLeftLeg
  name: wg-augment-cyber-leg
  description: wg-augment-cyber-leg-desc
  slot: LeftLeg
  kind: Limb
  part: SpeedLeftLeg
  styled: true

- type: wgAugment
  id: WGTestAugmentEyes
  name: wg-augment-cyber-arm
  description: wg-augment-cyber-arm-desc
  slot: Eyes
  kind: Organ
  organ: BasicCyberneticEyes
";

    private static HumanoidCharacterProfile Augmented(LegStance stance = LegStance.Default)
    {
        return HumanoidCharacterProfile.DefaultWithSpecies("Human").WithLegStance(stance).WithAugments(
            new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.Head] = new("WGTestAugmentHeadPlating", "WGAugmentStyleEEZengHu"),
                [WGAugmentSlot.RightArm] = new("WGAugmentCyberRightArm", "WGAugmentStyleHephaestus"),
                [WGAugmentSlot.LeftArm] = new("WGTestAugmentMissingLeftArm", null),
                [WGAugmentSlot.LeftLeg] = new("WGTestAugmentSpeedLeftLeg", "WGAugmentStyleCyborg"),
                [WGAugmentSlot.Eyes] = new("WGTestAugmentEyes", null),
            });
    }

    [Test]
    public async Task AugmentedBodyTest()
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        var bodies = SEntMan.System<SharedBodySystem>();
        var hands = SEntMan.System<SharedHandsSystem>();
        EntityUid held = default;
        EntityUid arm = default;

        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            held = SEntMan.SpawnEntity("Crowbar", SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates);
            // The interaction test mob only has its right hand.
            var right = SharedBodySystem.GetPartSlotContainerId("right hand");
            Assert.That(hands.TryPickup(SPlayer, held, right, checkActionBlocker: false), Is.True);

            var before = SEntMan.GetComponent<DamageableComponent>(SPlayer).TotalDamage;
            humanoids.LoadProfile(SPlayer, Augmented());
            var parts = bodies.GetBodyChildren(SPlayer).ToList();
            string Proto(EntityUid uid) => SEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID;

            arm = parts.Single(p => p.Component.PartType == BodyPartType.Arm && p.Component.Symmetry == BodyPartSymmetry.Right).Id;
            Assert.That(Proto(arm), Is.EqualTo("WGAugmentRightArm"));
            Assert.That(parts.Any(p => p.Component.PartType == BodyPartType.Hand && p.Component.Symmetry == BodyPartSymmetry.Right),
                "The cybernetic arm came without its hand.");
            Assert.That(parts.Any(p => p.Component.PartType == BodyPartType.Arm && p.Component.Symmetry == BodyPartSymmetry.Left),
                Is.False, "The missing arm is still there.");
            Assert.That(Proto(parts.Single(p => p.Component.PartType == BodyPartType.Leg && p.Component.Symmetry == BodyPartSymmetry.Left).Id),
                Is.EqualTo("SpeedLeftLeg"));
            Assert.That(parts.Count(p => p.Component.PartType == BodyPartType.Leg), Is.EqualTo(2));
            Assert.That(SEntMan.GetComponent<BodyComponent>(SPlayer).LegEntities, Has.Count.EqualTo(2),
                "The replaced leg is still counted.");
            Assert.That(bodies.GetBodyOrgans(SPlayer).Any(o => Proto(o.Id) == "BasicCyberneticEyes"));
            Assert.That(bodies.GetBodyOrgans(SPlayer).Count(o => SEntMan.HasComponent<Content.Shared.Body.Organ.OrganComponent>(o.Id)
                && Proto(o.Id) == "OrganHumanEyes"), Is.Zero, "The old eyes stayed.");

            Assert.That(hands.IsHolding(SPlayer, held), "The replaced arm dropped what its hand held.");
            Assert.That(SEntMan.GetComponent<DamageableComponent>(SPlayer).TotalDamage, Is.LessThanOrEqualTo(before),
                "Swapping parts hurt the body.");

            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(SPlayer);
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RArm].Id?.Id, Is.EqualTo("WGMobAugmentHephaestusRArm"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RHand].Id?.Id, Is.EqualTo("WGMobAugmentHephaestusRHand"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.LLeg].Id?.Id, Is.EqualTo("WGMobAugmentCyborgLLeg"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.Head].Id?.Id, Is.EqualTo("WGMobAugmentEEZengHuHead"));
            Assert.That(humanoid.PermanentlyHidden, Does.Contain(HumanoidVisualLayers.LArm));
            Assert.That(SEntMan.GetComponent<BodyPartAppearanceComponent>(arm).ID?.Id, Is.EqualTo("WGMobAugmentHephaestusRArm"),
                "A severed arm would lose its style.");

            // Loading the same augments again leaves the parts alone.
            humanoids.LoadProfile(SPlayer, Augmented());
            Assert.That(bodies.GetBodyChildrenOfType(SPlayer, BodyPartType.Arm, symmetry: BodyPartSymmetry.Right).Single().Id,
                Is.EqualTo(arm), "A reload replaced an augment it already had.");

            // A profile without them (a clone, a scrambled body) leaves them as they are, as it leaves the parts.
            humanoids.LoadProfile(SPlayer, HumanoidCharacterProfile.DefaultWithSpecies("Human"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RArm].Id?.Id, Is.EqualTo("WGMobAugmentHephaestusRArm"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.Head].Id?.Id, Is.EqualTo("WGMobAugmentEEZengHuHead"));
            Assert.That(humanoid.PermanentlyHidden, Does.Contain(HumanoidVisualLayers.LArm));

            // A missing arm takes its hand, and what it held drops.
            humanoids.LoadProfile(SPlayer, HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugments(
                new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.RightArm] = new("WGTestAugmentMissingRightArm", null) }));
            Assert.That(SEntMan.GetComponent<HandsComponent>(SPlayer).Hands, Is.Empty, "The missing arm's hand is still usable.");
            Assert.That(hands.IsHolding(SPlayer, held), Is.False);
        });
    }

    [Test]
    public async Task DigitigradeLegTest()
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        await Server.WaitAssertion(() =>
        {
            humanoids.LoadProfile(SPlayer, Augmented(LegStance.Digitigrade));
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(SPlayer);
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.LLeg].Id?.Id, Is.EqualTo("WGMobAugmentCyborgLLegDigi"));
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.LFoot].Id?.Id, Is.EqualTo("WGMobAugmentCyborgLFootDigi"));
            // The natural right leg keeps the leg style's digitigrade sprite.
            Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RLeg].Id?.Id, Does.Not.Contain("Augment"));
        });
    }
}
