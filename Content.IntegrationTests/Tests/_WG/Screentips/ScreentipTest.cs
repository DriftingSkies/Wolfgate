#nullable enable
using System.Reflection;
using Content.Client._WG.Screentips;
using Content.Client.Gameplay;
using Content.Client.UserInterface.Controls;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WG.Screentips;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Screentips;

/// <summary>
/// A screentip shows the name centred at the top of the view, hides again once the cursor isn't over the view, and
/// keeps its colour after leaving and re-entering the game.
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
