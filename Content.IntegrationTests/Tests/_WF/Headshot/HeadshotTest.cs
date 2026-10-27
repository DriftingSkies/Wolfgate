using System.Linq;
using Content.Server._WF.Headshot;
using Content.Server.Database;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WF.Headshot;

/// <summary>The headshot URL through the DB and validation, and which clothing hides the face.</summary>
[TestFixture]
[TestOf(typeof(HeadshotSystem))]
public sealed class HeadshotTest
{
    private const string Url = "https://example.com/headshot.png";

    [Test]
    public async Task ProfileRoundTripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var db = GetDb(server);
        var user = new NetUserId(Guid.NewGuid());

        HumanoidCharacterProfile valid = default!;
        HumanoidCharacterProfile invalid = default!;
        await server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            valid = (HumanoidCharacterProfile) profile.WithHeadshotUrl($"  {Url} ").Validated(pair.Player!, IoCManager.Instance!);
            invalid = (HumanoidCharacterProfile) profile.WithHeadshotUrl("http://127.0.0.1/a.png").Validated(pair.Player!, IoCManager.Instance!);
        });

        Assert.Multiple(() =>
        {
            Assert.That(valid.HeadshotUrl, Is.EqualTo(Url), "validation trims a good URL");
            Assert.That(invalid.HeadshotUrl, Is.Empty, "validation drops a non-https URL");
            Assert.That(valid.Clone().HeadshotUrl, Is.EqualTo(Url), "copies keep the URL");
            Assert.That(valid.MemberwiseEquals(valid.WithHeadshotUrl(string.Empty)), Is.False);
        });

        await db.InitPrefsAsync(user, valid);
        var prefs = await db.GetPlayerPreferencesAsync(user);
        var loaded = (HumanoidCharacterProfile) prefs!.Characters.Single().Value;
        Assert.That(loaded.HeadshotUrl, Is.EqualTo(Url));

        await pair.CleanReturnAsync();
    }

    /// <summary>Only clothing that hides the identity hides the headshot; eyes or mouth alone don't.</summary>
    [Test]
    public async Task FaceVisibilityTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var headshot = entMan.System<HeadshotSystem>();
            var inventory = entMan.System<InventorySystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);

            Assert.That(headshot.IsFaceVisible(mob), Is.True, "bare face");

            var glasses = entMan.SpawnEntity("ClothingEyesGlassesSunglasses", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, glasses, "eyes", silent: true, force: true));
            Assert.That(headshot.IsFaceVisible(mob), Is.True, "sunglasses cover only the eyes");

            var mask = entMan.SpawnEntity("ClothingMaskGas", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, mask, "mask", silent: true, force: true));
            Assert.That(headshot.IsFaceVisible(mob), Is.False, "a gas mask hides the face");

            Assert.That(inventory.TryUnequip(mob, "mask", silent: true, force: true));
            Assert.That(headshot.IsFaceVisible(mob), Is.True, "mask removed");
        });

        await pair.CleanReturnAsync();
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

    /// <summary>A long URL scrolls inside the box; the window keeps its width.</summary>
    [Test]
    public async Task WindowKeepsWidthTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var url = "not-a-link-" + new string('a', 400);
            var window = new Content.Client._WF.Headshot.HeadshotWindow(url);
            window.Measure(new System.Numerics.Vector2(4000, 4000));
            Assert.That(window.DesiredSize.X, Is.LessThan(560f));
            window.Close();
        });
        await pair.CleanReturnAsync();
    }
}
