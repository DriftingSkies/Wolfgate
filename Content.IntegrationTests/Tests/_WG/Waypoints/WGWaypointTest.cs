#nullable enable annotations

using Content.IntegrationTests.Tests._WG.CombatConsole;
using Content.IntegrationTests.Tests._WG.EngineCompat;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WG.Waypoints;
using Content.Client.Shuttles.UI;
using Content.Server._WG.Waypoints;
using Content.Server.NPC.HTN;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shuttles;
using Content.Shared._WG.Waypoints;
using Content.Shared.CCVar;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Waypoints;

/// <summary>Checks waypoints reach every radar on the grid and a helm's panel really engages the autopilot.</summary>
[TestFixture]
public sealed class WGWaypointTest
{
    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Descendants(child))
            yield return nested;
    }

    [Test]
    public async Task ServerKeepsTheListWithinItsLimits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var system = entities.System<WGWaypointSystem>();
            var scanner = entities.SpawnEntity("ComputerRadar", map.GridCoords);
            var floating = entities.SpawnEntity(null, new MapCoordinates(new Vector2(500, 500), map.MapId));

            Assert.That(system.TryAdd(floating, "Lost", Vector2.One), Is.False, "No grid means nowhere to keep it.");
            Assert.That(system.TryAdd(scanner, "Bad", new Vector2(float.NaN, 0)), Is.False);
            Assert.That(entities.HasComponent<WGWaypointsComponent>(map.Grid.Owner), Is.False);

            Assert.That(system.TryAdd(scanner, "  Rally ", new Vector2(10, 20)), Is.True);
            Assert.That(system.TryAdd(scanner, "", new Vector2(30, 40)), Is.True);
            var stored = entities.GetComponent<WGWaypointsComponent>(map.Grid.Owner).Waypoints;
            Assert.That(stored[0].Name, Is.EqualTo("Rally"));
            Assert.That(stored[1].Name, Is.EqualTo("WP-2"));
            Assert.That(stored[0].Map, Is.EqualTo(entities.GetNetEntity(map.MapUid)));

            Assert.That(system.TryRemove(scanner, stored[0].Id), Is.True);
            Assert.That(system.TryRemove(scanner, 999), Is.False);
            Assert.That(stored.Select(waypoint => waypoint.Name), Is.EqualTo(new[] { "WP-2" }));

            while (stored.Count < WGWaypointRules.MaxWaypoints)
                Assert.That(system.TryAdd(scanner, "", Vector2.Zero), Is.True);
            Assert.That(system.TryAdd(scanner, "", Vector2.Zero), Is.False, "The list has a cap.");

            entities.DeleteEntity(scanner);
            entities.DeleteEntity(floating);
            entities.RemoveComponent<WGWaypointsComponent>(map.Grid.Owner);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadarCursorReadsTheWorldPositionUnderIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        await pair.RunTicksSync(5);
        var clientEntities = pair.Client.EntMan;
        await pair.Client.WaitAssertion(() =>
        {
            var grid = clientEntities.GetEntity(pair.Server.EntMan.GetNetEntity(map.Grid.Owner));
            using var radar = new ShuttleNavControl { WgFitInstrument = true, SetSize = new Vector2(400, 400), WorldRange = 40 };
            radar.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 400)));
            radar.SetMatrix(new EntityCoordinates(grid, new Vector2(3, 4)), Angle.Zero);
            Assert.That(radar.WgCockpitTryGetWorldToView(out var worldToView, out _), Is.True);

            var world = new Vector2(10, -6);
            var view = Vector2.Transform(world, worldToView) / radar.UIScale;
            var read = radar.WgCursorWorldPosition(view);
            Assert.That(read, Is.Not.Null);
            Assert.That(read!.Value.X, Is.EqualTo(world.X).Within(0.01f));
            Assert.That(read.Value.Y, Is.EqualTo(world.Y).Within(0.01f));

            Assert.That(radar.WgScannerWorldPosition()!.Value.X, Is.EqualTo(3f).Within(0.01f),
                "A radar with no console reads the point it is centred on.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HelmPanelAddsDeletesAndFliesToWaypoints()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid console = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            console = entities.SpawnEntity("ComputerShuttle", map.GridCoords);
            actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            // An unpowered helm's planner would fail the move and clear the target before it can be read.
            entities.GetComponent<HTNComponent>(console).Enabled = false;
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            entities.System<SharedUserInterfaceSystem>().OpenUi(console, ShuttleConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(30);

        WGWaypointPanel Panel() => Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
            .OfType<ShuttleConsoleWindow>().Last().FindControl<NavScreen>("NavContainer")
            .FindControl<ShuttleNavControl>("NavRadar").Parent!.Children.OfType<WGWaypointPanel>().Single();

        T Find<T>(string name) where T : Control => Descendants(Panel()).OfType<T>().Single(control => control.Name == name);

        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(Panel().Visible, Is.False, "The panel starts closed.");
            var navRadar = Panel().Parent!;
            var key = navRadar.Children.OfType<Button>().Single(button => button.Name == "WgWaypointKey");
            Assert.That(key.Visible, Is.True, "A helm on a grid offers waypoints.");
            WGButtonTestInput.Toggle(key, true);
            Assert.That(Panel().Visible, Is.True, "The key opens the panel.");
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitAssertion(() =>
        {
            Find<LineEdit>("WgWaypointName").Text = "Rally";
            Find<LineEdit>("WgWaypointX").Text = "not a number";
            Find<LineEdit>("WgWaypointY").Text = "20";
            Press(Find<Button>("WgWaypointAdd"));
            Assert.That(Find<Label>("WgWaypointStatus").Visible, Is.True, "A bad coordinate is reported.");

            Find<LineEdit>("WgWaypointX").Text = "10.5";
            Press(Find<Button>("WgWaypointAdd"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            var stored = entities.GetComponent<WGWaypointsComponent>(map.Grid.Owner).Waypoints;
            Assert.That(stored.Select(waypoint => (waypoint.Name, waypoint.Position)),
                Is.EqualTo(new[] { ("Rally", new Vector2(10.5f, 20)) }));
        });

        await pair.Client.WaitAssertion(() =>
        {
            var grid = pair.Client.EntMan.GetEntity(entities.GetNetEntity(map.Grid.Owner));
            Assert.That(pair.Client.EntMan.GetComponent<WGWaypointsComponent>(grid).Waypoints, Has.Count.EqualTo(1),
                "The list reaches the client.");
            var row = Find<Button>("WgWaypointRemove1");
            var scroll = Descendants(Panel()).OfType<ScrollContainer>().Single();
            Assert.That(scroll.Height, Is.GreaterThan(row.Height - 1), "The list must have room to show its rows.");
            Assert.That(row.GlobalPosition.Y + row.Height, Is.LessThan(Panel().GlobalPosition.Y + Panel().Height + 1),
                "A row must sit inside the panel, not be clipped away.");
            Press(Find<Button>("WgWaypointFly1"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            var helm = entities.GetComponent<ShuttleConsoleComponent>(console);
            var blackboard = entities.GetComponent<HTNComponent>(console).Blackboard;
            var targetSet = blackboard.TryGetValue<EntityCoordinates>(helm.AutopilotTargetKey, out var target, entities);
            Assert.That(targetSet, Is.True, "Go sets the autopilot's target.");
            var world = entities.System<SharedTransformSystem>().ToMapCoordinates(target);
            Assert.That(world.MapId, Is.EqualTo(map.MapId));
            Assert.That(world.Position, Is.EqualTo(new Vector2(10.5f, 20)));
            Assert.That(blackboard.TryGetValue<Angle>(helm.AutopilotRotationKey, out var heading, entities), Is.True);
            var shipRotation = entities.System<SharedTransformSystem>().GetWorldRotation(map.Grid.Owner);
            Assert.That(heading.Theta, Is.EqualTo((shipRotation + Math.PI).Theta).Within(0.001), "The ship keeps its heading.");
        });

        await pair.Client.WaitAssertion(() => Press(Find<Button>("WgWaypointRemove1")));
        await pair.RunTicksSync(30);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGWaypointsComponent>(map.Grid.Owner).Waypoints, Is.Empty);
            entities.System<SharedUserInterfaceSystem>().CloseUi(console, ShuttleConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MassScannerManagesWaypointsWithoutAutopilot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid scanner = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            scanner = entities.SpawnEntity("ComputerRadar", map.GridCoords);
            actor = entities.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            entities.System<WGWaypointSystem>().TryAdd(scanner, "Rally", new Vector2(1, 2));
            entities.System<SharedUserInterfaceSystem>().OpenUi(scanner, RadarConsoleUiKey.Key, actor);
        });
        await pair.RunTicksSync(30);

        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<RadarConsoleWindow>().Last();
            var layers = window.FindControl<ShuttleNavControl>("RadarScreen").Parent!;
            WGButtonTestInput.Toggle(layers.Children.OfType<Button>().Single(button => button.Name == "WgWaypointKey"), true);
        });
        await pair.RunTicksSync(5);

        await pair.Client.WaitAssertion(() =>
        {
            var window = Descendants(pair.Client.ResolveDependency<IUserInterfaceManager>().WindowRoot)
                .OfType<RadarConsoleWindow>().Last();
            var panel = window.FindControl<ShuttleNavControl>("RadarScreen").Parent!.Children.OfType<WGWaypointPanel>().Single();
            Button Button(string name) => Descendants(panel).OfType<Button>().Single(control => control.Name == name);
            Assert.That(Button("WgWaypointFly").Visible, Is.False, "A mass scanner has no autopilot to send.");
            Assert.That(Button("WgWaypointFly1").Visible, Is.False);
            Press(Button("WgWaypointRemove1"));
        });
        await pair.RunTicksSync(30);

        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WGWaypointsComponent>(map.Grid.Owner).Waypoints, Is.Empty,
                "A mass scanner can delete from the list.");
            entities.System<SharedUserInterfaceSystem>().CloseUi(scanner, RadarConsoleUiKey.Key, actor);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(scanner);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task HelmFliesToAWaypointWhoeverIsAtIt(bool ghost)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        var grid = map.Grid.Owner;
        var transforms = entities.System<SharedTransformSystem>();
        var start = Vector2.Zero;
        EntityUid console = default, actor = default;
        await pair.Server.WaitAssertion(() =>
        {
            // On a live server NPCs with no living player near them sleep; the helm's planner is one of them.
            pair.Server.CfgMan.SetCVar(CCVars.NPCEnabled, true);
            pair.Server.CfgMan.SetCVar(CCVars.NPCPauseWhenNoPlayersInRange, true);
            var maps = entities.System<SharedMapSystem>();
            var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = -3; x <= 3; x++)
            for (var y = -3; y <= 3; y++)
                maps.SetTile(grid, map.Grid.Comp, new Vector2i(x, y), plating);
            entities.EnsureComponent<ShuttleComponent>(grid);
            foreach (var (position, rotation) in new[] { (new Vector2(0.5f, 3.5f), 0), (new Vector2(0.5f, -2.5f), 180), (new Vector2(3.5f, 0.5f), 270), (new Vector2(-2.5f, 0.5f), 90) })
                transforms.SetLocalRotation(entities.SpawnEntity("DebugThruster", new EntityCoordinates(grid, position)), Angle.FromDegrees(rotation));
            entities.SpawnEntity("DebugGyroscope", new EntityCoordinates(grid, new Vector2(1.5f, 1.5f)));
            console = entities.SpawnEntity("ComputerShuttle", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
            entities.RemoveComponent<ApcPowerReceiverComponent>(console);
            actor = entities.SpawnEntity(ghost ? "AdminObserver" : "MobHuman", new EntityCoordinates(grid, new Vector2(-0.5f, 0.5f)));
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            start = transforms.GetWorldPosition(grid);
        });
        // Past the two second sleep check, so a helm nobody living is near has already gone to sleep.
        await pair.RunTicksSync(150);

        await pair.Server.WaitPost(() => entities.EventBus.RaiseLocalEvent(console, new ShuttleConsoleAutopilotPositionMessage
        {
            Coordinates = new MapCoordinates(start + new Vector2(300, 0), map.MapId),
            Angle = transforms.GetWorldRotation(grid),
        }));

        var moved = 0f;
        for (var i = 0; i < 40 && moved < 5f; i++)
        {
            await pair.RunTicksSync(30);
            await pair.Server.WaitPost(() => moved = (transforms.GetWorldPosition(grid) - start).Length());
        }
        Assert.That(moved, Is.GreaterThanOrEqualTo(5f), "The autopilot must actually fly the ship.");

        await pair.Server.WaitPost(() =>
        {
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
        });
        await pair.CleanReturnAsync();
    }

    private static void Press(BaseButton button) => WGButtonTestInput.Click(button);
}
