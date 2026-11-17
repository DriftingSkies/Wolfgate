using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// Eye markings have their own category: every playable species keeps all the eye markings it may wear, left and right
/// together, alongside a full head budget.
/// </summary>
[TestFixture]
[TestOf(typeof(MarkingSet))]
public sealed class EyeMarkingsTest
{
    [Test]
    public async Task EyesBesideFullHeadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            Assert.That(markings.Markings["ShadekinEyeL"].MarkingCategory, Is.EqualTo(MarkingCategories.Eyes));

            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart))
                {
                    var sex = species.Sexes.First();
                    var eyes = markings.MarkingsByCategoryAndSpeciesAndSex(MarkingCategories.Eyes, species.ID, sex).Keys.ToList();
                    if (eyes.Count < 2)
                        continue;

                    var points = proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points;
                    var headBudget = points.TryGetValue(MarkingCategories.Head, out var head) ? head.Points : 3;
                    var worn = markings.MarkingsByCategoryAndSpeciesAndSex(MarkingCategories.Head, species.ID, sex).Keys
                        .OrderBy(id => id)
                        .Take(headBudget)
                        .Concat(eyes)
                        .Select(id => markings.Markings[id].AsMarking())
                        .ToList();

                    var appearance = HumanoidCharacterProfile.DefaultWithSpecies(species.ID).Appearance.WithMarkings(worn);
                    var kept = HumanoidCharacterAppearance.EnsureValid(appearance, species.ID, sex).Markings
                        .Select(m => m.MarkingId)
                        .ToHashSet();

                    Assert.That(eyes.Where(id => !kept.Contains(id)), Is.Empty,
                        $"{species.ID} lost eye markings beside a full head.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
