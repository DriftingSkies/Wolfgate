#nullable enable
using Content.Client._WG.GameHub;
using Content.Client._WG.JoinQueue;
using Content.Shared.CCVar;
using Robust.Client.State;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using ServerQueue = Content.Server._WG.JoinQueue.JoinQueueManager;

namespace Content.IntegrationTests.Tests._WG.JoinQueue;

/// <summary>A player waiting in the join queue can fetch the Freedoom data its arcade buttons need.</summary>
[TestFixture]
[TestOf(typeof(JoinQueueScreen))]
public sealed class JoinQueueArcadeTest
{
    [Test]
    public async Task QueuedPlayerReachesTheGameData()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var states = client.ResolveDependency<IStateManager>();
        var data = client.ResolveDependency<WGGameDataManager>();

        var cap = cfg.GetCVar(CCVars.SoftMaxPlayers);
        var adminsCount = cfg.GetCVar(CCVars.AdminsCountForMaxPlayers);
        var bypass = cfg.GetCVar(CCVars.AdminBypassMaxPlayers);
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SoftMaxPlayers, 0);
            cfg.SetCVar(CCVars.AdminsCountForMaxPlayers, true);
            cfg.SetCVar(CCVars.AdminBypassMaxPlayers, false);
        });

        var screen = states.CurrentState.GetType();
        client.SetConnectTarget(server);
        await client.WaitPost(() => client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, null!));
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() => Assert.That(server.ResolveDependency<ServerQueue>().WaitingCount, Is.EqualTo(1)));
        await client.WaitAssertion(() => Assert.That(states.CurrentState, Is.TypeOf<JoinQueueState>()));

        await client.WaitPost(() => client.System<WGGameHubSystem>().RequestManifest(WGGameHubSystem.DoomGame));
        // The server reads the data's size and version on a worker thread.
        for (var i = 0; i < 20 && data.GetStatus(WGGameHubSystem.DoomGame) == WGGameDataStatus.Unknown; i++)
        {
            await pair.RunTicksSync(10);
        }

        Assert.That(data.GetStatus(WGGameHubSystem.DoomGame), Is.Not.EqualTo(WGGameDataStatus.Unknown),
            "the server never answered a queued player's game data request");

        // Leave the queue as it was for the next test.
        await server.WaitPost(() => cfg.SetCVar(CCVars.SoftMaxPlayers, 30));
        await pair.RunTicksSync(30);
        await client.WaitPost(() => states.RequestStateChange(screen));
        await server.WaitPost(() =>
        {
            cfg.SetCVar(CCVars.SoftMaxPlayers, cap);
            cfg.SetCVar(CCVars.AdminsCountForMaxPlayers, adminsCount);
            cfg.SetCVar(CCVars.AdminBypassMaxPlayers, bypass);
        });
        await pair.CleanReturnAsync();
    }
}
