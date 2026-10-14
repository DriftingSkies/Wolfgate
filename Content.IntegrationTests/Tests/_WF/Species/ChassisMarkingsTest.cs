using System.Collections.Generic;
using System.Linq;
using Content.Client.Humanoid;
using Content.Shared._WF.Species;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Species;

/// <summary>
/// Species whose body parts are markings (IPC): the chassis keeps one part on every body part whatever else is worn,
/// and every marking the species may pick is drawn on its creator doll and on its body's own sprite.
/// </summary>
[TestFixture]
public sealed class ChassisMarkingsTest
{
    private static List<SpeciesPrototype> ChassisSpecies(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<SpeciesPrototype>()
            .Where(s => s.RoundStart
                        && proto.TryIndex<MarkingPointsPrototype>(s.MarkingPoints, out var points)
                        && points.Points.ContainsKey(MarkingCategories.Chassis))
            .OrderBy(s => s.ID)
            .ToList();
    }

    /// <summary>The body parts the species' chassis defaults cover, with the default on each.</summary>
    private static Dictionary<HumanoidVisualLayers, MarkingPrototype> Defaults(SpeciesPrototype species, MarkingManager markings, IPrototypeManager proto)
    {
        return proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points[MarkingCategories.Chassis].DefaultMarkings
            .Select(id => markings.Markings[id])
            .ToDictionary(m => m.BodyPart);
    }

    /// <summary>Every marking a character of the species may pick outside the hair pickers, without Mismatched parts.</summary>
    private static List<MarkingPrototype> Pickable(SpeciesPrototype species, MarkingManager markings, IPrototypeManager proto)
    {
        return Enum.GetValues<MarkingCategories>()
            .Where(c => !MismatchedPartsRules.IsHair(c))
            .SelectMany(c => markings.MarkingsByCategoryAndSpeciesAndSex(c, species.ID, species.Sexes.First()).Values)
            .Where(m => MismatchedPartsRules.Drawable(m, species.ID, proto))
            .OrderBy(m => m.ID)
            .ToList();
    }

