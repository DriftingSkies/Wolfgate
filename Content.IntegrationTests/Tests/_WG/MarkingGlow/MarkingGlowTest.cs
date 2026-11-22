using System.Collections.Generic;
using System.Linq;
using Content.Client.Humanoid;
using Content.Server.Database;
using Content.Shared._WG.MarkingGlow;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.MarkingGlow;

/// <summary>Markings, eyes and hair a profile makes glow are drawn unshaded, and nothing else is.</summary>
[TestFixture]
[TestOf(typeof(MarkingGlowRules))]
public sealed class MarkingGlowTest
{
    private const string Tattoo = "TattooHiveChest";
    private const string TattooLayer = "TattooHiveChest-tattoo_hive_chest";
    private const string Hair = "HumanHairAfro";
    private const string HairLayer = "HumanHairAfro-afro";

    private static HumanoidCharacterProfile Profile()
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
        return profile.WithCharacterAppearance(profile.Appearance
            .WithHairStyleName(Hair)
            .WithMarkings(new List<Marking> { new(Tattoo, new List<Color> { Color.Red }) }));
    }

    [Test]
    public async Task ProfileAndBodyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var collection = server.ResolveDependency<IDependencyCollection>();

        await server.WaitAssertion(() =>
        {
            var profile = Profile()
                .WithMarkingGlow(Tattoo, true)
                .WithMarkingGlow("SomeoneElsesMarking", true)
                .WithMarkingGlow(MarkingGlowRules.Eyes, true);
            Assert.That(profile.MemberwiseEquals(profile.WithMarkingGlow(MarkingGlowRules.Eyes, false)), Is.False,
                "glow is part of the profile");

            var session = server.PlayerMan.Sessions.First();
            var valid = profile.Clone();
            valid.EnsureValid(session, collection);
            Assert.That(valid.MarkingGlow, Is.EqualTo(new[] { MarkingGlowRules.Eyes, Tattoo }),
                "glow for a marking the character doesn't wear is dropped");

            var mob = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
            server.EntMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, valid);
            Assert.That(server.EntMan.GetComponent<HumanoidAppearanceComponent>(mob).MarkingGlow, Is.EqualTo(valid.MarkingGlow));
            server.EntMan.DeleteEntity(mob);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DatabaseRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");

        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        var db = new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);

        var user = new NetUserId(Guid.NewGuid());
        var profile = Profile().WithMarkingGlow(Tattoo, true).WithMarkingGlow(MarkingGlowRules.FacialHair, true);
        await db.InitPrefsAsync(user, profile);
        await db.SaveCharacterSlotAsync(user, Profile(), 1);

        var prefs = await db.GetPlayerPreferencesAsync(user);
        Assert.Multiple(() =>
        {
            Assert.That(((HumanoidCharacterProfile) prefs!.Characters[0]).MarkingGlow,
                Is.EqualTo(new[] { MarkingGlowRules.FacialHair, Tattoo }));
            Assert.That(((HumanoidCharacterProfile) prefs.Characters[1]).MarkingGlow, Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GlowDrawsUnshadedTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var glowing = Profile()
            .WithMarkingGlow(Tattoo, true)
            .WithMarkingGlow(MarkingGlowRules.Hair, true)
            .WithMarkingGlow(MarkingGlowRules.Eyes, true);
        EntityUid doll = default;

        await client.WaitAssertion(() =>
        {
            var species = client.ResolveDependency<IPrototypeManager>().Index<SpeciesPrototype>(glowing.Species);
            doll = client.EntMan.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
            client.EntMan.System<HumanoidAppearanceSystem>().LoadProfile(doll, glowing);
            AssertGlow(client.EntMan, doll, true, "glowing");

            client.EntMan.System<HumanoidAppearanceSystem>().LoadProfile(doll, Profile());
            AssertGlow(client.EntMan, doll, false, "after glow is taken off");
        });

        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(MarkingGlowCVars.Enabled, false));
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            client.EntMan.System<HumanoidAppearanceSystem>().LoadProfile(doll, glowing);
            AssertGlow(client.EntMan, doll, false, "with the feature off");
            client.EntMan.DeleteEntity(doll);
        });

        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(MarkingGlowCVars.Enabled, true));
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();
    }

    private static void AssertGlow(IEntityManager entMan, EntityUid doll, bool glowing, string when)
    {
        var sprites = entMan.System<SpriteSystem>();
        var sprite = new Entity<SpriteComponent>(doll, entMan.GetComponent<SpriteComponent>(doll));
        var expected = glowing ? (ProtoId<ShaderPrototype>?) SpriteSystem.UnshadedId : null;

        Assert.That(sprites.LayerMapTryGet(sprite, TattooLayer, out var tattoo, false), Is.True);
        Assert.That(sprites.LayerMapTryGet(sprite, HairLayer, out var hair, false), Is.True);
        Assert.That(sprites.LayerMapTryGet(sprite, HumanoidVisualLayers.Eyes, out var eyes, false), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(((SpriteComponent.Layer) sprite.Comp![tattoo]).ShaderPrototype, Is.EqualTo(expected), $"the marking, {when}");
            Assert.That(((SpriteComponent.Layer) sprite.Comp[hair]).ShaderPrototype, Is.EqualTo(expected), $"the hair, {when}");
            Assert.That(((SpriteComponent.Layer) sprite.Comp[eyes]).ShaderPrototype, Is.EqualTo(expected), $"the eyes, {when}");
        });
    }
}
