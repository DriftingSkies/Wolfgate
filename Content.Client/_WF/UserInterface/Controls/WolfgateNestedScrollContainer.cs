using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.UserInterface.Controls;

/// <summary>
/// A vertical scroll box for use inside another scroll container. It passes the wheel on to its parent when it has
/// nothing to scroll or is already at the edge it is scrolled towards, so the outer page never gets stuck behind it.
/// </summary>
public sealed class WolfgateNestedScrollContainer : ScrollContainer
{
    public WolfgateNestedScrollContainer()
    {
        HScrollEnabled = false;
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        if (args.Delta.Y == 0f || !Overflows())
            return;

        // Re-clamped first: a list that shrank leaves the target past its new end.
        VScrollTarget = VScrollTarget;
        var before = VScrollTarget;
        VScrollTarget = before - args.Delta.Y * ScrollSpeedY;
        // Clamped to where it was: at the edge, so the parent scrolls instead.
        if (!MathHelper.CloseTo(VScrollTarget, before))
            args.Handle();
    }

    /// <summary>Whether the content is taller than the box.</summary>
    private bool Overflows()
    {
        var height = 0f;
        foreach (var child in Children)
        {
            if (child is not ScrollBar)
                height = MathF.Max(height, child.DesiredSize.Y);
        }

        return height > Height && !MathHelper.CloseTo(height, Height, 1e-3);
    }
}
