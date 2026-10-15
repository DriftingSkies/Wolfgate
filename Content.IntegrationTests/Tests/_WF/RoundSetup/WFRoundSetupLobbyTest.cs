using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.RoundSetup;
using Content.IntegrationTests.Pair;
using Content.Server._NF.SectorServices;
using Content.Server._WF.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.RoundSetup;
using Content.Shared.Access.Systems;
using Content.Shared.Access.Components;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Power;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

/// <summary>Exercises authenticated lobby operations through the real client/server protocol.</summary>
[TestFixture]
[TestOf(typeof(WFRoundSetupSystem))]
public sealed class WFRoundSetupLobbyTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFRoundSetupQuotaTestStation
  parent: TestStation
  components:
  - type: StationJobs
    availableJobs:
      Contractor: [1, 1]
";

    /// <summary>Creation defaults to requests, host credentials rotate and malformed changes preserve state.</summary>
    [Test]
    public async Task PrivateLobbyConfigurationAndCredentials()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var player = pair.Client.User!.Value;
        var originalNetworks = false;
        await pair.Server.WaitPost(() =>
        {
            originalNetworks = pair.Server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, false);
        });
        var initial = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(initial.CanJoin, Is.True);
        Assert.That(initial.OwnLobby, Is.Null);
        Assert.That(initial.CrashlandedAvailable, Is.False);
        var unavailable = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Crashlanded,
        });
        Assert.That(unavailable.OwnLobby, Is.Null, "Crash starts require the planet network feature.");
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true));

        var invalid = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = (WFRoundScenario) byte.MaxValue,
        });
        Assert.That(invalid.OwnLobby, Is.Null, "Unknown scenarios must never create a lobby.");

        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Crashlanded,
            Access = WFLobbyAccess.Public,
            AllowLateJoin = true,
        });
        Assert.That(state.CrashlandedAvailable, Is.True);
        var lobby = OwnLobby(state);
        var lobbyId = lobby.Id;
        Assert.Multiple(() =>
        {
            Assert.That(lobby.Host, Is.EqualTo(player));
            Assert.That(lobby.Scenario, Is.EqualTo(WFRoundScenario.Crashlanded));
            Assert.That(lobby.Access, Is.EqualTo(WFLobbyAccess.Requests));
            Assert.That(lobby.AllowLateJoin, Is.False);
            Assert.That(lobby.Started, Is.False);
            Assert.That(lobby.Code, Is.Null);
            Assert.That(lobby.MemberCount, Is.EqualTo(1));
            Assert.That(lobby.Members.Single().UserId, Is.EqualTo(player));
            Assert.That(lobby.Members.Single().Connected, Is.True);
            Assert.That(lobby.Members.Single().Spawned, Is.False);
            Assert.That(lobby.Members.Single().Ready, Is.False);
            Assert.That(lobby.Requests, Is.Empty);
            Assert.That(lobby.Roles, Is.Empty);
            Assert.That(lobby.Members.Single().Job, Is.Null);
            Assert.That(lobby.Members.Single().RoleId, Is.Null);
            Assert.That(state.InviteTargets.Any(target => target.UserId == player), Is.False);
        });

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = lobbyId + 1,
            Access = WFLobbyAccess.Public,
        });
        Assert.That(OwnLobby(state).Access, Is.EqualTo(WFLobbyAccess.Requests), "Host operations require the owned lobby ID.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = lobbyId,
            Access = (WFLobbyAccess) byte.MaxValue,
            AllowLateJoin = true,
        });
        Assert.That(OwnLobby(state).Access, Is.EqualTo(WFLobbyAccess.Requests));
        Assert.That(OwnLobby(state).AllowLateJoin, Is.False, "Malformed configuration must apply no fields.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = lobbyId,
            Access = WFLobbyAccess.Code,
            AllowLateJoin = true,
        });
        var originalCode = OwnLobby(state).Code;
        Assert.That(OwnLobby(state).AllowLateJoin, Is.True);
        Assert.That(WFRoundSetupPolicy.TryNormalizeCode(originalCode, out var normalized), Is.True);
        Assert.That(normalized, Is.EqualTo(originalCode));

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.RegenerateCode) { LobbyId = lobbyId });
        var replacementCode = OwnLobby(state).Code;
        Assert.That(replacementCode, Is.Not.EqualTo(originalCode));
        Assert.That(WFRoundSetupPolicy.TryNormalizeCode(replacementCode, out _), Is.True);

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = lobbyId,
            Access = WFLobbyAccess.Closed,
        });
        Assert.That(OwnLobby(state).Code, Is.Null, "A code is only returned while code admission is active.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = lobbyId,
            Access = WFLobbyAccess.Code,
        });
        Assert.That(OwnLobby(state).Code, Is.Not.EqualTo(replacementCode), "Reopening code admission revokes old credentials.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.TransferHost)
        {
            LobbyId = lobbyId,
            Target = new NetUserId(Guid.NewGuid()),
        });
        Assert.That(OwnLobby(state).Host, Is.EqualTo(player), "Ownership cannot transfer to a nonmember.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        Assert.That(state.OwnLobby, Is.EqualTo(lobbyId), "A player cannot own two lobbies.");
        Assert.That(state.Lobbies.Count(entry => entry.Host == player), Is.EqualTo(1));

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Launch) { LobbyId = lobbyId });
        Assert.That(OwnLobby(state).Started, Is.False, "Launch cannot bypass the pregame countdown.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        Assert.That(state.OwnLobby, Is.Null);
        Assert.That(state.Lobbies.Any(entry => entry.Id == lobbyId), Is.False);
        await pair.Server.WaitAssertion(() => Assert.That(pair.Server.System<WFCrashScenarioSystem>().HasScenario(lobbyId), Is.False));
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }

    /// <summary>Scenario jobs are offered by the server and readiness follows validated personal choices.</summary>
    [Test]
    public async Task JobsAndReadinessUseValidatedScenarioChoices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var ticker = pair.Server.System<GameTicker>();
        var player = pair.Client.User!.Value;
        var originalNetworks = false;
        await pair.Server.WaitPost(() =>
        {
            originalNetworks = pair.Server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
        });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Normal });
        Assert.That(state.OwnLobby, Is.Null, "Normal joining must never create a scenario crew.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Crashlanded });
        var lobbyId = OwnLobby(state).Id;
        Assert.That(state.Jobs.Select(job => job.Id), Does.Contain("Contractor"));
        Assert.That(state.Jobs.All(job => WFCrashScenarioSystem.AllowedJobs.Contains(job.Id)), Is.True);
        Assert.That(state.Jobs.All(job => job.Station == null && job.StationName == string.Empty), Is.True);
        var job = state.Jobs.First(choice => choice.Id == "Contractor");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId,
            Job = job.Id,
            Station = job.Station,
        });
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo(job.Id));

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId,
            Job = "WFNotAnExistingJob",
        });
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo(job.Id), "Forged jobs cannot replace a valid selection.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId,
            Job = job.Id,
            Station = new NetEntity(int.MaxValue),
        });
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo(job.Id), "Crash jobs cannot target a client-selected station.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
        await pair.Server.WaitAssertion(() => Assert.That(ticker.PlayerGameStatuses[player], Is.EqualTo(PlayerGameStatus.ReadyToPlay)));

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId,
            Job = job.Id,
            Station = job.Station,
        });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False, "Changing a job requires confirming readiness again.");
        await pair.Server.WaitAssertion(() => Assert.That(ticker.PlayerGameStatuses[player], Is.EqualTo(PlayerGameStatus.NotReadyToPlay)));

        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        Assert.That(state.OwnLobby, Is.Null);
        await pair.Server.WaitAssertion(() => Assert.That(ticker.PlayerGameStatuses[player], Is.EqualTo(PlayerGameStatus.NotReadyToPlay)));
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }

    /// <summary>Hosts choose their own job and explicitly create bounded crew openings without reserving them.</summary>
    [Test]
    public async Task RoleReservationsRejectInvalidChanges()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var originalNetworks = false;
        await pair.Server.WaitPost(() =>
        {
            originalNetworks = pair.Server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
        });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Crashlanded, Job = "Contractor", RoleId = 1, Capacity = 16,
        });
        var lobby = OwnLobby(state);
        var lobbyId = lobby.Id;
        Assert.Multiple(() =>
        {
            Assert.That(lobby.Roles, Is.Empty, "A new lobby advertises no crew openings.");
            Assert.That(lobby.MemberCount, Is.EqualTo(1));
            Assert.That(lobby.Members.Single().Job, Is.Null, "Creating a lobby cannot implicitly select a job.");
            Assert.That(lobby.Members.Single().RoleId, Is.Null);
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False, "A host must choose an eligible personal job first.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId, Job = "Contractor",
        });
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo("Contractor"));
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.Null);
        Assert.That(OwnLobby(state).Roles, Is.Empty, "The host's own job must not become a public crew opening.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True, "A solo host can ready up without creating crew roles.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleName = "  Salvage  Crew  ", Job = "Contractor",
        });
        var opening = OwnLobby(state).Roles.Single();
        Assert.Multiple(() =>
        {
            Assert.That(opening.Name, Is.EqualTo("Salvage Crew"));
            Assert.That(opening.Capacity, Is.EqualTo(1), "New role requests default to one crew opening.");
            Assert.That(opening.Occupied, Is.Zero, "The host's personal job does not consume a matching crew opening.");
            Assert.That(opening.CanSelect, Is.True);
            Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
        });
        foreach (var capacity in new[] { 0, WFRoundSetupPolicy.MaximumMembers + 1 })
        {
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = lobbyId, RoleId = opening.Id, RoleName = opening.Name, Job = opening.Job, Capacity = capacity,
            });
            Assert.That(OwnLobby(state).Roles.Single().Capacity, Is.EqualTo(1));
        }
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id, RoleName = "[color=red]Crew[/color]", Job = opening.Job, Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Single().Name, Is.EqualTo("Salvage Crew"));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleName = "Command", Job = "StationRepresentative", Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Length, Is.EqualTo(1), "Hosts cannot offer jobs absent from the scenario.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleId = int.MaxValue, RoleName = "Unknown", Job = opening.Job, Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Length, Is.EqualTo(1), "Unknown IDs cannot create or overwrite roles.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectRole)
        {
            LobbyId = lobbyId, RoleId = int.MaxValue,
        });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.Null);
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.RemoveRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id,
        });
        Assert.That(OwnLobby(state).Roles, Is.Empty, "The last empty crew role may be removed.");
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo("Contractor"));
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleName = "Scavenger", Job = "Contractor", Capacity = 1,
        });
        opening = OwnLobby(state).Roles.Single();
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id,
        });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.EqualTo(opening.Id));
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False);
        Assert.That(OwnLobby(state).Roles.Single().Occupied, Is.EqualTo(1));
        Assert.That(OwnLobby(state).Roles.Single().CanSelect, Is.True, "An existing reservation remains valid when its role is full.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectRole) { LobbyId = lobbyId });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.EqualTo(opening.Id), "An omitted role ID cannot release a reservation.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id, RoleName = opening.Name, Job = "Passenger", Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Single().Job, Is.EqualTo("Contractor"), "Occupied roles cannot change their members' jobs.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.RemoveRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id,
        });
        Assert.That(OwnLobby(state).Roles.Single().Id, Is.EqualTo(opening.Id), "Occupied roles cannot be removed.");

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId + 1, Job = "Contractor",
        });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.EqualTo(opening.Id), "Stale controls cannot change the current group.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId + 1 });
        Assert.That(state.OwnLobby, Is.EqualTo(lobbyId));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId, Job = "Contractor",
        });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.Null, "A host may return to a personal job without consuming a crew slot.");
        Assert.That(OwnLobby(state).Roles.Single().Occupied, Is.Zero);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.RemoveRole)
        {
            LobbyId = lobbyId, RoleId = opening.Id,
        });
        Assert.That(OwnLobby(state).Roles, Is.Empty);
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, originalNetworks));
        await pair.CleanReturnAsync();
    }
    /// <summary>Opening Ship setup does not pick or buy a vessel and each selection requires fresh readiness.</summary>
    [Test]
    public async Task ShipChoiceRequiresExplicitReadyWithoutChargingInPregame()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship,
            Vessel = "Guppy",
        });
        var lobbyId = OwnLobby(state).Id;
        Assert.That(OwnLobby(state).Vessel, Is.Null, "Creation cannot select a ship or imply purchase consent.");
        Assert.That(state.HostBalance, Is.Not.Null);
        var balance = state.HostBalance;
        Assert.That(state.Vessels, Is.Not.Empty);
        var vessel = state.Vessels[0];

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False, "The paying host must select a vessel first.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel)
        {
            LobbyId = lobbyId, Vessel = "WFNotAnOfferedVessel",
        });
        Assert.That(OwnLobby(state).Vessel, Is.Null);

        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel)
        {
            LobbyId = lobbyId, Vessel = vessel.Id,
        });
        Assert.That(OwnLobby(state).Vessel, Is.EqualTo(vessel.Id));
        Assert.That(state.HostBalance, Is.EqualTo(balance));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
        {
            LobbyId = lobbyId, Job = "Contractor",
        });
        Assert.That(OwnLobby(state).Roles, Is.Empty);
        await pair.Server.WaitPost(() => pair.Server.System<GameTicker>().ToggleReady(pair.Player!, true));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False, "The global Ready button cannot imply ship-purchase consent.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SetReady) { LobbyId = lobbyId, Ready = true });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
        Assert.That(state.HostBalance, Is.EqualTo(balance), "Readying up must not charge before the scenario launches.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel)
        {
            LobbyId = lobbyId, Vessel = vessel.Id,
        });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False, "Selecting the ship again invalidates previous confirmation.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = lobbyId });
        Assert.That(state.HostBalance, Is.Null);
        Assert.That(state.Vessels, Is.Empty);
        await pair.CleanReturnAsync();
    }
    /// <summary>A network launch buys one ship, spawns its owner aboard and gives their ID the matching deed.</summary>
    [TestCase(false, "Contractor")]
    [TestCase(true, "Contractor")]
    [TestCase(false, "MdMedic")]
    public async Task ShipLaunchSpawnsAndChargesOnceThroughNetworkRequests(bool pregame, string selectedJob)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var player = pair.Player!;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var saved = preferences.GetPreferences(player.UserId);
        var slot = saved.SelectedCharacterIndex;
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        WFRoundSetupVessel vessel = null!;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            var ticker = server.System<GameTicker>();
            if (!pregame)
                ticker.StartRound(force: true);
            Assert.That(ticker.RunLevel, Is.EqualTo(pregame ? GameRunLevel.PreRoundLobby : GameRunLevel.InRound));
            Assert.That(player.AttachedEntity, Is.Null, "An unready player must remain available to set up their start.");
            if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
            {
                var host = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                server.EntMan.AddComponent<StationSectorServiceHostComponent>(host);
            }
            vessel = server.System<WFShipScenarioSystem>().GetVessels().Single(entry => entry.Id == "Guppy");
        });
        await pair.RunTicksSync(2);
        await server.WaitPost(() => preferences.SetProfile(player.UserId, slot,
            original.WithoutAllTraitPreferences().WithBankBalance(vessel.Price + 10000)).Wait());
        try
        {
            var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
            var lobbyId = OwnLobby(state).Id;
            Assert.That(state.HostBalance, Is.EqualTo(vessel.Price + 10000));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel)
            {
                LobbyId = lobbyId, Vessel = vessel.Id,
            });
            Assert.That(OwnLobby(state).Vessel, Is.EqualTo(vessel.Id));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob)
            {
                LobbyId = lobbyId, Job = selectedJob,
            });
            Assert.That(OwnLobby(state).Roles, Is.Empty);
            if (pregame)
            {
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = lobbyId });
                Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
            }
            if (pregame)
            {
                await server.WaitAssertion(() =>
                {
                    var ticker = server.System<GameTicker>();
                    ticker.StartRound(force: true);
                    Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
                });
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            }
            else
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = lobbyId });
            Assert.That(OwnLobby(state).Started, Is.True);
            Assert.That(OwnLobby(state).Members.Single().Spawned, Is.True);
            Assert.That(state.HostBalance, Is.EqualTo(10000));
            var body = EntityUid.Invalid;
            var hull = EntityUid.Invalid;
            await server.WaitAssertion(() =>
            {
                var em = server.EntMan;
                Assert.That(player.AttachedEntity, Is.Not.Null);
                body = player.AttachedEntity!.Value;
                var grid = em.GetComponent<TransformComponent>(body).GridUid;
                Assert.That(grid, Is.Not.Null);
                hull = grid!.Value;
                Assert.That(em.GetComponent<WFShipScenarioComponent>(hull).LobbyId, Is.EqualTo(lobbyId));
                Assert.That(em.GetComponent<ShipOwnershipComponent>(hull).OwnerUserId, Is.EqualTo(player.UserId));
                var cards = server.System<SharedIdCardSystem>();
                Assert.That(cards.TryFindIdCard(body, out var card), Is.True);
                Assert.That(em.GetComponent<ShuttleDeedComponent>(card.Owner).ShuttleUid, Is.EqualTo(hull));
                if (selectedJob == "MdMedic")
                    Assert.That(em.GetComponent<AccessComponent>(card.Owner).Tags, Does.Contain("Medical"),
                        "Responders retain their real medical access when spawning aboard a crew ship.");
                Assert.That(em.GetComponent<ShuttleDeedComponent>(hull).ShuttleUid, Is.EqualTo(hull));
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot]).BankBalance,
                    Is.EqualTo(10000), "The selected character must pay the listing price exactly once.");
            });

            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ApplyRoster) { LobbyId = lobbyId, Roster = WFCrewRoster.Medical });
            var responder = OwnLobby(state).Roles.Single(role => role.Job == "MdMedic");
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = lobbyId, RoleId = responder.Id, RoleName = "Rescue team", Job = "MdMedic", Capacity = 5,
            });
            Assert.That(OwnLobby(state).Roles.Single(role => role.Id == responder.Id).Capacity, Is.EqualTo(5));
            Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo(selectedJob));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Launch) { LobbyId = lobbyId });
            Assert.That(OwnLobby(state).Started, Is.True);
            Assert.That(state.HostBalance, Is.EqualTo(10000));
            await server.WaitAssertion(() =>
            {
                Assert.That(player.AttachedEntity, Is.EqualTo(body));
                Assert.That(server.EntMan.GetComponent<TransformComponent>(body).GridUid, Is.EqualTo(hull));
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(player.UserId).Characters[slot]).BankBalance,
                    Is.EqualTo(10000), "A duplicate launch must neither replace the body nor charge for another ship.");
            });
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ChangeScenario)
            {
                LobbyId = lobbyId, Scenario = WFRoundScenario.Crashlanded,
            });
            Assert.That(OwnLobby(state).Scenario, Is.EqualTo(WFRoundScenario.Ship), "A launched crew cannot replace its scenario.");
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
            {
                LobbyId = lobbyId, ShipStart = WFShipStartLocation.CentralDock,
            });
            Assert.That(OwnLobby(state).ShipStart, Is.EqualTo(WFShipStartLocation.RandomSpace));
            await server.WaitPost(() => server.System<GameTicker>().Respawn(player));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(state.OwnLobby, Is.Null, "Respawning must leave the previous crew.");
            Assert.That(state.CanJoin, Is.True);
            Assert.That(state.Lobbies.Any(lobby => lobby.Id == lobbyId), Is.False, "An empty crew is removed.");
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
            Assert.That(state.OwnLobby, Is.Not.Null, "The respawned player can start a fresh setup.");
            Assert.That(state.OwnLobby, Is.Not.EqualTo(lobbyId));
            Assert.That(OwnLobby(state).Members.Single().Spawned, Is.False);
            await server.WaitAssertion(() => Assert.That(server.EntMan.EntityExists(hull), Is.True,
                "Leaving the crew must preserve the already-purchased ship."));
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers));
            await server.WaitPost(() => preferences.SetProfile(player.UserId, slot, original).Wait());
            await server.WaitPost(() => server.System<GameTicker>().RestartRound());
            await pair.RunTicksSync(2);
        }
        await pair.CleanReturnAsync();
    }

    /// <summary>Hosts can change the start without recreating their crew, and each change revokes readiness.</summary>
    [Test]
    public async Task ChangingCrewTypeAndLocationPreservesCrewAndResetsConsent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var enabled = false;
        await pair.Server.WaitPost(() =>
        {
            enabled = pair.Server.CfgMan.GetCVar(PlanetCVars.PlanetNetworks);
            pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, true);
        });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship, Title = "The Wayfarers",
        });
        var id = state.OwnLobby!.Value;
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = id, Job = "Contractor", RoleName = "Engineer", Capacity = 2,
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = "Guppy" });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.True);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
        {
            LobbyId = id, ShipStart = WFShipStartLocation.CentralDock,
        });
        Assert.That(OwnLobby(state).ShipStart, Is.EqualTo(WFShipStartLocation.CentralDock));
        Assert.That(OwnLobby(state).Members.Single().Ready, Is.False);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
        {
            LobbyId = id, ShipStart = (WFShipStartLocation) byte.MaxValue,
        });
        Assert.That(OwnLobby(state).ShipStart, Is.EqualTo(WFShipStartLocation.CentralDock));
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ChangeScenario)
        {
            LobbyId = id, Scenario = WFRoundScenario.Crashlanded,
        });
        var lobby = OwnLobby(state);
        Assert.Multiple(() =>
        {
            Assert.That(lobby.Id, Is.EqualTo(id));
            Assert.That(lobby.Title, Is.EqualTo("The Wayfarers"));
            Assert.That(lobby.Scenario, Is.EqualTo(WFRoundScenario.Crashlanded));
            Assert.That(lobby.Vessel, Is.Null);
            Assert.That(lobby.ShipStart, Is.EqualTo(WFShipStartLocation.RandomSpace));
            Assert.That(lobby.Roles.Single().Name, Is.EqualTo("Engineer"));
            Assert.That(lobby.Roles.Single().Capacity, Is.EqualTo(2));
            Assert.That(lobby.Members.Single().Job, Is.EqualTo("Contractor"));
            Assert.That(lobby.Members.Single().Ready, Is.False);
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ChangeScenario)
        {
            LobbyId = id, Scenario = WFRoundScenario.Normal,
        });
        Assert.That(OwnLobby(state).Scenario, Is.EqualTo(WFRoundScenario.Crashlanded));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ChangeScenario)
        {
            LobbyId = id, Scenario = WFRoundScenario.Ship,
        });
        Assert.That(OwnLobby(state).Scenario, Is.EqualTo(WFRoundScenario.Ship));
        Assert.That(OwnLobby(state).Vessel, Is.Null);
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.Server.WaitPost(() => pair.Server.CfgMan.SetCVar(PlanetCVars.PlanetNetworks, enabled));
        await pair.CleanReturnAsync();
    }

    /// <summary>Public crew titles are normalized and malformed updates leave existing settings intact.</summary>
    [Test]
    public async Task CrewNamesAreValidatedAndEditable()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship, Title = "  The   Wayfarers  ",
        });
        var id = state.OwnLobby!.Value;
        Assert.That(OwnLobby(state).Title, Is.EqualTo("The Wayfarers"));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = id, Title = "[color=red]Bad", Access = WFLobbyAccess.Public,
        });
        Assert.That(OwnLobby(state).Title, Is.EqualTo("The Wayfarers"));
        Assert.That(OwnLobby(state).Access, Is.EqualTo(WFLobbyAccess.Requests));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = id, Title = "Salvage & Rescue", Access = WFLobbyAccess.Public,
        });
        Assert.That(OwnLobby(state).Title, Is.EqualTo("Salvage & Rescue"));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
        {
            LobbyId = id, Title = " ",
        });
        Assert.That(OwnLobby(state).Title, Is.Empty);
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>Normal joining creates no scenario and preserves the saved profile used by ordinary allocation.</summary>
    [Test]
    public async Task NormalJoiningDoesNotCreateScenarioOrChangePreferences()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var player = pair.Client.User!.Value;
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Normal });
        Assert.That(state.OwnLobby, Is.Null);
        Assert.That(state.Lobbies, Is.Empty);
        await pair.Server.WaitAssertion(() =>
        {
            var original = HumanoidCharacterProfile.DefaultWithSpecies()
                .WithJobPriorities(new Dictionary<ProtoId<JobPrototype>, JobPriority>
                {
                    ["Pilot"] = JobPriority.High,
                    ["Contractor"] = JobPriority.Low,
                })
                .WithPreferenceUnavailable(PreferenceUnavailableMode.SpawnAsOverflow);
            var profiles = new Dictionary<NetUserId, HumanoidCharacterProfile> { [player] = original };
            var choices = pair.Server.System<WFRoundSetupSystem>().GetRoundStartProfiles(profiles);
            Assert.That(choices[player], Is.SameAs(original));
            Assert.That(choices[player].JobPriorities[new ProtoId<JobPrototype>("Pilot")], Is.EqualTo(JobPriority.High));
            Assert.That(choices[player].PreferenceUnavailable, Is.EqualTo(PreferenceUnavailableMode.SpawnAsOverflow));
        });
        await pair.CleanReturnAsync();
    }
    /// <summary>Sends each mutation after the server's rate limit and allows a network round trip.</summary>
    private static async Task<WFRoundSetupState> Request(TestPair pair, WFRoundSetupRequest request)
    {
        var system = pair.Client.System<WFRoundSetupClientSystem>();
        await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        await pair.Client.WaitPost(() => system.Send(request));
        await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        WFRoundSetupState state = null;
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(system.State, Is.Not.Null);
            state = system.State;
        });
        return state;
    }

    /// <summary>A reconnect restores crew choices after the new session has loaded its database state.</summary>
    [Test]
    public async Task ReconnectingHostRetainsCrewAndCanReopenSetup()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Ship, Title = "Reconnect crew",
        });
        var id = OwnLobby(state).Id;
        var net = pair.Client.ResolveDependency<IClientNetManager>();
        var userName = pair.Player!.Name;
        var userId = pair.Player.UserId;
        var players = pair.Server.ResolveDependency<Robust.Server.Player.IPlayerManager>();
        await pair.Client.WaitPost(() => net.ClientDisconnect("Reconnect regression test"));
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() => Assert.That(players.Sessions, Is.Empty));
        pair.Client.SetConnectTarget(pair.Server);
        await pair.Client.WaitPost(() => net.ClientConnect(null!, 0, userName));
        await pair.RunTicksSync(180);
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
        Assert.Multiple(() =>
        {
            Assert.That(state.CanJoin, Is.True);
            Assert.That(pair.Client.User, Is.EqualTo(userId));
            Assert.That(state.OwnLobby, Is.EqualTo(id));
            Assert.That(OwnLobby(state).Title, Is.EqualTo("Reconnect crew"));
            Assert.That(OwnLobby(state).Members.Single().Connected, Is.True);
        });
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>Guest reservations can be reused before and during descent, including with unpowered helms.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrashGuestCanRespawnAndRejoinExistingCrew(bool descend)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var originalName = pair.Player!.Name;
        var net = pair.Client.ResolveDependency<IClientNetManager>();
        await EnableFeature(pair);
        var (layers, _, _) = await BuildOwnedStack(pair);
        try
        {
            var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Crashlanded });
            var id = OwnLobby(state).Id;
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = id, Job = "Contractor", Capacity = 3,
            });
            var role = OwnLobby(state).Roles.Single().Id;
            await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
            {
                LobbyId = id, Access = WFLobbyAccess.Public, AllowLateJoin = true,
            });
            await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = "Contractor" });
            await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            await server.WaitPost(() => ticker.StartRound(force: true));
            state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Single().Spawned, Is.True);
            await server.WaitAssertion(() =>
            {
                var crash = server.System<WFCrashScenarioSystem>();
                Assert.That(crash.TryGetSpawn(id, out var spawn), Is.True);
                Assert.That(crash.TryGetStation(id, out var station), Is.True);
                var helms = Children(server.EntMan, spawn.EntityId).Where(server.EntMan.HasComponent<ShuttleConsoleComponent>).ToArray();
                Assert.That(helms, Is.Not.Empty);
                foreach (var helm in helms)
                {
                    var receiver = server.EntMan.GetComponent<ApcPowerReceiverComponent>(helm);
                    receiver.PowerDisabled = true;
                    receiver.Powered = false;
                }
                foreach (var helm in helms)
                {
                    var power = new PowerChangedEvent(false, 0);
                    server.EntMan.EventBus.RaiseLocalEvent(helm, ref power);
                }
                Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(station, "Contractor", out var slots), Is.True);
                Assert.That(slots, Is.Null, "Losing every helm must not close the crew's advertised jobs.");
            });
            await ConnectAs("WFCrashRejoinGuest");
            for (var attempt = 0; attempt < 2; attempt++)
            {
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Join) { LobbyId = id, RoleId = role });
                var member = OwnLobby(state).Members.Single(entry => entry.UserId == pair.Client.User);
                Assert.That(member.Job, Is.EqualTo("Contractor"));
                Assert.That(OwnLobby(state).Roles.Single().CanSelect, Is.True);
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
                Assert.That(state.Message, Is.Null, $"Guest spawn {attempt + 1}: {state.Message}");
                Assert.That(OwnLobby(state).Members.Single(entry => entry.UserId == pair.Client.User).Spawned, Is.True);
                await server.WaitAssertion(() => Assert.That(pair.Player!.AttachedEntity, Is.Not.Null));
                if (attempt == 0 && descend)
                {
                    await server.WaitAssertion(() =>
                    {
                        var crash = server.System<WFCrashScenarioSystem>();
                        Assert.That(crash.TryGetSpawn(id, out var spawn), Is.True);
                        server.EntMan.GetComponent<WFCrashScenarioComponent>(spawn.EntityId).DescendAt =
                            server.ResolveDependency<IGameTiming>().CurTime;
                    });
                    await pair.RunTicksSync(pair.SecondsToTicks(2f));
                }
                await server.WaitPost(() => ticker.Respawn(pair.Player!));
                state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Refresh));
                Assert.That(state.OwnLobby, Is.Null);
                Assert.That(state.Lobbies.Single(lobby => lobby.Id == id).Roles.Single().Occupied, Is.Zero);
            }
        }
        finally
        {
            await Teardown(pair, layers);
            await server.WaitPost(() => ticker.RestartRound());
            await pair.RunTicksSync(2);
            await ConnectAs(originalName);
        }
        await pair.CleanReturnAsync();

        async Task ConnectAs(string name)
        {
            await pair.Client.WaitPost(() => net.ClientDisconnect("Switch regression-test player"));
            await pair.RunTicksSync(10);
            pair.Client.SetConnectTarget(server);
            await pair.Client.WaitPost(() => net.ClientConnect(null!, 0, name));
            await pair.RunTicksSync(180);
        }
    }

    /// <summary>A captain can recruit qualified specialists without receiving their eligibility or station command privileges.</summary>
    [Test]
    public async Task ShipRostersSeparateRecruitmentFromPersonalEligibility()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Ship });
        var id = OwnLobby(state).Id;
        Assert.That(state.RoleJobs.Select(job => job.Id), Is.EquivalentTo(WFShipCrewJobs.Jobs));
        Assert.That(state.RoleJobs.Any(job => job.Id == "Captain"), Is.False);
        Assert.That(OwnLobby(state).Portrait, Is.Not.Null);
        await pair.Server.WaitAssertion(() =>
        {
            var prototype = pair.Server.ProtoMan.Index<EntityPrototype>("WFShipScenarioStation");
            Assert.That(prototype.TryGetComponent<StationJobsComponent>(out var jobs, pair.Server.EntMan.ComponentFactory), Is.True);
            Assert.That(jobs!.SetupAvailableJobs.Keys.Select(job => job.Id), Is.EquivalentTo(WFShipCrewJobs.Jobs));
        });
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ApplyRoster) { LobbyId = id, Roster = WFCrewRoster.Medical });
        Assert.That(OwnLobby(state).Roles.Length, Is.EqualTo(4));
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.Null, "Adding a roster must not assign its host.");
        var captain = OwnLobby(state).Roles.Single(role => role.Job == "Contractor");
        var responder = OwnLobby(state).Roles.Single(role => role.Job == "MdMedic");
        Assert.That(responder.Capacity, Is.EqualTo(3));
        Assert.That(responder.CanSelect, Is.EqualTo(state.Jobs.Any(job => job.Id == "MdMedic")));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectRole) { LobbyId = id, RoleId = captain.Id });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.EqualTo(captain.Id));
        Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo("Contractor"));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = id, RoleId = captain.Id, RoleName = "Captain", Job = "MdMedic", Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Single(role => role.Id == captain.Id).Job, Is.EqualTo("Contractor"));
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.ApplyRoster) { LobbyId = id, Roster = WFCrewRoster.Medical });
        Assert.That(OwnLobby(state).Roles.Length, Is.EqualTo(4), "Adding the same roster must not duplicate slots.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
        {
            LobbyId = id, RoleName = "Captain", Job = "Captain", Capacity = 1,
        });
        Assert.That(OwnLobby(state).Roles.Length, Is.EqualTo(4), "Forged jobs must be rejected.");
        state = await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.SelectRole) { LobbyId = id });
        Assert.That(OwnLobby(state).Members.Single().RoleId, Is.Null);
        Assert.That(OwnLobby(state).Roles.Single(role => role.Id == captain.Id).Occupied, Is.Zero);
        await Request(pair, new WFRoundSetupRequest(WFRoundSetupAction.Leave) { LobbyId = id });
        await pair.CleanReturnAsync();
    }

    /// <summary>Finds the caller's lobby in its personalized snapshot.</summary>
    private static WFRoundSetupLobby OwnLobby(WFRoundSetupState state)
    {
        Assert.That(state.OwnLobby, Is.Not.Null);
        return state.Lobbies.Single(lobby => lobby.Id == state.OwnLobby);
    }
}
