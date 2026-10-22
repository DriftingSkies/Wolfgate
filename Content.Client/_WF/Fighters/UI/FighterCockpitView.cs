using System.Linq;
using System.Numerics;
using Content.Client._WF.Cockpit;
using Content.Client._WF.CombatConsole;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Screens;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._WF.Fighters;
using Content.Shared.Damage;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.Fighters.UI;

/// <summary>
/// The fighter's fullscreen flight deck: instruments on the left, the real world view and chat in the middle and
/// the radar scope on the right. Built from the ship cockpit's parts but owned by the fighter alone.
/// </summary>
public sealed class FighterCockpitView : Control
{
    private const float Edge = 8;
    private const float Gap = 6;

    private readonly IEntityManager _entities;
    private readonly IPlayerManager _player;
    private readonly Action _exit;
    private readonly SharedTransformSystem _transform;
    private readonly EntityUid _fighter;
    private readonly WFCockpitLease _lease;
    private readonly ShuttleNavControl _radar;
    private readonly Control _left;
    private readonly Control _right;
    private readonly Control _world;
    private readonly Control _comms;
    private readonly WFCockpitSpeechClip _speechClip;
    private readonly Control _alerts;
    private readonly Button _followCursor;
    private readonly Dictionary<FighterFlightMode, Button> _modes = new();
    private readonly Label _steerLamp;
    private readonly Label _burnLamp;
    private readonly Label _gridLamp;
    private readonly Label _rechargeLamp;
    private bool _restored;
    private bool _exitQueued;

    private FighterCockpitView(EntityUid fighter, InGameScreen screen, MainViewport viewport, Action exit,
        Action followCursor, Action leave, Action<FighterFlightMode> setMode, WFCockpitLease lease)
    {
        _entities = IoCManager.Resolve<IEntityManager>();
        _player = IoCManager.Resolve<IPlayerManager>();
        _exit = exit;
        _transform = _entities.System<SharedTransformSystem>();
        _fighter = fighter;
        _lease = lease;
        Name = "WFFighterCockpitView";
        HorizontalExpand = VerticalExpand = true;
        RectClipContent = true;

        // The normal HUD is parked out of sight and handed back unchanged on exit.
        var chat = screen.ChatBox;
        var hiddenHud = new Control { Visible = false };
        hiddenHud.AddStyleClass("WfNativeStyle");
        var normalHud = new List<Control>(screen.Children);
        _lease.Clear(screen);
        foreach (var control in normalHud)
            hiddenHud.AddChild(_lease.Take(control, restoreVisibility: false));
        AddChild(hiddenHud);

        _world = _lease.Take(viewport, restoreVisibility: false);
        _comms = Panel("wf-fighter-cockpit-comms", _lease.Take(chat, restoreVisibility: false), true);
        if (chat is ResizableChatBox resizable)
        {
            resizable.WfCockpitDocked = true;
            _lease.Remember(() => resizable.WfCockpitDocked = false);
        }
        Native(chat);
        Native(viewport);

        var controls = WFCockpitLease.Descendants(hiddenHud).ToArray();
        var viewportContainer = controls.OfType<LayoutContainer>().First(control => control.Name == "ViewportContainer");
        var speech = new LayoutContainer { MouseFilter = MouseFilterMode.Ignore };
        speech.AddStyleClass("WfNativeStyle");
        _speechClip = new WFCockpitSpeechClip();
        _speechClip.AddChild(speech);
        var chatController = UserInterfaceManager.GetUIController<ChatUIController>();
        chatController.SetSpeechBubbleRoot(speech);
        _lease.Remember(() => chatController.SetSpeechBubbleRoot(viewportContainer));
        _alerts = _lease.Take(controls.OfType<AlertsUI>().Single(), restoreVisibility: false);
        Native(_alerts);

        _left = BuildInstruments(exit, followCursor, leave, setMode, out _followCursor, out _steerLamp, out _burnLamp, out _gridLamp, out _rechargeLamp);

        _radar = new ShuttleNavControl();
        _radar.SetConsole(fighter);
        _right = Scope("wf-fighter-cockpit-radar", _radar);

        AddChild(_left);
        AddChild(_world);
        AddChild(_speechClip);
        AddChild(_alerts);
        AddChild(_comms);
        AddChild(_right);
        LayoutContainer.SetAnchorPreset(this, LayoutContainer.LayoutPreset.Wide);
        screen.AddChild(this);
        Install(this);
        UserInterfaceManager.GetUIController<FighterCockpitUIController>().Track(this);
    }

