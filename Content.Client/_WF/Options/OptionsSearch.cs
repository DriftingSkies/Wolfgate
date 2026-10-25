using Content.Client.Options.UI;
using Content.Client.Options.UI.Tabs;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Options;

/// <summary>Filters every tab of the options window by setting name, hiding tabs with nothing left.</summary>
public sealed class OptionsSearch
{
    private readonly TabContainer _tabs;
    private readonly HashSet<Control> _hidden = new();
    private readonly HashSet<int> _hiddenTabs = new();

    public OptionsSearch(TabContainer tabs)
    {
        _tabs = tabs;
    }

    /// <summary>
    /// Shows the settings whose text contains <paramref name="query"/> and the headers above them; a matching header
    /// or tab title keeps its whole section or tab. Switches to the first tab with a match if the current one has none.
    /// Returns whether anything matched. An empty query restores everything.
    /// </summary>
    public bool Filter(string query)
    {
        query = query.Trim();
        Restore();

        // TabContainer only rebuilds its tab strip on a measure, and changing a tab's visibility doesn't trigger one.
        _tabs.InvalidateMeasure();

        var all = query.Length == 0;
        var firstMatch = -1;

        for (var i = 0; i < _tabs.ChildCount; i++)
        {
            var tab = _tabs.GetChild(i);

            if (tab is KeyRebindTab keys)
            {
                if (keys.FindControl<BoxContainer>("KeybindsContainer") is not { } binds)
                    continue;

                var titleHit = all || Contains(_tabs.GetActualTabTitle(i), query);
                if (!KeybindSearch.Filter(binds, titleHit ? string.Empty : query) && !titleHit)
                    HideTab(i);
                else if (firstMatch < 0 && _tabs.GetTabVisible(i))
                    firstMatch = i;

                continue;
            }

            if (all || !_tabs.GetTabVisible(i))
                continue;

            if (Contains(_tabs.GetActualTabTitle(i), query) || FilterChildren(tab, query))
            {
                if (firstMatch < 0)
                    firstMatch = i;
            }
            else
            {
                HideTab(i);
            }
        }

        if (all)
            return true;

        if (firstMatch < 0)
        {
            // Nothing matched: keep the tab strip so the window isn't left without one.
            RestoreTabs();
            return false;
        }

        if (_hiddenTabs.Contains(_tabs.CurrentTab))
            _tabs.CurrentTab = firstMatch;

        return true;
    }

    private void Restore()
    {
        foreach (var control in _hidden)
        {
            control.Visible = true;
        }

        _hidden.Clear();
        RestoreTabs();
    }

    private void RestoreTabs()
    {
        foreach (var tab in _hiddenTabs)
        {
            _tabs.SetTabVisible(tab, true);
        }

        _hiddenTabs.Clear();
    }

    private void HideTab(int tab)
    {
        if (!_tabs.GetTabVisible(tab))
            return;

        _tabs.SetTabVisible(tab, false);
        _hiddenTabs.Add(tab);
    }

    /// <summary>
    /// Hides the children of <paramref name="container"/> that don't match. A section header (a key-text label) that
    /// matches keeps every sibling up to the next header. Returns whether any child is left shown.
    /// </summary>
    private bool FilterChildren(Control container, string query)
    {
        var any = false;
        Control? header = null;
        var headerHit = false;
        var sectionShown = false;

        foreach (var child in container.Children)
        {
            // Hidden by the tab itself, or the apply/save strip, which stays put.
            if (!child.Visible || child is OptionsTabControlRow or StripeBack)
                continue;

            if (child is Label label && label.HasStyleClass(StyleNano.StyleClassLabelKeyText))
            {
                CloseSection(header, headerHit || sectionShown);
                header = child;
                headerHit = Contains(label.Text, query);
                sectionShown = false;
                any |= headerHit;
                continue;
            }

            bool shown;
            if (headerHit)
                shown = true;
            else if (child is BoxContainer or ScrollContainer or PanelContainer)
                shown = FilterChildren(child, query);
            else
                shown = Matches(child, query);

            if (!shown)
                Hide(child);

            sectionShown |= shown;
            any |= shown;
        }

        CloseSection(header, headerHit || sectionShown);
        return any;
    }

    private void CloseSection(Control? header, bool shown)
    {
        if (header != null && !shown)
            Hide(header);
    }

    private void Hide(Control control)
    {
        control.Visible = false;
        _hidden.Add(control);
    }

    /// <summary>Whether any label or checkbox text inside <paramref name="control"/> contains the query.</summary>
    private static bool Matches(Control control, string query)
    {
        switch (control)
        {
            case Label label when Contains(label.Text, query):
            case CheckBox box when Contains(box.Text, query):
                return true;
        }

        foreach (var child in control.Children)
        {
            if (Matches(child, query))
                return true;
        }

        return false;
    }

    private static bool Contains(string? text, string query)
    {
        return text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }
}
