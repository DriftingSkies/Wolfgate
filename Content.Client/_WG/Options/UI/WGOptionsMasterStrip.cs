using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WG.Options.UI;

/// <summary>
/// The SS14 / Wolfgate switch above the options tabs: one wide button per page of a tab container whose own strip is
/// hidden. During a search a page with no match is disabled, and the others show how many of their tabs match.
/// </summary>
public sealed class WGOptionsMasterStrip : PanelContainer
{
    public const string StyleClassStrip = "WGOptionsMasterStrip";
    public const string StyleClassTab = "WGOptionsMasterTab";

    private readonly BoxContainer _buttons;
    private readonly ButtonGroup _group = new(false);
    private readonly List<Button> _tabs = new();
    private TabContainer? _pages;
    private bool _searching;

    public WGOptionsMasterStrip()
    {
        StyleClasses.Add(StyleClassStrip);
        _buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 0,
        };
        AddChild(_buttons);
    }

    /// <summary>Builds one button per page of <paramref name="pages"/>, titled after the page's tab title.</summary>
    public void Bind(TabContainer pages)
    {
        _pages = pages;
        _buttons.RemoveAllChildren();
        _tabs.Clear();

        for (var i = 0; i < pages.ChildCount; i++)
        {
            var index = i;
            var button = new Button
            {
                ToggleMode = true,
                Group = _group,
                HorizontalExpand = true,
                StyleClasses = { StyleClassTab },
            };
            button.OnPressed += _ => pages.CurrentTab = index;
            _tabs.Add(button);
            _buttons.AddChild(button);
        }

        pages.OnTabChanged += _ => Refresh(_searching);
        Refresh(false);
    }

    /// <summary>
    /// Matches the buttons to the pages: the current one pressed, a hidden one disabled and, when
    /// <paramref name="searching"/>, each title followed by its count of visible tabs.
    /// </summary>
    public void Refresh(bool searching)
    {
        _searching = searching;
        if (_pages == null)
            return;

        for (var i = 0; i < _tabs.Count; i++)
        {
            var button = _tabs[i];
            var title = _pages.GetActualTabTitle(i);
            var visible = _pages.GetTabVisible(i);

            button.Text = searching && visible && _pages.GetChild(i) is TabContainer page
                ? Loc.GetString("wg-options-master-tab-matches", ("title", title), ("count", VisibleTabs(page)))
                : title;
            button.Disabled = !visible;
            // The group unpresses the others; unpressing one directly throws.
            if (i == _pages.CurrentTab)
                button.Pressed = true;
        }
    }

    private static int VisibleTabs(TabContainer page)
    {
        var count = 0;
        for (var i = 0; i < page.ChildCount; i++)
        {
            if (page.GetTabVisible(i))
                count++;
        }

        return count;
    }
}
