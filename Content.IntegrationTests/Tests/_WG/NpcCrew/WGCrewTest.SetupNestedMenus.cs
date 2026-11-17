#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WG.Ghost;
using Content.Client._WG.NpcCrew;
using Content.Client.Administration.Managers;
using Content.Client.ContextMenu.UI;
using Content.Client.UserInterface.Systems.Ghost.Widgets;
using Content.Client.Verbs.UI;
using Content.Server._WG.Wolfmed.Life;
using Content.Server.Administration.Managers;
using Content.Server.Warps;
using Content.Server.Mind;
using Content.Shared._WG.Ghost;
using Content.Shared._WG.CCVar;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared.Administration;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Grouped entity hover menus and the real ghost warp window still receive server data around crew setup.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewSetupPreservesNestedHoverVerbsAndGhostWarpReplies(bool ordinaryGhost)
    {
        var first = await SpawnTarget("MobHuman");
        var second = await SpawnTarget("MobHuman");
        var third = await SpawnTarget("WallSolid");
        var minds = Server.System<MindSystem>();
        var console = Server.ResolveDependency<IServerConsoleHost>();
        var configuration = Server.ResolveDependency<IConfigurationManager>();
        var diagnostics = configuration.GetCVar(NpcCrewCVars.UiDiagnostics);
        var admins = Server.ResolveDependency<IAdminManager>();
        var wasAdmin = admins.IsAdmin(ServerSession);
        await Server.WaitPost(() =>
        {
            configuration.SetCVar(NpcCrewCVars.UiDiagnostics, true);
            if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
            SEntMan.EnsureComponent<WarpPointComponent>(SEntMan.GetEntity(third)).Location = "Crew UI regression destination";
            minds.ControlMob(ServerSession.UserId, SPlayer);
            console.ExecuteCommand(ServerSession, "aghost");
        });
        var clientAdmins = Client.ResolveDependency<IClientAdminManager>();
        var clientGhosts = Client.System<Content.Client.Ghost.GhostSystem>();
        for (var i = 0; i < 60 && (!clientAdmins.HasFlag(AdminFlags.Admin) || !clientGhosts.IsGhost); i++)
            await RunTicks(1);
        Assert.That(clientAdmins.HasFlag(AdminFlags.Admin) && clientGhosts.IsGhost, Is.True);

        var replies = new ConcurrentQueue<VerbsResponseEvent>();
        var warpReplies = new ConcurrentQueue<GhostOrbitTargetsEvent>();
        var requests = new ConcurrentQueue<object>();
        var verbSystem = Client.System<Content.Client.Verbs.VerbSystem>();
        var orbitSystem = Client.System<Content.Client._WG.Ghost.GhostOrbitSystem>();
        var context = UiMan.GetUIController<ContextMenuUIController>();
        var entities = UiMan.GetUIController<EntityMenuUIController>();
        var verbs = UiMan.GetUIController<VerbMenuUIController>();
        var grouping = (int) UiProperty(entities, "GroupingContextMenuType")!;
        GhostOrbitWindow warpWindow = default!;
        await Client.WaitAssertion(() =>
        {
            var gui = UiMan.GetActiveUIWidgetOrNull<GhostGui>();
            Assert.That(gui, Is.Not.Null);
            warpWindow = gui!.TargetWindow;
        });
        WGCrewSetupWindow? crewWindow = null;
        await Client.WaitPost(() => verbSystem.OnVerbsResponse += replies.Enqueue);
        await Client.WaitPost(() => orbitSystem.TargetsReceived += warpReplies.Enqueue);
        await Server.WaitPost(() => SEntMan.EntityNetManager!.ReceivedSystemMessage += CaptureRequest);
        try
        {
            await CheckMenus();
            await Client.WaitPost(() =>
            {
                crewWindow = new WGCrewSetupWindow();
                crewWindow.OpenCentered();
            });
            await RunTicks(10);
            await CheckMenus();
            await Client.WaitPost(() => crewWindow!.Close());
            await CheckMenus();

            for (var cycle = 0; cycle < 2; cycle++)
            {
                await Server.WaitAssertion(() =>
                {
                    var ghost = ServerSession.AttachedEntity!.Value;
                    Assert.That(SEntMan.HasComponent<GhostComponent>(ghost), Is.True);
                    console.ExecuteCommand(ServerSession, $"spawnoutfitghost {SEntMan.GetNetEntity(ghost).Id} ERTEngineerGearEVA");
                    var body = ServerSession.AttachedEntity!.Value;
                    Assert.That(body, Is.Not.EqualTo(ghost));
                    Assert.That(SEntMan.HasComponent<GhostComponent>(body), Is.False);
                    Assert.That(SEntMan.GetComponent<ActorComponent>(body).PlayerSession, Is.SameAs(ServerSession));
                });
                await RunTicks(10);
                await Server.WaitAssertion(() =>
                {
                    var body = ServerSession.AttachedEntity!.Value;
                    var brain = Server.System<WolfmedLifeSystem>().GetBrainOrgan(body);
                    Assert.That(brain, Is.Not.Null);
                    Server.System<OrganHealthSystem>().SetHealth(brain!.Value, FixedPoint2.Zero);
                });
                await RunTicks(60);
                await Server.WaitAssertion(() =>
                {
                    var body = ServerSession.AttachedEntity!.Value;
                    Assert.That(Server.System<MobStateSystem>().IsDead(body), Is.True);
                    if (ordinaryGhost)
                    {
                        Assert.That(minds.TryGetMind(ServerSession, out var mindId, out var mind), Is.True);
                        Assert.That(Server.System<Content.Server.Ghost.GhostSystem>().OnGhostAttempt(mindId, true, mind: mind), Is.True);
                    }
                    else
                    {
                        console.ExecuteCommand(ServerSession, "aghost");
                    }
                    var ghost = ServerSession.AttachedEntity!.Value;
                    Assert.That(SEntMan.HasComponent<GhostComponent>(ghost), Is.True);
                    Assert.That(SEntMan.GetComponent<ActorComponent>(ghost).PlayerSession, Is.SameAs(ServerSession));
                    Assert.That(admins.IsAdmin(ghost), Is.True);
                });
                await RunTicks(10);
                await Client.WaitAssertion(() =>
                {
                    Assert.That(clientGhosts.IsGhost, Is.True);
                    Assert.That(CEntMan.GetNetEntity(ClientSession.AttachedEntity), Is.EqualTo(SEntMan.GetNetEntity(ServerSession.AttachedEntity)));
                });
                await CheckMenus();
            }
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                verbSystem.OnVerbsResponse -= replies.Enqueue;
                orbitSystem.TargetsReceived -= warpReplies.Enqueue;
                UiMan.SetHovered(null);
                entities.OnGroupingChanged(grouping);
                crewWindow?.Close();
                crewWindow?.Teardown();
                warpWindow.Close();
            });
            await Server.WaitPost(() =>
            {
                SEntMan.EntityNetManager!.ReceivedSystemMessage -= CaptureRequest;
                minds.ControlMob(ServerSession.UserId, SPlayer);
                configuration.SetCVar(NpcCrewCVars.UiDiagnostics, diagnostics);
                if (!wasAdmin)
                    admins.DeAdmin(ServerSession);
            });
            await RunTicks(5);
        }

        void CaptureRequest(object? sender, object request)
        {
            if (request is RequestServerVerbsEvent or GhostOrbitRequestEvent)
                requests.Enqueue(request);
        }

        async Task Hover(ContextMenuElement entry)
        {
            await Client.WaitAssertion(() =>
            {
                // Headless Clyde has no movable cursor; advance the native timer before layout resamples (0, 0).
                UiMan.SetHovered(entry);
                Client.ResolveDependency<ITimerManager>().UpdateTimers(new FrameEventArgs(0.21f));
                Assert.That(entry.SubMenu?.Visible, Is.True, "The ordinary hover timer must open the submenu.");
            });
        }

        async Task CheckMenus()
        {
            requests.Clear();
            replies.Clear();
            warpReplies.Clear();
            EntityMenuElement group = default!;
            await Client.WaitPost(() =>
            {
                entities.OnGroupingChanged(1);
                entities.OpenRootMenu(new List<EntityUid> { CEntMan.GetEntity(first), CEntMan.GetEntity(second), CEntMan.GetEntity(third) });
                group = context.RootMenu.MenuBody.Children.OfType<EntityMenuElement>().Single(element => element.Count == 2);
            });
            await Hover(group);
            EntityMenuElement entity = default!;
            await Client.WaitPost(() => entity = group.SubMenu!.MenuBody.Children.OfType<EntityMenuElement>().Single(element => element.Entity == CEntMan.GetEntity(first)));
            await Hover(entity);
            for (var i = 0; i < 60 && !replies.Any(reply => reply.Entity == first); i++)
                await RunTicks(1);
            await Client.WaitAssertion(() =>
            {
                Assert.That(requests.OfType<RequestServerVerbsEvent>().Any(request => request.EntityUid == first), Is.True, "The nested hover must reach the server.");
                Assert.That(replies.Any(reply => reply.Entity == first), Is.True, "The server's reply must reach the client.");
                Assert.That(verbs.OpenMenu, Is.SameAs(entity.SubMenu));
            });
            foreach (var category in new[] { VerbCategory.Admin, VerbCategory.Debug, VerbCategory.Tricks })
            {
                VerbMenuElement entry = default!;
                await Client.WaitPost(() => entry = verbs.OpenMenu!.MenuBody.Children.OfType<VerbMenuElement>().Single(element =>
                    element.Verb == null && element.SubMenu != null && element.SubMenu.MenuBody.Children.OfType<VerbMenuElement>()
                        .Any(child => child.Verb?.Category?.Text == category.Text)));
                await Hover(entry);
                await Client.WaitAssertion(() => Assert.That(entry.SubMenu!.MenuBody.ChildCount, Is.GreaterThan(0), category.Text));
            }
            await Client.WaitPost(() => { UiMan.SetHovered(null); context.Close(); });

            await Client.WaitAssertion(() =>
            {
                Assert.That(UiMan.GetActiveUIWidgetOrNull<GhostGui>()?.TargetWindow, Is.SameAs(warpWindow));
                warpWindow.OpenCentered();
            });
            try
            {
                for (var i = 0; i < 60; i++)
                {
                    var received = false;
                    await Client.WaitPost(() => received = UiField<List<GhostOrbitTarget>>(warpWindow, "_targets").Any(target => target.Entity == third));
                    if (received && warpReplies.Any(reply => reply.Targets.Any(target => target.Entity == third)))
                        break;
                    await RunTicks(1);
                }
                await Client.WaitAssertion(() =>
                {
                    Assert.That(requests.OfType<GhostOrbitRequestEvent>(), Is.Not.Empty, "The real Ghost Warp window must reach the server.");
                    Assert.That(UiField<List<GhostOrbitTarget>>(warpWindow, "_targets").Any(target => target.Entity == third), Is.True,
                        "Ghost Warp must display the server's known destination.");
                });
            }
            finally
            {
                await Client.WaitPost(() => warpWindow.Close());
            }
        }
    }
}
