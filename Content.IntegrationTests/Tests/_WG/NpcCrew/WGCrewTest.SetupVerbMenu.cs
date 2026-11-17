#nullable enable
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Collections.Concurrent;
using System.Linq;
using Content.Client._WG.NpcCrew;
using Content.Client.Administration.Managers;
using Content.Client.ContextMenu.UI;
using Content.Client.Verbs.UI;
using Content.Server.Administration.Managers;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Administration;
using Content.Shared.Verbs;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Prediction replays cannot accelerate the crew window's two-second network poll.</summary>
    [Test]
    public async Task CrewSetupPollsFramesNotPredictionReplays()
    {
        var observer = Server.System<WGCrewSetupRequestObserver>();
        observer.Requests.Clear();
        var sentId = 0;
        await Client.WaitAssertion(() =>
        {
            var system = Client.System<WGCrewSetupClientSystem>();
            typeof(WGCrewSetupClientSystem).GetField("_pollTimer", UiPrivate)!.SetValue(system, 0f);
            var firstId = UiField<int>(system, "_nextRequestId");
            Action<WGCrewSetupResponse> listener = _ => { };
            system.Received += listener;
            try
            {
                system.FrameUpdate(1.99f);
                for (var replay = 0; replay < 600; replay++)
                    system.Update(1f / 60f);
                Assert.That(UiField<int>(system, "_nextRequestId"), Is.EqualTo(firstId),
                    "Replayed simulation ticks must not send UI polling requests.");
                system.FrameUpdate(0.02f);
                sentId = UiField<int>(system, "_nextRequestId");
                Assert.That(sentId, Is.EqualTo(firstId + 1));
                system.FrameUpdate(0f);
                Assert.That(UiField<int>(system, "_nextRequestId"), Is.EqualTo(sentId));
            }
            finally
            {
                system.Received -= listener;
            }
            system.FrameUpdate(10f);
            Assert.That(UiField<int>(system, "_nextRequestId"), Is.EqualTo(sentId), "Polling ends with the last window.");
        });
        await RunTicks(10);
        Assert.That(observer.Requests.Count(request => request.RequestId == sentId && request.Action == WGCrewSetupAction.Crews), Is.EqualTo(1));
    }

    /// <summary>Native admin verb categories receive server contents before, during, and after crew setup.</summary>
    [Test]
    public async Task CrewSetupPreservesServerVerbMenus()
    {
        var target = await SpawnTarget("MobHuman");
        var admins = Server.ResolveDependency<IAdminManager>();
        var clientAdmins = Client.ResolveDependency<IClientAdminManager>();
        var wasAdmin = admins.IsAdmin(ServerSession);
        await Server.WaitPost(() =>
        {
            if (admins.IsAdmin(ServerSession, includeDeAdmin: true))
                admins.ReAdmin(ServerSession);
            else
                admins.PromoteHost(ServerSession);
        });
        for (var i = 0; i < 60 && !clientAdmins.HasFlag(AdminFlags.Admin); i++)
            await RunTicks(1);
        Assert.That(clientAdmins.HasFlag(AdminFlags.Admin), Is.True);

        var responses = new ConcurrentQueue<VerbsResponseEvent>();
        var system = Client.System<Content.Client.Verbs.VerbSystem>();
        var context = UiMan.GetUIController<ContextMenuUIController>();
        var verbs = UiMan.GetUIController<VerbMenuUIController>();
        WGCrewSetupWindow? window = null;
        await Client.WaitPost(() => system.OnVerbsResponse += responses.Enqueue);
        try
        {
            await AssertVerbRoundtrip();
            await Client.WaitPost(() =>
            {
                window = new WGCrewSetupWindow();
                window.OpenCentered();
            });
            await RunTicks(5);
            await AssertVerbRoundtrip();
            await ClickControl(UiField<OptionButton>(window!, "_grid"));
            await Client.WaitAssertion(() =>
            {
                var popup = UiField<Popup>(UiField<OptionButton>(window!, "_grid"), "_popup");
                Assert.That(popup.Visible, Is.True);
                window!.Close();
                Assert.That(popup.Parent, Is.Null, "Closing setup must remove its dropdown from the shared modal root.");
                Assert.That(popup.Visible, Is.False);
            });
            await AssertVerbRoundtrip();
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                system.OnVerbsResponse -= responses.Enqueue;
                context.Close();
                window?.Close();
                window?.Teardown();
            });
            if (!wasAdmin)
                await Server.WaitPost(() => admins.DeAdmin(ServerSession));
        }

        async Task AssertVerbRoundtrip()
        {
            responses.Clear();
            await Client.WaitPost(() => verbs.OpenVerbMenu(target, force: true));
            for (var i = 0; i < 30 && !responses.Any(response => response.Entity == target); i++)
                await RunTicks(2);
            await Client.WaitAssertion(() =>
            {
                var response = responses.LastOrDefault(item => item.Entity == target);
                Assert.That(response, Is.Not.Null, "The server must respond to the native verb request.");
                Assert.That(response!.Verbs, Is.Not.Null.And.Not.Empty);
                Assert.That(verbs.OpenMenu, Is.Not.Null);
                Assert.That(verbs.OpenMenu!.Visible, Is.True);
                foreach (var category in new[] { VerbCategory.Admin, VerbCategory.Debug, VerbCategory.Tricks })
                {
                    Assert.That(response.Verbs!.Any(verb => verb.Category?.Text == category.Text), Is.True, category.Text);
                    var entry = verbs.OpenMenu.MenuBody.Children.OfType<VerbMenuElement>().Single(element =>
                        element.Verb == null && element.SubMenu != null && element.SubMenu.MenuBody.Children
                            .OfType<VerbMenuElement>().Any(child => child.Verb?.Category?.Text == category.Text));
                    context.OpenSubMenu(entry);
                    Assert.That(entry.SubMenu!.Visible, Is.True, category.Text);
                    Assert.That(entry.SubMenu.MenuBody.ChildCount, Is.GreaterThan(0), category.Text);
                }
                context.Close();
            });
        }
    }
}
