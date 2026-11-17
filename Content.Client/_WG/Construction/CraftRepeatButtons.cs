using Content.Client.Stylesheets;
using Content.Shared._WG.Construction;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Construction;

/// <summary>
/// Joined row of x2..x50 and "All" buttons under the construction menu's Craft button.
/// </summary>
public sealed class CraftRepeatButtons : BoxContainer
{
    private static readonly int[] Counts = [2, 5, 10, 20, 50];

    /// <summary>
    /// Raised with the number of crafts asked for.
    /// </summary>
    public event Action<int>? OnRepeat;

    public CraftRepeatButtons()
    {
        Orientation = LayoutOrientation.Horizontal;
        HorizontalExpand = true;

        foreach (var count in Counts)
        {
            AddButton(Loc.GetString("wg-craft-repeat-count", ("count", count)),
                Loc.GetString("wg-craft-repeat-tooltip", ("count", count)),
                count,
                count == Counts[0] ? StyleBase.ButtonOpenRight : StyleBase.ButtonOpenBoth);
        }

        AddButton(Loc.GetString("wg-craft-repeat-all"),
            Loc.GetString("wg-craft-repeat-all-tooltip"),
            CraftRepeatRequestEvent.MaxCount,
            StyleBase.ButtonOpenLeft);
    }

    private void AddButton(string text, string tooltip, int count, string styleClass)
    {
        var button = new Button
        {
            Text = text,
            ToolTip = tooltip,
            HorizontalExpand = true,
        };
        button.AddStyleClass(styleClass);
        button.OnPressed += _ => OnRepeat?.Invoke(count);
        AddChild(button);
    }
}
