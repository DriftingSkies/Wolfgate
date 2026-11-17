#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.LiveFixes;
using Content.Server.Database;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Traits;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.LiveFixes;

/// <summary>Saving a character must not break the unique job and trait indexes.</summary>
[TestFixture]
public sealed class CharacterSaveTest
{
    private static ServerDbSqlite CreateDb(Pair.TestPair pair, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        if (interceptor != null)
            builder.AddInterceptors(interceptor);

        return new ServerDbSqlite(() => builder.Options,
            true,
            pair.Server.ResolveDependency<IConfigurationManager>(),
            true,
            pair.Server.ResolveDependency<ILogManager>().GetSawmill("db.ops"));
    }

    [Test]
    public async Task MovingHighPriorityToAnotherJobSaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = CreateDb(pair);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        var jobs = pair.Server.ProtoMan.EnumeratePrototypes<JobPrototype>().Take(2).Select(j => new ProtoId<JobPrototype>(j.ID)).ToArray();
        var first = new HumanoidCharacterProfile().WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { jobs[0], JobPriority.High },
            { jobs[1], JobPriority.Low },
        });
        var second = first.WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { jobs[0], JobPriority.Low },
            { jobs[1], JobPriority.High },
        });

        await db.SaveCharacterSlotAsync(user, first, 0);
        await db.SaveCharacterSlotAsync(user, second, 0);

        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.JobPriorities[jobs[1]], Is.EqualTo(JobPriority.High));
        await pair.CleanReturnAsync();
    }

    [Test]
    public void TwoHighPriorityJobsSaveAsOne()
    {
        // Straight to the rows, since WithJobPriorities would already have demoted the second one.
        var rows = WGProfileRows.Jobs(new Dictionary<ProtoId<JobPrototype>, JobPriority>
        {
            { "WGTestJobA", JobPriority.High },
            { "WGTestJobB", JobPriority.High },
            { "WGTestJobC", JobPriority.Never },
        });

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows.Single(r => r.JobName == "WGTestJobA").Priority, Is.EqualTo(DbJobPriority.High));
        Assert.That(rows.Single(r => r.JobName == "WGTestJobB").Priority, Is.EqualTo(DbJobPriority.Medium));
    }

    [Test]
    public async Task CollidingSaveRetries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var collision = new FailOnce();
        var db = CreateDb(pair, collision);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        collision.Armed = true;
        await db.SaveCharacterSlotAsync(user, new HumanoidCharacterProfile().WithBankBalance(1234), 0);

        Assert.That(collision.Armed, Is.False, "The first save should have collided.");
        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.BankBalance, Is.EqualTo(1234));
        await pair.CleanReturnAsync();
    }

    /// <summary>Fails the next save the way a collision with another save would.</summary>
    private sealed class FailOnce : SaveChangesInterceptor
    {
        public bool Armed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed)
                return base.SavingChangesAsync(eventData, result, cancellationToken);

            Armed = false;
            throw new DbUpdateException("Simulated collision");
        }
    }

    [Test]
    public async Task ResavingKeepsTraits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var db = CreateDb(pair);
        var user = new NetUserId(Guid.NewGuid());
        await db.InitPrefsAsync(user, new HumanoidCharacterProfile());

        var protoMan = pair.Server.ProtoMan;
        var trait = protoMan.EnumeratePrototypes<TraitPrototype>().First();
        var profile = new HumanoidCharacterProfile().WithTraitPreference(trait.ID, protoMan);

        await db.SaveCharacterSlotAsync(user, profile, 0);
        await db.SaveCharacterSlotAsync(user, profile, 0);

        var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
        Assert.That(loaded.TraitPreferences, Does.Contain(new ProtoId<TraitPrototype>(trait.ID)));
        await pair.CleanReturnAsync();
    }
}
