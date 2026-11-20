using System.Collections.Generic;
using Content.Server._WG.Augments;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._WG.Augments;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// A trait that fits its own arms at spawn deletes the hands and implants on the old ones; they are put back on the
/// trait's arms after it runs.
/// </summary>
[TestFixture]
[TestOf(typeof(WGAugmentSystem))]
public sealed class AugmentTraitLimbsTest
{
    private static readonly ProtoId<TraitPrototype> Trait = "PrybarProsthetics";
    private static readonly ProtoId<WGAugmentPrototype> Hand = "WGAugmentDexLeftHand";
    private static readonly ProtoId<WGAugmentPrototype> Blade = "WGAugmentLeftMantisBlade";

    [Test]
    public async Task TraitArmsKeepAugmentsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var augments = server.System<WGAugmentSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var traits = new[] { Trait };
            var choices = WGAugmentRules.Validate("Human",
                new Dictionary<WGAugmentSlot, WGAugmentChoice> { [WGAugmentSlot.LeftHand] = new(Hand, "WGAugmentStyleCyborg") },
                traits,
                proto,
                factory);
            var implants = WGAugmentRules.ValidateImplants("Human", choices, new[] { Blade }, traits, proto, factory);
            Assert.That(choices.Keys, Is.EquivalentTo(new[] { WGAugmentSlot.LeftHand }));
            Assert.That(implants, Is.EquivalentTo(new[] { Blade }), "The trait's arm didn't take the mantis blade.");

            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithTraitPreference(Trait, proto)
                .WithAugments(choices)
                .WithAugmentImplants(implants);

            var body = entities.SpawnEntity("MobHuman", map.GridCoords);
            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(body, profile);
            Assert.That(augments.ReadInstalled(body), Does.Contain(Hand).And.Contain(Blade));

            // The trait's arms replace the old ones, hands and implants with them.
            entities.AddComponent<PrybarProstheticsComponent>(body);
            Assert.That(augments.ReadInstalled(body), Does.Not.Contain(Hand).And.Not.Contain(Blade));

            augments.ReapplyOverTraits(body, profile);
            var installed = augments.ReadInstalled(body);
            Assert.That(installed, Does.Contain(Hand), "The dex hand wasn't put back on the trait's arm.");
            Assert.That(installed, Does.Contain(Blade), "The mantis blade wasn't put back in the trait's arm.");
            Assert.That(installed, Does.Contain((ProtoId<WGAugmentPrototype>) "WGAugmentJawsOfLifeLeftArm"),
                "Putting the augments back took the trait's arm off.");
            entities.DeleteEntity(body);
        });
        await pair.CleanReturnAsync();
    }
}
