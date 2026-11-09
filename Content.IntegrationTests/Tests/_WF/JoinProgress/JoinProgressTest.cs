using System.Linq;
using System.Text;
using Content.Client._WF.JoinProgress;
using Content.Client.Launcher;
using Robust.Client.State;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Utility;
using ServerJoinProgressManager = Content.Server._WF.JoinProgress.JoinProgressManager;

namespace Content.IntegrationTests.Tests._WF.JoinProgress;

/// <summary>The download size reaches a joining client, and the join screen follows the engine's signals.</summary>
[TestFixture]
[TestOf(typeof(ServerJoinProgressManager))]
public sealed class JoinProgressTest
{
    [Test]
    public async Task DownloadSizeReachesAJoiningClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var serverManager = server.ResolveDependency<ServerJoinProgressManager>();
        var clientManager = client.ResolveDependency<JoinProgressManager>();
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var cfg = server.ResolveDependency<IConfigurationManager>();

        Assert.That(clientManager.DownloadInfo, Is.Null, "No uploaded files means no message.");

        await Reconnect();
        Assert.That(clientManager.DownloadInfo, Is.Null, "No uploaded files means no message.");

        var path = new ResPath("a.txt");
        await server.WaitPost(() =>
        {
            serverManager.TrackFiles(new[] { (path, new byte[100]) });
            serverManager.TrackFiles(new[] { (path, new byte[1000]), (new ResPath("bb.txt"), new byte[10]) });
        });

        await Reconnect();

        var expected = 1000 + 10 + Encoding.UTF8.GetByteCount("a.txt") + Encoding.UTF8.GetByteCount("bb.txt") + 2 * 9 + 4;
        var info = clientManager.DownloadInfo;
        Assert.That(info, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(info!.TotalBytes, Is.EqualTo(expected), "A file uploaded again must not count twice.");
            Assert.That(info.FileCount, Is.EqualTo(2));
            Assert.That(info.OverGameConnection, Is.True);
        });

        // The flag follows the channel the engine actually gave this client, not the cvar: the test network has no
        // websocket, so with the fast channel switched on the files still travel over the game connection.
        await server.WaitPost(() => cfg.SetCVar(CVars.TransferHttp, true));
        await Reconnect();
        Assert.That(clientManager.DownloadInfo!.OverGameConnection, Is.True, "no websocket was connected, so the files use the game connection");

        await server.WaitPost(() => cfg.SetCVar(CVars.TransferHttp, false));
        await client.WaitPost(() => clientNet.ClientDisconnect("For testing"));
        await pair.RunTicksSync(10);
        Assert.That(clientManager.DownloadInfo, Is.Null, "A size from the old connection must not outlive it.");

        await client.WaitPost(() => clientNet.ClientConnect(null!, 0, null!));
        await pair.RunTicksSync(10);
        await pair.CleanReturnAsync();

        async Task Reconnect()
        {
            await client.WaitPost(() => clientNet.ClientDisconnect("For testing"));
            await pair.RunTicksSync(10);
            await client.WaitPost(() => clientNet.ClientConnect(null!, 0, null!));
            await pair.RunTicksSync(10);
        }
    }

    [Test]
    public async Task JoinScreenFollowsTheEngineSignals()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var client = pair.Client;
        var clientNet = client.ResolveDependency<IClientNetManager>();
        var stateManager = client.ResolveDependency<IStateManager>();

        await client.WaitPost(() => clientNet.ClientDisconnect("For testing"));
        await pair.RunTicksSync(10);

        JoinProgressTracker tracker = default!;
        await client.WaitPost(() =>
        {
            var state = stateManager.RequestStateChange<LauncherConnecting>();
            tracker = state.JoinTracker;
            clientNet.ClientConnect(null!, 0, null!);
        });
        await pair.RunTicksSync(10);

        // The integration net manager has no connect states, so joining starts here at the net Connected event.
        Assert.That(tracker.Stage, Is.EqualTo(JoinStage.EnteringGame));
        Assert.That(tracker.Timings(TimeSpan.MaxValue).Select(t => t.Stage), Does.Contain(JoinStage.Downloading));

        await pair.CleanReturnAsync();
    }
}
