using Content.Client._WG.Options;
using Robust.Client.Graphics;

namespace Content.Client.Options.UI;

/// <summary>The SS14 and Wolfgate master tabs of the options window, and the search bar that spans both.</summary>
public sealed partial class OptionsMenu
{
    /// <summary>Master page holding the stock SS14 tabs.</summary>
    public const int WgStockPage = 0;

    /// <summary>Master page holding the Wolfgate tabs.</summary>
    public const int WgWolfgatePage = 1;

    private OptionsSearch _wgSearch = default!;

    private void WgInitialize()
    {
        WgMaster.SetTabTitle(WgStockPage, Loc.GetString("wg-options-master-ss14"));
        WgMaster.SetTabTitle(WgWolfgatePage, Loc.GetString("wg-options-master-wolfgate"));
        // Each page draws its own tab panel.
        WgMaster.PanelStyleBoxOverride = new StyleBoxEmpty();
        WgMasterStrip.Bind(WgMaster);

        _wgSearch = new OptionsSearch(WgMaster);
        WgSearchBar.OnTextChanged += args => WgFilter(args.Text);
    }

    /// <summary>Shows a stock SS14 tab, as the <c>options</c> command does.</summary>
    public void WgShowStockTab(int tab)
    {
        WgClearSearch();
        WgMaster.CurrentTab = WgStockPage;
        Tabs.CurrentTab = tab;
    }

    /// <summary>Shows one of the Wolfgate tabs, such as <see cref="ConsentTabIndex"/>.</summary>
    public void WgShowWolfgateTab(int tab)
    {
        WgClearSearch();
        WgMaster.CurrentTab = WgWolfgatePage;
        WolfgateTabs.CurrentTab = tab;
    }

    private void WgFilter(string query)
    {
        WgSearchEmpty.Visible = !_wgSearch.Filter(query);
        WgMasterStrip.Refresh(query.Trim().Length > 0 && !WgSearchEmpty.Visible);
    }

    private void WgClearSearch()
    {
        if (WgSearchBar.Text.Length == 0)
            return;

        WgSearchBar.Text = string.Empty;
        WgFilter(string.Empty);
    }
}
