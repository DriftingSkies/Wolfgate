using Content.Shared._WF.Census;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client._WF.Census;

/// <summary>
/// The Census button for the lobby footer and the game menu: shows how many censuses the player has yet to
/// answer and hides while censuses are off. What a press does is up to where it sits.
/// </summary>
public sealed class CensusButton : Button
{
    private readonly IConfigurationManager _cfg;
    private readonly CensusUIController _controller;

    public CensusButton()
    {
        _cfg = IoCManager.Resolve<IConfigurationManager>();
        _controller = UserInterfaceManager.GetUIController<CensusUIController>();
        Text = Loc.GetString("wf-census-button");
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _controller.Changed += Refresh;
        _cfg.OnValueChanged(CensusCVars.Enabled, OnEnabledChanged);
        Refresh();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _controller.Changed -= Refresh;
        _cfg.UnsubValueChanged(CensusCVars.Enabled, OnEnabledChanged);
    }

    private void OnEnabledChanged(bool _)
    {
        Refresh();
    }

    private void Refresh()
    {
        var count = _controller.Unanswered;
        Text = count > 0
            ? Loc.GetString("wf-census-button-count", ("count", count))
            : Loc.GetString("wf-census-button");
        Visible = _controller.Enabled;
    }
}
