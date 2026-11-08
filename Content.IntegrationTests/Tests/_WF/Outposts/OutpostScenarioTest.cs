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

/// <summary>Outpost crews: the leader's saved outpost drops onto a world at round start and the crew wakes in it.</summary>
[TestFixture]
[TestOf(typeof(WFRoundSetupSystem))]
public sealed class OutpostScenarioTest
{
    private const int Funds = 1_000_000;

    /// <summary>
    /// Two Outpost crews readied before the round each land their leader's outpost and wake in its cryopod as the
    /// settler, charged its load price; a third whose leader can't pay starts Normal, uncharged and with no outpost.
    /// </summary>
    [Test]
    public async Task ReadiedCrewsLandAndAnUnpaidOneFallsBack()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = server.System<GameTicker>();
        var outposts = server.System<WFOutpostSystem>();
        var setup = server.System<WFRoundSetupSystem>();
        var ships = server.System<WFShipScenarioSystem>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var onRequest = typeof(WFRoundSetupSystem).GetMethod("OnRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var host = pair.Player!;
        var hostSlot = prefs.GetPreferences(host.UserId).SelectedCharacterIndex;
        var original = (HumanoidCharacterProfile) prefs.GetPreferences(host.UserId).SelectedCharacter!;
        var roleTimers = server.CfgMan.GetCVar(CCVars.GameRoleTimers);
        var autoVote = server.CfgMan.GetCVar(CCVars.AutoVoteEnabled);
        var accounts = new List<NetUserId> { host.UserId };
        List<EntityUid>? layers = null;

        try
        {
            // A save of a founded outpost with a cryopod on it, from a round of its own.
            await server.WaitPost(() => ticker.StartRound(force: true));
            await pair.RunTicksSync(2);
            var (founding, viewer) = await OutpostFoundingTest.Setup(pair);
            layers = founding;
            await server.WaitAssertion(() =>
            {
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(viewer, OutpostFoundingTest.SpawnKit(entMan, founding[0]),
                    new EntityCoordinates(founding[0], site.X, site.Y), out var grid, out var reason), Is.True, reason);
                var gridComp = entMan.GetComponent<MapGridComponent>(grid!.Value);
                var pod = entMan.SpawnEntity("WFOutpostCryopod",
                    server.System<SharedMapSystem>().GridTileToLocal(grid.Value, gridComp, new Vector2i(0, 2)));
                Assert.That(entMan.GetComponent<TransformComponent>(pod).Anchored, Is.True, "Precondition: the pod stands anchored.");
                Assert.That(outposts.TrySave(viewer, (grid.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value)), 0, out reason),
                    Is.True, reason);
            });
            await pair.RunTicksSync(1);
            await PoolManager.WaitUntil(server, () => !outposts.IsBusy(host.UserId), maxTicks: 600);
            var row = await db.GetOutpostSaveDataAsync(host.UserId, 0);
            Assert.That(row, Is.Not.Null, "Precondition: the outpost was saved.");
            var save = row!.Value.Save with { SavedRound = 0 };
            var price = WFOutpostSystem.LoadPrice(save.Appraisal);

            await Teardown(pair, layers);
            layers = null;
            await server.WaitPost(() => ticker.RestartRound());
            await pair.RunTicksSync(5);

            // Back in the lobby: a crash world, and two more leaders with the same save.
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
            var others = (await server.AddDummySessions(2)).ToArray();
            var second = others[0];
            var poor = others[1];
            accounts.AddRange(others.Select(other => other.UserId));
            await WaitLoaded(pair, others);

            foreach (var account in accounts)
                await db.SetOutpostSaveAsync(account, save, row.Value.Data);
            await Fund(host, Funds);
            await Fund(second, Funds);
            await Fund(poor, Funds);

            foreach (var leader in new[] { host, second, poor })
            {
                await Act(leader, new WFRoundSetupRequest(WFRoundSetupAction.Create) { Scenario = WFRoundScenario.Outpost });
                var id = LobbyField<int>(leader.UserId, "Id");
                await Act(leader, new WFRoundSetupRequest(WFRoundSetupAction.SelectJob) { LobbyId = id, Job = WFOutpostLimits.SettlerJob });
                await server.WaitPost(() => outposts.GetSlots(leader.UserId));
                await PoolManager.WaitUntil(server, () => outposts.GetSlots(leader.UserId) != null, maxTicks: 600);
                await Act(leader, new WFRoundSetupRequest(WFRoundSetupAction.SelectOutpost) { LobbyId = id, OutpostSlot = 0 });
                Assert.That(LobbyField<int?>(leader.UserId, "OutpostSlot"), Is.EqualTo(0), "The leader's save wasn't chosen.");
                await Act(leader, new WFRoundSetupRequest(WFRoundSetupAction.ConfirmJoin)
                {
                    LobbyId = id, PaymentRevision = LobbyField<int>(leader.UserId, "PaymentRevision"),
                });
                await server.WaitAssertion(() => Assert.That(ticker.PlayerGameStatuses[leader.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay),
                    "The leader isn't readied."));
            }

