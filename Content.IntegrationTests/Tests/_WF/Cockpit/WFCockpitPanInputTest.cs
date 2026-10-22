using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._WF.Cockpit;
using Content.Client._WF.ShipAccess;
using Content.Client._WF.Shuttles.UI;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Cockpit;

/// <summary>Checks right-button capture, plotting scale and drag continuity across every cockpit map.</summary>
[TestFixture]
public sealed class WFCockpitPanInputTest
{
    [Test]
    public async Task RightDragCapturesPlotsWithoutTakingButtonsOrLeftClick()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var input = pair.Client.ResolveDependency<IInputManager>();
            var mainWindow = pair.Client.ResolveDependency<IClyde>().MainWindow;
            foreach (var map in new MapGridControl[]
                     { new ShuttleNavControl(), new ShuttleDockControl(), new ShuttleMapControl(), new ShipViewControl(), new ShipAccessDoorMapControl() })
            {
                using (map)
                using (var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore })
                using (var button = new Button { Text = "Pan input test" })
                {
                    var lease = new WFCockpitLease();
                    map.WfCockpitInteraction(lease);
                    map.WfFitInstrument = true;
                    map.SetSize = new Vector2(400, 300);
                    host.AddChild(map);
                    host.AddChild(button);
                    ui.RootControl.AddChild(host);
                    host.Measure(new Vector2(700, 350));
                    host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(700, 350)));
                    map.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
                    button.Arrange(UIBox2.FromDimensions(new Vector2(450, 100), new Vector2(160, 40)));
                    var start = Pointer(map, map.Size / 2);
                    var outside = Pointer(button, button.Size / 2);
                    Assert.That(ui.MouseGetControl(start), Is.SameAs(map), map.GetType().Name);
                    Assert.That(ui.MouseGetControl(outside), Is.SameAs(button));
                    Assert.That(Key(map, Keyboard.Key.MouseLeft, KeyEventType.Down, start).Handled, Is.False,
                        "Left-click selection and firing must remain on their native path.");
                    Assert.That(Key(map, Keyboard.Key.MouseMiddle, KeyEventType.Down, start).Handled, Is.False);
                    input.KeyDown(new KeyEventArgs(Keyboard.Key.Shift, false, false, false, false, false, 0));
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start).Handled, Is.False,
                        "Shift held leaves the press to other handlers.");
                    input.KeyUp(new KeyEventArgs(Keyboard.Key.Shift, false, false, false, false, false, 0));
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start, shift: true).Handled, Is.True,
                        "A modifier the window reports but the engine never saw pressed must not block panning.");
                    Key(map, Keyboard.Key.MouseRight, KeyEventType.Up, start);
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, outside).Handled, Is.False,
                        "A plot must never take right-clicks that begin on another control.");
                    map.Offset = Vector2.Zero;
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start).Handled, Is.True,
                        "Capture must stop context-menu dispatch before a right-button drag begins.");
                    var moved = new ScreenCoordinates(start.Position + new Vector2(24, 12), start.Window);
                    Tick(map, moved);
                    var scale = (float) typeof(MapGridControl).GetProperty("MinimapScale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!;
                    Assert.That(Vector2.Distance(map.Offset, new Vector2(-24, 12) / scale), Is.LessThan(0.001f));
                    Tick(map, outside);
                    var held = map.Offset;
                    Assert.That(Vector2.Distance(held, new Vector2(start.Position.X - outside.Position.X, outside.Position.Y - start.Position.Y) / scale),
                        Is.LessThan(0.001f), "The grabbed plot must keep moving when the pointer crosses another panel.");
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Up, outside).Handled, Is.True);
                    Tick(map, start);
                    Assert.That(map.Offset, Is.EqualTo(held), "The captured release must stop dragging outside the plot.");
                    Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start);
                    Focus(map, mainWindow, false);
                    Tick(map, outside);
                    Assert.That(map.Offset, Is.EqualTo(held), "Focus loss requires a fresh right-button press.");
                    Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start);
                    map.Visible = false;
                    map.Visible = true;
                    Tick(map, outside);
                    Assert.That(map.Offset, Is.EqualTo(held), "Switching MFD pages must release the old plot.");
                    Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start);
                    lease.Restore();
                    Tick(map, outside);
                    Assert.That(map.Offset, Is.EqualTo(held));
                    Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, start).Handled, Is.False,
                        "Returning the map restores native standalone input instead of leaving raw capture installed.");
                    if ((bool) typeof(MapGridControl).GetProperty("Draggable", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(map)!)
                    {
                        var native = new GUIBoundKeyEventArgs(EngineKeyFunctions.UseSecondary, BoundKeyState.Down, start, true,
                            start.Position / map.UIScale - map.GlobalPosition, start.Position - map.GlobalPixelPosition);
                        typeof(MapGridControl).GetMethod("KeyBindDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(map, new object[] { native });
                        Assert.That(native.Handled, Is.True, "Standalone consoles retain upstream right-click panning.");
                    }
                    host.Orphan();
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DiagnosticsNameWhatRefusedThePress()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var diagnostics = pair.Client.ResolveDependency<IEntityManager>().System<WFCockpitPanDiagnosticSystem>();
            using var map = new ShuttleNavControl();
            using var host = new Control { MouseFilter = Control.MouseFilterMode.Ignore };
            using var cover = new Control { Name = "PanTestCover", MouseFilter = Control.MouseFilterMode.Pass };
            var lease = new WFCockpitLease();
            map.WfCockpitInteraction(lease);
            map.WfFitInstrument = true;
            map.SetSize = new Vector2(400, 300);
            host.AddChild(map);
            host.AddChild(cover);
            ui.RootControl.AddChild(host);
            cover.SetSize = new Vector2(200, 300);
            host.Measure(new Vector2(400, 300));
            host.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            map.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(400, 300)));
            cover.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(200, 300)));
            var covered = Pointer(map, new Vector2(100, 150));
            var open = Pointer(map, new Vector2(300, 150));
            Assert.That(ui.MouseGetControl(covered), Is.SameAs(cover), "The test cover must sit over the plot.");
            Assert.That(ui.MouseGetControl(open), Is.SameAs(map));

            Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, covered).Handled, Is.False);
            var report = diagnostics.Report().ToList();
            Assert.That(report, Has.Some.Contains(Loc.GetString("wf-cockpit-pan-covered")), string.Join(" | ", report));
            Assert.That(report, Has.Some.Contains("PanTestCover"), "The report names the control in the way.");

            Assert.That(Key(map, Keyboard.Key.MouseRight, KeyEventType.Down, open).Handled, Is.True);
            report = diagnostics.Report().ToList();
            Assert.That(report, Has.Some.Contains(Loc.GetString("wf-cockpit-pan-started")), string.Join(" | ", report));
            Assert.That(report, Has.None.Contains(Loc.GetString("wf-cockpit-pan-covered")), "Each press starts a fresh report.");
            Key(map, Keyboard.Key.MouseRight, KeyEventType.Up, open);
            lease.Restore();
            host.Orphan();
        });
        await pair.CleanReturnAsync();
    }

    private static ScreenCoordinates Pointer(Control control, Vector2 local) =>
        new(control.GlobalPixelPosition + local * control.UIScale, control.Window!.Id);

    private static KeyEventArgs Key(MapGridControl map, Keyboard.Key key, KeyEventType type, ScreenCoordinates pointer, bool shift = false)
    {
        var args = new KeyEventArgs(key, false, false, false, shift, false, 0);
        typeof(MapGridControl).GetMethod("WfCockpitPanKey", BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(KeyEventArgs), typeof(KeyEventType), typeof(ScreenCoordinates) }, null)!
            .Invoke(map, new object[] { args, type, pointer });
        return args;
    }

    private static void Tick(MapGridControl map, ScreenCoordinates pointer) =>
        typeof(MapGridControl).GetMethod("WfUpdateCockpitPan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(map, new object[] { pointer });

    private static void Focus(MapGridControl map, IClydeWindow window, bool focused) =>
        typeof(MapGridControl).GetMethod("WfCockpitWindowFocused", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(map, new object[] { new WindowFocusedEventArgs(focused, window) });
}
