using System.Linq;
using System.Reflection;
using Content.Server._WG.RoundSetup;
using Content.Server._NF.SectorServices;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Components;
using Content.Server.Voting.Managers;
using Content.Shared.CCVar;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Content.Shared.Preferences;
using Content.Shared._WG.RoundSetup;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.RoundSetup;

public sealed partial class WGRoundSetupLobbyTest
{
    /// <summary>Only the leader allocates shares; every payer approves the current quote, and outsiders see only the public roster.</summary>
    [TestCase("Contractor")]
    [TestCase("Mercenary")]
    public async Task CrewContributionsRequireCurrentIndividualConsent(string job)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true });
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var host = pair.Player!;
        var slot = prefs.GetPreferences(host.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
        var ticker = server.System<GameTicker>();
        var setup = server.System<WGRoundSetupSystem>();
        var ships = server.System<WGShipScenarioSystem>();
        var onRequest = typeof(WGRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var roleTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
            server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
            foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray()) vote.Cancel();
        });
        await pair.RunTicksSync(2);
        var crew = (await server.AddDummySessions(1)).Single();
        try
        {
            var loaded = false;
            for (var i = 0; i < 300 && !loaded; i++)
            {
                await server.WaitPost(() => loaded = crew.Status == SessionStatus.InGame && prefs.HavePreferencesLoaded(crew) &&
                    server.ResolveDependency<UserDbDataManager>().IsLoadComplete(crew));
                if (!loaded) { await pair.RunTicksSync(1); await Task.Delay(10); }
            }
            Assert.That(loaded, Is.True);
            await server.WaitPost(() =>
            {
                prefs.SetProfile(host.UserId, slot, HumanoidCharacterProfile.DefaultWithSpecies("Diona").WithoutAllTraitPreferences().WithName("Crew Leader").WithBankBalance(1000000)).Wait();
                prefs.SetProfile(crew.UserId, prefs.GetPreferences(crew.UserId).SelectedCharacterIndex,
                    HumanoidCharacterProfile.DefaultWithSpecies("Diona").WithoutAllTraitPreferences().WithName("Crew Responder").WithBankBalance(1000000)).Wait();
                ticker.StartRound(force: true);
                if (!server.System<SectorServiceSystem>().GetServiceEntity().Valid)
                    server.EntMan.AddComponent<StationSectorServiceHostComponent>(server.EntMan.SpawnEntity(null, MapCoordinates.Nullspace));
            });
            var state = await Request(pair, new(WGRoundSetupAction.Create) { Scenario = WGRoundScenario.Ship });
            var id = state.OwnLobby!.Value;
            await Request(pair, new(WGRoundSetupAction.SelectJob) { LobbyId = id, Job = job });
            await Request(pair, new(WGRoundSetupAction.SelectVessel) { LobbyId = id, Vessel = "Guppy" });
            await Request(pair, new(WGRoundSetupAction.Configure) { LobbyId = id, Access = WGLobbyAccess.Public, AllowLateJoin = true });
            state = await Request(pair, new(WGRoundSetupAction.UpsertRole) { LobbyId = id, Job = job, RoleName = "Navigator", Capacity = 2 });
            await AsCrew(new(WGRoundSetupAction.Join) { LobbyId = id, RoleId = OwnLobby(state).Roles.Single().Id });
            state = await Request(pair, new(WGRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Length, Is.EqualTo(2));
            var price = state.Payment!.Price;
            state = await Request(pair, new(WGRoundSetupAction.SetContribution) { LobbyId = id, Target = host.UserId, Contribution = price - 600 });
            state = await Request(pair, new(WGRoundSetupAction.SetContribution) { LobbyId = id, Target = crew.UserId, Contribution = 600 });
            var revision = state.Payment!.Revision;
            await AsCrew(new(WGRoundSetupAction.SetContribution) { LobbyId = id, Target = host.UserId, Contribution = 0, PaymentRevision = revision });
            state = await Request(pair, new(WGRoundSetupAction.Refresh));
            Assert.That(state.Payment!.Contribution, Is.EqualTo(price - 600));
            await AsCrew(new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id, PaymentRevision = revision - 1 });
            state = await Request(pair, new(WGRoundSetupAction.Refresh));
            Assert.That(OwnLobby(state).Members.Single(m => m.UserId == crew.UserId).Ready, Is.False);
            state = await Request(pair, new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Started, Is.False, "Host approval waits for other contributors.");
            Assert.That(state.Payment!.AllApproved, Is.False);
            state = await Request(pair, new(WGRoundSetupAction.SetContribution) { LobbyId = id, Target = crew.UserId, Contribution = 500 });
            Assert.That(OwnLobby(state).Members.All(m => !m.Ready), Is.True, "Changing a share revokes all approvals.");
            state = await Request(pair, new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(state.JoinConfirmed, Is.False, "An incomplete allocation cannot be approved.");
            state = await Request(pair, new(WGRoundSetupAction.SetContribution) { LobbyId = id, Target = host.UserId, Contribution = price - 500 });
            await AsCrew(new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id, PaymentRevision = state.Payment!.Revision });
            state = await Request(pair, new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Started, Is.True, state.Message);
            Assert.That(OwnLobby(state).Members.All(m => m.Spawned), Is.True);
            await server.WaitAssertion(() =>
            {
                Assert.That(ships.GetBalance(host), Is.EqualTo(1000000 - price + 500));
                Assert.That(ships.GetBalance(crew), Is.EqualTo(1000000 - 500));
                foreach (var player in new[] { host, crew })
                foreach (var equipmentSlot in new[] { "jumpsuit", "shoes", "back", "id" })
                    Assert.That(server.System<InventorySystem>().TryGetSlotEntity(player.AttachedEntity!.Value, equipmentSlot, out _),
                        Is.True, $"{job} needs {equipmentSlot} on the actual crew spawn path.");
                ticker.Respawn(host);
            });
            state = await Request(pair, new(WGRoundSetupAction.Refresh));
            Assert.That(state.OwnLobby, Is.Null);
            var publicCrew = state.Lobbies.Single(lobby => lobby.Id == id);
            Assert.That(publicCrew.Roster.Single().Name, Is.EqualTo("Crew Responder"));
            Assert.That(publicCrew.Roster.Single().Title, Is.EqualTo("Navigator"));
            Assert.That(publicCrew.Roster.Single().Spawned, Is.True);
            Assert.That(publicCrew.Members, Is.Empty);
            Assert.That(publicCrew.Requests, Is.Empty);
            Assert.That(publicCrew.Code, Is.Null);
            Assert.That(state.Payment, Is.Null);
            await server.WaitPost(() =>
            {
                var profile = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
                var prototypes = server.ResolveDependency<IPrototypeManager>();
                var loadout = profile.GetLoadoutOrDefault("Job" + job, host, profile.Species, server.EntMan, prototypes).Clone();
                loadout.AddLoadout("MercenaryJumpsuit", "MercenaryClothingUniformJumpskirtMercenary", prototypes);
                prefs.SetProfile(host.UserId, slot, profile.WithLoadout(loadout)).Wait();
            });
            await Request(pair, new(WGRoundSetupAction.Join) { LobbyId = id, RoleId = publicCrew.Roles.Single().Id });
            state = await Request(pair, new(WGRoundSetupAction.ConfirmJoin) { LobbyId = id });
            Assert.That(OwnLobby(state).Members.Single(member => member.UserId == host.UserId).Spawned, Is.True, state.Message);
            await server.WaitAssertion(() =>
            {
                Assert.That(server.System<InventorySystem>().TryGetSlotEntity(host.AttachedEntity!.Value, "jumpsuit", out var uniform), Is.True);
                Assert.That(server.EntMan.GetComponent<MetaDataComponent>(uniform!.Value).EntityPrototype!.ID,
                    Is.EqualTo("ClothingUniformJumpskirtMercenary"), "A respawned crew member uses their newly selected job loadout.");
                Assert.That(ships.GetBalance(host), Is.EqualTo(1000000 - price + 500), "Rejoining must not charge the vessel again.");
            });
        }
        finally
        {
            await server.RemoveAllDummySessions();
            await server.WaitPost(() =>
            {
                prefs.SetProfile(host.UserId, slot, original).Wait();
                ticker.RestartRound();
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, roleTimers);
            });
            await pair.RunTicksSync(2);
        }
        await pair.CleanReturnAsync();

        async Task AsCrew(WGRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(crew) }));
            await pair.RunTicksSync(pair.SecondsToTicks(0.5f));
        }
    }
}