            // The third leader's money goes after they readied, so their crew can't pay at round start.
            await Fund(poor, price - 1);
            await server.WaitPost(() => ticker.StartRound(force: true));
            await pair.RunTicksSync(2);

            await server.WaitAssertion(() =>
            {
                Assert.That(LobbyField<WFRoundScenario>(poor.UserId, "Scenario"), Is.EqualTo(WFRoundScenario.Normal),
                    "The crew that couldn't pay didn't fall back to Normal.");
                Assert.That(ships.GetBalance(poor), Is.EqualTo(price - 1), "The crew that couldn't pay was charged.");
                Assert.That(LobbyField<bool>(host.UserId, "OutpostLanding") || LobbyField<EntityUid?>(host.UserId, "OutpostGrid") != null,
                    Is.True, "The first outpost isn't on its way down.");
                Assert.That(LobbyField<bool>(second.UserId, "OutpostLanding") || LobbyField<EntityUid?>(second.UserId, "OutpostGrid") != null,
                    Is.True, "The second outpost isn't on its way down.");
                Assert.That(ships.GetBalance(host), Is.EqualTo(Funds - price), "The first leader wasn't charged the load price.");
                Assert.That(ships.GetBalance(second), Is.EqualTo(Funds - price), "The second leader wasn't charged the load price.");
            });

            for (var i = 0; i < 60 && (host.AttachedEntity == null || second.AttachedEntity == null); i++)
                await pair.RunTicksSync(pair.SecondsToTicks(1f));

            await server.WaitAssertion(() =>
            {
                var grids = new HashSet<EntityUid>();
                foreach (var leader in new[] { host, second })
                {
                    Assert.That(leader.AttachedEntity, Is.Not.Null, $"{leader.Name} never woke up at their outpost.");
                    var body = leader.AttachedEntity!.Value;
                    var xform = entMan.GetComponent<TransformComponent>(body);
                    Assert.That(entMan.TryGetComponent<WFOutpostComponent>(xform.GridUid, out var outpost), Is.True,
                        $"{leader.Name} didn't wake up on an outpost.");
                    Assert.That(outpost!.Owner, Is.EqualTo(leader.UserId), $"{leader.Name} woke up on someone else's outpost.");
                    Assert.That(outpost.Ground, Is.EqualTo(layers[0]), "The outpost didn't land on the world's ground.");
                    grids.Add(xform.GridUid!.Value);

                    var pod = entMan.EntityQuery<WFOutpostCryopodComponent>()
                        .Select(comp => comp.Owner)
                        .Single(uid => entMan.GetComponent<TransformComponent>(uid).GridUid == xform.GridUid);
                    Assert.That((xform.LocalPosition - entMan.GetComponent<TransformComponent>(pod).LocalPosition).Length(),
                        Is.LessThan(0.5f), $"{leader.Name} isn't at the cryopod.");

                    var mind = server.System<SharedMindSystem>().GetMind(body);
                    Assert.That(server.System<SharedJobSystem>().MindTryGetJobId(mind, out var job), Is.True);
                    Assert.That(job?.Id, Is.EqualTo(WFOutpostLimits.SettlerJob), $"{leader.Name} isn't the settler.");
                    Assert.That(LobbyField<bool>(leader.UserId, "Started"), Is.True);
                }

                Assert.That(grids, Has.Count.EqualTo(2), "Both crews landed on the same outpost.");
                Assert.That(outposts.TryGetOutpost(poor.UserId, out _), Is.False, "The crew that couldn't pay got an outpost.");
            });
        }
        finally
        {
            await server.RemoveAllDummySessions();
            foreach (var account in accounts)
            {
                foreach (var stored in await db.GetOutpostSavesAsync(account))
                    await db.DeleteOutpostSaveAsync(account, stored.Slot);
            }

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

        T LobbyField<T>(NetUserId leader, string field)
        {
            var lobbies = (IDictionary) typeof(WFRoundSetupSystem)
                .GetField("_lobbies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(setup)!;
            foreach (var lobby in lobbies.Values)
            {
                if ((NetUserId) lobby!.GetType().GetField("Host")!.GetValue(lobby)! == leader)
                    return (T) lobby.GetType().GetField(field)!.GetValue(lobby)!;
            }

            Assert.Fail($"{leader} leads no crew.");
            return default!;
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
