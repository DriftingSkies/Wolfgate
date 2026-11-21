#nullable enable
using System.Numerics;
using System.Reflection;
using Content.Client._WG.Screentips;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Examine;
using Content.Shared._WG.Screentips;
using Robust.Client.Graphics;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Screentips;

/// <summary>
/// A screentip shows the name centred at the top of the view, hides again once the cursor isn't over the view, skips
/// mobs in the dark and anything behind a wall or out of examine range, and keeps its colour after leaving and
/// re-entering the game.
/// </summary>
[TestOf(typeof(ScreentipUIController))]
public sealed class ScreentipTest : InteractionTest
{
    [Test]
    public async Task ShowsAtTopOfViewThenHides()
    {
        await SpawnTarget("Crowbar");
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var tips = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ScreentipUIController>();
        var panelField = typeof(ScreentipUIController).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            await Client.WaitAssertion(() =>
            {
                cfg.SetCVar(ScreentipCVars.Mode, (int) ScreentipMode.TopOfView);
                var viewport = (Control) ((MainViewport) GetWidget<MainViewport>()).Viewport;

                tips.Show(CTarget!.Value, "a crowbar", ScreentipMode.TopOfView, viewport);
                Assert.That(tips.Shown, Is.EqualTo(CTarget));
                Assert.That(tips.Text, Is.EqualTo("a crowbar"));

                var panel = (PanelContainer) panelField.GetValue(tips)!;
                Assert.That(panel.Visible, Is.True);
                var centre = panel.GetValue<float>(LayoutContainer.MarginLeftProperty) + panel.DesiredSize.X / 2;
                Assert.That(centre, Is.EqualTo(viewport.GlobalPosition.X + viewport.Size.X / 2).Within(1f),
                    "The tip is centred over the view.");
            });

            // The headless client's cursor is never over the view, so the next frame hides the tip.
            await RunTicks(3);
            await Client.WaitAssertion(() => Assert.That(tips.Shown, Is.Null));
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(ScreentipCVars.Mode, ScreentipCVars.Mode.DefaultValue));
        }
    }

    [Test]
    public async Task MobInDarkIsNotNamed()
    {
        await SpawnTarget("MobHuman");
        var tips = Client.System<WGHoverDarknessSystem>();

        // The test map has no ambient light and no lamps.
        await RunTicks(3);
        await Client.WaitAssertion(() => Assert.That(tips.InDark(CTarget!.Value), Is.True, "A mob in an unlit room is in the dark."));

        await Server.WaitPost(() =>
        {
            var lights = Server.System<SharedPointLightSystem>();
            var light = lights.EnsureLight(STarget!.Value);
            lights.SetRadius(STarget!.Value, 5f, light);
            lights.SetEnergy(STarget!.Value, 1f, light);
            lights.SetEnabled(STarget!.Value, true, light);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(tips.InDark(CTarget!.Value), Is.False, "A lit mob is named."));
    }

    [Test]
    public async Task DarknessGateFollowsViewLighting()
    {
        await SpawnTarget("MobHuman");
        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ScreentipUIController>();
        var darkness = Client.System<WGHoverDarknessSystem>();
        var lightManager = Client.ResolveDependency<ILightManager>();
        var darknessField = typeof(ScreentipUIController).GetField("_darkness", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lightingWas = lightManager.Enabled;
        await RunTicks(3);

        try
        {
            await Client.WaitAssertion(() =>
            {
                Assert.That(darknessField.GetValue(controller), Is.SameAs(darkness), "The controller gets the darkness system.");

                var viewport = ((MainViewport) GetWidget<MainViewport>()).Viewport;
                lightManager.Enabled = true;
                Assert.That(darkness.HiddenInDark(CTarget!.Value, viewport), Is.True, "An unlit mob is hidden while the view draws lighting.");

                // A ghost's lighting toggle turns lighting off, and then everything is visible.
                lightManager.Enabled = false;
                Assert.That(darkness.HiddenInDark(CTarget!.Value, viewport), Is.False, "Nothing is hidden with lighting off.");
            });
        }
        finally
        {
            await Client.WaitPost(() => lightManager.Enabled = lightingWas);
        }
    }

    [Test]
    public async Task ItemInDarkIsStillNamed()
    {
        await SpawnTarget("Crowbar");
        var tips = Client.System<WGHoverDarknessSystem>();
        await RunTicks(3);
        await Client.WaitAssertion(() => Assert.That(tips.InDark(CTarget!.Value), Is.False, "Only players and mobs hide in the dark."));
    }

    [Test]
    public async Task NotNamedBehindWallsOrOutOfRange()
    {
        await SpawnTarget("Crowbar");
        var tips = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ScreentipUIController>();
        // UI system dependencies are matched on the system's exact type; a shared base type is never filled in.
        var examine = typeof(ScreentipUIController).GetField("_examine", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(examine.GetValue(tips), Is.Not.Null, "The controller gets the examine system.");

        // The test mob can't examine at all; a real player mob can, and the component isn't networked.
        await Client.WaitPost(() => CEntMan.EnsureComponent<ExaminerComponent>(CPlayer));
        await Client.WaitAssertion(() => Assert.That(tips.CanSee(CTarget!.Value), Is.True, "An item beside the player is named."));

        await Server.WaitPost(() =>
        {
            var player = Transform.GetMapCoordinates(SPlayer);
            Transform.SetMapCoordinates(STarget!.Value, player.Offset(new Vector2(3, 0)));
            SEntMan.SpawnEntity("WallSolid", player.Offset(new Vector2(2, 0)));
        });
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(tips.CanSee(CTarget!.Value), Is.False, "An item behind a wall is not named."));

        await Server.WaitPost(() =>
            Transform.SetMapCoordinates(STarget!.Value, Transform.GetMapCoordinates(SPlayer).Offset(new Vector2(0, 20))));
        await RunTicks(5);
        await Client.WaitAssertion(() => Assert.That(tips.CanSee(CTarget!.Value), Is.False, "An item past examine range is not named."));
    }

    [Test]
    public async Task ColourSurvivesReentry()
    {
        await SpawnTarget("Crowbar");
        var cfg = Client.ResolveDependency<IConfigurationManager>();
        var states = Client.ResolveDependency<IStateManager>();
        var tips = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ScreentipUIController>();
        var labelField = typeof(ScreentipUIController).GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            await Client.WaitAssertion(() =>
            {
                cfg.SetCVar(ScreentipCVars.Color, "#ff0000");
                var viewport = (Control) ((MainViewport) GetWidget<MainViewport>()).Viewport;
                tips.Show(CTarget!.Value, "a crowbar", ScreentipMode.TopOfView, viewport);

                // Leaving and re-entering the game builds a new label, which must still take the colour.
                var gameplay = (GameplayState) states.CurrentState;
                tips.OnStateExited(gameplay);
                tips.OnStateEntered(gameplay);
                tips.Show(CTarget!.Value, "a crowbar", ScreentipMode.TopOfView, viewport);

                var label = (Label) labelField.GetValue(tips)!;
                Assert.That(label.FontColorOverride, Is.EqualTo(Color.Red));
            });
        }
        finally
        {
            await Client.WaitPost(() => cfg.SetCVar(ScreentipCVars.Color, ScreentipCVars.Color.DefaultValue));
        }
    }
}
