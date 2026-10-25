using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.RoundSetup;
using Content.IntegrationTests.Pair;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.Voting.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipPa;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WF.RoundSetup;

public sealed partial class WFRoundSetupLobbyTest
{
    /// <summary>Deed-backed changes require the owner, preserve ships and payment, and move access, announcements and late arrivals.</summary>
    [TestCase(WFCrewFaction.None, true)]
    [TestCase(WFCrewFaction.None, false)]
    [TestCase(WFCrewFaction.TSF, false)]
    [TestCase(WFCrewFaction.PDV, false)]
    public async Task CrewShipChangesRequireOwnerConsent(WFCrewFaction faction, bool leaderOwnsShip)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var em = server.EntMan;
        var owner = pair.Player!;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WFRoundSetupSystem>();
        var ships = server.System<WFShipScenarioSystem>();
        var preferences = server.ResolveDependency<IServerPreferencesManager>();
        var original = preferences.GetPreferences(owner.UserId);
        var slot = original.SelectedCharacterIndex;
        var profile = (HumanoidCharacterProfile) original.Characters[slot];
        var timers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var preset = ticker.Preset;
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var job = faction switch { WFCrewFaction.TSF => "Deputy", WFCrewFaction.PDV => "Pirate", _ => "Contractor" };
        var vessel = faction switch { WFCrewFaction.TSF => "Calypso", WFCrewFaction.PDV => "Scorpion", _ => "Guppy" };
        const int balance = 10000000;
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var players = await server.AddDummySessions(2);
        try
        {
            var loaded = false;
            for (var attempt = 0; attempt < 300 && !loaded; attempt++)
            {
                await server.WaitPost(() => loaded = players.All(player => player.Status == SessionStatus.InGame &&
                    preferences.HavePreferencesLoaded(player) && server.ResolveDependency<UserDbDataManager>().IsLoadComplete(player)));
                if (!loaded)
                {
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
            }
            Assert.That(loaded, Is.True);
            EntityUid factionStation = default;
            await server.WaitPost(() =>
            {
                foreach (var player in players.Append(owner))
                    preferences.SetProfile(player.UserId, preferences.GetPreferences(player.UserId).SelectedCharacterIndex,
                        HumanoidCharacterProfile.DefaultWithSpecies("Human").WithoutAllTraitPreferences().WithName(player == owner ? "Ship Owner" : player == players[0] ? "Crew Leader" : "New Recruit").WithBankBalance(balance)).Wait();
                ticker.SetGamePreset("Sandbox");
                ticker.StartRound(force: true);
                if (faction != WFCrewFaction.None)
                {
                    factionStation = em.SpawnEntity("WFRoundSetupQuotaTestStation", MapCoordinates.Nullspace);
                    server.System<StationJobsSystem>().TrySetJobSlot(factionStation, job, 8, createSlot: true);
                }
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    em.AddComponent<StationSectorServiceHostComponent>(em.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            var state = await Request(pair, new(WFRoundSetupAction.Create)
            { Scenario = faction == WFCrewFaction.None ? WFRoundScenario.Ship : WFRoundScenario.FactionShip, Faction = faction });
            var id = OwnLobby(state).Id;
            await Request(pair, new(WFRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = vessel });
            await Request(pair, new(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = job });
            state = await Request(pair, new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId });
            Assert.That(OwnLobby(state).Started, Is.False, "An assignment cannot skip the original paid launch.");
            await Request(pair, new(WFRoundSetupAction.Configure) { LobbyId = id, Access = WFLobbyAccess.Public, AllowLateJoin = true });
            state = await Request(pair, new(WFRoundSetupAction.UpsertRole) { LobbyId = id, Job = job, Capacity = 3 });
            var role = OwnLobby(state).Roles.Single().Id;
            state = await Request(pair, new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Started, Is.True, state.Message);
            await RequestAs(players[0], new(WFRoundSetupAction.Join) { LobbyId = id, RoleId = role });
            await RequestAs(players[0], new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Count(member => member.Spawned), Is.EqualTo(2));
            var paidBalance = state.HostBalance;
            var originalShip = OwnLobby(state).CurrentShip!.Entity;
            var body = owner.AttachedEntity!.Value;
            var oldGrid = em.GetComponent<TransformComponent>(body).GridUid!.Value;
            var replacement = await pair.CreateTestMap();
            var replacementGrid = replacement.Grid.Owner;
            EntityUid card = default;
            NetEntity replacementNet = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<WFShipAccessSystem>().TryGetWornCard(body, out card), Is.True);
                // Crew starts need enclosed deck, so the centre tile must have neighbours.
                for (var x = -1; x <= 1; x++)
                {
                    for (var y = -1; y <= 1; y++)
                        server.System<SharedMapSystem>().SetTile(replacementGrid, replacement.Grid.Comp, new Vector2i(x, y), replacement.Tile.Tile);
                }
                em.EnsureComponent<ShuttleComponent>(replacementGrid);
                if (faction != WFCrewFaction.None)
                    em.EnsureComponent<CompanyComponent>(replacementGrid).CompanyName = faction.ToString();
                Assert.That(server.System<ShipyardSystem>().TryAssignDeed(replacementGrid, card, owner, null, "Crew replacement"), Is.True);
                replacementNet = em.GetNetEntity(replacementGrid);
                foreach (var coords in new[] { new EntityCoordinates(oldGrid, default), replacement.GridCoords })
                {
                    var speaker = em.SpawnEntity("WFWallmountShipPaSpeaker", coords);
                    em.GetComponent<ApcPowerReceiverComponent>(speaker).NeedsPower = false;
                }
            });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(state.CrewShips.Single(choice => choice.Owner == owner.UserId).Ship.Entity, Is.EqualTo(replacementNet));
            Assert.That(state.CrewShips.Single(choice => choice.Owner == owner.UserId).Error, Is.Null);
            state = await Request(pair, new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId, Ship = originalShip });
            Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "A stale selected ship must not authorize the new deed.");
            await server.WaitPost(() => em.EnsureComponent<CompanyComponent>(replacementGrid).CompanyName = faction == WFCrewFaction.TSF ? "PDV" : "TSF");
            state = await Request(pair, new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId, Ship = replacementNet });
            Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "Faction boundaries apply to replacements too.");
            await server.WaitPost(() => em.GetComponent<CompanyComponent>(replacementGrid).CompanyName = faction == WFCrewFaction.None ? "None" : faction.ToString());
            var requestId = 0;
            if (leaderOwnsShip)
            {
                state = await Request(pair, new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId, Ship = replacementNet });
            }
            else
            {
                state = await Request(pair, new(WFRoundSetupAction.TransferHost) { LobbyId = id, Target = players[0].UserId });
                Assert.That(OwnLobby(state).Host, Is.EqualTo(players[0].UserId));
                state = await Request(pair, new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId, Ship = replacementNet });
                Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "A non-leader cannot switch the crew ship unilaterally.");
                await AskOwner();
                await RequestAs(players[0], new(WFRoundSetupAction.ApproveShip) { LobbyId = id, Ship = replacementNet, ShipChangeId = requestId });
                state = await Request(pair, new(WFRoundSetupAction.Refresh));
                Assert.That(state.PendingShip, Is.Not.Null, "The leader cannot approve on the owner's behalf.");
                Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip));
                state = await Request(pair, new(WFRoundSetupAction.CancelShip) { LobbyId = id, Ship = replacementNet, ShipChangeId = requestId });
                Assert.That(state.PendingShip, Is.Null);
                Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "Declining leaves the crew on its original ship.");
                await AskOwner();
                await server.WaitPost(() => em.RemoveComponent<ShuttleDeedComponent>(card));
                state = await Request(pair, new(WFRoundSetupAction.ApproveShip) { LobbyId = id, Ship = replacementNet, ShipChangeId = requestId });
                Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "A request is invalid once the owner loses their deed.");
                await server.WaitAssertion(() => Assert.That(server.System<ShipyardSystem>()
                    .TryAssignDeed(replacementGrid, card, owner, null, "Crew replacement"), Is.True));
                var obsoleteRequest = requestId;
                await AskOwner();
                state = await Request(pair, new(WFRoundSetupAction.ApproveShip)
                { LobbyId = id, Ship = replacementNet, ShipChangeId = obsoleteRequest });
                Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(originalShip), "An old dialog cannot approve a newer request for the same ship.");
                WFRoundSetupWindow window = null!;
                Button approve = null!;
                await pair.Client.WaitPost(() =>
                {
                    window = new WFRoundSetupWindow();
                    window.FitTo(new Vector2(800, 600));
                    window.OpenCentered();
                    window.ShowLobby(id);
                });
                await pair.RunTicksSync(pair.SecondsToTicks(0.3f));
                await pair.Client.WaitAssertion(() =>
                {
                    for (var pass = 0; pass < 2; pass++)
                    {
                        window.Measure(new Vector2(800, 600));
                        window.Arrange(UIBox2.FromDimensions(Vector2.Zero, window.DesiredSize));
                    }
                    var sections = Controls(window).Single(control => control.Name == "RoundSetupSections");
                    approve = Controls(window).OfType<Button>().Single(button => button.Name == "RoundSetupApproveShip");
                    Assert.That(approve.GlobalPosition.Y, Is.GreaterThanOrEqualTo(sections.GlobalPosition.Y + sections.Height),
                        "Approval must be below the full-width tabs, not squeeze them into half the window.");
                    Assert.That(approve.Disabled, Is.False);
                    Assert.That(approve.HasStyleClass("ButtonAccept"), Is.True);
                    Assert.That(Controls(window).Any(control => control.Name == "RoundSetupDeclineShip"), Is.True);
                });
                await RoundSetupUiTest.Click(pair, approve);
                await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
                state = await Request(pair, new(WFRoundSetupAction.Refresh));
                await pair.Client.WaitPost(() => window.Close());
            }
            Assert.That(OwnLobby(state).CurrentShip!.Entity, Is.EqualTo(replacementNet), state.Message);
            Assert.That(OwnLobby(state).Faction, Is.EqualTo(faction));
            Assert.That(OwnLobby(state).Vessel, Is.EqualTo(vessel), "Saved starting plans remain separate from live ship assignment.");
            Assert.That(state.PendingShip, Is.Null);
            await server.WaitAssertion(() =>
            {
                Assert.That(owner.AttachedEntity, Is.EqualTo(body));
                Assert.That(em.GetComponent<TransformComponent>(body).GridUid, Is.EqualTo(oldGrid), "Changing ships does not teleport existing members.");
                Assert.That(em.EntityExists(oldGrid), Is.True);
                Assert.That(ships.GetActiveGrids(id), Is.EquivalentTo(new[] { replacementGrid }));
                Assert.That(em.GetComponent<WFShipAccessComponent>(oldGrid).CrewKeys, Is.Empty);
                var access = em.GetComponent<WFShipAccessComponent>(replacementGrid);
                Assert.That(access.CrewId, Is.EqualTo(id));
                Assert.That(access.Mode, Is.EqualTo(WFShipAccessMode.Crew));
                Assert.That(access.CrewKeys.Count, Is.EqualTo(2));
                Assert.That(((HumanoidCharacterProfile) preferences.GetPreferences(owner.UserId).Characters[slot]).BankBalance, Is.EqualTo(paidBalance));
            });
            await RequestAs(players[1], new(WFRoundSetupAction.Join) { LobbyId = id, RoleId = role });
            await RequestAs(players[1], new(WFRoundSetupAction.ConfirmJoin) { LobbyId = id });
            state = await Request(pair, new(WFRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Count(member => member.Spawned), Is.EqualTo(3));
            await server.WaitAssertion(() =>
            {
                Assert.That(em.GetComponent<TransformComponent>(players[1].AttachedEntity!.Value).GridUid, Is.EqualTo(replacementGrid));
                Assert.That(em.GetComponent<WFShipAccessComponent>(replacementGrid).CrewKeys.Count, Is.EqualTo(3));
                var messages = em.GetComponent<ShipPaBroadcastComponent>(replacementGrid).Broadcasts;
                Assert.That(messages.Any(broadcast => broadcast.Caption != null && broadcast.Caption.Contains("joined")), Is.True);
                Assert.That(em.TryGetComponent<ShipPaBroadcastComponent>(oldGrid, out var oldPa) &&
                    oldPa.Broadcasts.Any(broadcast => broadcast.Caption != null && broadcast.Caption.Contains("joined")), Is.False);
                if (faction != WFCrewFaction.None)
                {
                    Assert.That(server.System<StationJobsSystem>().TryGetJobSlot(factionStation, job, out var slots), Is.True);
                    Assert.That(slots, Is.EqualTo(5), "All three members still use normal faction job quotas.");
                }
                em.DeleteEntity(replacementGrid);
                Assert.That(ships.TryGetSpawn(id, out _), Is.False, "A destroyed replacement must not send recruits back to the abandoned hull.");
                Assert.That(em.EntityExists(oldGrid), Is.True);
            });
            await pair.RunTicksSync(2);

            async Task AskOwner()
            {
                await RequestAs(players[0], new(WFRoundSetupAction.AssignShip) { LobbyId = id, Target = owner.UserId, Ship = replacementNet });
                var requested = await Request(pair, new(WFRoundSetupAction.Refresh));
                Assert.That(requested.PendingShip?.Ship.Entity, Is.EqualTo(replacementNet));
                requestId = requested.PendingShip!.RequestId;
                Assert.That(OwnLobby(requested).CurrentShip!.Entity, Is.EqualTo(originalShip), "Requesting does not change the ship before approval.");
            }
        }
        finally
        {
            await server.WaitPost(() =>
            {
                ticker.RestartRound();
                ticker.SetGamePreset(preset);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, timers);
                preferences.SetProfile(owner.UserId, slot, profile).Wait();
            });
            await server.RemoveAllDummySessions();
            await server.WaitPost(() => server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote));
        }
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        async Task RequestAs(ICommonSession player, WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(player) }));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        }
        static IEnumerable<Control> Controls(Control parent)
        {
            foreach (var child in parent.Children)
            {
                yield return child;
                foreach (var descendant in Controls(child))
                    yield return descendant;
            }
        }
    }
}
