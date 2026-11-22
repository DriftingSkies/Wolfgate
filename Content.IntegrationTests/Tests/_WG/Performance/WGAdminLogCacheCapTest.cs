using Content.Server.Administration.Logs;
using Content.Server.GameTicking;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>
/// A round past <see cref="WGAdminLogCacheCVars.CacheMax"/> drops its in-memory cache, and its searches still find
/// every log, from the database.
/// </summary>
[TestFixture]
[TestOf(typeof(AdminLogManager))]
public sealed class WGAdminLogCacheCapTest
{
    [Test]
    public async Task SearchesFindEveryLogPastTheCap()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            AdminLogsEnabled = true,
            DummyTicker = false,
            Connected = true,
            Dirty = true,
        });
        var server = pair.Server;
        var logs = server.ResolveDependency<IAdminLogManager>();
        var manager = (AdminLogManager) logs;
        var round = server.System<GameTicker>().RoundId;
        var tag = Guid.NewGuid().ToString();
        const int count = 30;

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(WGAdminLogCacheCVars.CacheMax, 10);
            for (var i = 0; i < count; i++)
            {
                logs.Add(LogType.Unknown, $"cache cap test {tag} {i}");
            }
        });

        await PoolManager.WaitUntil(server, async () =>
        {
            var found = await logs.CurrentRoundLogs(new LogFilter { Search = tag });
            return found.Count >= count;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(manager.WGIsRoundCached(round), Is.False, "the round's cache should have been dropped at the cap.");
            server.CfgMan.SetCVar(WGAdminLogCacheCVars.CacheMax, 250_000);
        });

        await pair.CleanReturnAsync();
    }
}
