#nullable enable
using System.Linq;
using System.Threading;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Preferences.Managers;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.Performance;

/// <summary>The bank's lobby refresh isn't awaited, so a refresh for a player with no play time data must not throw.</summary>
[TestFixture]
public sealed class WGPreferencesRefreshTest
{
    [Test]
    public async Task RefreshWithoutPlayTimeDoesNotThrow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var playTime = server.ResolveDependency<PlayTimeTrackingManager>();
        var session = server.ResolveDependency<ISharedPlayerManager>().Sessions.First();

        Task refresh = default!;
        await server.WaitPost(() =>
        {
            playTime.ClientDisconnected(session);
            refresh = prefs.RefreshPreferencesAsync(session, CancellationToken.None);
        });
        await RunUntilDone(pair, refresh);
        Assert.That(refresh.IsFaulted, Is.False, () => $"The refresh threw: {refresh.Exception}");

        // The pair goes back to the pool, and its next save needs this data.
        Task load = default!;
        await server.WaitPost(() => load = playTime.LoadData(session, CancellationToken.None));
        await RunUntilDone(pair, load);

        await pair.CleanReturnAsync();
    }

    private static async Task RunUntilDone(Pair.TestPair pair, Task task)
    {
        for (var i = 0; i < 300 && !task.IsCompleted; i++)
        {
            await pair.Server.WaitRunTicks(1);
        }

        Assert.That(task.IsCompleted, "The database task never finished.");
    }
}
