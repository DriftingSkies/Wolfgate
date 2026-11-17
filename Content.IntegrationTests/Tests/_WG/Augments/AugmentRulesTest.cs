using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.Augments;
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
            var read = WGAugmentRules.FromStored(WGAugmentRules.ToStored(choices));
            Assert.That(read, Is.EquivalentTo(choices));
            Assert.That(WGAugmentRules.FromStored("garbage;Nope:Thing;LeftArm"), Is.Empty);

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
