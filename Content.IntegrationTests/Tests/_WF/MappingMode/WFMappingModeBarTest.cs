#nullable enable
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._WF.MappingMode;
using Content.Client._WF.MappingMode.Modes;
using Content.Client._WF.MappingMode.Screen;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._WF.MappingMode;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.ContentPack;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// The mode bar starts bottom left over the viewport, clear of the menu bar, the quick bar, the cursor readout and the
/// chat at the headless 1280x720 and at smaller UI scales, which stand in for bigger screens; a drop near the right edge snaps it there upright and saves it, a reloaded screen
/// restores it, and Reset layout puts it back.
/// </summary>
[TestFixture]
public sealed class WFMappingModeBarTest
{
    private static Box2 Rect(Control control)
    {
        return Box2.FromDimensions(control.GlobalPosition, control.Size);
    }

    private static bool Overlap(Box2 a, Box2 b)
    {
        return a.Left < b.Right && b.Left < a.Right && a.Bottom < b.Top && b.Bottom < a.Top;
    }

    [Test]
    public async Task ModeBarPlacesSnapsAndSaves()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var client = pair.Client;
        var clientSys = client.EntMan.System<WFMappingClientSystem>();
        var uiMan = client.ResolveDependency<IUserInterfaceManager>();
        var states = client.ResolveDependency<IStateManager>();
        var clyde = client.ResolveDependency<IClyde>();
        var userData = client.ResolveDependency<IResourceManager>().UserData;
        var path = WFMappingScreenUIController.LayoutPath;
        await Enable(pair);

        await client.WaitPost(() =>
        {
            if (userData.Exists(path))
                userData.Delete(path);

            clientSys.Send(new WFMappingCreateRequest("bar test"));
        });

        for (var i = 0; i < 100 && uiMan.ActiveScreen is not WFMappingScreen; i++)
        {
            await pair.RunTicksSync(1);
        }

        await pair.RunTicksSync(20);

        // The default spot at 1280x720, and on bigger screens: a smaller UI scale gives a bigger virtual screen.
        void AssertDefaultSpot(string scale)
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            var bar = Rect(screen.ModeBar);
            var view = Rect(screen.ViewportContainer);
            var when = $"UI scale {scale}, screen {clyde.ScreenSize}, viewport {view}, bar {bar}";
            TestContext.Out.WriteLine(when);

            Assert.Multiple(() =>
            {
                Assert.That(screen.ModeBar.Buttons.ChildCount, Is.EqualTo(Enum.GetValues<WFMappingBuildMode>().Length));
                Assert.That(screen.Layout.Bar, Is.Null, "No saved layout: the default spot.");
                Assert.That(bar.Width, Is.Positive, when);
                Assert.That(view.Contains(bar.BottomLeft) && view.Contains(bar.TopRight), Is.True,
                    $"The bar sits inside the viewport. {when}");
                Assert.That(bar.Left - view.Left, Is.EqualTo(WFMappingScreenLayout.Margin).Within(1f),
                    $"The bar starts on the left. {when}");
                Assert.That(bar.Center.Y, Is.GreaterThan(view.Center.Y), $"The bar sits in the bottom half. {when}");

                var topBar = screen.GetWidget<GameTopMenuBar>()!;
                Assert.That(Overlap(bar, Rect(topBar)), Is.False, $"Clear of the menu bar {Rect(topBar)}. {when}");
                Assert.That(Overlap(bar, Rect(screen.QuickBarHost)), Is.False,
                    $"Clear of the quick bar {Rect(screen.QuickBarHost)}. {when}");
                Assert.That(Overlap(bar, Rect(screen.StatusHost)), Is.False,
                    $"Clear of the cursor readout {Rect(screen.StatusHost)}. {when}");
                Assert.That(Overlap(bar, Rect(screen.GetWidget<ChatBox>()!)), Is.False, $"Clear of the chat. {when}");
            });
        }

        var oldScale = 1f;
        await client.WaitPost(() =>
        {
            Assert.That(clyde.ScreenSize, Is.EqualTo(new Vector2i(1280, 720)));
            oldScale = client.CfgMan.GetCVar(CVars.DisplayUIScale);
        });

        foreach (var scale in new[] { 1f, 0.75f, 0.5f })
        {
            await client.WaitPost(() => client.CfgMan.SetCVar(CVars.DisplayUIScale, scale));
            await pair.RunTicksSync(10);
            await client.WaitAssertion(() => AssertDefaultSpot(scale.ToString(CultureInfo.InvariantCulture)));
        }

        await client.WaitPost(() => client.CfgMan.SetCVar(CVars.DisplayUIScale, oldScale));
        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;

            // Dropped near the middle of the right edge: snapped there, upright, and saved.
            var area = screen.ViewportContainer.Size;
            screen.MoveBar(new Vector2(area.X - screen.ModeBar.Size.X - 20f, area.Y / 3f));
            screen.DropBar();
            Assert.Multiple(() =>
            {
                Assert.That(screen.Layout.Bar?.X, Is.EqualTo(1f));
                Assert.That(screen.Layout.Vertical, Is.True, "A side edge stands the bar upright.");
                Assert.That(screen.ModeBar.Vertical, Is.True);
                Assert.That(userData.Exists(path), Is.True, "The layout is saved.");
            });
        });

        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            var bar = Rect(screen.ModeBar);
            var view = Rect(screen.ViewportContainer);
            Assert.That(view.Right - bar.Right, Is.EqualTo(WFMappingScreenLayout.Margin).Within(1f),
                $"The bar sits on the right edge. Viewport {view}, bar {bar}");

            // A reloaded screen reads the saved place back.
            ((GameplayState) states.CurrentState).ReloadMainScreen();
        });

        await pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            Assert.Multiple(() =>
            {
                Assert.That(screen.Layout.Bar?.X, Is.EqualTo(1f), "The saved layout is restored.");
                Assert.That(screen.Layout.Vertical, Is.True);
            });

            uiMan.GetUIController<WFMappingScreenUIController>().ResetLayout();
            Assert.Multiple(() =>
            {
                Assert.That(screen.Layout.Bar, Is.Null, "Reset puts the bar back in its default spot.");
                Assert.That(userData.Exists(path), Is.False, "Reset forgets the saved layout.");
            });
        });

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var screen = (WFMappingScreen) uiMan.ActiveScreen!;
            var bar = Rect(screen.ModeBar);
            var view = Rect(screen.ViewportContainer);
            Assert.That(bar.Left - view.Left, Is.EqualTo(WFMappingScreenLayout.Margin).Within(1f),
                $"Back on the left. Viewport {view}, bar {bar}");
        });

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
