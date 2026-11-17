using System.Linq;
using Content.Shared._WG.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;

namespace Content.Client._WG.UserInterface.WindowPopout;

/// <summary>
/// Adds a pop-out button next to the close button of every in-game window, letting players drag windows onto
/// another monitor. Works from outside the window classes, so no window needs to opt in.
/// </summary>
public sealed partial class WindowPopoutUIController : UIController
{
    [Dependency] private IConfigurationManager _cfg = default!;

    // Header depth of FancyWindow, DefaultWindow and the custom PDA-style windows, with a little slack.
    private const int CloseButtonSearchDepth = 5;

    private readonly Dictionary<BaseWindow, WolfgatePopoutWindow> _popouts = new();
    private bool _enabled;

    public override void Initialize()
    {
        base.Initialize();

        _cfg.OnValueChanged(WolfgateCVars.WindowPopout, value => _enabled = value, true);
        UIManager.WindowRoot.OnChildAdded += OnWindowAdded;
    }

    private void OnWindowAdded(Control child)
    {
        if (child is not BaseWindow window || FindCloseButton(window) is not { Parent: { } header } close)
            return;

        var button = header.Children.OfType<WolfgatePopoutButton>().FirstOrDefault();
        if (button == null)
        {
            button = new WolfgatePopoutButton();
            button.OnPressed += _ => Toggle(window);
            header.AddChild(button);
            button.SetPositionInParent(close.GetPositionInParent());
        }

        // Windows are re-added on every open, so a changed setting applies from the next open.
        button.Visible = _enabled || button.PoppedOut;
    }

    public bool IsPoppedOut(BaseWindow window)
    {
        return _popouts.ContainsKey(window);
    }

    /// <summary>
    /// Pops an open window out into its own OS window, or docks a popped-out one back into the game window.
    /// </summary>
    public void Toggle(BaseWindow window)
    {
        if (_popouts.TryGetValue(window, out var existing))
        {
            existing.Dock();
            return;
        }

        if (!window.IsOpen)
            return;

        var popout = new WolfgatePopoutWindow(window);
        popout.Closed += () =>
        {
            _popouts.Remove(window);
            SetButtonState(window, false);
        };

        _popouts[window] = popout;
        SetButtonState(window, true);
        popout.PopOut();
    }

    private static void SetButtonState(BaseWindow window, bool poppedOut)
    {
        var button = FindCloseButton(window)?.Parent?.Children.OfType<WolfgatePopoutButton>().FirstOrDefault();
        if (button != null)
            button.PoppedOut = poppedOut;
    }

    /// <summary>
    /// Breadth-first, so the title bar's close button wins over any close-styled button in the contents.
    /// </summary>
    private static TextureButton? FindCloseButton(Control window)
    {
        var queue = new Queue<(Control Control, int Depth)>();
        queue.Enqueue((window, 0));

        while (queue.TryDequeue(out var entry))
        {
            if (entry.Control is TextureButton button && button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton))
                return button;

            if (entry.Depth >= CloseButtonSearchDepth)
                continue;

            foreach (var child in entry.Control.Children)
            {
                queue.Enqueue((child, entry.Depth + 1));
            }
        }

        return null;
    }
}
