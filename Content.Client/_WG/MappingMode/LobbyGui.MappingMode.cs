using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    private readonly List<Control> _wgMappingHidden = new();
    private Control? _wgMappingPanel;
    private Button? _wgMappingObserve;

    /// <summary>The mapping panel on the lobby, if the mode is on.</summary>
    public Control? WGMappingPanel => _wgMappingPanel;

    /// <summary>
    /// Turns the lobby into the mapping menu: hides the round, crew and character controls, puts the mapping panel in
    /// the hero area and adds an admin-only Observe entry, since the upstream one stays disabled before a round.
    /// </summary>
    public void WGApplyMappingMode(Control panel, Action observe)
    {
        // The engine keeps one LobbyGui for every visit, so a panel from an earlier visit is replaced, not kept.
        _wgMappingPanel?.Orphan();
        _wgMappingObserve?.Orphan();
        _wgMappingObserve = null;

        WGMappingHide(ReadyButton.Parent);
        WGMappingHide(CustomizeButton);
        WGMappingHide(ObserveButton);
        WGMappingHide(CallVoteButton);
        WGMappingHide(ActiveCrews);
        WGMappingHide(StartTime);
        foreach (var child in CharacterHero.Children)
        {
            WGMappingHide(child);
        }

        _wgMappingPanel = panel;
        CharacterHero.AddChild(panel);

        _wgMappingObserve = new Button
        {
            Text = Loc.GetString("wg-mapping-mode-observe"),
            ToolTip = Loc.GetString("wg-mapping-mode-observe-tooltip"),
            TextAlign = Label.AlignMode.Left,
            StyleClasses = { "LobbyNav" },
            Visible = false,
        };
        _wgMappingObserve.OnPressed += _ => observe();
        if (ObserveButton.Parent is { } menu)
        {
            menu.AddChild(_wgMappingObserve);
            _wgMappingObserve.SetPositionInParent(ObserveButton.GetPositionInParent() + 1);
        }
    }

    /// <summary>Shows the mapping Observe entry; only admins get it.</summary>
    public void WGSetMappingObserveVisible(bool visible)
    {
        if (_wgMappingObserve != null)
            _wgMappingObserve.Visible = visible;
    }

    /// <summary>Puts the normal lobby back after mapping mode is turned off.</summary>
    public void WGRemoveMappingMode()
    {
        if (_wgMappingPanel == null)
            return;

        _wgMappingPanel.Orphan();
        _wgMappingPanel = null;
        _wgMappingObserve?.Orphan();
        _wgMappingObserve = null;

        foreach (var control in _wgMappingHidden)
        {
            control.Visible = true;
        }

        _wgMappingHidden.Clear();
    }

    private void WGMappingHide(Control? control)
    {
        if (control is not { Visible: true })
            return;

        control.Visible = false;
        _wgMappingHidden.Add(control);
    }
}