    private static HumanoidCharacterProfile Wearing(SpeciesPrototype species, params MarkingPrototype[] worn)
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID).WithSex(species.Sexes.First());
        return profile.WithCharacterAppearance(profile.Appearance.WithMarkings(worn.Select(m => m.AsMarking()).ToList()));
    }

    private static IEnumerable<string> LayerKeys(MarkingPrototype marking)
    {
        foreach (var sprite in marking.Sprites)
        {
            if (sprite is SpriteSpecifier.Rsi rsi)
                yield return $"{marking.ID}-{rsi.RsiState}";
        }
    }

    private static List<HumanoidVisualLayers> Parts(MarkingSet set, MarkingManager markings)
    {
        return set.TryGetCategory(MarkingCategories.Chassis, out var worn)
            ? worn.Select(m => markings.Markings[m.MarkingId].BodyPart).OrderBy(p => p).ToList()
            : new List<HumanoidVisualLayers>();
    }

    /// <summary>
    /// A body keeps one chassis part on every body part: with nothing picked, with a marking of every other category
    /// it has points for, with a part missing, with two parts on one body part, and with a part swapped for another.
    /// </summary>
    [Test]
    public async Task BodiesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();
        var humanoids = entMan.System<SharedHumanoidAppearanceSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var chassisSpecies = ChassisSpecies(proto);
            Assert.That(chassisSpecies.Select(s => s.ID), Does.Contain("IPC"));

            Assert.Multiple(() =>
            {
                foreach (var species in chassisSpecies)
                {
                    var defaults = Defaults(species, markings, proto);
                    var expected = defaults.Keys.OrderBy(p => p).ToList();
                    var pickable = Pickable(species, markings, proto);
                    var points = proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points;

                    // One marking of every other category on every body part, as far as the budget goes.
                    var decorations = pickable
                        .Where(m => m.MarkingCategory != MarkingCategories.Chassis)
                        .GroupBy(m => (m.MarkingCategory, m.BodyPart))
                        .Select(g => g.First())
                        .GroupBy(m => m.MarkingCategory)
                        .SelectMany(g => points.TryGetValue(g.Key, out var limit) ? g.Take(limit.Points) : g)
                        .ToArray();
                    Assert.That(decorations.Select(m => m.MarkingCategory).Distinct(), Is.SupersetOf(new[]
                    {
                        MarkingCategories.Head, MarkingCategories.HeadTop, MarkingCategories.Snout, MarkingCategories.Chest,
                        MarkingCategories.Arms, MarkingCategories.Legs, MarkingCategories.Tail,
                    }), $"{species.ID} has no marking to pick in a body category.");

                    var other = pickable.First(m => m.MarkingCategory == MarkingCategories.Chassis && !defaults.ContainsValue(m));
                    var part = defaults[other.BodyPart];
                    var cases = new (string Name, MarkingPrototype[] Worn, string[] Kept, string[] Dropped)[]
                    {
                        ("nothing picked", Array.Empty<MarkingPrototype>(), Array.Empty<string>(), Array.Empty<string>()),
                        ("decorations", decorations, decorations.Select(m => m.ID).ToArray(), Array.Empty<string>()),
                        ("missing part", defaults.Values.Where(m => m != part).Concat(decorations).ToArray(),
                            decorations.Select(m => m.ID).Append(part.ID).ToArray(), Array.Empty<string>()),
                        ("two on one part", new[] { other, part }, new[] { other.ID }, new[] { part.ID }),
                        ("swapped part", defaults.Values.Select(m => m == part ? other : m).ToArray(), new[] { other.ID }, new[] { part.ID }),
                    };

                    foreach (var (name, worn, kept, dropped) in cases)
                    {
                        var profile = Wearing(species, worn);
                        profile = profile.WithCharacterAppearance(
                            HumanoidCharacterAppearance.EnsureValid(profile.Appearance, species.ID, profile.Sex));
                        var mob = entMan.Spawn(species.Prototype, map.MapCoords);
                        humanoids.LoadProfile(mob, profile);
                        var set = entMan.GetComponent<HumanoidAppearanceComponent>(mob).MarkingSet;
                        var ids = set.GetForwardEnumerator().Select(m => m.MarkingId).ToList();

                        Assert.That(Parts(set, markings), Is.EqualTo(expected), $"{species.ID}, {name}: wrong chassis.");
                        Assert.That(ids, Is.SupersetOf(kept), $"{species.ID}, {name}: lost a marking.");
                        Assert.That(ids.Intersect(dropped), Is.Empty, $"{species.ID}, {name}: kept a covered part.");
                        entMan.DeleteEntity(mob);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The mob's own sprite has a layer for every layer the species draws on, so none is added on top of the clothes,
    /// and the doll and a body both draw every marking the species may pick, over a whole chassis.
    /// </summary>
    [Test]
    public async Task DrawnTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var markings = client.ResolveDependency<MarkingManager>();

        await client.WaitAssertion(() =>
        {
            var humanoids = entMan.System<HumanoidAppearanceSystem>();
            var sprites = entMan.System<SpriteSystem>();

            Assert.Multiple(() =>
            {
                foreach (var species in ChassisSpecies(proto))
                {
                    var layers = proto.Index<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet).Sprites.Keys;
                    var defaults = Defaults(species, markings, proto).Values.ToList();

                    foreach (var prototype in new[] { species.DollPrototype, species.Prototype })
                    {
                        var fresh = entMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
                        var freshSprite = entMan.GetComponent<SpriteComponent>(fresh);
                        foreach (var layer in layers)
                        {
                            Assert.That(sprites.LayerMapTryGet((fresh, freshSprite), layer, out _, false), Is.True,
                                $"{prototype} has no {layer} layer, so it is added on top of everything.");
                        }

                        entMan.DeleteEntity(fresh);

                        foreach (var marking in Pickable(species, markings, proto))
                        {
                            var mob = entMan.SpawnEntity(prototype, MapCoordinates.Nullspace);
                            humanoids.LoadProfile(mob, Wearing(species, marking));
                            var sprite = entMan.GetComponent<SpriteComponent>(mob);

                            var chassis = defaults.Where(m => marking.MarkingCategory != MarkingCategories.Chassis || m.BodyPart != marking.BodyPart);
                            foreach (var key in LayerKeys(marking).Concat(chassis.SelectMany(LayerKeys)))
                            {
                                var drawn = sprites.LayerMapTryGet((mob, sprite), key, out var index, false) && sprite[index].Visible;
                                Assert.That(drawn, Is.True, $"{prototype} wearing {marking.ID} ({marking.BodyPart}, {marking.MarkingCategory}): {key} is not drawn.");
                            }

                            entMan.DeleteEntity(mob);
                        }
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
