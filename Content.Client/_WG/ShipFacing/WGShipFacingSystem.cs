using Content.Client.Shuttles.UI;
using Content.Shared._WG.ShipFacing;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipFacing;

/// <summary>
/// While the pilot holds the facing key, sends the world point under the cursor, read off the world view or the
/// radar, so the server turns the ship's nose onto it. Over any other control the last point stands.
/// </summary>
public sealed partial class WGShipFacingSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>Soonest a moved point is sent again.</summary>
    private static readonly TimeSpan MoveInterval = TimeSpan.FromSeconds(0.05);

    /// <summary>How often a still point is resent, well inside the server's lifetime for it.</summary>
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>How far the point has to move to be sent before the keep-alive, in metres.</summary>
    private const float MoveDistance = 0.25f;

    private bool _held;
    private MapCoordinates? _target;
    private MapCoordinates? _sent;
    private TimeSpan _sentAt;

    private readonly WGHeldAngle _eyeRotation = new();

    /// <summary>Whether the pilot is holding the facing key, so Shift there means steering, not a modified click.</summary>
    public bool Held => _held;

    /// <summary>Whether the local player is at a helm holding the facing key.</summary>
    private bool Steering => _held && _player.LocalEntity is { } user &&
                             TryComp<PilotComponent>(user, out var pilot) && pilot.Console != null;

    /// <summary>
    /// The rotation a view that turns with the ship draws with: held still while steering, since a view that turns with
    /// the hull makes a still cursor a point on the hull, which the nose never reaches.
    /// </summary>
    public Angle HoldRotation(WGHeldAngle angle, Angle live) => angle.Apply(live, Steering, _timing.RealTime);

    /// <summary>The local player's camera rotation, held still while steering.</summary>
    public Angle HoldEyeRotation(EntityUid eye, Angle live) =>
        eye == _player.LocalEntity ? HoldRotation(_eyeRotation, live) : live;

    public override void Initialize()
    {
        base.Initialize();
        _clyde.OnWindowFocused += OnWindowFocused;

        CommandBinds.Builder
            .Bind(WGShipFacingKeys.WGShuttleFaceCursor,
                InputCmdHandler.FromDelegate(_ => _held = true, _ =>
                {
                    if (!KeyDown())
                        Release();
                }, handle: true, outsidePrediction: true))
            .Register<WGShipFacingSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _clyde.OnWindowFocused -= OnWindowFocused;
        CommandBinds.Unregister<WGShipFacingSystem>();
    }

    /// <summary>A key let go in another window never reports its release here.</summary>
    private void OnWindowFocused(WindowFocusedEventArgs args)
    {
        if (!args.Focused)
            Release();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (!_held)
            return;

        if (!KeyDown() || _player.LocalEntity is not { } user || !TryComp<PilotComponent>(user, out var pilot) || pilot.Console == null)
        {
            Release();
            return;
        }

        if (CursorTarget() is { } cursor)
            _target = cursor;

        if (_target is not { } target)
            return;

        var now = _timing.RealTime;
        var moved = _sent is not { } sent
                    || sent.MapId != target.MapId
                    || (sent.Position - target.Position).LengthSquared() > MoveDistance * MoveDistance;

        if (now < _sentAt + (moved ? MoveInterval : KeepAliveInterval))
            return;

        RaiseNetworkEvent(new WGShipFacingTargetEvent(target));
        _sent = target;
        _sentAt = now;
    }

    /// <summary>
    /// Whether a key bound to facing is physically down. A combo containing it, such as Shift+click or Shift+1, lifts
    /// the binding while the key is still held, and the engine never reports that key's later release.
    /// </summary>
    private bool KeyDown()
    {
        foreach (var binding in _input.GetKeyBindings(WGShipFacingKeys.WGShuttleFaceCursor))
        {
            if (_input.IsKeyDown(binding.BaseKey))
                return true;
        }
        return false;
    }

    private void Release()
    {
        _held = false;
        _target = null;
        if (_sent == null)
            return;

        RaiseNetworkEvent(new WGShipFacingTargetEvent(null));
        _sent = null;
    }

    /// <summary>The world point under the cursor on the radar or a world view, or null over anything else.</summary>
    private MapCoordinates? CursorTarget()
    {
        var mouse = _input.MouseScreenPosition;
        if (!mouse.IsValid)
            return null;

        for (var control = _ui.MouseGetControl(mouse); control != null; control = control.Parent)
        {
            if (control is ShuttleNavControl nav)
            {
                var coords = nav.GetMouseCoordinatesFromCenter();
                return coords.IsValid(EntityManager) ? _transform.ToMapCoordinates(coords) : null;
            }

            if (control is IViewportControl viewport)
            {
                var point = viewport.PixelToMap(mouse.Position);
                return point.MapId == MapId.Nullspace ? null : point;
            }
        }

        return null;
    }
}
