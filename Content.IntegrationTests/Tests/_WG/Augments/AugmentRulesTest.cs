using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Augments;
using Content.Shared._WG.Biotype;
using Content.Shared.Humanoid;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>Stored choices round-trip, validation drops what doesn't fit, and every style draws legs of both kinds.</summary>
[TestFixture]
[TestOf(typeof(WGAugmentRules))]
public sealed class AugmentRulesTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: wgAugment
  id: WGTestRulesMissingLeftArm
  name: wg-augment-cyber-arm
  description: wg-augment-cyber-arm-desc
  slot: LeftArm
  kind: Missing
";

    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var choices = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleXion"),
                [WGAugmentSlot.Heart] = new("WGAugmentCyberHeart", null),
            };
            var implants = new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentDermalArmor" };
            var stored = WGAugmentRules.ToStored(choices, implants);
            Assert.That(WGAugmentRules.FromStored(stored), Is.EquivalentTo(choices));
            Assert.That(WGAugmentRules.ImplantsFromStored(stored), Is.EquivalentTo(implants));
            Assert.That(WGAugmentRules.FromStored("garbage;Nope:Thing;LeftArm"), Is.Empty);

            // Implants saved one per slot before a part held several still read, and not as slot choices.
            const string legacy = "LeftArmImplant:WGAugmentLeftMantisBlade;Muscles:WGAugmentMuscleFibers";
            Assert.That(WGAugmentRules.ImplantsFromStored(legacy).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WGAugmentLeftMantisBlade", "WGAugmentMuscleFibers" }));
            Assert.That(WGAugmentRules.FromStored(legacy), Is.Empty);

            // Unlocks round-trip with which of them were found; one stored before that was tracked was paid for.
            var unlocks = new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentDermalArmor", "WGAugmentCyberEyes" };
            var found = new HashSet<ProtoId<WGAugmentPrototype>> { "WGAugmentCyberEyes" };
            var storedUnlocks = WGAugmentRules.ToStored(unlocks, found);
            Assert.That(WGAugmentRules.UnlocksFromStored(storedUnlocks), Is.EquivalentTo(unlocks));
            Assert.That(WGAugmentRules.FoundFromStored(storedUnlocks), Is.EquivalentTo(found));
            Assert.That(WGAugmentRules.UnlocksFromStored("WGAugmentDermalArmor;WGAugmentCyberEyes"), Is.EquivalentTo(unlocks));
            Assert.That(WGAugmentRules.FoundFromStored("WGAugmentDermalArmor;WGAugmentCyberEyes"), Is.Empty);

            var valid = WGAugmentRules.Validate("Human", new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                // Wrong slot for the augment.
                [WGAugmentSlot.RightArm] = new("WGAugmentCyberLeftArm", null),
                // Unknown augment.
                [WGAugmentSlot.Heart] = new("WGAugmentNotReal", null),
                // A legs-only style can't plate an arm; the first style that fits takes its place.
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleZhenkov"),
            }, new ProtoId<TraitPrototype>[0], proto, factory);
            Assert.That(valid.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftArm }));
            Assert.That(valid[WGAugmentSlot.LeftArm].Style?.Id, Is.Not.EqualTo("WGAugmentStyleZhenkov").And.Not.Null);

            // Prybar Prosthetics and Bionic Legs fit their own limbs at spawn, so augments there are dropped.
            var all = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
                [WGAugmentSlot.RightLeg] = new("WGAugmentCyberRightLeg", "WGAugmentStyleCyborg"),
            };
            Assert.That(WGAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "PrybarProsthetics" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WGAugmentSlot.RightLeg }));
            Assert.That(WGAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "BionicLegs" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WGAugmentSlot.LeftArm }));

            // A mantis blade needs a synthetic arm: a cybernetic one, a trait's, or an IPC's own.
            var none = new ProtoId<TraitPrototype>[0];
            var unclaimed = new Dictionary<WGAugmentSlot, ProtoId<TraitPrototype>>();
            var natural = new Dictionary<WGAugmentSlot, WGAugmentChoice>();
            var cyberArm = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentCyberLeftArm", "WGAugmentStyleCyborg"),
            };
            var jawsArm = new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGAugmentJawsOfLifeLeftArm", "WGAugmentStyleCyborg"),
            };
            var wanted = new ProtoId<WGAugmentPrototype>[] { "WGAugmentLeftMantisBlade", "WGAugmentDermalArmor", "WGAugmentMuscleFibers" };
            Assert.That(WGAugmentRules.ValidateImplants("Human", natural, wanted, none, proto, factory).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WGAugmentDermalArmor", "WGAugmentMuscleFibers" }));
            Assert.That(WGAugmentRules.ValidateImplants("Human", cyberArm, wanted, none, proto, factory), Has.Count.EqualTo(3));
            Assert.That(WGAugmentRules.Synthetic("Human", WGAugmentSlot.LeftArm, natural, unclaimed, proto, factory), Is.False);
            Assert.That(WGAugmentRules.Synthetic("Human", WGAugmentSlot.LeftArm, natural,
                WGAugmentRules.ClaimedSlots(new ProtoId<TraitPrototype>[] { "PrybarProsthetics" }, proto), proto, factory));
            Assert.That(WGAugmentRules.Synthetic("IPC", WGAugmentSlot.LeftArm, natural, unclaimed, proto, factory));

            // A converted character is ruled by its biotype's body: a synthetic human has the chassis, with no heart
            // to replace and no flesh for an organic implant.
            Assert.That(WGAugmentRules.SlotAvailable("Human", WGAugmentSlot.Heart, proto, factory));
            Assert.That(WGAugmentRules.SlotAvailable("Human", WGAugmentSlot.Heart, proto, factory, WGBiotype.Synthetic), Is.False);
            Assert.That(WGAugmentRules.SlotAvailable("Human", WGAugmentSlot.Eyes, proto, factory, WGBiotype.Synthetic));
            Assert.That(WGAugmentRules.AugmentsFor("Human", WGAugmentSlot.Heart, proto, factory, WGBiotype.Synthetic), Is.Empty);
            Assert.That(WGAugmentRules.Validate("Human", new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.Heart] = new("WGAugmentSandevistan", null),
                [WGAugmentSlot.Eyes] = new("WGAugmentCyberEyes", null),
            }, none, proto, factory, WGBiotype.Synthetic).Keys, Is.EquivalentTo(new[] { WGAugmentSlot.Eyes }));
            Assert.That(WGAugmentRules.Synthetic("Human", WGAugmentSlot.LeftArm, natural, unclaimed, proto, factory, WGBiotype.Synthetic));
            Assert.That(WGAugmentRules.Synthetic("IPC", WGAugmentSlot.LeftArm, natural, unclaimed, proto, factory, WGBiotype.Organic), Is.False);
            Assert.That(WGAugmentRules.ValidateImplants("Human", natural,
                    new ProtoId<WGAugmentPrototype>[] { "WGAugmentLeftMantisBlade", "WGAugmentAdrenalBooster" },
                    none, proto, factory, WGBiotype.Synthetic).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WGAugmentLeftMantisBlade" }));
            Assert.That(WGAugmentRules.ValidateImplants("Human", natural,
                    new ProtoId<WGAugmentPrototype>[] { "WGAugmentLeftMantisBlade", "WGAugmentAdrenalBooster" },
                    none, proto, factory).Select(i => i.Id),
                Is.EquivalentTo(new[] { "WGAugmentAdrenalBooster" }));

            // Better arms hold more implants; a missing one none.
            Assert.That(WGAugmentRules.ImplantSlots(WGAugmentSlot.LeftArm, natural, unclaimed, proto), Is.EqualTo(1));
            Assert.That(WGAugmentRules.ImplantSlots(WGAugmentSlot.LeftArm, cyberArm, unclaimed, proto), Is.EqualTo(1));
            Assert.That(WGAugmentRules.ImplantSlots(WGAugmentSlot.LeftArm, jawsArm, unclaimed, proto), Is.EqualTo(2));
            Assert.That(WGAugmentRules.ImplantSlots(WGAugmentSlot.Chest, natural, unclaimed, proto), Is.EqualTo(2));

            // A hand can't go on a missing arm, and nor can its implants.
            var armless = WGAugmentRules.Validate("Human", new Dictionary<WGAugmentSlot, WGAugmentChoice>
            {
                [WGAugmentSlot.LeftArm] = new("WGTestRulesMissingLeftArm", null),
                [WGAugmentSlot.LeftHand] = new("WGAugmentCyberLeftHand", "WGAugmentStyleCyborg"),
            }, none, proto, factory);
            Assert.That(armless.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftArm }));
            Assert.That(WGAugmentRules.ImplantSlots(WGAugmentSlot.LeftHand, armless, unclaimed, proto), Is.Zero);

            foreach (var claim in proto.EnumeratePrototypes<WGAugmentTraitSlotsPrototype>())
                Assert.That(proto.HasIndex<TraitPrototype>(claim.ID), $"{claim.ID} claims slots for a trait that doesn't exist.");

            foreach (var style in proto.EnumeratePrototypes<WGAugmentStylePrototype>())
            {
                foreach (var layer in new[] { HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot })
                {
                    Assert.That(style.Sprites.ContainsKey(layer), $"{style.ID} has no {layer}.");
                    Assert.That(style.Digitigrade.ContainsKey(layer), $"{style.ID} has no digitigrade {layer}.");
                }
            }

            foreach (var augment in proto.EnumeratePrototypes<WGAugmentPrototype>())
            {
                if (augment.Part is { } part)
                    Assert.That(proto.HasIndex(part), $"{augment.ID} names a missing part.");
                if (augment.Organ is { } organ)
                    Assert.That(proto.HasIndex(organ), $"{augment.ID} names a missing organ.");
                Assert.That(augment.Kind != WGAugmentKind.Limb || augment.Part != null, $"{augment.ID} is a limb without a part.");
                Assert.That(augment.Kind != WGAugmentKind.Organ || augment.Organ != null, $"{augment.ID} is an organ without one.");
                Assert.That(!augment.Styled || WGAugmentRules.StylesFor(augment.Slot, proto).Count > 0, $"{augment.ID} has no style to wear.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
