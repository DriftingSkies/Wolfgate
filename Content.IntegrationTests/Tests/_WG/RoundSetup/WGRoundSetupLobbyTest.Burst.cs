using Content.IntegrationTests.Tests._WG.EngineCompat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Battlefield14.FactionTracking;
using Content.Server._NF.SectorServices;
using Content.Server._WG.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Voting.Managers;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Station.Components;
using Content.Shared.Preferences;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class WGRoundSetupLobbyTest
{
    private static ProtoId<GameMapPrototype> WGFactionBurstMapProto => "WGFactionBurstMap";
    private static readonly ProtoId<VesselPrototype> GuppyProto = "Guppy";

    [TestPrototypes]
    private const string BurstMap = @"
- type: gameMap
  id: WGFactionBurstMap
  mapName: WGFactionBurstMap
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationNameSetup
        mapNameTemplate: TSF test base
      - type: StationJobs
        availableJobs:
          Deputy: [10, 10]
          Cadet: [10, 10]
          Sheriff: [0, 0]
    PDV:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationNameSetup
        mapNameTemplate: PDV test base
      - type: StationJobs
        availableJobs:
          Pirate: [10, 10]
          PDVInfiltrator: [10, 10]
          PirateCaptain: [0, 0]
";

    /// <summary>Forty ready players launch in one round-start pass, with no duplicate charges, missing crew or intersecting hulls.</summary>
    [TestCase(5)]
    [TestCase(1)]
    public async Task FortyFactionPlayersLaunchTogether(int crewSize)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WGRoundSetupSystem>();
        var ships = server.System<WGShipScenarioSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var userDb = server.ResolveDependency<UserDbDataManager>();
        var hook = server.System<WGRoundSetupEligibilityTestSystem>();
        var originalMap = server.CfgMan.GetCVar(CCVars.GameMap);
        var originalTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var originalAutoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var originalPreset = ticker.Preset;
        var onRequest = typeof(WGRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var findLobby = typeof(WGRoundSetupSystem).GetMethod("FindLobby", BindingFlags.NonPublic | BindingFlags.Instance)!;
        const int balance = 10000000;
        // Dummy sessions cannot receive the direct channel messages used by active votes.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var players = await server.AddDummySessions(40);
        var crewCount = players.Length / crewSize;
        var lobbies = new int[crewCount];
        var bases = new EntityUid[2];
        var stations = new EntityUid[2];
        var hulls = new EntityUid[crewCount];
        var timer = new Stopwatch();
        try
        {
            var loaded = false;
            var loadTimer = Stopwatch.StartNew();
            while (!loaded && loadTimer.Elapsed < TimeSpan.FromSeconds(30))
            {
                await server.WaitPost(() => loaded = players.All(player => player.Status == SessionStatus.InGame &&
                    preferences.HavePreferencesLoaded(player) && userDb.IsLoadComplete(player)));
                if (!loaded)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(loaded, Is.True, "All forty accounts must finish loading before crew setup.");
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                server.CfgMan.SetCVar(CCVars.GameMap, "WGFactionBurstMap");
                server.ResolveDependency<IGameMapManager>().SelectMap("WGFactionBurstMap");
                ticker.SetGamePreset("WGFactionCrewTestPreset");
                foreach (var player in players)
                {
                    var slot = preferences.GetPreferences(player.UserId).SelectedCharacterIndex;
                    preferences.SetProfile(player.UserId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                        .WithoutAllTraitPreferences().WithName(player.Name).WithBankBalance(balance)).Wait();
                }
                hook.BeforeRoundSpawn = () =>
                {
                    var jobs = server.System<StationJobsSystem>();
                    var em = server.EntMan;
                    var stationSystem = server.System<StationSystem>();
                    stationSystem.InitializeNewStation(server.ProtoMan.Index<GameMapPrototype>(WGFactionBurstMapProto).Stations["PDV"], null);
                    for (var faction = 0; faction < 2; faction++)
                    {
                        var job = faction == 0 ? "Deputy" : "Pirate";
                        stations[faction] = em.EntityQuery<StationJobsComponent>().Select(comp => comp.Uid())
                            .Single(uid => setup.IsStandardStation(uid) && jobs.TryGetJobSlot(uid, job, out _));
                        var vessel = server.ProtoMan.Index<VesselPrototype>(GuppyProto);
                        Assert.That(server.System<MapLoaderSystem>().TryLoadGrid(ticker.DefaultMap, vessel.ShuttlePath,
                            out var loaded, offset: new Vector2(faction == 0 ? -8000 : 8000, 0)), Is.True);
                        bases[faction] = loaded!.Value.Owner;
                        var oldGrids = em.GetComponent<StationDataComponent>(stations[faction]).Grids.ToArray();
                        stationSystem.AddGridToStation(stations[faction], bases[faction]);
                        foreach (var oldGrid in oldGrids)
                            em.DeleteEntity(oldGrid);
                        // Full or incompatible docks must not prevent the simultaneous base starts.
                        foreach (var dock in server.System<DockingSystem>().GetDocks(bases[faction]).ToArray())
                            em.DeleteEntity(dock.Owner);
                    }
                    em.EnsureComponent<StationSectorServiceHostComponent>(stations[0]);
                    timer.Start();
                };
            });
            await BatchHosts(crew => new(WGRoundSetupAction.Create)
            {
                Scenario = WGRoundScenario.FactionShip, Faction = Faction(crew), Access = WGLobbyAccess.Public,
            });
            await server.WaitAssertion(() =>
            {
                for (var crew = 0; crew < crewCount; crew++)
                {
                    var lobby = findLobby.Invoke(setup, new object[] { players[crew * crewSize].UserId });
                    Assert.That(lobby, Is.Not.Null);
                    lobbies[crew] = (int) lobby!.GetType().GetField("Id")!.GetValue(lobby)!;
                }
            });
            await BatchHosts(crew => new(WGRoundSetupAction.Configure) { LobbyId = lobbies[crew], Access = WGLobbyAccess.Public });
            await BatchHosts(crew => new(WGRoundSetupAction.SelectJob) { LobbyId = lobbies[crew], Job = Job(crew) });
            await BatchHosts(crew => new(WGRoundSetupAction.SelectVessel) { LobbyId = lobbies[crew], Vessel = Vessel(crew) });
            await BatchHosts(crew => new(WGRoundSetupAction.SelectShipStart)
            { LobbyId = lobbies[crew], ShipStart = crew % 4 < 2 ? WGShipStartLocation.FactionBase : WGShipStartLocation.RandomSpace });
            if (crewSize > 1)
            {
                await BatchHosts(crew => new(WGRoundSetupAction.UpsertRole)
                { LobbyId = lobbies[crew], Job = Job(crew), Capacity = crewSize - 1 });
                await server.WaitPost(() =>
                {
                    for (var crew = 0; crew < crewCount; crew++)
                    for (var member = 1; member < crewSize; member++)
                        RequestFor(players[crew * crewSize + member], new(WGRoundSetupAction.Join) { LobbyId = lobbies[crew], RoleId = 1 });
                });
                await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            }
            await server.WaitAssertion(() =>
            {
                // Alternate factions while readying, respecting the existing population-balancing gate.
                for (var member = 0; member < crewSize; member++)
                for (var crew = 0; crew < crewCount; crew++)
                    RequestFor(players[crew * crewSize + member], new(WGRoundSetupAction.ConfirmJoin) { LobbyId = lobbies[crew] });
                Assert.That(players.Count(player => ticker.PlayerGameStatuses[player.UserId] == PlayerGameStatus.ReadyToPlay), Is.EqualTo(40));
                Assert.That(server.System<FactionTrackingSystem>().GetFactionCounts(ticker), Is.EqualTo((20, 20)));
                ticker.StartRound(force: true);
                timer.Stop();
                Assert.That(players.Count(player => ticker.UserHasJoinedGame(player)), Is.EqualTo(40));
                Assert.That(players.Select(player => player.AttachedEntity).Distinct().Count(), Is.EqualTo(40));
                var em = server.EntMan;
                var transform = server.System<SharedTransformSystem>();
                for (var crew = 0; crew < crewCount; crew++)
                {
                    var host = players[crew * crewSize];
                    var price = server.ProtoMan.Index<VesselPrototype>(Vessel(crew)).Price;
                    Assert.That(ships.GetBalance(host), Is.EqualTo(balance - price));
                    Assert.That(ships.TryGetSpawn(lobbies[crew], out var spawn), Is.True);
                    hulls[crew] = spawn.EntityId;
                    for (var member = 0; member < crewSize; member++)
                    {
                        var player = players[crew * crewSize + member];
                        var body = player.AttachedEntity!.Value;
                        Assert.That(em.GetComponent<TransformComponent>(body).GridUid, Is.EqualTo(hulls[crew]));
                        Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(body, out var card), Is.True);
                        Assert.That(card.Comp.CompanyName.Id, Is.EqualTo(Faction(crew).ToString()));
                        Assert.That(em.HasComponent<ShuttleDeedComponent>(card.Owner), Is.EqualTo(member == 0));
                        if (member > 0)
                            Assert.That(ships.GetBalance(player), Is.EqualTo(balance), "Crew members must not pay for the host's ship.");
                    }
                    if (crew % 4 < 2)
                        Assert.That(Vector2.Distance(transform.GetWorldPosition(hulls[crew]), transform.GetWorldPosition(bases[crew % 2])), Is.LessThan(1600f));
                    Assert.That(ships.TryCharge(lobbies[crew], host, out _), Is.True);
                    Assert.That(ships.GetBalance(host), Is.EqualTo(balance - price), "Retrying a launch cannot double-charge.");
                }
                Assert.That(hulls.Distinct().Count(), Is.EqualTo(crewCount));
                for (var first = 0; first < hulls.Length; first++)
                {
                    var bounds = transform.GetWorldMatrix(hulls[first]).TransformBox(em.GetComponent<MapGridComponent>(hulls[first]).LocalAABB);
                    for (var second = first + 1; second < hulls.Length; second++)
                        Assert.That(bounds.Intersects(transform.GetWorldMatrix(hulls[second]).TransformBox(em.GetComponent<MapGridComponent>(hulls[second]).LocalAABB)), Is.False);
                }
                for (var role = 0; role < 4; role++)
                {
                    Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(stations[role % 2], Job(role), out var remaining), Is.True);
                    Assert.That(remaining, Is.Zero, "Exactly ten slots in each of four faction jobs must be consumed.");
                }
            });
            TestContext.Out.WriteLine($"40 players / {crewCount} ships / {crewSize} per crew: spawn pass {timer.Elapsed.TotalSeconds:F2}s; all spawned, correct payments, distinct hulls, no overlaps.");
        }
        finally
        {
            await server.WaitPost(() =>
            {
                hook.BeforeRoundSpawn = null;
                server.CfgMan.SetCVar(CCVars.GameMap, originalMap);
                ticker.RestartRound();
                ticker.SetGamePreset(originalPreset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, originalTimers);
            });
            await server.RemoveAllDummySessions();
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, originalAutoVote));
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        void RequestFor(ICommonSession player, WGRoundSetupRequest request)
        {
            if (findLobby.Invoke(setup, new object[] { player.UserId }) is { } lobby)
                request.PaymentRevision = (int) lobby.GetType().GetField("PaymentRevision")!.GetValue(lobby)!;
            onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(player) });
        }

        async System.Threading.Tasks.Task BatchHosts(Func<int, WGRoundSetupRequest> request)
        {
            await server.WaitPost(() =>
            {
                for (var crew = 0; crew < crewCount; crew++)
                    RequestFor(players[crew * crewSize], request(crew));
            });
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
        }
        static WGCrewFaction Faction(int crew) => crew % 2 == 0 ? WGCrewFaction.TSF : WGCrewFaction.PDV;
        static string Job(int crew) => (crew % 4) switch { 0 => "Deputy", 1 => "Pirate", 2 => "Cadet", _ => "PDVInfiltrator" };
        static string Vessel(int crew) => crew % 2 == 0 ? "Calypso" : "Scorpion";
    }
}
