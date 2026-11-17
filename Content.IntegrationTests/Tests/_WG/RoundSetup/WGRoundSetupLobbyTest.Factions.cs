using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WG.RoundSetup;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Station.Components;
using Content.Server._WG.RoundSetup;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Content.Server.Station.Components;
using Content.Server._Battlefield14.FactionTracking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.RoundSetup;
using Content.Shared.Access.Systems;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class WGRoundSetupLobbyTest
{
    [TestPrototypes]
    private const string FactionMap = @"
- type: gamePreset
  id: WGFactionCrewTestPreset
  name: sandbox-title
  description: sandbox-description
  autobalancerEnabled: true
- type: gameMap
  id: WGFactionCrewTestMap
  mapName: WGFactionCrewTestMap
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationNameSetup
        mapNameTemplate: Faction test station
      - type: StationJobs
        availableJobs:
          Bailiff: [1, 1]
          Deputy: [3, 3]
          Sheriff: [1, 1]
          PirateFirstMate: [1, 1]
          Pirate: [3, 3]
          PirateCaptain: [1, 1]
";

    /// <summary>Faction crews preserve qualifications, recruitment ceilings, normal job slots and paid ownership.</summary>
    [TestCase(false, WGCrewFaction.TSF, "Bailiff", "Deputy", "Sheriff", "Calypso", "Scorpion")]
    [TestCase(true, WGCrewFaction.TSF, "Bailiff", "Deputy", "Sheriff", "Calypso", "Scorpion")]
    [TestCase(false, WGCrewFaction.PDV, "PirateFirstMate", "Pirate", "PirateCaptain", "Scorpion", "Calypso")]
    [TestCase(true, WGCrewFaction.PDV, "PirateFirstMate", "Pirate", "PirateCaptain", "Scorpion", "Calypso")]
    public async Task FactionCrewsUseExistingSlotsAndPaidShips(bool pregame, WGCrewFaction faction, string hostJob,
        string recruitJob, string higherJob, string vessel, string enemyVessel)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var player = pair.Player!;
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var saved = preferences.GetPreferences(player.UserId);
        var slot = saved.SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) saved.Characters[slot];
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var originalMap = server.CfgMan.GetCVar(CCVars.GameMap);
        var station = EntityUid.Invalid;
        var originalPreset = server.System<GameTicker>().Preset;
        const int balance = 10000000;
        var errors = new List<string>();
        void CaptureError(WGRoundSetupState snapshot)
        {
            if (snapshot.Message != null)
                errors.Add(snapshot.Message);
        }
        await pair.Client.WaitPost(() => pair.Client.System<WGRoundSetupClientSystem>().Received += CaptureError);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            server.System<GameTicker>().SetGamePreset("WGFactionCrewTestPreset");
            preferences.SetProfile(player.UserId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Human")
                .WithoutAllTraitPreferences().WithBankBalance(balance)).Wait();
            if (pregame)
            {
                server.CfgMan.SetCVar(CCVars.GameMap, "WGFactionCrewTestMap");
                server.ResolveDependency<IGameMapManager>().SelectMap("WGFactionCrewTestMap");
            }
            else
            {
                server.System<GameTicker>().StartRound(force: true);
                station = server.EntMan.SpawnEntity("WGRoundSetupQuotaTestStation", MapCoordinates.Nullspace);
                var jobs = server.System<StationJobsSystem>();
                foreach (var job in WGFactionCrewPolicy.Jobs(faction))
                    Assert.That(jobs.TrySetJobSlot(station, job, 1, createSlot: true), Is.True);
            }
            if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
            {
                var host = server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                server.EntMan.AddComponent<StationSectorServiceHostComponent>(host);
            }
        });
        try
        {
            var state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.Create)
            { Scenario = WGRoundScenario.FactionShip, Faction = faction });
            var id = OwnLobby(state).Id;
            Assert.That(state.Jobs.Select(job => job.Id), Does.Contain(hostJob));
            Assert.That(state.Vessels.Select(ship => ship.Id), Does.Contain(vessel));
            Assert.That(state.Vessels.Select(ship => ship.Id), Does.Not.Contain(enemyVessel));
            Assert.That(state.Vessels.Select(ship => ship.Id), Does.Not.Contain("Polaris"));
            Assert.That(state.Vessels.Select(ship => ship.Id), Does.Not.Contain("Europa"));
            var price = state.Vessels.Single(ship => ship.Id == vessel).Price;
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectJob) { LobbyId = id, Job = hostJob });
            Assert.That(OwnLobby(state).Members.Single().Job, Is.EqualTo(hostJob));
            Assert.That(state.RoleJobs.Select(job => job.Id), Does.Contain(recruitJob));
            Assert.That(state.RoleJobs.Select(job => job.Id), Does.Not.Contain(higherJob));
            WGRoundSetupWindow window = null!;
            await pair.Client.WaitPost(() =>
            {
                window = new WGRoundSetupWindow();
                window.FitTo(new Vector2(800, 600));
                window.OpenCentered();
                window.ShowLobby(id);
            });
            await pair.RunTicksSync(2);
            await pair.Client.WaitAssertion(() =>
            {
                for (var pass = 0; pass < 2; pass++)
                {
                    window.Measure(new Vector2(800, 600));
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, window.DesiredSize));
                }
                var controls = Descendants(window).ToArray();
                var page = controls.Single(control => control.Name == "RoundSetupPage");
                var scroll = (ScrollContainer) controls.Single(control => control.Name == "RoundSetupPageScroll");
                Assert.That(controls.Any(control => control.Name == "RoundSetupPersonalTitle"), Is.False);
                Assert.That(scroll.VScrollEnabled, Is.False);
                Assert.That(page.DesiredSize.Y, Is.LessThanOrEqualTo(scroll.Height + 1), "Faction preparation must fit without clipping.");
                Assert.That(((Button) controls.Single(control => control.Name == "RoundSetupNext")).Disabled, Is.False);
                window.Close();
            });
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.UpsertRole)
            { LobbyId = id, Job = higherJob, RoleName = "Unauthorized command", Capacity = 1 });
            Assert.That(OwnLobby(state).Roles, Is.Empty);
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.UpsertRole)
            { LobbyId = id, Job = recruitJob, RoleName = string.Empty, Capacity = 3 });
            Assert.That(OwnLobby(state).Roles.Single().Job, Is.EqualTo(recruitJob));
            var opening = OwnLobby(state).Roles.Single();
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.UpsertRole)
            { LobbyId = id, RoleId = opening.Id, Job = recruitJob, RoleName = "Forged rank", Capacity = 3 });
            Assert.That(OwnLobby(state).Roles.Single().Name, Is.Empty);
            var tierTwo = faction == WGCrewFaction.TSF ? "Fujian" : "Neptune";
            Assert.That(state.Vessels.Select(ship => ship.Id), Does.Not.Contain(tierTwo));
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = tierTwo });
            Assert.That(OwnLobby(state).Vessel, Is.Null, "T2 requests must be rejected by the server.");
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectShipStart) { LobbyId = id, ShipStart = WGShipStartLocation.CentralDock });
            Assert.That(OwnLobby(state).ShipStart, Is.EqualTo(WGShipStartLocation.RandomSpace));
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectShipStart) { LobbyId = id, ShipStart = WGShipStartLocation.FactionBase });
            Assert.That(OwnLobby(state).ShipStart, Is.EqualTo(WGShipStartLocation.FactionBase));
            await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectShipStart) { LobbyId = id, ShipStart = WGShipStartLocation.RandomSpace });
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = enemyVessel });
            Assert.That(OwnLobby(state).Vessel, Is.Null, "A forged opposing-faction ship request must fail.");
            await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = vessel });
            await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SetShipName) { LobbyId = id, ShipName = "Resolute" });
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SetTitle) { LobbyId = id, RoleName = "Patrol leader" });
            Assert.That(OwnLobby(state).Members.Single().Title, Is.Empty, "Faction ranks cannot be renamed.");
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SaveSetup) { LobbyId = id, SaveName = "Faction patrol" });
            var setup = state.SavedSetups.Single();
            Assert.That(setup.Faction, Is.EqualTo(faction));
            Assert.That(setup.ShipName, Is.EqualTo("Resolute"));
            await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.Leave) { LobbyId = id });
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.LoadSetup) { SavedId = setup.Id });
            id = OwnLobby(state).Id;
            Assert.That(OwnLobby(state).Faction, Is.EqualTo(faction));
            Assert.That(OwnLobby(state).ShipName, Is.EqualTo("Resolute"));
            if (!pregame)
            {
                await server.WaitPost(() => server.System<StationJobsSystem>().TrySetJobSlot(station, hostJob, 0));
                state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
                Assert.That(OwnLobby(state).Started, Is.False, "A crew cannot manufacture extra faction command slots.");
                Assert.That(state.HostBalance, Is.EqualTo(balance), "An unavailable job must never charge the host.");
                await server.WaitPost(() => server.System<StationJobsSystem>().TrySetJobSlot(station, hostJob, 1));
            }
            errors.Clear();
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
            if (pregame)
            {
                await server.WaitAssertion(() =>
                {
                    var ticker = server.System<GameTicker>();
                    var counts = server.System<FactionTrackingSystem>().GetFactionCounts(ticker);
                    Assert.That(faction == WGCrewFaction.TSF ? counts.blufor : counts.redfor, Is.EqualTo(1),
                        "Pregame balancing uses the selected crew job instead of saved civilian preferences.");
                    ticker.StartRound(force: true);
                    var jobs = server.System<StationJobsSystem>();
                    station = server.EntMan.EntityQuery<StationJobsComponent>().Select(comp => comp.Uid())
                        .Single(uid => server.System<WGRoundSetupSystem>().IsStandardStation(uid) && jobs.TryGetJobSlot(uid, hostJob, out _));
                });
                state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.Refresh));
            }
            Assert.That(OwnLobby(state).Started, Is.True, string.Join("; ", errors));
            Assert.That(OwnLobby(state).Members.Single().Spawned, Is.True);
            Assert.That(state.HostBalance, Is.EqualTo(balance - price));
            await server.WaitAssertion(() =>
            {
                var em = server.EntMan;
                Assert.That(player.AttachedEntity, Is.Not.Null);
                var hull = em.GetComponent<TransformComponent>(player.AttachedEntity!.Value).GridUid!.Value;
                Assert.That(em.GetComponent<WGShipScenarioComponent>(hull).LobbyId, Is.EqualTo(id));
                Assert.That(server.System<SharedIdCardSystem>().TryFindIdCard(player.AttachedEntity.Value, out var card), Is.True);
                Assert.That(card.Comp.CompanyName.Id, Is.EqualTo(faction.ToString()));
                Assert.That(card.Comp.LocalizedJobTitle, Is.EqualTo(server.ProtoMan.Index<JobPrototype>(hostJob).LocalizedName),
                    "Faction IDs must retain the official job title.");
                Assert.That(em.GetComponent<ShuttleDeedComponent>(card.Owner).ShuttleName, Does.Contain("Resolute"));
                Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(station, hostJob, out var remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(0), "The real faction station owns the consumed job slot.");
            });
            state = await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.SetShipName) { LobbyId = id, ShipName = "Too late" });
            Assert.That(OwnLobby(state).ShipName, Is.EqualTo("Resolute"));
            await Request(pair, new WGRoundSetupRequest(WGRoundSetupAction.DeleteSetup) { SavedId = setup.Id });
        }
        finally
        {
            await pair.Client.WaitPost(() => pair.Client.System<WGRoundSetupClientSystem>().Received -= CaptureError);
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.GameMap, originalMap);
                server.System<GameTicker>().RestartRound();
                server.System<GameTicker>().SetGamePreset(originalPreset);
                preferences.SetProfile(player.UserId, slot, original).Wait();
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
            });
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        static IEnumerable<Control> Descendants(Control control)
        {
            foreach (var child in control.Children)
            {
                yield return child;
                foreach (var descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
