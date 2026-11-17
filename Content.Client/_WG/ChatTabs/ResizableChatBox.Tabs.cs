using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

// The floating chat box shrinks to its input line while its log is popped out; see Content.Client/_WG/ChatTabs.
public sealed partial class ResizableChatBox
{
    private Vector2 _wgDockedMinSize;
    private float _wgDockedMarginBottom;
    private bool _wgShrunk;

    protected override void WgLogMoved(bool poppedOut)
    {
        // In the cockpit the box fills a panel, so it keeps its size and shows the notice like any docked chat box.
        if (poppedOut ? WgCockpitDocked : !_wgShrunk)
        {
            base.WgLogMoved(poppedOut);
            return;
        }

        _wgShrunk = poppedOut;
        if (!poppedOut)
        {
            MinSize = _wgDockedMinSize;
            LayoutContainer.SetMarginBottom(this, _wgDockedMarginBottom);
            return;
        }

        _wgDockedMinSize = MinSize;
        _wgDockedMarginBottom = GetValue<float>(LayoutContainer.MarginBottomProperty);

        // Anchored by its top edge, so the bottom margin is where the box ends.
        MinSize = new Vector2(MinSize.X, 0);
        Measure(new Vector2(Size.X, float.PositiveInfinity));
        LayoutContainer.SetMarginBottom(this, GetValue<float>(LayoutContainer.MarginTopProperty) + DesiredSize.Y);

        // A clamp already waiting would read the box's old height and undo this; it starts over.
        if (_clampIn > 0)
            ClampAfterDelay();
    }
}
