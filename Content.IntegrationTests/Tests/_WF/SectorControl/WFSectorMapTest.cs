#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.SectorControl;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.SectorControl;
using Content.Shared.Ghost;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.SectorControl;

/// <summary>The sector map: the client keeps the sector map with nothing held, outlines and names every cell, names the grids a MAP scan would, and the window lays out and fits them.</summary>
[TestOf(typeof(WFSectorMapControl))]
public sealed class WFSectorMapTest : WFSectorTestBase
{
    private static readonly WFSectorCell Held = new(4, -2);
    private static readonly WFSectorCell Open = new(-6, 3);
    private static readonly Vector2 Outpost = new(4000f, 1500f);
    private static readonly Vector2 Depot = new(-9000f, 6000f);

    private void SpawnStations()
    {
        SpawnStation(Outpost, "Test Outpost");
        SpawnStation(Depot, "Test Depot");
    }

    /// <summary>A player is sent the sector map with nothing held and keeps it, every cell in the radius outlined once and named; grids come from the client, as the MAP tab lists them.</summary>
    [Test]
    public async Task SectorMapNamesTheGridsTheMapTabWould()
    {
        await StartRound();
        var hidden = new List<NetEntity>();
        await Server.WaitAssertion(() =>
        {
            var shuttles = Server.System<SharedShuttleSystem>();
            var outpost = SpawnStation(Outpost, "Test Outpost");
            SEntMan.EnsureComponent<IFFComponent>(outpost);
            SpawnStation(Depot, "Test Depot");
            var flags = new[] { IFFFlags.Hide, IFFFlags.HideLabel, IFFFlags.HideLabelAlways };
            for (var i = 0; i < flags.Length; i++)
            {
                var grid = SpawnStation(new Vector2(2000f + 1000f * i, -3000f), $"Test Hidden {flags[i]}");
                shuttles.AddIFFFlag(grid, flags[i]);
                hidden.Add(SEntMan.GetNetEntity(grid));
            }

            var sync = Server.System<WFSectorSyncSystem>();
            var ev = sync.Build(MapData.MapId);
            Assert.That(ev.Claims, Is.Empty);
            Assert.That(ev.Sector, Is.True, "The round's map is the sector map.");
            sync.SendTo(ServerSession);
        });

        await RunTicks(30);
        await Client.WaitAssertion(() =>
        {
            var client = Client.System<WFSectorClientSystem>();
            Assert.That(client.TryGetMap(MapData.MapId, out var view), Is.True, "The sector map is kept with nothing held.");
            Assert.That(view.Sector, Is.True);
            Assert.That(view.Owners, Is.Empty);
            Assert.That(view.Extent, Is.Null, "Nothing held or contested.");

            // Every cell inside the radius, each edge once.
            var cells = WFSectorHex.CellsWithin(WFSectorClientSystem.SectorRadius, view.CellSize);
            var edges = new HashSet<(int, int)>();
            foreach (var cell in cells)
            {
                for (var i = 0; i < 6; i++)
                {
                    edges.Add(Key((WFSectorHex.Corner(cell, view.CellSize, i) + WFSectorHex.Corner(cell, view.CellSize, i + 1)) / 2f));
                }
            }

            Assert.That(view.Grid.Length, Is.EqualTo(edges.Count * 2), "Each shared edge is drawn once.");
            var drawn = new HashSet<(int, int)>();
            for (var i = 0; i < view.Grid.Length; i += 2)
            {
                drawn.Add(Key((view.Grid[i] + view.Grid[i + 1]) / 2f));
            }

            Assert.That(drawn.SetEquals(edges), Is.True, "The outline covers every edge of every cell.");
            Assert.That(view.CellLabels, Has.Count.EqualTo(cells.Count), "Every cell is named, held or not.");
            var open = view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Open));
            Assert.That(open.Color, Is.EqualTo(WFSectorClientSystem.OpenCellColor));
            Assert.That(open.Centre, Is.EqualTo(CentreOf(Open)));

            // The hidden grids reached the client; the map leaves them out.
            foreach (var net in hidden)
            {
                Assert.That(CEntMan.GetComponent<IFFComponent>(CEntMan.GetEntity(net)).Flags, Is.Not.EqualTo(IFFFlags.None));
            }

            var map = new WFSectorMapControl();
            Assert.That(map.TryGetOwnShip(MapData.MapId, out _, out _), Is.True, "The player stands on the test grid.");
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(MapData.MapId, landmarks);
            Assert.That(landmarks.Select(landmark => landmark.Name), Is.EqualTo(new[] { "Test Outpost", "Test Depot" }),
                "Plain and IFF-less grids are named, nearest the origin first; hidden ones and the player's own are not.");
            Assert.That((landmarks[0].Position - Outpost).Length(), Is.LessThan(2f), "At the grid's centre of mass.");

            var all = new List<WFSectorLandmarkView>();
            client.CollectLandmarks(MapData.MapId, null, all);
            Assert.That(all.Select(landmark => landmark.Grid), Does.Contain(MapData.CGridUid),
                "The player's own grid is only left out because it is theirs.");
            Assert.That(landmarks.Select(landmark => landmark.Grid), Does.Not.Contain(MapData.CGridUid));
        });
    }

    /// <summary>Rounds an edge midpoint to a key that matches across the cells sharing it.</summary>
    private static (int, int) Key(Vector2 point)
    {
        return ((int) MathF.Round(point.X), (int) MathF.Round(point.Y));
    }

    /// <summary>The map window opens, lays out with the map above its bar, fits the grids and the held cell, zooms about a point and reads out cells with their distance out.</summary>
    [Test]
    public async Task MapWindowLaysOutAndFitsTheSector()
    {
        await StartRound();
        await Server.WaitAssertion(() =>
        {
            SpawnStations();
            Assert.That(Server.System<WFSectorTerritorySystem>().TryClaim(MapData.MapId, Held, Holder, null), Is.True);
        });

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
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(view.Map, landmarks);
            Assert.That(landmarks, Has.Count.EqualTo(2));

            // A held cell's callsign is in its faction's colour, an open one's in grey.
            Assert.That(view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Held)).Color, Is.EqualTo(Color.Red));
            Assert.That(view.CellLabels.Single(label => label.Text == WFSectorHex.Callsign(Open)).Color, Is.EqualTo(WFSectorClientSystem.OpenCellColor));

            Assert.That(map.PixelSize.X, Is.GreaterThan(300));
            Assert.That(map.PixelSize.Y, Is.GreaterThan(200));
            Assert.That(panel.CentreButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.WholeSectorButton.Size.X, Is.GreaterThan(0f));
            Assert.That(panel.CentreButton.GlobalPosition.Y, Is.GreaterThanOrEqualTo(map.GlobalPosition.Y + map.Size.Y),
                "The bar sits under the map.");
            Assert.That(panel.ScaleLabel.Text, Does.EndWith("km across"));

            // Fitted to every named grid and the held cell, inside the zoom limits.
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

            Assert.That(map.Readout(view, CentreOf(Held)), Is.EqualTo($"{WFSectorHex.Callsign(Held)} ({Km(CentreOf(Held))} km): Faction id"));
            Assert.That(map.Readout(view, CentreOf(Open)), Is.EqualTo($"{WFSectorHex.Callsign(Open)} ({Km(CentreOf(Open))} km): open space"));

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

    /// <summary>The distance of a point from the origin as the readout prints it.</summary>
    private static string Km(Vector2 point)
    {
        return (point.Length() / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>The map marks each encounter once, at its first ship, however many ships it has, and leaves out those on other maps.</summary>
    [Test]
    public async Task OneMarkerPerEncounter()
    {
        await Client.WaitAssertion(() =>
        {
            var raiders = new NetEntity(900001);
            var convoy = new NetEntity(900002);
            var elsewhere = new NetEntity(900003);
            var markers = new List<WFEncounterMarker>
            {
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(100f, 0f)),
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(200f, 0f)),
                Marker(convoy, "Convoy", WFEncounterCategory.Traffic, MapData.MapId, new Vector2(-500f, 0f)),
                Marker(raiders, "Raiders", WFEncounterCategory.Threat, MapData.MapId, new Vector2(300f, 0f)),
                Marker(convoy, "Convoy", WFEncounterCategory.Traffic, MapData.MapId, new Vector2(-600f, 0f)),
                Marker(elsewhere, "Elsewhere", WFEncounterCategory.Patrol, MapId.Nullspace, new Vector2(0f, 0f)),
            };

            var map = new WFSectorMapControl();
            var encounters = new List<WFSectorEncounterView>();
            map.CollectEncounters(MapData.MapId, markers, encounters);
            Assert.That(encounters, Has.Count.EqualTo(2), "Two encounters on this map, five ships between them.");
            Assert.That(encounters[0], Is.EqualTo(new WFSectorEncounterView("Raiders", WFEncounterCategory.Threat, new Vector2(100f, 0f))));
            Assert.That(encounters[1], Is.EqualTo(new WFSectorEncounterView("Convoy", WFEncounterCategory.Traffic, new Vector2(-500f, 0f))));
        });
    }

    private static WFEncounterMarker Marker(NetEntity encounter, string name, WFEncounterCategory category, MapId map, Vector2 position)
    {
        return new WFEncounterMarker
        {
            Encounter = encounter,
            Name = name,
            Category = category,
            Map = map,
            Position = position,
        };
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
            Assert.That(map.TryGetView(out var view), Is.True, "The ghost is sent the sector map when it attaches.");
            Assert.That(view.Map, Is.EqualTo(MapData.MapId));
            Assert.That(view.Sector, Is.True);
            var landmarks = new List<WFSectorLandmarkView>();
            map.CollectLandmarks(view.Map, landmarks);
            Assert.That(landmarks.Select(landmark => landmark.Name), Is.SupersetOf(new[] { "Test Outpost", "Test Depot" }));

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
