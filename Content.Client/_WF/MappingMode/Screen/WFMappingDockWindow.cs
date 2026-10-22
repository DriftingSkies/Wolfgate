using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.MappingMode.Screen;

/// <summary>A floating window holding an undocked mapping screen panel; the window pop-out button works on it.</summary>
public sealed class WFMappingDockWindow : FancyWindow
{
    /// <summary>Where the undocked panel goes.</summary>
    public readonly BoxContainer Body;

    public WFMappingDockWindow(string title)
    {
        Title = title;
        MinSize = new Vector2(280, 200);
        SetSize = new Vector2(380, 560);

        Body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        ContentsContainer.AddChild(Body);
    }

    /// <summary>
    /// Raised when the window has left the UI tree and is still out of it once the frame's deferred actions run:
    /// closed, or its pop-out OS window torn down. A pop-out moves it out and back in at once, which does not count.
    /// </summary>
    public event Action? Lost;

    protected override void ExitedTree()
    {
        base.ExitedTree();
        UserInterfaceManager.DeferAction(() =>
        {
            if (!IsInsideTree)
                Lost?.Invoke();
        });
    }
}
