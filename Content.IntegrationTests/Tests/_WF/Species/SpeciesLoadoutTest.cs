#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// Default loadouts never hold the same loadout twice. A group whose minimum is above what a species can take (the
/// species-gated gear groups) was topped up with the one valid entry again on every validation, so an Avali spawned
/// with four auto-injectors and a Vox with a spare tank harness at its feet.
/// </summary>
[TestFixture]
public sealed class SpeciesLoadoutTest
{
    private const string AvaliPen = "WFLoadoutSpeciesAvaliAutoInjector";

    [Test]
    public async Task DefaultsNeverRepeatALoadoutTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ProtoMan;
        var collection = server.ResolveDependency<IDependencyCollection>();
        var repeats = new List<string>();
        var pens = new List<string>();

        await server.WaitPost(() =>
        {
            foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>())
            {
                if (!species.RoundStart)
                    continue;

                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID);
                foreach (var role in proto.EnumeratePrototypes<RoleLoadoutPrototype>())
                {
                    var loadout = new RoleLoadout(role.ID);
                    loadout.SetDefault(profile, null, proto);

                    // Validation runs again on every load and spawn, and tops a short group up each time.
                    for (var pass = 0; pass < 3; pass++)
                        loadout.EnsureValid(profile, null, collection);

                    foreach (var (group, selected) in loadout.SelectedLoadouts)
                    {
                        if (selected.Distinct().Count() != selected.Count)
                            repeats.Add($"{species.ID} {role.ID} {group}: {string.Join(", ", selected.Select(x => x.Prototype.Id))}");
                    }

                    var penCount = loadout.SelectedLoadouts.Values.Sum(x => x.Count(y => y.Prototype == AvaliPen));
                    if (species.ID == "Avali" && penCount > 1)
                        pens.Add($"{role.ID}: {penCount}");
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(repeats, Is.Empty, $"{repeats.Count} default loadout groups repeat a loadout.");
            Assert.That(pens, Is.Empty, "An Avali starts with more than one auto-injector.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A saved profile that already holds the repeats loses them on validation.</summary>
    [Test]
    public async Task SavedRepeatsAreDroppedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var collection = server.ResolveDependency<IDependencyCollection>();
        var group = new ProtoId<LoadoutGroupPrototype>("NFSpeciesSpecific");
        List<Loadout> selected = new();

        await server.WaitPost(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Avali");
            var loadout = new RoleLoadout("JobContractor");
            loadout.SelectedLoadouts[group] = Enumerable.Range(0, 4).Select(_ => new Loadout { Prototype = AvaliPen }).ToList();
            loadout.EnsureValid(profile, null, collection);
            selected = loadout.SelectedLoadouts[group];
        });

        Assert.That(selected.Select(x => x.Prototype.Id), Is.EqualTo(new[] { AvaliPen }));

        await pair.CleanReturnAsync();
    }
}
