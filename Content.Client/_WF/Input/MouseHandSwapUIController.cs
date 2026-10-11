using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;

namespace Content.Client._WF.Input;

/// <summary>
/// Lets a mouse button bound to swap hands work with the cursor over the HUD, as a key does.
/// </summary>
public sealed class MouseHandSwapUIController : UIController
{
    [Dependency] private IInputManager _input = default!;

    public override void Initialize()
    {
        base.Initialize();
        _input.UIKeyBindStateChanged += OnUIKeyBind;
    }

    private bool OnUIKeyBind(BoundKeyEventArgs args)
    {
        // Only mouse binds can focus. A key already reaches the game, and must not while a text box has it.
        if (args.State != BoundKeyState.Down || !args.CanFocus || args.Handled)
            return false;

        if (args.Function != ContentKeyFunctions.SwapHands && args.Function != ContentKeyFunctions.SwapHandsPrevious)
            return false;

        // The engine keeps a click on any control from the game. A viewport passes it on itself.
        if (UIManager.MouseGetControl(args.PointerLocation) is null or IViewportControl)
            return false;

        _input.ViewportKeyEvent(null, args);
        return false;
    }
}