    /// <summary>Takes over the screen for a fighter, or returns null when there is no game screen to take over.</summary>
    public static FighterCockpitView? TryCreate(EntityUid fighter, Action exit, Action followCursor, Action leave,
        Action<FighterFlightMode> setMode)
    {
        var ui = IoCManager.Resolve<IUserInterfaceManager>();
        if (ui.GetUIController<WFCockpitUIController>().Active ||
            ui.ActiveScreen is not InGameScreen screen || screen.GetWidget<MainViewport>() is not { } viewport)
        {
            return null;
        }

        var lease = new WFCockpitLease();
        try
        {
            return new FighterCockpitView(fighter, screen, viewport, exit, followCursor, leave, setMode, lease);
        }
        catch
        {
            foreach (var view in screen.Children.OfType<FighterCockpitView>().ToArray())
            {
                screen.RemoveChild(view);
            }
            lease.Restore();
            throw;
        }
    }

    private Control BuildInstruments(Action exit, Action followCursor, Action leave, Action<FighterFlightMode> setMode,
        out Button followButton, out Label steerLamp, out Label burnLamp, out Label gridLamp, out Label rechargeLamp)
    {
        var title = Label("wf-fighter-cockpit-title", Accent);
        title.HorizontalExpand = true;
        var exitButton = Button("wf-fighter-cockpit-exit");
        exitButton.HorizontalExpand = false;
        exitButton.OnPressed += _ => exit();

        var heading = new WFHeadingInstrument(() => TryGet(out TransformComponent? xform)
            ? (-_transform.GetWorldRotation(xform)).Reduced().FlipPositive().Degrees
            : null) { MinHeight = 150, SetHeight = 150 };
        var velocity = new WFVelocityVectorInstrument(() =>
            TryGet(out PhysicsComponent? body) && TryGet(out TransformComponent? xform)
                ? WFCockpitVelocityReading.FromWorld(body.LinearVelocity, _transform.GetWorldRotation(xform))
                : null) { MinHeight = 150 };

        var speed = new WFGlassGauge("wf-fighter-cockpit-speed", () =>
        {
            double? value = TryGet(out PhysicsComponent? body) ? body.LinearVelocity.Length() : null;
            var max = TryGet(out FighterComponent? fighter) ? fighter.MaxSpeed : 40f;
            return WFGaugeReading.Number(value, 0, max, "wf-gauge-unit-speed", 1);
        }, true) { SetHeight = 44, CompactStrip = true };
        var hull = new WFGlassGauge("wf-fighter-cockpit-hull", () =>
        {
            double? value = null;
            if (TryGet(out FighterComponent? fighter) && TryGet(out DamageableComponent? damage) && fighter.HullIntegrity > 0)
                value = Math.Clamp(1 - damage.TotalDamage.Float() / fighter.HullIntegrity, 0, 1) * 100;
            return WFGaugeReading.Number(value, 0, 100, "wf-gauge-unit-percent", 1, value < 50 ? Red : Green);
        }, true) { SetHeight = 44, CompactStrip = true };
        var gun = new WFGlassGauge("wf-fighter-cockpit-gun", () =>
        {
            BatteryAmmoProviderComponent? ammo = TryGet(out ProjectileBatteryAmmoProviderComponent? projectile)
                ? projectile
                : TryGet(out HitscanBatteryAmmoProviderComponent? hitscan) ? hitscan : null;
            double? value = ammo is { Capacity: > 0 } ? 100.0 * ammo.Shots / ammo.Capacity : null;
            return WFGaugeReading.Number(value, 0, 100, "wf-gauge-unit-percent", 0, value < 20 ? Red : null);
        }, true) { SetHeight = 44, CompactStrip = true };

        steerLamp = Label("wf-fighter-cockpit-steer-keys", Muted);
        burnLamp = Label("wf-fighter-cockpit-burn", Muted);
        gridLamp = Label("wf-fighter-cockpit-gun-locked", Muted);
        rechargeLamp = Label("wf-fighter-cockpit-recharging", Muted);
        var lamps = Row(steerLamp, burnLamp, gridLamp, rechargeLamp);

        followButton = Button("wf-fighter-cockpit-follow-cursor", true);
        followButton.OnPressed += _ => followCursor();
        var leaveButton = Button("wf-fighter-cockpit-leave");
        leaveButton.OnPressed += _ => leave();

        var modeGroup = new ButtonGroup();
        var modeRow = new BoxContainer { SeparationOverride = 2, HorizontalExpand = true };
        foreach (var (mode, key) in new[]
                 {
                     (FighterFlightMode.Cruise, "shuttle-console-inertia-dampener-off"),
                     (FighterFlightMode.Drive, "shuttle-console-inertia-dampener-dampen"),
                     (FighterFlightMode.Park, "shuttle-console-inertia-dampener-anchor"),
                 })
        {
            var button = Button(key, true);
            button.Group = modeGroup;
            button.OnPressed += _ => setMode(mode);
            modeRow.AddChild(button);
            _modes[mode] = button;
        }

        var body = Column(
            Row(title, exitButton),
            Panel("wf-fighter-cockpit-instruments", Column(heading, velocity, speed, hull, gun, lamps)),
            Panel("wf-fighter-cockpit-controls", Column(modeRow, followButton, leaveButton)));
        return Scroll(body);
    }

