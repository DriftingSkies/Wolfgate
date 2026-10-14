using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Humanoid;

/// <summary>
/// The creator's marking picker on a species whose body parts are markings: other markings on a body part leave its
/// chassis part alone, a chassis part swaps in place, and taking one off puts the default back.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfgateMarkingPicker))]
public sealed class WolfgateMarkingPickerChassisTest
{
    private const string Species = "IPC";

    [Test]
    public async Task ChassisTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var markings = client.ResolveDependency<MarkingManager>();

        await client.WaitAssertion(() =>
        {
            var species = proto.Index<SpeciesPrototype>(Species);
            var sex = species.Sexes.First();
            var defaults = proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points[MarkingCategories.Chassis].DefaultMarkings;
            var head = markings.Markings[defaults.First(id => markings.Markings[id].BodyPart == HumanoidVisualLayers.Head)];
            var otherHead = markings.MarkingsByCategoryAndSpeciesAndSex(MarkingCategories.Chassis, Species, sex).Values
                .OrderBy(m => m.ID)
                .First(m => m.BodyPart == HumanoidVisualLayers.Head && m.ID != head.ID);
            // One marking of each body category on each body part, as far as the budget goes; wings are filed under Chest.
            var decorations = new[] { MarkingCategories.Head, MarkingCategories.Chest, MarkingCategories.Arms, MarkingCategories.Legs }
                .SelectMany(c => markings.MarkingsByCategoryAndSpeciesAndSex(c, Species, sex).Values
                    .OrderBy(m => m.ID)
                    .GroupBy(m => m.BodyPart)
                    .Select(g => g.First())
                    .Take(3))
                .ToList();
            Assert.That(decorations.Select(m => m.MarkingCategory).Distinct().Count(), Is.EqualTo(4));

            var set = new MarkingSet();
            var picker = new WolfgateMarkingPicker();
            picker.OnMarkingAdded += changed => set = changed;
            picker.OnMarkingRemoved += changed => set = changed;
            picker.SetData(new List<Marking>(), Species, sex, Color.White, Color.Black);

            List<string> Chassis() => set.TryGetCategory(MarkingCategories.Chassis, out var worn)
                ? worn.Select(m => m.MarkingId).OrderBy(id => id).ToList()
                : new List<string>();
            var whole = defaults.OrderBy(id => id).ToList();

            foreach (var decoration in decorations)
            {
                picker.Toggle(decoration);
                Assert.That(set.TryGetMarking(decoration.MarkingCategory, decoration.ID, out _), Is.True,
                    $"{decoration.ID} ({decoration.BodyPart}, {decoration.MarkingCategory}) was not added.");
                Assert.That(Chassis(), Is.EqualTo(whole), $"{decoration.ID} changed the chassis.");
            }

            picker.Toggle(otherHead);
            var swapped = whole.Select(id => id == head.ID ? otherHead.ID : id).OrderBy(id => id).ToList();
            Assert.That(Chassis(), Is.EqualTo(swapped), "A chassis part did not swap in place.");

            picker.Toggle(otherHead);
            Assert.That(Chassis(), Is.EqualTo(whole), "Taking a chassis part off did not put the default back.");

            picker.Toggle(head);
            Assert.That(Chassis(), Is.EqualTo(whole), "The default chassis part came off.");

            foreach (var decoration in decorations)
                Assert.That(set.TryGetMarking(decoration.MarkingCategory, decoration.ID, out _), Is.True, $"{decoration.ID} was lost.");
        });

        await pair.CleanReturnAsync();
    }
}
