using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.Traits;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Traits;

/// <summary>
/// Trait editor layouts match real categories and put every trait of a grouped category in exactly one group.
/// </summary>
[TestFixture]
public sealed class TraitLayoutTest
{
    [Test]
    public async Task LayoutsCoverTheirCategories()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var layout in proto.EnumeratePrototypes<WFTraitLayoutPrototype>())
                {
                    Assert.That(proto.HasIndex<TraitCategoryPrototype>(layout.ID), Is.True,
                        $"Trait layout {layout.ID} has no matching trait category.");

                    if (layout.Groups.Count == 0)
                        continue;

                    var seen = new HashSet<string>();
                    foreach (var group in layout.Groups)
                    {
                        foreach (var id in group.Traits)
                        {
                            Assert.That(proto.TryIndex(id, out var trait), Is.True,
                                $"Trait layout {layout.ID} names unknown trait {id}.");
                            Assert.That(trait?.Category?.Id, Is.EqualTo(layout.ID),
                                $"Trait {id} is grouped under {layout.ID} but belongs to {trait?.Category}.");
                            Assert.That(seen.Add(id), Is.True, $"Trait {id} is in more than one {layout.ID} group.");
                        }
                    }

                    var ungrouped = proto.EnumeratePrototypes<TraitPrototype>()
                        .Where(t => t.Category?.Id == layout.ID && !seen.Contains(t.ID))
                        .Select(t => t.ID)
                        .ToList();
                    Assert.That(ungrouped, Is.Empty,
                        $"These {layout.ID} traits are in no group and would land under Other.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
