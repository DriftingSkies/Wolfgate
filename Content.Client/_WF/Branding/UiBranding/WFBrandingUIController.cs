using System.Linq;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Branding.UiBranding;

/// <summary>
/// Puts the Wolfgate logo in the middle of every titled window's header and in a footer strip at its bottom. No
/// window opts in: every frame it checks each open window, popped-out ones included, and restores either mark if
/// something removed, hid, reordered or faded it.
/// </summary>
public sealed class WFBrandingUIController : UIController
{
    [Dependency] private IResourceCache _cache = default!;

    public const string HeaderTexturePath = "/Textures/_WF/Branding/UiBranding/branding_header.png";
    public const string FooterTexturePath = "/Textures/_WF/Branding/UiBranding/branding_footer.png";

    // Header depth of FancyWindow, DefaultWindow and custom headers, with slack.
    private const int CloseButtonSearchDepth = 6;

    private readonly HashSet<BaseWindow> _windows = new();
    // Windows with no title bar, such as radial menus; forgotten when they close.
    private readonly HashSet<BaseWindow> _untitled = new();
    private readonly List<BaseWindow> _scratch = new();
    private Texture? _headerTexture;
    private Texture? _footerTexture;

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        foreach (var child in UIManager.WindowRoot.Children)
        {
            if (child is BaseWindow window)
                _windows.Add(window);
        }

        // Popped-out windows live under an OS window's root, so they're tracked from when they were docked.
        _scratch.Clear();
        _scratch.AddRange(_windows);
        foreach (var window in _scratch)
        {
            if (window.Disposed || !window.IsInsideTree)
            {
                _windows.Remove(window);
                _untitled.Remove(window);
                continue;
            }

            if (!_untitled.Contains(window))
                Enforce(window);
        }
    }

    private void Enforce(BaseWindow window)
    {
        var header = window.Children.OfType<WFBrandingHeader>().FirstOrDefault();
        var row = header?.Row is { } cached && IsInside(cached, window) ? cached : FindCloseButton(window)?.Parent;
        if (row == null)
        {
            _untitled.Add(window);
            return;
        }

        _headerTexture ??= _cache.GetResource<TextureResource>(HeaderTexturePath).Texture;
        _footerTexture ??= _cache.GetResource<TextureResource>(FooterTexturePath).Texture;

        if (header == null)
        {
            header = new WFBrandingHeader(_headerTexture);
            window.AddChild(header);
        }

        // Windows the older title-bar badge already brands keep that; everything else gets the centred mark.
        header.Row = row.Children.Any(c => c.Name == WFWindowBranding.BadgeName) ? null : row;
        Restore(header);
        KeepLast(header);

        if (FindStack(row, window) is not { } stack)
            return;

        var footer = stack.Children.OfType<WFBrandingFooter>().FirstOrDefault();
        if (footer == null)
        {
            footer = new WFBrandingFooter(_footerTexture);
            stack.AddChild(footer);
        }

        Restore(footer);
        footer.SetHeight = WFBrandingFooter.StripHeight;
        KeepLast(footer);
    }

    private static void Restore(Control control)
    {
        control.Visible = true;
        control.Modulate = Color.White;
        control.ModulateSelfOverride = null;
        control.MouseFilter = Control.MouseFilterMode.Ignore;
    }

    private static void KeepLast(Control control)
    {
        if (control.Parent is { } parent && control.GetPositionInParent() != parent.ChildCount - 1)
            control.SetPositionInParent(parent.ChildCount - 1);
    }

    /// <summary>The window's vertical stack that holds the header, where the footer goes last.</summary>
    private static BoxContainer? FindStack(Control row, BaseWindow window)
    {
        for (var control = row.Parent; control != null && control != window; control = control.Parent)
        {
            if (control is BoxContainer { Orientation: BoxContainer.LayoutOrientation.Vertical } stack)
                return stack;
        }

        return null;
    }

    private static bool IsInside(Control control, BaseWindow window)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent == window)
                return true;
        }

        return false;
    }

    /// <summary>Breadth-first, so the title bar's close button wins over any close-styled button in the contents.</summary>
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
