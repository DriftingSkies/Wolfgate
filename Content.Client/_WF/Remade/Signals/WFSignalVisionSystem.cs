using Content.Shared._WF.Remade.Signals;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Remade.Signals;

/// <summary>
/// What a Signal sees and reads: the screen drained and closed in dark red, and a strip with its Psi. Both come on when the
/// player's body is a Signal and go when it isn't, such as while it drives a Remade.
/// </summary>
public sealed partial class WFSignalVisionSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    private WFSignalOverlay? _overlay;
    private WFSignalHud? _hud;

    /// <summary>Whether the Signal look is on. Tests read it.</summary>
    public bool Active => _overlay != null && _hud != null;

    public override void Shutdown()
    {
        base.Shutdown();
        Stop();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_player.LocalEntity is not { } body || !TryComp<WFSignalComponent>(body, out var signal))
        {
            Stop();
            return;
        }

        if (_overlay == null)
        {
            _overlay = new WFSignalOverlay();
            _overlays.AddOverlay(_overlay);
        }

        if (_hud == null)
        {
            _hud = new WFSignalHud(_cache);
            _ui.WindowRoot.AddChild(_hud);
            LayoutContainer.SetAnchorAndMarginPreset(_hud, LayoutContainer.LayoutPreset.CenterTop, margin: 48);
            LayoutContainer.SetGrowHorizontal(_hud, LayoutContainer.GrowDirection.Both);
        }

        _hud.Update(signal);
    }

    private void Stop()
    {
        if (_overlay != null)
        {
            _overlays.RemoveOverlay(_overlay);
            _overlay = null;
        }

        if (_hud != null)
        {
            _hud.Orphan();
            _hud = null;
        }
    }
}
