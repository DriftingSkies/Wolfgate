#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Client.Shuttles.UI;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.SectorControl;
using Robust.Client.UserInterface;
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
}
