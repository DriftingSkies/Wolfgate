using Content.Client.Light;
using Content.Shared._WF.Shuttles;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WF.Shuttles.UI;

/// <summary>
/// Low-light feed for a pilot's hull camera. It floods the light buffer rather than switching the
/// eye's lighting off, because the engine drops FOV along with lighting and the camera would then
/// see straight through the hull.
/// </summary>
public sealed partial class ShuttleLowLightOverlay : Overlay
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    /// <summary>
    /// A sensor's washed-out green, short of pure white so lit areas still read as lit.
    /// </summary>
    private static readonly Color FeedColor = new(0.75f, 0.9f, 0.78f);

    public ShuttleLowLightOverlay()
    {
        IoCManager.InjectDependencies(this);

        // After content has finished writing ambient light, so this has the last word on it.
        ZIndex = AfterLightTargetOverlay.ContentZIndex + 1;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Only the pilot's own view, not a previewer or any other viewport that happens to be open.
        // With any altitude every view is drawn through a stand-in for its eye, the pilot's included.
        return _entManager.TryGetComponent<ShuttleCameraComponent>(_player.LocalEntity, out var camera) &&
               camera.LowLight &&
               camera.View != ShuttleCameraView.Helm &&
               _entManager.TryGetComponent<EyeComponent>(_player.LocalEntity, out var eye) &&
               ShuttleHullRoofOverlay.IsPilotPass(args.Viewport.Eye, eye.Eye, primaryOnly: true);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        args.WorldHandle.RenderInRenderTarget(args.Viewport.LightRenderTarget, () => { }, FeedColor);
    }
}
