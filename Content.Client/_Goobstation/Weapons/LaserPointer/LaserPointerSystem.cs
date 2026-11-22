using System.Numerics;
using Content.Client.Gameplay;
using Content.Client.Hands.Systems;
using Content.Shared._Goobstation.Weapons.SmartGun;
using Content.Shared.CombatMode;
using Content.Shared.Hands.Components;
using Content.Shared.Wieldable.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.State;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Goobstation.Weapons.LaserPointer;

public sealed partial class LaserPointerSystem : SharedLaserPointerSystem
{
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IStateManager _state = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private HandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();

        _overlay.AddOverlay(new LaserPointerOverlay(EntityManager, _prototype));
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<LaserPointerOverlay>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Timing.IsFirstTimePredicted)
            return;

        if (!TryComp(_player.LocalEntity, out HandsComponent? hands) ||
            !TryComp(_player.LocalEntity.Value, out TransformComponent? xform))
            return;

        var player = _player.LocalEntity.Value;

        EntityUid? hovered = null;
        MapCoordinates? mousePos = null;
        if (_state.CurrentState is GameplayStateBase screen && TryComp(player, out CombatModeComponent? combat) &&
            combat.IsInCombatMode)
        {
            mousePos = _eye.PixelToMap(_input.MouseScreenPosition);

            if (mousePos.Value.MapId == MapId.Nullspace)
                mousePos = null;
            else
                hovered = screen.GetDamageableClickedEntity(mousePos.Value);
        }

        Vector2? dir = mousePos == null ? null : mousePos.Value.Position - _transform.GetWorldPosition(xform);

        // WOLFGATE(Performance) START: a wielded pointer's aim is sent when it changes, at most 10 times a second
        // Every held pointer sent one each tick, wielded or not, and each dirtied a component every player is sent.
        // Unwielding already drops the line on the server, so an unwielded pointer sends nothing.
        var from = _transform.GetWorldPosition(xform);
        foreach (var held in _hands.EnumerateHeld(player, hands))
        {
            if (!HasComp<LaserPointerComponent>(held))
                continue;

            if (!TryComp(held, out WieldableComponent? wieldable) || !wieldable.Wielded)
            {
                _wgSent.Remove(held);
                continue;
            }

            if (WGTakeAim(held, hovered, dir, from))
                RaisePredictiveEvent(new LaserPointerEntityHoveredEvent(GetNetEntity(hovered), dir, GetNetEntity(held)));
        }

        // foreach (var held in _hands.EnumerateHeld(player, hands))
        // {
        //     if (!HasComp<LaserPointerComponent>(held))
        //         continue;
        //
        //     if (hovered == null || !TryComp(held, out WieldableComponent? wieldable) || !wieldable.Wielded)
        //     {
        //         RaisePredictiveEvent(new LaserPointerEntityHoveredEvent(null, dir, GetNetEntity(held)));
        //         continue;
        //     }
        //
        //     RaisePredictiveEvent(
        //         new LaserPointerEntityHoveredEvent(GetNetEntity(hovered.Value), dir, GetNetEntity(held)));
        // }
        // WOLFGATE END
    }
}
