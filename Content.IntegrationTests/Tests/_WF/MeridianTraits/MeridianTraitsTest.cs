#nullable enable
using Content.Server._WF.MeridianTraits;
using Content.Shared._WF.RMCCore.Deafness;
using Content.Shared.Damage.Components;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.MeridianTraits;

/// <summary>
/// Every Meridian trait applies to a human, Deaf survives its deafness being removed, and exclusive traits can't be
/// taken together.
/// </summary>
[TestFixture]
public sealed class MeridianTraitsTest
{
    // Exclusive traits go on separate mobs.
    private static readonly string[][] TraitSets =
    {
        new[] { "WFDeaf", "WFPseudobulbarAffect", "WFIlluminated", "WFRadFiend", "WFHardenedSoles", "WFCanineTraits" },
        new[] { "WFFragility", "WFFelineTraits" },
        new[] { "WFAvianTraits" },
    };

    [Test]
    public async Task TraitsApplyAndDeafnessStays()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protoMan = server.ProtoMan;
        var map = await pair.CreateTestMap();

        var mobs = new EntityUid[TraitSets.Length];
        await server.WaitPost(() =>
        {
            for (var i = 0; i < TraitSets.Length; i++)
            {
                mobs[i] = entMan.SpawnEntity("MobHuman", map.MapCoords);
                foreach (var id in TraitSets[i])
                {
                    entMan.AddComponents(mobs[i], protoMan.Index<TraitPrototype>(id).Components, false);
                }
            }

            entMan.RemoveComponent<DeafComponent>(mobs[0]);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < TraitSets.Length; i++)
            {
                foreach (var id in TraitSets[i])
                {
                    foreach (var entry in protoMan.Index<TraitPrototype>(id).Components.Values)
                    {
                        Assert.That(entMan.HasComponent(mobs[i], entry.Component.GetType()),
                            $"{id} should add {entry.Component.GetType().Name}.");
                    }
                }
            }

            Assert.That(entMan.HasComponent<WFPermanentDeafComponent>(mobs[0]));
            Assert.That(entMan.HasComponent<DeafComponent>(mobs[0]), "Permanent deafness should come back.");
            Assert.That(entMan.GetComponent<DamageProtectionBuffComponent>(mobs[0]).Modifiers, Contains.Key("radFiend"));
            Assert.That(entMan.GetComponent<DamageProtectionBuffComponent>(mobs[1]).Modifiers, Contains.Key("fragility"));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExclusiveTraitsAreDropped()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            var traits = new ProtoId<TraitPrototype>[] { "WFRadFiend", "WFFragility" };
            Assert.That(profile.GetValidTraits(traits, server.ProtoMan),
                Is.EquivalentTo(new ProtoId<TraitPrototype>[] { "WFFragility" }));
        });

        await pair.CleanReturnAsync();
    }
}
