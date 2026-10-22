#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Administration.Systems;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Shared.Follower.Components;
using Content.Shared.Ghost;
using Robust.Server.Console;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// Admin follow aghosts the admin and orbits the target with the ghost, leaving the body where it was.
/// </summary>
[TestOf(typeof(AdminFollowSystem))]
public sealed class AdminFollowTest : InteractionTest
{
    [Test]
    public async Task FollowAghostsBeforeOrbiting()
    {
        await SpawnTarget("MobHuman");
        var target = STarget!.Value;
        var body = SPlayer;

        var admins = Server.ResolveDependency<IAdminManager>();
        var console = Server.ResolveDependency<IServerConsoleHost>();
        await Server.WaitPost(() =>
        {
            if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
            Server.System<MindSystem>().ControlMob(ServerSession.UserId, body);
        });
        await RunTicks(5);

        await Server.WaitPost(() => console.ExecuteCommand(ServerSession, $"follow {SEntMan.GetNetEntity(target)}"));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            var ghost = ServerSession.AttachedEntity;
            Assert.That(ghost, Is.Not.Null.And.Not.EqualTo(body), "The admin should be moved into a ghost.");
            Assert.That(SEntMan.HasComponent<GhostComponent>(ghost!.Value), Is.True);
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(ghost.Value).EntityPrototype?.ID,
                Is.EqualTo(GameTicker.AdminObserverPrototypeName));
            Assert.That(SEntMan.GetComponent<FollowerComponent>(ghost.Value).Following, Is.EqualTo(target));
            Assert.That(SEntMan.HasComponent<FollowerComponent>(body), Is.False, "The body should stay put.");
        });
    }
}
