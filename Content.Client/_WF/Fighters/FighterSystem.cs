using Content.Shared._WF.Fighters;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._WF.Fighters;

/// <summary>
/// Puts the local pilot on the shuttle keys while flying, and streams the cursor's bearing when cursor follow is on.
/// </summary>
public sealed partial class FighterSystem : SharedFighterSystem
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;

    /// <summary>Seconds between two cursor updates.</summary>
    private const float AimInterval = 0.05f;

    private float _aimAccumulator;
    private Angle? _lastAim;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FighterPilotComponent, ComponentStartup>(OnPilotStartup);
        SubscribeLocalEvent<FighterPilotComponent, ComponentShutdown>(OnPilotShutdown);
        SubscribeLocalEvent<FighterPilotComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<FighterPilotComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnPilotStartup(Entity<FighterPilotComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity == ent.Owner)
            _input.Contexts.SetActiveContext("shuttle");
    }

    private void OnPilotShutdown(Entity<FighterPilotComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            _input.Contexts.SetActiveContext("human");
    }

    private void OnPlayerAttached(Entity<FighterPilotComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        _input.Contexts.SetActiveContext("shuttle");
    }

    private void OnPlayerDetached(Entity<FighterPilotComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _input.Contexts.SetActiveContext("human");
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _aimAccumulator += frameTime;
        if (_aimAccumulator < AimInterval)
            return;

        _aimAccumulator = 0f;

        if (!TryComp<FighterPilotComponent>(_player.LocalEntity, out var pilot)
            || !TryComp<FighterComponent>(pilot.Fighter, out var fighter)
            || !fighter.FollowCursor)
        {
            _lastAim = null;
            return;
        }

        var mouse = _eyeManager.PixelToMap(_input.MouseScreenPosition);
        var origin = Transforms.GetMapCoordinates(pilot.Fighter);
        if (mouse.MapId == MapId.Nullspace || mouse.MapId != origin.MapId)
            return;

        var delta = mouse.Position - origin.Position;
        if (delta.LengthSquared() < 0.01f)
            return;

        var angle = delta.ToAngle();
        if (_lastAim is { } last && Math.Abs(Angle.ShortestDistance(last, angle).Degrees) < 0.5)
            return;

        _lastAim = angle;
        RaiseNetworkEvent(new FighterCursorAimEvent { Fighter = GetNetEntity(pilot.Fighter), Angle = angle });
    }
}
