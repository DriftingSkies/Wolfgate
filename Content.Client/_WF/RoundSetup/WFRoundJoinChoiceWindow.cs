using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Offers the scenario crew flow first, then station roles, then waking up at the player's outpost when they can.</summary>
public sealed class WFRoundJoinChoiceWindow : DefaultWindow
{
    private readonly BoxContainer _body;
    private bool _outpost;

    /// <summary>Closes the chooser before opening the selected joining flow.</summary>
    public WFRoundJoinChoiceWindow(Action crew, Action stationRoles, bool pregame = false)
    {
        Title = Loc.GetString(pregame ? "wf-round-setup-ready-choice-title" : "wf-round-setup-home-jobs");
        MinSize = new Vector2(320, 250);
        SetSize = new Vector2(540, 300);
        _body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 12, Margin = new Thickness(12) };
        Contents.AddChild(_body);
        AddChoice(Loc.GetString("wf-round-setup-" + (pregame ? "ready-crew" : "join-crew")),
            Loc.GetString("wf-round-setup-" + (pregame ? "ready-crew-help" : "join-crew-help")), "RoundJoinCrew", crew, true);
        AddChoice(Loc.GetString("wf-round-setup-" + (pregame ? "ready-station-roles" : "join-station-roles")),
            Loc.GetString("wf-round-setup-" + (pregame ? "ready-station-help" : "join-station-help")), "RoundJoinStation", stationRoles, false);
    }

    /// <summary>Adds the choice to join in a cryopod on the player's outpost, once.</summary>
    public void AddOutpost(string outpost, Action join)
    {
        if (_outpost)
            return;

        _outpost = true;
        SetSize = new Vector2(540, 400);
        AddChoice(Loc.GetString("wf-outpost-join-choice", ("outpost", outpost)), Loc.GetString("wf-outpost-join-choice-help"),
            "RoundJoinOutpost", join, false);
    }

    private void AddChoice(string title, string help, string name, Action action, bool primary)
    {
        var button = new Button { Name = name, Text = title, MinHeight = 48, HorizontalExpand = true };
        button.Label.ClipText = false;
        if (primary)
            button.AddStyleClass("ButtonAccept");
        button.OnPressed += _ => { Close(); action(); };
        _body.AddChild(button);
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(FormattedMessage.FromUnformatted(help));
        _body.AddChild(description);
    }
}
