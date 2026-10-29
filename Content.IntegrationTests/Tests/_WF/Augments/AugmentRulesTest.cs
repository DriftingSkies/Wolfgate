using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Augments;

/// <summary>Stored choices round-trip, validation drops what doesn't fit, and every style draws legs of both kinds.</summary>
[TestFixture]
[TestOf(typeof(WFAugmentRules))]
public sealed class AugmentRulesTest
{
    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var choices = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleXion"),
                [WFAugmentSlot.Heart] = new("WFAugmentCyberHeart", null),
            };
            var read = WFAugmentRules.FromStored(WFAugmentRules.ToStored(choices));
            Assert.That(read, Is.EquivalentTo(choices));
            Assert.That(WFAugmentRules.FromStored("garbage;Nope:Thing;LeftArm"), Is.Empty);

            var valid = WFAugmentRules.Validate("Human", new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                // Wrong slot for the augment.
                [WFAugmentSlot.RightArm] = new("WFAugmentCyberLeftArm", null),
                // Unknown augment.
                [WFAugmentSlot.Heart] = new("WFAugmentNotReal", null),
                // A legs-only style can't plate an arm; the first style that fits takes its place.
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleZhenkov"),
            }, new ProtoId<TraitPrototype>[0], proto, factory);
            Assert.That(valid.Keys, Is.EquivalentTo(new[] { WFAugmentSlot.LeftArm }));
            Assert.That(valid[WFAugmentSlot.LeftArm].Style?.Id, Is.Not.EqualTo("WFAugmentStyleZhenkov").And.Not.Null);

            // Prybar Prosthetics and Bionic Legs fit their own limbs at spawn, so augments there are dropped.
            var all = new Dictionary<WFAugmentSlot, WFAugmentChoice>
            {
                [WFAugmentSlot.LeftArm] = new("WFAugmentCyberLeftArm", "WFAugmentStyleCyborg"),
                [WFAugmentSlot.RightLeg] = new("WFAugmentCyberRightLeg", "WFAugmentStyleCyborg"),
            };
            Assert.That(WFAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "PrybarProsthetics" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WFAugmentSlot.RightLeg }));
            Assert.That(WFAugmentRules.Validate("Human", all, new ProtoId<TraitPrototype>[] { "BionicLegs" }, proto, factory).Keys,
                Is.EquivalentTo(new[] { WFAugmentSlot.LeftArm }));
            foreach (var claim in proto.EnumeratePrototypes<WFAugmentTraitSlotsPrototype>())
                Assert.That(proto.HasIndex<TraitPrototype>(claim.ID), $"{claim.ID} claims slots for a trait that doesn't exist.");

            foreach (var style in proto.EnumeratePrototypes<WFAugmentStylePrototype>())
            {
                foreach (var layer in new[] { HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot })
                {
                    Assert.That(style.Sprites.ContainsKey(layer), $"{style.ID} has no {layer}.");
                    Assert.That(style.Digitigrade.ContainsKey(layer), $"{style.ID} has no digitigrade {layer}.");
                }
            }

            foreach (var augment in proto.EnumeratePrototypes<WFAugmentPrototype>())
            {
                if (augment.Part is { } part)
                    Assert.That(proto.HasIndex(part), $"{augment.ID} names a missing part.");
                if (augment.Organ is { } organ)
                    Assert.That(proto.HasIndex(organ), $"{augment.ID} names a missing organ.");
                Assert.That(augment.Kind != WFAugmentKind.Limb || augment.Part != null, $"{augment.ID} is a limb without a part.");
                Assert.That(augment.Kind != WFAugmentKind.Organ || augment.Organ != null, $"{augment.ID} is an organ without one.");
                Assert.That(!augment.Styled || WFAugmentRules.StylesFor(augment.Slot, proto).Count > 0, $"{augment.ID} has no style to wear.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
