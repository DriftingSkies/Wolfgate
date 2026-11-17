#nullable enable
using System.Linq;
using Content.Server.Database;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ProfileSaves;

/// <summary>
/// Saving a character again one save after another keeps its traits and doesn't break the unique trait index, which
/// leaves overlapping saves as the way that index gets broken.
/// </summary>
[TestFixture]
public sealed class TraitResaveTest
{
    [Test]
    public async Task ResavingKeepsTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        var db = new ServerDbSqlite(() => builder.Options,
            true,
            server.ResolveDependency<IConfigurationManager>(),
            true,
            server.ResolveDependency<ILogManager>().GetSawmill("db.ops"));

        var trait = protoMan.EnumeratePrototypes<TraitPrototype>().First();
        var profile = new HumanoidCharacterProfile().WithTraitPreference(trait.ID, protoMan);
        Assert.That(profile.TraitPreferences, Does.Contain(new ProtoId<TraitPrototype>(trait.ID)));

        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());
        await db.SaveCharacterSlotAsync(user, profile, 0);
        await db.SaveCharacterSlotAsync(user, profile, 0);
        await db.SaveCharacterSlotAsync(user, profile.WithBankBalance(1234), 0);

        var loaded = (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(((HumanoidCharacterProfile) loaded).TraitPreferences, Does.Contain(new ProtoId<TraitPrototype>(trait.ID)));

        await pair.CleanReturnAsync();
    }
}
