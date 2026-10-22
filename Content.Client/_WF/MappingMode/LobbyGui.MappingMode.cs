using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI;

public sealed partial class LobbyGui
{
    private readonly List<Control> _wfMappingHidden = new();
    private Control? _wfMappingPanel;
    private Button? _wfMappingObserve;

    /// <summary>The mapping panel on the lobby, if the mode is on.</summary>
    public Control? WFMappingPanel => _wfMappingPanel;

    /// <summary>
    /// Turns the lobby into the mapping menu: hides the round, crew and character controls, puts the mapping panel in
    /// the hero area and adds an admin-only Observe entry, since the upstream one stays disabled before a round.
    /// </summary>
    public void WFApplyMappingMode(Control panel, Action observe)
    {
        // The engine keeps one LobbyGui for every visit, so a panel from an earlier visit is replaced, not kept.
        _wfMappingPanel?.Orphan();
        _wfMappingObserve?.Orphan();
        _wfMappingObserve = null;

        WFMappingHide(ReadyButton.Parent);
        WFMappingHide(CustomizeButton);
        WFMappingHide(ObserveButton);
        WFMappingHide(CallVoteButton);
        WFMappingHide(ActiveCrews);
        WFMappingHide(StartTime);
        foreach (var child in CharacterHero.Children)
        {
            WFMappingHide(child);
        }

        _wfMappingPanel = panel;
        CharacterHero.AddChild(panel);

        _wfMappingObserve = new Button
        {
            Text = Loc.GetString("wf-mapping-mode-observe"),
            ToolTip = Loc.GetString("wf-mapping-mode-observe-tooltip"),
            TextAlign = Label.AlignMode.Left,
            StyleClasses = { "LobbyNav" },
            Visible = false,
        };
        _wfMappingObserve.OnPressed += _ => observe();
        if (ObserveButton.Parent is { } menu)
        {
            menu.AddChild(_wfMappingObserve);
            _wfMappingObserve.SetPositionInParent(ObserveButton.GetPositionInParent() + 1);
        }
    }

    /// <summary>Shows the mapping Observe entry; only admins get it.</summary>
    public void WFSetMappingObserveVisible(bool visible)
    {
        if (_wfMappingObserve != null)
            _wfMappingObserve.Visible = visible;
    }

    /// <summary>Puts the normal lobby back after mapping mode is turned off.</summary>
    public void WFRemoveMappingMode()
    {
        if (_wfMappingPanel == null)
            return;

        _wfMappingPanel.Orphan();
        _wfMappingPanel = null;
        _wfMappingObserve?.Orphan();
        _wfMappingObserve = null;

        foreach (var control in _wfMappingHidden)
        {
            control.Visible = true;
        }

        _wfMappingHidden.Clear();
    }

    private void WFMappingHide(Control? control)
    {
        if (control is not { Visible: true })
            return;

        control.Visible = false;
        _wfMappingHidden.Add(control);
    }
}
