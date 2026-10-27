using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// An optional default marking, such as the synth tail, starts saved in a new profile and stays off once removed;
/// loading the profile does not put it back.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedHumanoidAppearanceSystem))]
public sealed class OptionalDefaultMarkingsTest
{
    [Test]
    public async Task SynthTailRemovableTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var humanoid = entities.System<SharedHumanoidAppearanceSystem>();
            var profile = HumanoidCharacterProfile.RandomWithSpecies("Synth");
            Assert.That(profile.Appearance.Markings.Select(m => m.MarkingId), Does.Contain("SynthTail"),
                "a new synth does not start with its tail saved.");

            var tailless = profile.WithCharacterAppearance(profile.Appearance.WithMarkings(
                profile.Appearance.Markings.Where(m => m.MarkingId != "SynthTail").ToList()));

            var mob = entities.SpawnEntity("MobSynth", MapCoordinates.Nullspace);
            humanoid.LoadProfile(mob, tailless);

            var markings = entities.GetComponent<HumanoidAppearanceComponent>(mob).MarkingSet;
            Assert.Multiple(() =>
            {
                Assert.That(markings.TryGetCategory(Content.Shared.Humanoid.Markings.MarkingCategories.Tail, out var tails)
                        ? tails.Select(m => m.MarkingId) : Enumerable.Empty<string>(),
                    Does.Not.Contain("SynthTail"), "loading the profile put the removed tail back.");
                Assert.That(markings.TryGetCategory(Content.Shared.Humanoid.Markings.MarkingCategories.Snout, out var snouts)
                        && snouts.Count > 0, Is.True, "the required snout default is no longer applied.");
            });

            entities.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Every species lets the player go without a tail.</summary>
    [Test]
    public async Task TailsOptionalTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        var required = proto.EnumeratePrototypes<MarkingPointsPrototype>()
            .Where(p => p.Points.TryGetValue(MarkingCategories.Tail, out var tail) && tail.Required)
            .Select(p => p.ID)
            .ToList();
        Assert.That(required, Is.Empty, "these marking points require a tail.");

        await pair.CleanReturnAsync();
    }
}
