using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.LegStyle;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.LegStyle;

/// <summary>
/// The leg style data: every style is one a species can actually pick and save, and the rules read it the way the
/// character editor shows it.
/// </summary>
[TestFixture]
[TestOf(typeof(LegStyleRules))]
public sealed class LegStyleRulesTest
{
    private static readonly HumanoidVisualLayers[] LegLayers =
    {
        HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg, HumanoidVisualLayers.LFoot, HumanoidVisualLayers.RFoot,
    };

    [Test]
    public async Task StylesAreUsableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var seen = new HashSet<(string, bool)>();
            Assert.Multiple(() =>
            {
                foreach (var style in proto.EnumeratePrototypes<LegStylePrototype>())
                {
                    Assert.That(style.Stance, Is.Not.EqualTo(LegStance.Default), $"{style.ID} has no stance.");
                    Assert.That(style.Sprites.Keys, Is.SubsetOf(LegLayers), $"{style.ID} replaces more than legs.");
                    Assert.That(style.Displacements.Keys.Concat(style.FemaleDisplacements.Keys).Distinct(),
                        Is.SubsetOf(style.DisplacementSlots), $"{style.ID} has a map for a slot it doesn't reshape.");

                    foreach (var species in style.Species)
                    {
                        Assert.That(seen.Add((species, style.Default)), Is.True,
                            $"{species} is in two leg styles of a kind; it has one pair of legs and the toggle offers one more.");
                        Assert.That(proto.TryIndex(species, out var speciesProto), Is.True, $"{style.ID} names unknown species {species}.");
                        if (speciesProto == null)
                            continue;

                        // The style has to differ from what the species draws, or the toggle does nothing. The style of
                        // a species' own legs swaps nothing.
                        var own = proto.Index<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet).Sprites;
                        if (style.Default)
                            Assert.That(style.Sprites, Is.Empty, $"{style.ID} replaces legs {species} already draws.");
                        else
                            Assert.That(style.Sprites.Any(s => own.GetValueOrDefault(s.Key) != s.Value.Id), Is.True,
                                $"{style.ID} gives {species} the legs it already has.");

                        // Reshaped slots have to exist on the species' inventory.
                        var template = proto.Index(speciesProto.Prototype).TryComp<InventoryComponent>(out var inventory, server.EntMan.ComponentFactory)
                            ? proto.Index<InventoryTemplatePrototype>(inventory.TemplateId)
                            : null;
                        Assert.That(template, Is.Not.Null, $"{species} has no inventory.");
                        Assert.That(style.DisplacementSlots, Is.SubsetOf(template!.Slots.Select(s => s.Name)),
                            $"{style.ID} reshapes a slot {species} doesn't have.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // A species with plantigrade legs of its own.
                Assert.That(LegStyleRules.TryGetAlternate("Tajaran", proto, out var alternate), Is.True);
                Assert.That(alternate, Is.EqualTo(LegStance.Digitigrade));
                Assert.That(LegStyleRules.IsDigitigrade("Tajaran", LegStance.Default, proto), Is.False);
                Assert.That(LegStyleRules.IsDigitigrade("Tajaran", LegStance.Digitigrade, proto), Is.True);
                Assert.That(LegStyleRules.Validate("Tajaran", LegStance.Plantigrade, proto), Is.EqualTo(LegStance.Default));
                Assert.That(LegStyleRules.Find("Tajaran", LegStance.Default, proto), Is.Null);

                // A species whose own digitigrade legs have a style for their clothing fit.
                Assert.That(LegStyleRules.TryGetAlternate("Vulpkanin", proto, out alternate), Is.True);
                Assert.That(alternate, Is.EqualTo(LegStance.Plantigrade));
                Assert.That(LegStyleRules.IsDigitigrade("Vulpkanin", LegStance.Default, proto), Is.True);
                Assert.That(LegStyleRules.IsDigitigrade("WGCanine", LegStance.Plantigrade, proto), Is.False);
                Assert.That(LegStyleRules.Validate("Vulpkanin", LegStance.Digitigrade, proto), Is.EqualTo(LegStance.Default));
                Assert.That(LegStyleRules.Find("Vulpkanin", LegStance.Default, proto)?.Default, Is.True);
                Assert.That(LegStyleRules.Find("Vulpkanin", LegStance.Plantigrade, proto)?.Default, Is.False);

                // A species that is digitigrade already.
                Assert.That(LegStyleRules.IsDigitigrade("Reptilian", LegStance.Default, proto), Is.True);
                Assert.That(LegStyleRules.IsDigitigrade("Reptilian", LegStance.Plantigrade, proto), Is.False);
                Assert.That(LegStyleRules.Validate("Reptilian", LegStance.Digitigrade, proto), Is.EqualTo(LegStance.Default));
                Assert.That(LegStyleRules.Validate("Reptilian", LegStance.Plantigrade, proto), Is.EqualTo(LegStance.Plantigrade));

                // A species with art and clothing maps of its own, each way round.
                Assert.That(LegStyleRules.IsDigitigrade("Thaven", LegStance.Digitigrade, proto), Is.True);
                Assert.That(LegStyleRules.IsDigitigrade("Synth", LegStance.Default, proto), Is.True);
                Assert.That(LegStyleRules.IsDigitigrade("Synth", LegStance.Plantigrade, proto), Is.False);

                // A species with no choice.
                Assert.That(LegStyleRules.TryGetAlternate("Moth", proto, out _), Is.False);
                Assert.That(LegStyleRules.IsDigitigrade("Moth", LegStance.Digitigrade, proto), Is.False);
            });

            // Saving drops legs the species doesn't have and keeps the ones it does.
            var session = server.PlayerMan.Sessions.First();
            var collection = server.ResolveDependency<IDependencyCollection>();
            var moth = HumanoidCharacterProfile.DefaultWithSpecies("Moth").WithLegStance(LegStance.Digitigrade);
            moth.EnsureValid(session, collection);
            Assert.That(moth.LegStance, Is.EqualTo(LegStance.Default));

            var human = HumanoidCharacterProfile.DefaultWithSpecies("Human").WithLegStance(LegStance.Digitigrade);
            human.EnsureValid(session, collection);
            Assert.That(human.LegStance, Is.EqualTo(LegStance.Digitigrade));
            Assert.That(human.MemberwiseEquals(human.WithLegStance(LegStance.Default)), Is.False,
                "The editor would not see a leg change as unsaved.");
        });

        await pair.CleanReturnAsync();
    }
}
