using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Prototypes;
using Content.Server.Database;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._WG.Prototypes;

/// <summary>Rows holding renamed prototype ids are rewritten in the DB by the load that reads them.</summary>
[TestFixture]
[TestOf(typeof(WGLegacyDbRows))]
public sealed class WGLegacyDbRowsTest
{
    private const string OldGenitals = "{\"v\":1,\"penis\":{\"shape\":\"GenitalShapePenisHuman\",\"lengthCm\":15},\"breasts\":{\"shape\":\"GenitalShapeBreastsPair\",\"cup\":3}}";

    [Test]
    public async Task ProfileRowSavedOnLoadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (db, options) = GetDb(pair.Server);
        var user = new NetUserId(Guid.NewGuid());

        await db.InitPrefsAsync(user, HumanoidCharacterProfile.DefaultWithSpecies("WGCanine"));
        await using (var context = new SqliteServerDbContext(options))
        {
            var row = await context.Profile.SingleAsync(p => p.Preference.UserId == user.UserId);
            row.Species = "Canine";
            row.Genitals = OldGenitals;
            row.Loadouts.Add(new ProfileRoleLoadout
            {
                RoleName = "JobContractor",
                Groups = { new ProfileLoadoutGroup { GroupName = "ContractorGun", Loadouts = { new ProfileLoadout { LoadoutName = "ContractorMosinLoadout" } } } },
            });
            await context.SaveChangesAsync();
        }

        var prefs = await db.GetPlayerPreferencesAsync(user);

        await using (var context = new SqliteServerDbContext(options))
        {
            var row = await context.Profile.AsNoTracking()
                .Include(p => p.Loadouts).ThenInclude(l => l.Groups).ThenInclude(g => g.Loadouts)
                .SingleAsync(p => p.Preference.UserId == user.UserId);
            var loadout = row.Loadouts.SelectMany(l => l.Groups).SelectMany(g => g.Loadouts).Single();

            Assert.Multiple(() =>
            {
                Assert.That(((HumanoidCharacterProfile) prefs!.Characters[0]).Species.Id, Is.EqualTo("WGCanine"));
                Assert.That(row.Species, Is.EqualTo("WGCanine"));
                Assert.That(loadout.LoadoutName, Is.EqualTo("WGContractorMosinLoadout"));
                Assert.That(row.Genitals, Does.Contain("\"WGGenitalShapePenisHuman\"").And.Contain("\"WGGenitalShapeBreastsPair\""));
                Assert.That(row.Genitals, Does.Not.Contain("\"GenitalShape"));
                Assert.That(row.Genitals, Does.Contain("\"lengthCm\":15").And.Contain("\"cup\":3"));
            });
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsentRowSavedOnLoadTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var (db, options) = GetDb(pair.Server);
        var user = new NetUserId(Guid.NewGuid());

        await using (var context = new SqliteServerDbContext(options))
        {
            context.ConsentSettings.Add(new ConsentSettings
            {
                UserId = user.UserId,
                ConsentFreetext = "",
                ConsentFreetextUpdatedAt = DateTime.UtcNow,
                ConsentToggles = new List<ConsentToggle>
                {
                    new() { ToggleProtoId = "AnatomySurgery", ToggleProtoState = "on" },
                    // Both ids stored: the old row goes, the current one is kept.
                    new() { ToggleProtoId = "UndergarmentStrip", ToggleProtoState = "on" },
                    new() { ToggleProtoId = "WGUndergarmentStrip", ToggleProtoState = "off" },
                },
                ReadReceipts = new List<ConsentFreetextReadReceipt>(),
            });
            await context.SaveChangesAsync();
        }

        await db.GetPlayerConsentSettingsAsync(user);

        await using (var context = new SqliteServerDbContext(options))
        {
            var toggles = await context.ConsentSettings.AsNoTracking()
                .Where(c => c.UserId == user.UserId)
                .SelectMany(c => c.ConsentToggles)
                .ToDictionaryAsync(t => t.ToggleProtoId, t => t.ToggleProtoState);

            Assert.That(toggles, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["WGAnatomySurgery"] = "on",
                ["WGUndergarmentStrip"] = "off",
            }));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An in-memory Sqlite database, as in GenitalProfilePersistenceTest, plus its options for direct row access.</summary>
    private static (ServerDbSqlite Db, DbContextOptions<SqliteServerDbContext> Options) GetDb(
        RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        var options = builder.Options;
        var log = server.ResolveDependency<ILogManager>().GetSawmill("db.ops.wg-legacy-rows-test");
        return (new ServerDbSqlite(() => options, true, cfg, true, log), options);
    }
}
