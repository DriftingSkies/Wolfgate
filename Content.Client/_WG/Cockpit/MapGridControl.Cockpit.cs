using System.Numerics;
using Content.Client._WG.Cockpit;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Client.UserInterface.Controls;

public partial class MapGridControl
{
    [Dependency] private IInputManager _wgCockpitInput = default!;

    private bool _wgCockpitPanDown;
    private bool _wgCockpitPanning;
    private Vector2? _wgCockpitMouse;

    /// <summary>Uses shared cockpit panning and compact map annotations.</summary>
    public bool WgCockpitControls { get; private set; }

    /// <summary>Enables right-mouse panning until the borrowed map returns to its console.</summary>
    public void WgCockpitInteraction(WGCockpitLease lease)
    {
        var enabled = WgCockpitControls;
        WgCockpitControls = true;
        _draggin = false;
        WgEndCockpitPan();
        lease.Remember(() =>
        {
            WgCockpitControls = enabled;
            _draggin = false;
            WgEndCockpitPan();
        });
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _wgCockpitInput.FirstChanceOnKeyEvent += WgCockpitPanKey;
        DisplayManager.OnWindowFocused += WgCockpitWindowFocused;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        WgUpdateCockpitPan(_wgCockpitInput.MouseScreenPosition);
    }

    protected override void VisibilityChanged(bool newVisible)
    {
        base.VisibilityChanged(newVisible);
        if (WgCockpitControls)
            WgEndCockpitPan();
    }

    protected override void ExitedTree()
    {
        _wgCockpitInput.FirstChanceOnKeyEvent -= WgCockpitPanKey;
        DisplayManager.OnWindowFocused -= WgCockpitWindowFocused;
        base.ExitedTree();
        if (WgCockpitControls)
            WgEndCockpitPan();
    }

    private void WgCockpitPanKey(KeyEventArgs args, KeyEventType type) =>
        WgCockpitPanKey(args, type, _wgCockpitInput.MouseScreenPosition);

    /// <summary>Ends a pan when its window loses focus; the focus flag itself never gates a press.</summary>
    private void WgCockpitWindowFocused(WindowFocusedEventArgs args)
    {
        if (!args.Focused && WgCockpitControls)
            WgEndCockpitPan();
    }

    /// <summary>Captures only a fresh right-button press on this plot before context menus can open.</summary>
    private void WgCockpitPanKey(KeyEventArgs args, KeyEventType type, ScreenCoordinates mouse)
    {
        if (args.Key != Keyboard.Key.MouseRight)
            return;
        if (_wgCockpitPanDown)
        {
            args.Handle();
            if (type == KeyEventType.Up)
                WgEndCockpitPan();
            return;
        }
        if (type != KeyEventType.Down || args.IsRepeat || !WgCockpitControls || !VisibleInTree || !mouse.IsValid ||
            mouse.Window != Window?.Id || !GlobalPixelRect.Contains((Vector2i) mouse.Position))
            return;
        // Window focus and the window's modifier flags were dropped as gates: either can go stale and silently
        // refuse every press. Modifiers come from the engine's own key state instead.
        var diagnostics = EntManager.System<WGCockpitPanDiagnosticSystem>();
        var source = Loc.GetString("wg-cockpit-pan-source-plot", ("plot", GetType().Name));
        var hit = UserInterfaceManager.MouseGetControl(mouse);
        var refusal = args.Handled ? "wg-cockpit-pan-already-taken"
            : WGCockpitPanDiagnosticSystem.ModifierHeld(_wgCockpitInput) ? "wg-cockpit-pan-modifier"
            : hit != this ? "wg-cockpit-pan-covered"
            : null;
        diagnostics.Record(args, mouse, source, refusal ?? "wg-cockpit-pan-started");
        if (refusal != null)
            return;
        args.Handle();
        _wgCockpitPanDown = _wgCockpitPanning = true;
        _wgCockpitMouse = mouse.Position;
    }

    private void WgUpdateCockpitPan(ScreenCoordinates mouse)
    {
        if (!WgCockpitControls)
            return;
        if (!VisibleInTree || !_wgCockpitPanDown)
        {
            WgEndCockpitPan();
            return;
        }

        if (!_wgCockpitPanning)
            return;
        if (!mouse.IsValid || mouse.Window != Window?.Id || MinimapScale <= 0f)
        {
            _wgCockpitMouse = null;
            return;
        }

        if (_wgCockpitMouse is { } last)
        {
            var delta = mouse.Position - last;
            if (delta != Vector2.Zero)
            {
                Recentering = false;
                Offset -= new Vector2(delta.X, -delta.Y) / MinimapScale;
                WgCockpitPanMoved();
            }
        }
        _wgCockpitMouse = mouse.Position;
    }

    /// <summary>Updates map-specific tracking after a right-mouse pan.</summary>
    protected virtual void WgCockpitPanMoved()
    {
    }

    private void WgEndCockpitPan()
    {
        _wgCockpitPanDown = false;
        _wgCockpitPanning = false;
        _wgCockpitMouse = null;
    }
}
