#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Access.Systems;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._WF.Outposts;
using Content.Shared.Clothing;
using Content.Shared.Mind;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles.Jobs;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.Planets.PlanetFixture;

namespace Content.IntegrationTests.Tests._WF.Outposts;

/// <summary>An outpost's owner joins the round from the lobby in one of its cryopods, as its settler.</summary>
[TestFixture]
[TestOf(typeof(WFOutpostSystem))]
public sealed class OutpostSettlerTest
{
    private const string Title = "Homesteader";

    /// <summary>
    /// Joining needs an outpost and a free cryopod on it; then the owner wakes up in the pod as the settler, with the
    /// title from their settler loadout on their ID. An outpost takes six pods and no more.
    /// </summary>
    [Test]
    public async Task TheOwnerWakesUpInAFreeCryopod()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var outposts = server.System<WFOutpostSystem>();
        var session = pair.Player!;
        await server.WaitPost(() => server.System<GameTicker>().StartRound(force: true));
        await pair.RunTicksSync(2);

        var (layers, viewer) = await OutpostFoundingTest.Setup(pair);
        var ground = layers[0];
        await SetSettlerTitle(pair);
        Entity<WFOutpostComponent> outpost = default;

        try
        {
            await server.WaitAssertion(() =>
            {
                server.PlayerMan.SetAttachedEntity(session, null);
                Assert.That(outposts.TryJoin(session, out var reason), Is.False, "Joined with no outpost.");
                Assert.That(reason, Is.EqualTo("wf-outpost-join-no-outpost"));

                server.PlayerMan.SetAttachedEntity(session, viewer);
                var site = OutpostFoundingTest.Site;
                Assert.That(outposts.TryFound(viewer, OutpostFoundingTest.SpawnKit(entMan, ground),
                    new EntityCoordinates(ground, site.X, site.Y), out var grid, out reason), Is.True, $"Founding was refused: {reason}.");
                outpost = (grid!.Value, entMan.GetComponent<WFOutpostComponent>(grid.Value));
                server.PlayerMan.SetAttachedEntity(session, null);
                entMan.DeleteEntity(viewer);

                Assert.That(outposts.GetJoinOffer(session), Is.Null, "An outpost with no cryopod was offered.");
                Assert.That(outposts.TryJoin(session, out reason), Is.False, "Joined with no cryopod.");
                Assert.That(reason, Is.EqualTo("wf-outpost-join-no-pod"));
            });

            EntityUid pod = default;
            await server.WaitAssertion(() =>
            {
                pod = SpawnPod(pair, outpost, new Vector2i(0, 2));
                Assert.That(outposts.GetJoinOffer(session), Is.EqualTo(entMan.GetComponent<MetaDataComponent>(outpost).EntityName));
                Assert.That(outposts.TryJoin(session, out var reason), Is.True, $"Joining was refused: {reason}.");
            });
            await pair.RunTicksSync(5);

            await server.WaitAssertion(() =>
            {
                var mob = session.AttachedEntity;
                Assert.That(mob, Is.Not.Null, "The owner has no body.");
                var xform = entMan.GetComponent<TransformComponent>(mob!.Value);
                var podXform = entMan.GetComponent<TransformComponent>(pod);
                Assert.That(xform.GridUid, Is.EqualTo(outpost.Owner), "The owner didn't wake up on the outpost.");
                Assert.That((xform.LocalPosition - podXform.LocalPosition).Length(), Is.LessThan(0.5f), "The owner isn't at the pod.");

                var mind = server.System<SharedMindSystem>().GetMind(mob.Value);
                Assert.That(server.System<SharedJobSystem>().MindTryGetJobId(mind, out var job), Is.True);
                Assert.That(job?.Id, Is.EqualTo(WFOutpostLimits.SettlerJob), "The owner isn't the settler.");
                Assert.That(server.System<IdCardSystem>().TryFindIdCard(mob.Value, out var card), Is.True, "The settler has no ID.");
                Assert.That(card.Comp.LocalizedJobTitle, Is.EqualTo(Title), "The ID doesn't carry the settler title.");
                Assert.That(outposts.GetJoinOffer(session), Is.Null, "A player in the round was offered to join again.");

                // Five more pods make six; a seventh is refused.
                foreach (var tile in new[] { new Vector2i(1, 2), new Vector2i(2, 2), new Vector2i(2, 1), new Vector2i(2, 0), new Vector2i(1, 0) })
                    SpawnPod(pair, outpost, tile);
                Assert.That(outposts.CountPods(outpost), Is.EqualTo(WFOutpostSystem.MaxPods));

                var grid = entMan.GetComponent<MapGridComponent>(outpost);
                var transform = server.System<SharedTransformSystem>();
                transform.SetCoordinates(mob.Value, server.System<SharedMapSystem>().GridTileToLocal(outpost, grid, new Vector2i(0, 0)));
                transform.SetWorldRotation(mob.Value, Direction.North.ToAngle());
                var pack = entMan.SpawnEntity("WFOutpostCryopodPack", xform.Coordinates);
                Assert.That(outposts.TryDeploy(mob.Value, (pack, entMan.GetComponent<WFDeployableComponent>(pack)), out _, out var reason),
                    Is.False, "A seventh pod was set up.");
                Assert.That(reason, Is.EqualTo("wf-outpost-cryopod-full"));
            });
        }
        finally
        {
            await Teardown(pair, layers);
        }

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnPod(TestPair pair, Entity<WFOutpostComponent> outpost, Vector2i tile)
    {
        var server = pair.Server;
        var grid = server.EntMan.GetComponent<MapGridComponent>(outpost);
        var pod = server.EntMan.SpawnEntity("WFOutpostCryopod", server.System<SharedMapSystem>().GridTileToLocal(outpost, grid, tile));
        Assert.That(server.EntMan.GetComponent<TransformComponent>(pod).Anchored, Is.True, "Precondition: the pod stands anchored.");
        return pod;
    }

    /// <summary>Gives the selected character a settler loadout with a custom title.</summary>
    private static async Task SetSettlerTitle(TestPair pair)
    {
        var server = pair.Server;
        var session = pair.Player!;
        var saving = Task.CompletedTask;
        await server.WaitPost(() =>
        {
            var prefs = server.ResolveDependency<IServerPreferencesManager>();
            var p = prefs.GetPreferences(session.UserId);
            var profile = (HumanoidCharacterProfile) p.SelectedCharacter;
            var loadout = new RoleLoadout(LoadoutSystem.GetJobPrototype(WFOutpostLimits.SettlerJob));
            loadout.SetDefault(profile, session, server.ProtoMan);
            loadout.CustomJobTitle = Title;
            saving = prefs.SetProfile(session.UserId, p.SelectedCharacterIndex, profile.WithLoadout(loadout));
        });
        await PoolManager.WaitUntil(server, () => saving.IsCompleted, maxTicks: 300);
    }
}
