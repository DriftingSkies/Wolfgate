#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>The sector map: stations reach the client before any claim, and the map window lays out and fits them.</summary>
[TestOf(typeof(WFSectorMapControl))]
public sealed class WFSectorMapTest : WFSectorTestBase
{
    private static readonly WFSectorCell Held = new(4, -2);
    private static readonly Vector2 Outpost = new(4000f, 1500f);
    private static readonly Vector2 Depot = new(-9000f, 6000f);

    private void SpawnStations()
    {
        SpawnStation(Outpost, "Test Outpost");
        SpawnStation(Depot, "Test Depot");
    }

    /// <summary>A player is sent the sector map's stations with no territory held, and the client keeps them, nearest the origin first.</summary>
    [Test]
    public async Task LandmarksReachTheClientBeforeAnyClaim()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            SpawnStations();
            var sync = Server.System<WFSectorSyncSystem>();
            var ev = sync.Build(MapData.MapId);
            Assert.That(ev.Claims, Is.Empty);
            Assert.That(ev.Landmarks.Select(landmark => landmark.Name), Is.EquivalentTo(new[] { "Test Outpost", "Test Depot" }));
            sync.SendTo(ServerSession);
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True, "Stations alone give the client a view of the map.");
            Assert.That(view.Owners, Is.Empty);
            Assert.That(view.Landmarks.Select(landmark => landmark.Name), Is.EqualTo(new[] { "Test Outpost", "Test Depot" }),
                "Nearest the origin first.");
            Assert.That((view.Landmarks[0].Position - Outpost).Length(), Is.LessThan(2f), "At the centre of the station's bounds.");
            Assert.That(view.Extent, Is.Not.Null);
        });
    }

    /// <summary>The map window opens, lays out with the map above its bar, fits the stations and the held cell, zooms about a point and reads out cells.</summary>
    [Test]
    public async Task MapWindowLaysOutAndFitsTheSector()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            SpawnStations();
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Held, Holder, null), Is.True);
        });

        // The claim's broadcast carries the stations too.
        await RunTicks(90);
        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(controller.Window, Is.Not.Null);
            var panel = controller.Window!.Panel;
            var map = panel.MapControl;
            Assert.That(map.TryGetView(out var view), Is.True, "The window shows the player's map.");
            Assert.That(view.Map, Is.EqualTo(MapData.MapId));
            Assert.That(view.Landmarks, Has.Count.EqualTo(2));

            Assert.That(map.PixelSize.X, Is.GreaterThan(300));
            Assert.That(map.PixelSize.Y, Is.GreaterThan(200));
            Assert.That(panel.CentreButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.WholeSectorButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.CentreButton.GlobalPosition.Y, Is.GreaterThanOrEqualTo(map.GlobalPosition.Y + map.Size.Y),
                "The bar sits under the map.");
            Assert.That(panel.ScaleLabel.Text, Does.EndWith("km across"));

            // Fitted to every station and the held cell, inside the zoom limits.
            var fitted = map.Range;
            Assert.That(fitted, Is.InRange(WFSectorMapControl.MinRange, WFSectorMapControl.MaxRange));
            var toView = map.WorldToView();
            var box = new UIBox2(Vector2.Zero, map.PixelSize);
            foreach (var point in new[] { Outpost, Depot, CentreOf(Held) })
            {
                Assert.That(box.Contains(Vector2.Transform(point, toView)), Is.True, $"{point} is on the map.");
            }

            var depot = Vector2.Transform(Depot, toView);
            Assert.That((map.ViewToWorld(depot) - Depot).Length(), Is.LessThan(1f), "Pixels map back to the same place.");

            Assert.That(map.Readout(view, CentreOf(Held)), Is.EqualTo($"{WFSectorHex.Callsign(Held)}: Faction id"));
            var open = new WFSectorCell(-6, 3);
            Assert.That(map.Readout(view, CentreOf(open)), Is.EqualTo($"{WFSectorHex.Callsign(open)}: open space"));

            // Zooming keeps the point under the cursor where it is, and stops at the limits.
            map.ZoomAt(depot, 0.5f);
            Assert.That(map.Range, Is.EqualTo(MathF.Max(fitted * 0.5f, WFSectorMapControl.MinRange)).Within(1f));
            Assert.That((map.ViewToWorld(depot) - Depot).Length(), Is.LessThan(1f));
            map.ZoomAt(depot, 1000f);
            Assert.That(map.Range, Is.EqualTo(WFSectorMapControl.MaxRange));
            map.ZoomAt(depot, 0.0001f);
            Assert.That(map.Range, Is.EqualTo(WFSectorMapControl.MinRange));
            map.ShowWholeSector();
            Assert.That(map.Range, Is.EqualTo(fitted).Within(1f));

            // The test player stands on the test map's grid.
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.True, "The player's own grid is marked.");
            Assert.That(map.CentreOnPlayer(), Is.True);
        });

        await Client.WaitPost(() => controller.ToggleWindow());
        await RunTicks(2);
        Assert.That(controller.Window, Is.Null, "Toggling closes the window.");
    }

    /// <summary>The shuttle console's sector mode shows the map, and another mode hides it.</summary>
    [Test]
    public async Task ShuttleConsoleHasASectorMode()
    {
        await Client.WaitAssertion(() =>
        {
            using var console = new ShuttleConsoleWindow();
            var panel = console.FindControl<WFSectorMapPanel>("SectorContainer");
            Assert.That(panel.Visible, Is.False);
            console.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Sector);
            Assert.That(panel.Visible, Is.True);
            console.SwitchMode(ShuttleConsoleWindow.ShuttleConsoleMode.Nav);
            Assert.That(panel.Visible, Is.False);
        });
    }

    /// <summary>The top bar has a sector map button with an icon, a tooltip and the key; clicking it, the key and the command all open and close the window, and the button stays pressed while the window is open.</summary>
    [Test]
    public async Task TopBarButtonAndKeyToggleTheWindow()
    {
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        var bar = GetWidget<GameTopMenuBar>();
        var button = GetControlFromField<MenuButton>("SectorMapButton", bar);
        await Client.WaitAssertion(() =>
        {
            Assert.That(button.Visible, Is.True);
            Assert.That(button.Icon, Is.Not.Null, "The icon comes from menu.rsi.");
            Assert.That(button.ToolTip, Is.EqualTo("Open the sector map."));
            Assert.That(button.BoundKey, Is.EqualTo(ContentKeyFunctions.OpenSectorMap));
            Assert.That(button.Pressed, Is.False);
            Assert.That(controller.Window, Is.Null);

            // Every context a player can be in, ghosts included, has the key; N is bound by default.
            foreach (var context in new[] { "common", "human", "ghost", "aghost" })
            {
                Assert.That(InputManager.Contexts.GetContext(context).FunctionExistsHierarchy(ContentKeyFunctions.OpenSectorMap), Is.True, context);
            }

            Assert.That(InputManager.AllBindings.Any(binding => binding.Function == ContentKeyFunctions.OpenSectorMap && binding.BaseKey == Keyboard.Key.N),
                Is.True, "N opens the sector map by default.");
        });

        await ClickControl(button);
        Assert.That(controller.Window, Is.Not.Null, "Clicking the button opens the window.");
        Assert.That(button.Pressed, Is.True);

        await ClickControl(button);
        Assert.That(controller.Window, Is.Null, "Clicking it again closes the window.");
        Assert.That(button.Pressed, Is.False);

        await PressClientKey(ContentKeyFunctions.OpenSectorMap);
        Assert.That(controller.Window, Is.Not.Null, "The key opens the window.");
        Assert.That(button.Pressed, Is.True, "The button follows the window.");

        await PressClientKey(ContentKeyFunctions.OpenSectorMap);
        Assert.That(controller.Window, Is.Null, "The key closes it.");
        Assert.That(button.Pressed, Is.False);

        await Client.WaitPost(() => controller.OpenWindow());
        Assert.That(button.Pressed, Is.True, "A window opened another way presses the button.");
        await Client.WaitPost(() => controller.Window!.Close());
        Assert.That(controller.Window, Is.Null);
        Assert.That(button.Pressed, Is.False, "Closing the window lets the button up.");
    }

    /// <summary>A ghost is sent the sector map when it attaches, with no claim and no earlier broadcast, and the window centres on the ghost, over open space or over a grid.</summary>
    [Test]
    public async Task GhostGetsTheSectorMapAndCentresOnItself()
    {
        await StartRound();
        var station = EntityUid.Invalid;
        await Server.WaitAssertion(() =>
        {
            station = SpawnStation(Outpost, "Test Outpost");
            SpawnStation(Depot, "Test Depot");
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
            Assert.That(Client.System<WFSectorClientSystem>().TryGetMap(MapData.MapId, out _), Is.False, "Nothing has been sent yet."));

        var open = new Vector2(6000f, 2500f);
        var ghost = EntityUid.Invalid;
        var netGhost = NetEntity.Invalid;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity("MobObserver", new EntityCoordinates(MapData.MapUid, open));
            netGhost = SEntMan.GetNetEntity(ghost);
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(30);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player is the ghost.");
        var controller = UiMan.GetUIController<WFSectorMapUIController>();
        await Client.WaitPost(() => controller.OpenWindow());
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.HasComponent<GhostComponent>(CEntMan.GetEntity(netGhost)), Is.True);
            var map = controller.Window!.Panel.MapControl;
            Assert.That(map.TryGetView(out var view), Is.True, "The ghost is sent the territory when it attaches.");
            Assert.That(view.Map, Is.EqualTo(MapData.MapId));
            Assert.That(view.Landmarks.Select(landmark => landmark.Name), Is.EquivalentTo(new[] { "Test Outpost", "Test Depot" }));

            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.False, "A ghost is aboard nothing.");
            Assert.That(map.CentreOnPlayer(), Is.True);
            Assert.That((map.Centre - open).Length(), Is.LessThan(1f), "Centre on me centres on the ghost.");
        });

        // Over a grid the ghost is parented to it, but still centres on itself and not on the grid.
        var above = new Vector2(3f, 2f);
        await Server.WaitPost(() => Server.System<SharedTransformSystem>().SetCoordinates(ghost, new EntityCoordinates(station, above)));
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var map = controller.Window!.Panel.MapControl;
            Assert.That(CEntMan.GetComponent<TransformComponent>(CEntMan.GetEntity(netGhost)).GridUid, Is.Not.Null, "The ghost is over the station's grid.");
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.False);
            map.ShowWholeSector();
            Assert.That(map.CentreOnPlayer(), Is.True);
            Assert.That((map.Centre - (Outpost + above)).Length(), Is.LessThan(1f));
        });

        await Client.WaitPost(() => controller.Window!.Close());
    }

    /// <summary>Presses and releases a key function in the client's own input, as a keypress outside the game world.</summary>
    private async Task PressClientKey(BoundKeyFunction function)
    {
        await Client.WaitPost(() =>
        {
            InputManager.ViewportKeyEvent(null, new BoundKeyEventArgs(function, BoundKeyState.Down, default, false));
            InputManager.ViewportKeyEvent(null, new BoundKeyEventArgs(function, BoundKeyState.Up, default, false));
        });
        await RunTicks(2);
    }
}
