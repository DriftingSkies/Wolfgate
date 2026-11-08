#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.RoundSetup;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Server.Voting.Managers;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.RoundSetup;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>An outpost's crew: saved with it, back when it lands, and how later players join it.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostCrewComponent))]
public sealed class OutpostCrewTest
{
    private const int Funds = 1_000_000;
    private const string CrewTitle = "Homestead";
    private const string RoleName = "Digger";
    private const string RoleJob = "Contractor";

    /// <summary>
    /// The owner wakes at their outpost and forms its crew, opens it to the public with a Digger opening and saves it.
    /// Next round the outpost lands for the owner's Outpost crew with that crew restored; a player who joins it mid-round
    /// wakes in a free pod as a Digger, and one who never joined can neither launch with it nor wake there.
    /// </summary>
    [Test]
    public async Task SavedCrewComesBackAndTakesInMembers()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var outposts = server.System<WFOutpostSystem>();
        var setup = server.System<WFRoundSetupSystem>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var host = pair.Player!;
        var hostSlot = prefs.GetPreferences(host.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
        var roleTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        List<EntityUid>? layers = null;

        try
        {
            // Round one: the owner's crew forms on their outpost, opens up and is saved with it.
            await server.WaitPost(() => ticker.StartRound(force: true));
            await pair.RunTicksSync(2);
            var (founding, viewer) = await OutpostFoundingTest.Setup(pair);
            layers = founding;
            EntityUid grid = default;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(viewer, OutpostFoundingTest.SpawnKit(entMan, founding[0]),
                    new EntityCoordinates(founding[0], site.X, site.Y), out var founded, out var reason), Is.True, reason);
                grid = founded!.Value;
                var gridComp = entMan.GetComponent<MapGridComponent>(grid);
                var maps = server.System<SharedMapSystem>();
                foreach (var tile in new[] { new Vector2i(0, 2), new Vector2i(2, 2) })
                {
                    var pod = entMan.SpawnEntity("WFOutpostCryopod", maps.GridTileToLocal(grid, gridComp, tile));
                    Assert.That(entMan.GetComponent<TransformComponent>(pod).Anchored, Is.True, "Precondition: the pod stands anchored.");
                }

                setup.JoinOutpostCrew(host, grid, "Outpost");
            });

            var crewId = LobbyField<int>(host.UserId, "Id");
            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.Configure)
            {
                LobbyId = crewId, Title = CrewTitle, Description = string.Empty, Access = WFLobbyAccess.Public, AllowLateJoin = true,
            });
            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.UpsertRole)
            {
                LobbyId = crewId, RoleName = RoleName, Job = RoleJob, Capacity = 2,
            });
            Assert.That(RoleId(host.UserId, RoleName), Is.Not.Null, "Precondition: the Digger opening was added.");

            await server.WaitAssertion(() =>
            {
                Assert.That(outposts.TrySave(viewer, (grid, entMan.GetComponent<WFOutpostComponent>(grid)), 0, out var reason),
                    Is.True, reason);
                var crew = entMan.GetComponent<WFOutpostCrewComponent>(grid);
                Assert.That(crew.Title, Is.EqualTo(CrewTitle));
                Assert.That(crew.Access, Is.EqualTo(WFLobbyAccess.Public));
                Assert.That(crew.Roles.Select(role => (role.Name, role.Job, role.Capacity)),
                    Is.EquivalentTo(new[] { (RoleName, RoleJob, 2) }), "The save didn't take the crew's openings.");
            });
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(host.UserId), maxTicks: 600);
            var row = await db.GetOutpostSaveDataAsync(host.UserId, 0);
            Assert.That(row, Is.Not.Null, "Precondition: the outpost was saved.");
            await db.SetOutpostSaveAsync(host.UserId, row!.Value.Save with { SavedRound = 0 }, row.Value.Data);

            await Teardown(pair, layers);
            layers = null;
            await server.WaitPost(() => ticker.RestartRound());
            await pair.RunTicksSync(5);

            // Round two: the owner's Outpost crew, made with none of that, lands the save.
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, false);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, false);
                foreach (var vote in server.ResolveDependency<IVoteManager>().ActiveVotes.ToArray())
                    vote.Cancel();
            });
            await EnableFeature(pair);
            await server.WaitPost(() => server.CfgMan.SetCVar(OutpostCVars.Enabled, true));
            (layers, _, _) = await BuildOwnedStack(pair);
            await Fund(host, Funds);

            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Outpost });
            var id = LobbyField<int>(host.UserId, "Id");
            Assert.That(RoleId(host.UserId, RoleName), Is.Null, "Precondition: the new crew has no openings of its own.");
            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = WFOutpostLimits.SettlerJob });
            await server.WaitPost(() => outposts.GetSlots(host.UserId));
            await PoolManager.WaitUntil(server, () => outposts.GetSlots(host.UserId) != null, maxTicks: 600);
            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.SelectOutpost) { LobbyId = id, OutpostSlot = 0 });
            await Act(host, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin)
            {
                LobbyId = id, PaymentRevision = LobbyField<int>(host.UserId, "PaymentRevision"),
            });
            await server.WaitAssertion(() => Assert.That(ticker.PlayerGameStatuses[host.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay)));

            await server.WaitPost(() => ticker.StartRound(force: true));
            for (var i = 0; i < 30 && host.AttachedEntity == null; i++)
                await pair.RunTicksSync(pair.SecondsToTicks(1f));

            EntityUid outpost = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(host.AttachedEntity, Is.Not.Null, "The owner never woke at their outpost.");
                outpost = entMan.GetComponent<TransformComponent>(host.AttachedEntity!.Value).GridUid!.Value;
                Assert.That(entMan.HasComponent<WFOutpostComponent>(outpost), Is.True, "The owner didn't wake on their outpost.");
                Assert.That(LobbyField<string>(host.UserId, "Title"), Is.EqualTo(CrewTitle), "The crew's name didn't come back.");
                Assert.That(LobbyField<WFLobbyAccess>(host.UserId, "Access"), Is.EqualTo(WFLobbyAccess.Public),
                    "The crew's admission didn't come back.");
            });
            var digger = RoleId(host.UserId, RoleName);
            Assert.That(digger, Is.Not.Null, "The crew's Digger opening didn't come back.");

            // Mid-round, a player joins the crew and wakes in its free pod; one outside it can't.
            var others = (await server.AddDummySessions(2)).ToArray();
            var joiner = others[0];
            var outsider = others[1];
            await WaitLoaded(pair, others);

            await Act(joiner, new WFRoundSetupRequest(WFRoundSetupAction.Join) { LobbyId = id, RoleId = digger });
            Assert.That(setup.IsInCrew(joiner.UserId), Is.True, "The public crew didn't take the joiner.");
            await Act(joiner, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin)
            {
                LobbyId = id, PaymentRevision = LobbyField<int>(host.UserId, "PaymentRevision"),
            });
            await Act(outsider, new WFRoundSetupRequest(WFRoundSetupAction.Launch) { LobbyId = id });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                Assert.That(joiner.AttachedEntity, Is.Not.Null, "The joiner never woke up.");
                var body = joiner.AttachedEntity!.Value;
                var xform = entMan.GetComponent<TransformComponent>(body);
                Assert.That(xform.GridUid, Is.EqualTo(outpost), "The joiner didn't wake on the crew's outpost.");
                var pods = entMan.EntityQuery<WFOutpostCryopodComponent>()
                    .Select(comp => comp.Owner)
                    .Where(uid => entMan.GetComponent<TransformComponent>(uid).GridUid == outpost)
                    .ToList();
                Assert.That(pods.Any(pod => (xform.LocalPosition - entMan.GetComponent<TransformComponent>(pod).LocalPosition).Length() < 0.5f),
                    Is.True, "The joiner isn't at a cryopod.");
                var mind = server.System<SharedMindSystem>().GetMind(body);
                Assert.That(server.System<SharedJobSystem>().MindTryGetJobId(mind, out var job), Is.True);
                Assert.That(job?.Id, Is.EqualTo(RoleJob), "The joiner didn't take the Digger opening's job.");

                Assert.That(outsider.AttachedEntity, Is.Null, "A player outside the crew spawned with it.");
                Assert.That(setup.IsInCrew(outsider.UserId), Is.False);
                Assert.That(outposts.GetJoinOffer(outsider), Is.Null, "A player who owns no outpost was offered one.");
            });
        }
        finally
        {
            await server.RemoveAllDummySessions();
            foreach (var stored in await db.GetOutpostSavesAsync(host.UserId))
                await db.DeleteOutpostSaveAsync(host.UserId, stored.Slot);

            if (layers != null)
                await Teardown(pair, layers);
            await server.WaitPost(() =>
            {
                prefs.SetProfile(host.UserId, hostSlot, original).Wait();
                ticker.RestartRound();
                server.CfgMan.SetCVar(CCVars.AutoVoteEnabled, autoVote);
                server.CfgMan.SetCVar(CCVars.GameRoleTimers, roleTimers);
            });
            await pair.RunTicksSync(2);
        }

        await pair.CleanReturnAsync();

        async Task Act(ICommonSession session, WFRoundSetupRequest request)
        {
            await pair.RunTicksSync(pair.SecondsToTicks(0.25f));
            await server.WaitPost(() => onRequest.Invoke(setup, new object[] { request, new EntitySessionEventArgs(session) }));
            await pair.RunTicksSync(1);
        }

        async Task Fund(ICommonSession session, int balance)
        {
            var funding = Task.CompletedTask;
            await server.WaitPost(() =>
            {
                var preferences = prefs.GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) preferences.SelectedCharacter;
                funding = prefs.SetProfile(session.UserId, preferences.SelectedCharacterIndex,
                    profile.WithoutAllTraitPreferences().WithBankBalance(balance));
            });
            await PoolManager.WaitUntil(server, () => funding.IsCompleted, maxTicks: 300);
        }

        object Lobby(NetUserId leader)
        {
            var lobbies = (IDictionary) typeof(WFRoundSetupSystem)
                .GetField("_lobbies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(setup)!;
            foreach (var lobby in lobbies.Values)
            {
                if ((NetUserId) lobby!.GetType().GetField("Host")!.GetValue(lobby)! == leader)
                    return lobby;
            }

            Assert.Fail($"{leader} leads no crew.");
            return null!;
        }

        T LobbyField<T>(NetUserId leader, string field)
        {
            var lobby = Lobby(leader);
            return (T) lobby.GetType().GetField(field)!.GetValue(lobby)!;
        }

        int? RoleId(NetUserId leader, string name)
        {
            var roles = LobbyField<IDictionary>(leader, "Roles");
            foreach (DictionaryEntry entry in roles)
            {
                if ((string) entry.Value!.GetType().GetField("Name")!.GetValue(entry.Value)! == name)
                    return (int) entry.Key;
            }

            return null;
        }
    }

    private static async Task WaitLoaded(TestPair pair, IEnumerable<ICommonSession> sessions)
    {
        var server = pair.Server;
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var userDb = server.ResolveDependency<UserDbDataManager>();
        foreach (var session in sessions)
        {
            await PoolManager.WaitUntil(server, () => session.Status == SessionStatus.InGame && prefs.HavePreferencesLoaded(session)
                && userDb.IsLoadComplete(session), maxTicks: 600);
        }
    }
}
