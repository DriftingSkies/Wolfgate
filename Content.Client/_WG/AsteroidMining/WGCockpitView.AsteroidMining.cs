using System.Numerics;
using Content.Client._WG.AsteroidMining;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Cockpit;

public sealed partial class WGCockpitView
{
    /// <summary>Lays the asteroid scanner over the ship cam and the NAV plot, and takes it off when the cockpit closes.</summary>
    private void InitializeScanner(MainViewport viewport)
    {
        var camera = viewport.Viewport;
        var world = new WGAsteroidScanner(camera, scanner => camera.Eye is { } eye
            ? (camera.GetWorldToScreenMatrix() * Matrix3x2.CreateTranslation(-scanner.GlobalPixelPosition), eye.Position.MapId)
            : null) { Name = "CockpitScanWorld" };
        camera.AddChild(world);

        var navigation = _console.FindControl<NavScreen>("NavContainer").FindControl<ShuttleNavControl>("NavRadar");
        var plot = new WGAsteroidScanner(navigation, _ =>
            navigation.WgCockpitTryGetWorldToView(out var worldToView, out var map) ? (worldToView, map) : null)
        {
            Name = "CockpitScanPlot",
        };
        navigation.AddChild(plot);
        LayoutContainer.SetAnchorPreset(plot, LayoutContainer.LayoutPreset.Wide);
        _lease.Remember(() =>
        {
            world.Orphan();
            plot.Orphan();
        });
    }
}
