#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-05 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1105Test
{
    /// <summary>
    /// Report 482: every mob something can drive, xenos included, has the z-physics that lets a body walk up stairs,
    /// which BaseMob gives the rest.
    /// </summary>
    [Test]
    public async Task XenosCanClimbStairs()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var factory = server.ResolveDependency<IComponentFactory>();
            var missing = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract
                            && p.TryGetComponent<MobStateComponent>(out _, factory)
                            && p.TryGetComponent<InputMoverComponent>(out _, factory)
                            && !p.TryGetComponent<CEZPhysicsComponent>(out _, factory))
                .Select(p => p.ID)
                .ToList();
            Assert.That(missing, Is.Empty, "these mobs can't walk up stairs");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 480: the arms and legs the Prybar Prosthetics and Bionic Legs traits fit are drawn to an Avali's own
    /// limb shape, as augments are, not the human-shaped bishop chassis.
    /// </summary>
    [Test]
    public async Task TraitLimbsKeepSpeciesShape()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var avali = em.SpawnEntity("MobAvali", map.GridCoords);
            em.AddComponent<PrybarProstheticsComponent>(avali);
            em.AddComponent<BionicLegsComponent>(avali);

            var humanoid = em.GetComponent<HumanoidAppearanceComponent>(avali);
            Assert.Multiple(() =>
            {
                Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.LLeg].Id?.Id, Is.EqualTo("WFMobAugmentEEBishopAvaliLLeg"));
                Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RFoot].Id?.Id, Is.EqualTo("WFMobAugmentEEBishopAvaliRFoot"));
                Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.LHand].Id?.Id, Is.EqualTo("WFMobAugmentEEBishopAvaliLHand"));
                Assert.That(humanoid.CustomBaseLayers[HumanoidVisualLayers.RArm].Id?.Id, Does.StartWith("WFMobAugmentEEBishop"));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 481: a Resomi's protogen markings are kept and drawn, and in game nothing of the body draws over them:
    /// the Resomi mob had its hands and feet, where the lights go, under its arms and legs.
    /// </summary>
    [Test]
    public async Task ResomiProtogenMarkingsShow()
    {
        string[] markings = { "ProtoResomiMediumArmor", "ProtoResomiVisor", "ProtoResomiLEDFace", "ProtoResomiLights" };
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Resomi");
            profile = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(markings
                .Select(id => new Marking(id, new List<Color> { Color.White })).ToList()));
            var species = client.ResolveDependency<IPrototypeManager>().Index<SpeciesPrototype>("Resomi");
            var doll = em.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
            em.System<Content.Client.Humanoid.HumanoidAppearanceSystem>().LoadProfile(doll, profile);

            var humanoid = em.GetComponent<HumanoidAppearanceComponent>(doll);
            var sprite = em.GetComponent<SpriteComponent>(doll);
            var sprites = em.System<SpriteSystem>();
            foreach (var id in markings)
            {
                Assert.That(humanoid.MarkingSet.Markings.Values.Any(l => l.Any(m => m.MarkingId == id)), Is.True, $"{id} was dropped");
                var proto = client.ResolveDependency<IPrototypeManager>().Index<MarkingPrototype>(id);
                var state = ((Robust.Shared.Utility.SpriteSpecifier.Rsi) proto.Sprites[0]).RsiState;
                Assert.That(sprites.LayerMapTryGet((doll, sprite), $"{id}-{state}", out var index, false), Is.True, $"{id} has no layer");
                Assert.That(sprite[index].Visible, Is.True, $"{id} is hidden");
            }

            em.DeleteEntity(doll);

            // In game the hands and feet, which carry the lights and visor, draw over the arms and legs, as on the doll.
            var mob = em.SpawnEntity("MobResomi", MapCoordinates.Nullspace);
            var mobSprite = em.GetComponent<SpriteComponent>(mob);
            Assert.That(sprites.LayerMapTryGet((mob, mobSprite), HumanoidVisualLayers.LLeg, out var leg, false), Is.True);
            Assert.That(sprites.LayerMapTryGet((mob, mobSprite), HumanoidVisualLayers.LArm, out var arm, false), Is.True);
            foreach (var part in new[] { HumanoidVisualLayers.RHand, HumanoidVisualLayers.LHand, HumanoidVisualLayers.RFoot, HumanoidVisualLayers.LFoot })
            {
                Assert.That(sprites.LayerMapTryGet((mob, mobSprite), part, out var index, false), Is.True);
                Assert.That(index, Is.GreaterThan(leg).And.GreaterThan(arm), $"{part} draws under the limbs");
            }

            em.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }
}
