using Content.Shared._WF.Photography;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;

namespace Content.Client._WF.Photography;

/// <summary>Outlines in red the tiles the held camera would photograph at the mouse.</summary>
public sealed class PhotoPreviewOverlay : Overlay
{
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private readonly SharedHandsSystem _hands;
    private readonly SharedPhotoCameraSystem _camera;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public PhotoPreviewOverlay()
    {
        IoCManager.InjectDependencies(this);
        _hands = _entMan.System<SharedHandsSystem>();
        _camera = _entMan.System<SharedPhotoCameraSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye != _eye.CurrentEye
            || _player.LocalEntity is not { } player
            || !_entMan.TryGetComponent<HandsComponent>(player, out var hands)
            || _hands.GetActiveItem((player, hands)) is not { } held
            || !_entMan.TryGetComponent<PhotoCameraComponent>(held, out var camera)
            || _ui.CurrentlyHovered is not IViewportControl viewport
            || !_input.MouseScreenPosition.IsValid)
            return;

        var target = viewport.PixelToMap(_input.MouseScreenPosition.Position);
        if (target.MapId != args.MapId)
            return;

        var square = _camera.GetSquare(target, PhotoRules.TilesForAperture(camera.Aperture));
        args.WorldHandle.DrawRect(square.Bounds(), Color.Red, filled: false);
    }
}
