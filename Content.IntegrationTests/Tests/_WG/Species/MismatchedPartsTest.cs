using System.Collections.Generic;
using System.Linq;
using Content.Client.Humanoid;
using Content.Server.Database;
using Content.Shared._WG.Species;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// Mismatched parts: the styles each playable species may pick with the option off and on, validation, the saved column,
/// random profiles, and the hair, facial hair and another species' marking on the bodies and creator dolls of every
/// playable species.
/// </summary>
[TestFixture]
[TestOf(typeof(MismatchedPartsRules))]
public sealed class MismatchedPartsTest
{
    private static readonly MarkingCategories[] Categories = { MarkingCategories.Hair, MarkingCategories.FacialHair };

    /// <summary>Categories searched, in order, for a marking only other species wear.</summary>
    private static readonly MarkingCategories[] ForeignCategories =
    {
        MarkingCategories.Tail, MarkingCategories.HeadTop, MarkingCategories.HeadSide, MarkingCategories.Snout,
    };

    private static readonly Color HairColor = Color.FromHex("#3355FF");
    private static readonly Color BeardColor = Color.FromHex("#FF5533");

    private static List<SpeciesPrototype> Playable(IPrototypeManager proto)
    {
        return proto.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).OrderBy(s => s.ID).ToList();
    }

    private static Sex SexFor(SpeciesPrototype species)
    {
        return species.Sexes.Contains(Sex.Male) ? Sex.Male : species.Sexes.First();
    }

    /// <summary>
    /// A profile with the option set, wearing a style of each hair category (its own if the species wears the category)
    /// and, first in its markings, a marking only other species wear.
    /// </summary>
    private static HumanoidCharacterProfile Profile(
        SpeciesPrototype species,
        bool mismatchedParts,
        MarkingManager markings,
        IPrototypeManager proto,
        out string hair,
        out string beard,
        out string foreign)
    {
        hair = FirstDrawable(MarkingCategories.Hair, species.ID, markings, proto);
        beard = FirstDrawable(MarkingCategories.FacialHair, species.ID, markings, proto);
        foreign = Foreign(species, markings, proto);
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID).WithSex(SexFor(species));
        var worn = new List<Marking> { markings.Markings[foreign].AsMarking() };
        worn.AddRange(profile.Appearance.Markings);
        return profile
            .WithCharacterAppearance(profile.Appearance
                .WithHairStyleName(hair)
                .WithHairColor(HairColor)
                .WithFacialHairStyleName(beard)
                .WithFacialHairColor(BeardColor)
                .WithMarkings(worn))
            .WithMismatchedParts(mismatchedParts);
    }

    /// <summary>
    /// The first style whose first sprite is an RSI state, so its layer can be checked: one of the species' own if it
    /// wears the category, otherwise one the option opens.
    /// </summary>
    internal static string FirstDrawable(MarkingCategories category, string species, MarkingManager markings, IPrototypeManager proto)
    {
        var native = MismatchedPartsRules.IsNative(category, species, markings, proto);
        var style = MismatchedPartsRules.Styles(category, species, !native, markings, proto)
            .OrderBy(p => p.Key)
            .FirstOrDefault(p => p.Value.Sprites.FirstOrDefault() is SpriteSpecifier.Rsi)
            .Key;
        Assert.That(style, Is.Not.Null, $"{species} has no {category} style with an RSI sprite.");
        return style!;
    }

    /// <summary>A marking drawn from RSI states on one layer that only other species wear, for any sex.</summary>
    private static string Foreign(SpeciesPrototype species, MarkingManager markings, IPrototypeManager proto)
    {
        var foreign = ForeignCategories
            .SelectMany(c => markings.MarkingsByCategory(c).Values.OrderBy(m => m.ID))
            .FirstOrDefault(m => m.SpeciesRestrictions != null
                                 && !m.SpeciesRestrictions.Contains(species.ID)
                                 && m.SexRestriction == null
                                 && m.Layering == null
                                 && m.Sprites.Count > 0
                                 && m.Sprites.All(s => s is SpriteSpecifier.Rsi)
                                 && MismatchedPartsRules.Drawable(m, species.ID, proto));
        Assert.That(foreign, Is.Not.Null, $"No marking only other species wear for {species.ID}.");
        return foreign!.ID;
    }

    private static string[] Worn(MarkingSet set, MarkingCategories category)
    {
        return set.TryGetCategory(category, out var list) ? list.Select(m => m.MarkingId).ToArray() : Array.Empty<string>();
    }

    /// <summary>
    /// With the option on every playable species is offered every species' styles of every category; with it off only
    /// its own, and no hair category it can't wear. The option gives one point to a category with none, and drops markings
    /// on body parts the species' sprite lacks.
    /// </summary>
    [Test]
    public async Task RulesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var species in Playable(proto))
                {
                    foreach (var category in Enum.GetValues<MarkingCategories>())
                    {
                        var off = MismatchedPartsRules.Styles(category, species.ID, false, markings, proto);
                        var on = MismatchedPartsRules.Styles(category, species.ID, true, markings, proto);

                        Assert.That(on.Keys, Is.EquivalentTo(markings.MarkingsByCategory(category).Keys),
                            $"{species.ID} is not offered every {category} with the option on.");
                        if (!MismatchedPartsRules.IsHair(category) || MismatchedPartsRules.IsNative(category, species.ID, markings, proto))
                            Assert.That(off.Keys, Is.EquivalentTo(markings.MarkingsByCategoryAndSpecies(category, species.ID).Keys),
                                $"{species.ID} is not offered its own {category} with the option off.");
                        else
                            Assert.That(off, Is.Empty, $"{species.ID} is offered {category} it can't wear.");
                    }

                    foreach (var category in Categories)
                        Assert.That(MismatchedPartsRules.Styles(category, species.ID, true, markings, proto), Is.Not.Empty,
                            $"{species.ID} has no {category} with the option on.");

                    var set = MarkingSet.ForProfile(new List<Marking>(), species.MarkingPoints, true, markings, proto);
                    var closed = proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points
                        .Where(p => p.Value.Points <= 0)
                        .Select(p => p.Key);
                    foreach (var category in closed)
                        Assert.That(set.PointsLeft(category), Is.EqualTo(1), $"The option left {species.ID}'s {category} closed.");

                    // A marking on a body part the species' sprite lacks can't be drawn or picked, so it is dropped.
                    if (markings.Markings.Values.OrderBy(m => m.ID).FirstOrDefault(m => !MismatchedPartsRules.Drawable(m, species.ID, proto)) is { } undrawable)
                    {
                        var withUndrawable = MarkingSet.ForProfile(new List<Marking> { undrawable.AsMarking() }, species.MarkingPoints, true, markings, proto);
                        withUndrawable.EnsureSpecies(species.ID, null, true, markings, proto);
                        Assert.That(withUndrawable.TryGetMarking(undrawable.MarkingCategory, undrawable.ID, out _), Is.False,
                            $"{species.ID} kept {undrawable.ID}, which its sprite can't draw.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Validation keeps unlocked hair only with the option on, copies and the database keep the option, and random
    /// profiles leave it off.
    /// </summary>
    [Test]
    public async Task ProfileTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        HumanoidCharacterProfile on = default!;
        string hair = default!;
        string beard = default!;
        await server.WaitAssertion(() =>
        {
            var species = Playable(proto).FirstOrDefault(s =>
                !MismatchedPartsRules.IsNative(MarkingCategories.Hair, s.ID, markings, proto)
                && !MismatchedPartsRules.IsNative(MarkingCategories.FacialHair, s.ID, markings, proto));
            Assert.That(species, Is.Not.Null, "No playable species lacks both hair and facial hair.");

            var profile = Profile(species!, true, markings, proto, out hair, out beard, out var foreign);
            on = (HumanoidCharacterProfile) profile.Validated(pair.Player!, IoCManager.Instance!);
            var off = (HumanoidCharacterProfile) profile.WithMismatchedParts(false).Validated(pair.Player!, IoCManager.Instance!);

            Assert.Multiple(() =>
            {
                Assert.That(on.MismatchedParts, Is.True);
                Assert.That(on.Appearance.HairStyleId, Is.EqualTo(hair));
                Assert.That(on.Appearance.FacialHairStyleId, Is.EqualTo(beard));
                Assert.That(on.Appearance.Markings.Select(m => m.MarkingId), Does.Contain(foreign),
                    "Validation dropped another species' marking.");
                Assert.That(off.Appearance.Markings.Select(m => m.MarkingId), Does.Not.Contain(foreign),
                    "Validation kept another species' marking without the option.");
                Assert.That(off.Appearance.HairStyleId, Is.EqualTo(HairStyles.DefaultHairStyle), "Validation kept hair the option no longer allows.");
                Assert.That(off.Appearance.FacialHairStyleId, Is.EqualTo(HairStyles.DefaultFacialHairStyle));
                Assert.That(on.Clone().MismatchedParts, Is.True, "Copies drop the option.");
                Assert.That(on.MemberwiseEquals(on.WithMismatchedParts(false)), Is.False);

                foreach (var playable in Playable(proto))
                {
                    Assert.That(HumanoidCharacterProfile.RandomWithSpecies(playable.ID).MismatchedParts, Is.False,
                        $"A random {playable.ID} has the option on.");
                }
            });
        });

        await db.InitPrefsAsync(user, on);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.Multiple(() =>
        {
            Assert.That(loaded.MismatchedParts, Is.True, "The option was not saved.");
            Assert.That(loaded.Appearance.HairStyleId, Is.EqualTo(hair));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A body of every playable species wears the hair, facial hair and another species' marking of a validated profile
    /// with the option on, and one with it off wears none it can't have.
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
            Assert.Multiple(() =>
            {
                foreach (var species in Playable(proto))
                {
                    foreach (var enabled in new[] { true, false })
                    {
                        var profile = Profile(species, enabled, markings, proto, out var hair, out var beard, out var foreign);
                        profile = profile.WithCharacterAppearance(
                            HumanoidCharacterAppearance.EnsureValid(profile.Appearance, species.ID, profile.Sex, enabled));
                        var mob = entMan.Spawn(species.Prototype, map.MapCoords);
                        humanoids.LoadProfile(mob, profile);
                        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);

                        Assert.That(humanoid.MismatchedParts, Is.EqualTo(enabled), $"{species.ID}: option not copied.");
                        foreach (var (category, style) in new[] { (MarkingCategories.Hair, hair), (MarkingCategories.FacialHair, beard) })
                        {
                            var expected = enabled || MismatchedPartsRules.IsNative(category, species.ID, markings, proto)
                                ? new[] { style }
                                : Array.Empty<string>();
                            Assert.That(Worn(humanoid.MarkingSet, category), Is.EqualTo(expected),
                                $"{species.ID} (option {(enabled ? "on" : "off")}) wears the wrong {category}.");
                        }

                        var foreignCategory = markings.Markings[foreign].MarkingCategory;
                        Assert.That(Worn(humanoid.MarkingSet, foreignCategory).Contains(foreign), Is.EqualTo(enabled),
                            $"{species.ID} (option {(enabled ? "on" : "off")}): wrong for {foreign}.");

                        entMan.DeleteEntity(mob);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The creator doll of every playable species draws hair and facial hair in the profile's colours and another
    /// species' marking with the option on, and draws none its species can't wear with it off.
    /// </summary>
    [Test]
    public async Task DollsTest()
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
                foreach (var species in Playable(proto))
                {
                    foreach (var enabled in new[] { true, false })
                    {
                        var profile = Profile(species, enabled, markings, proto, out var hair, out var beard, out var foreign);
                        var doll = entMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
                        humanoids.LoadProfile(doll, profile);
                        var sprite = entMan.GetComponent<SpriteComponent>(doll);

                        foreach (var (category, style, color) in new[]
                                 {
                                     (MarkingCategories.Hair, hair, HairColor),
                                     (MarkingCategories.FacialHair, beard, BeardColor),
                                 })
                        {
                            var native = MismatchedPartsRules.IsNative(category, species.ID, markings, proto);
                            var where = $"{species.ID} doll, option {(enabled ? "on" : "off")}, {style}";
                            var keys = LayerKeys(markings.Markings[style]).ToList();
                            foreach (var key in keys)
                            {
                                var found = sprites.TryGetLayer((doll, sprite), key, out var layer, false);
                                if (!enabled && !native)
                                {
                                    Assert.That(found && layer!.Visible, Is.False, $"{where}: drawn without the option.");
                                    continue;
                                }

                                Assert.That(found, Is.True, $"{where}: no {key} layer.");
                                if (!found)
                                    continue;

                                Assert.That(layer!.Visible, Is.True, $"{where}: {key} is hidden.");
                                if (!native && key == keys[0])
                                    Assert.That(layer.Color, Is.EqualTo(color), $"{where}: {key} lost the profile colour.");
                            }
                        }

                        foreach (var key in LayerKeys(markings.Markings[foreign]))
                        {
                            var drawn = sprites.TryGetLayer((doll, sprite), key, out var foreignLayer, false) && foreignLayer!.Visible;
                            Assert.That(drawn, Is.EqualTo(enabled),
                                $"{species.ID} doll, option {(enabled ? "on" : "off")}: wrong for {key}.");
                        }

                        entMan.DeleteEntity(doll);
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<string> LayerKeys(MarkingPrototype marking)
    {
        foreach (var sprite in marking.Sprites)
        {
            if (sprite is SpriteSpecifier.Rsi rsi)
                yield return $"{marking.ID}-{rsi.RsiState}";
        }
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
    }
}