    private bool TryGet<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? comp) where T : IComponent
    {
        return _entities.TryGetComponent(_fighter, out comp);
    }

    private void Native(Control control)
    {
        if (control.HasStyleClass("WfNativeStyle"))
            return;
        control.AddStyleClass("WfNativeStyle");
        _lease.Remember(() => control.RemoveStyleClass("WfNativeStyle"));
    }

    public void UpdateState(NavInterfaceState state)
    {
        _radar.UpdateState(state);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // The deck belongs to whoever is flying; a pilot who got out, or anyone else, drops it.
        if (!TryGet(out FighterComponent? fighter) ||
            !_entities.TryGetComponent(_player.LocalEntity, out FighterPilotComponent? pilot) || pilot.Fighter != _fighter)
        {
            // Closing disposes this control, so not while the UI tree is mid-update.
            if (!_exitQueued)
            {
                _exitQueued = true;
                Robust.Shared.Timing.Timer.Spawn(0, () =>
                {
                    if (!_restored)
                        _exit();
                });
            }
            return;
        }

        _followCursor.Pressed = fighter.FollowCursor;
        foreach (var (mode, button) in _modes)
            button.Pressed = fighter.FlightMode == mode;
        _steerLamp.Text = Loc.GetString(fighter.FollowCursor ? "wf-fighter-cockpit-steer-cursor" : "wf-fighter-cockpit-steer-keys");
        _steerLamp.FontColorOverride = Accent;

        var burning = _entities.System<SharedAppearanceSystem>().TryGetData(_fighter, FighterVisuals.MainBurn, out bool main) && main;
        _burnLamp.FontColorOverride = burning ? Green : Muted;

        var locked = !fighter.CanFireOnGrid && TryGet(out TransformComponent? xform) && xform.GridUid != null;
        _gridLamp.FontColorOverride = locked ? Red : Muted;
        _rechargeLamp.FontColorOverride = fighter.GunRecharging ? Red : Muted;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var (left, right, bottom) = Edges(availableSize);
        var height = availableSize.Y - 2 * Edge;
        var centre = availableSize.X - left - right - 2 * Edge - 2 * Gap;
        _left.Measure(new Vector2(left, height));
        _right.Measure(new Vector2(right, height));
        _world.Measure(new Vector2(centre, height - bottom - Gap));
        _comms.Measure(new Vector2(centre, bottom));
        _speechClip.Measure(availableSize);
        _alerts.Measure(new Vector2(144, height));
        return availableSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 size)
    {
        var (left, right, bottom) = Edges(size);
        var height = size.Y - 2 * Edge;
        var centreX = Edge + left + Gap;
        var centre = size.X - left - right - 2 * Edge - 2 * Gap;
        var world = UIBox2.FromDimensions(new Vector2(centreX, Edge), new Vector2(centre, height - bottom - Gap));

        _left.Arrange(UIBox2.FromDimensions(new Vector2(Edge), new Vector2(left, height)));
        _right.Arrange(UIBox2.FromDimensions(new Vector2(size.X - Edge - right, Edge), new Vector2(right, height)));
        _world.Arrange(world);
        _comms.Arrange(UIBox2.FromDimensions(new Vector2(centreX, world.Bottom + Gap), new Vector2(centre, bottom)));
        // Speech bubbles use screen coordinates; the clip keeps them inside the world view.
        _speechClip.Arrange(world);
        var alertHeight = Math.Min(_alerts.DesiredSize.Y, Math.Max(1, world.Height - 12));
        _alerts.Arrange(UIBox2.FromDimensions(new Vector2(world.Right - 150, world.Top + 6), new Vector2(144, alertHeight)));
        return size;
    }

    /// <summary>The instrument and radar column widths and the chat row's height.</summary>
    private static (float Left, float Right, float Bottom) Edges(Vector2 size)
    {
        return (Math.Clamp(size.X * 0.18f, 220, 300),
            Math.Clamp(size.X * 0.26f, 260, 440),
            Math.Clamp(size.Y * 0.22f, 130, 240));
    }

    /// <summary>Hands the HUD, viewport and chat back exactly as they were.</summary>
    public void Restore()
    {
        if (_restored)
            return;

        _restored = true;
        UserInterfaceManager.GetUIController<FighterCockpitUIController>().Forget(this);
        Parent?.RemoveChild(this);
        _lease.Restore();
    }
}
