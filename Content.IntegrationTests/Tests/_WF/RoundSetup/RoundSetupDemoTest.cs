using System.Linq;
using Content.Client._WF.RoundSetup;
using Content.IntegrationTests.Pair;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.RoundSetup;
using Content.Shared.CCVar;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>The local demo requires explicit opt-in and a real, loaded, matching host.</summary>
[TestFixture]
public sealed class RoundSetupDemoTest
{
    /// <summary>Defaults remain empty; an enabled matching host gets one ready public lobby with three openings.</summary>
    [Test]
    public async Task DemoRequiresOptInAndMatchingHostAndSeedsOnce()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var previousPlanets = false;
        var previousHost = string.Empty;
        await server.WaitAssertion(() =>
        {
            Assert.That(server.CfgMan.GetCVar(WFRoundSetupCVars.DemoEnabled), Is.False);
            Assert.That(server.CfgMan.GetCVar(CCVars.GameLobbyDuration), Is.EqualTo(360));
            Assert.That(ticker.LobbyDuration, Is.EqualTo(TimeSpan.FromMinutes(6)));
            previousPlanets = server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            previousHost = server.CfgMan.GetCVar(WFRoundSetupCVars.DemoHost);
            server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
            server.CfgMan.SetCVar(WFRoundSetupCVars.DemoHost, pair.Player!.Name);
        });
        try
        {
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies, Is.Empty, "An ordinary server must never create a demo lobby.");
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WFRoundSetupCVars.DemoHost, "WFAbsentDemoHost");
                server.CfgMan.SetCVar(WFRoundSetupCVars.DemoEnabled, true);
            });
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies, Is.Empty, "A configured name cannot stand in for a connected player.");

            await server.WaitPost(() => server.CfgMan.SetCVar(WFRoundSetupCVars.DemoHost, pair.Player!.Name));
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies.Length, Is.EqualTo(1));
            var lobby = state.Lobbies.Single();
            Assert.Multiple(() =>
            {
                Assert.That(state.OwnLobby, Is.EqualTo(lobby.Id));
                Assert.That(lobby.Scenario, Is.EqualTo(WFRoundScenario.Crashlanded));
                Assert.That(lobby.Access, Is.EqualTo(WFLobbyAccess.Public));
                Assert.That(lobby.AllowLateJoin, Is.True);
                Assert.That(lobby.Started, Is.False);
                Assert.That(lobby.Host, Is.EqualTo(pair.Player!.UserId));
                Assert.That(lobby.Members.Single().Job, Is.EqualTo("Contractor"));
                Assert.That(lobby.Members.Single().RoleId, Is.Null, "The host keeps a personal job selection.");
                Assert.That(lobby.Members.Single().Ready, Is.True);
                Assert.That(lobby.Roles.Single().Job, Is.EqualTo("Contractor"));
                Assert.That(lobby.Roles.Single().Name, Is.Empty);
                Assert.That(lobby.Roles.Single().Capacity, Is.EqualTo(3));
                Assert.That(lobby.Roles.Single().Occupied, Is.Zero, "All three advertised slots are available to guests.");
            });
            await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobby.Id });
            await pair.RunTicksSync(pair.SecondsToTicks(2.5f));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(state.Lobbies, Is.Empty, "Closing the fixture must not create a new lobby every poll.");
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(WFRoundSetupCVars.DemoEnabled, false);
                server.CfgMan.SetCVar(WFRoundSetupCVars.DemoHost, previousHost);
                server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, previousPlanets);
                ticker.RestartRound();
            });
            await pair.RunTicksSync(2);
        }
        await pair.CleanReturnAsync();
    }

    private static async Task<WFRoundSetupState> Request(TestPair pair, WFRoundSetupRequest request)
    {
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        var clientSystem = pair.Client.System<WFRoundSetupClientSystem>();
        await pair.Client.WaitPost(() => clientSystem.Send(request));
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        WFRoundSetupState state = null!;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(clientSystem.State, Is.Not.Null);
            state = clientSystem.State!;
        });
        return state;
    }
}
