using Content.Shared._WF.Hideouts;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Hideouts;

/// <summary>
/// The outpost registry at Central: the player's outpost, standing or saved, with landing and riding down; or, with
/// none, the worlds and their free plots to found one on.
/// </summary>
public sealed class WFHideoutRegistryWindow : DefaultWindow
{
    public event Action<string>? OnFound;
    public event Action? OnLand;
    public event Action? OnRide;

    private readonly BoxContainer _body = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };

    public WFHideoutRegistryWindow()
    {
        Title = Loc.GetString("wf-hideout-registry-title");
        MinSize = new System.Numerics.Vector2(480, 420);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_body);
        Contents.AddChild(scroll);
    }

    public void UpdateState(WFHideoutRegistryState state)
    {
        _body.RemoveAllChildren();
        _body.AddChild(new Label { Text = Loc.GetString("wf-hideout-registry-balance", ("balance", state.Balance)) });

        if (state.Outpost != null)
        {
            _body.AddChild(Heading(Loc.GetString("wf-hideout-registry-yours")));
            _body.AddChild(new Label { Text = Loc.GetString("wf-hideout-registry-standing", ("outpost", state.Outpost), ("world", state.OutpostWorld ?? "?")) });
            _body.AddChild(Button(Loc.GetString("wf-hideout-registry-ride", ("price", state.RideFee)), state.RideFee > state.Balance, () => OnRide?.Invoke()));
            return;
        }

        if (state.Reading)
        {
            _body.AddChild(Secondary(Loc.GetString("wf-hideout-registry-reading")));
            return;
        }

        if (state.Landing)
        {
            _body.AddChild(Secondary(Loc.GetString("wf-hideout-registry-landing")));
            return;
        }

        if (state.Save != null)
        {
            _body.AddChild(Heading(Loc.GetString("wf-hideout-registry-yours")));
            _body.AddChild(new Label { Text = Loc.GetString("wf-hideout-registry-saved", ("outpost", state.Save)) });
            _body.AddChild(Button(Loc.GetString("wf-hideout-registry-land", ("price", state.LandPrice)), state.LandPrice > state.Balance, () => OnLand?.Invoke()));
            _body.AddChild(Secondary(Loc.GetString("wf-hideout-registry-or-found")));
        }

        _body.AddChild(Heading(Loc.GetString("wf-hideout-registry-found-heading")));
        _body.AddChild(Secondary(Loc.GetString("wf-hideout-registry-found-help", ("price", state.FoundPrice))));
        foreach (var world in state.Worlds)
        {
            var id = world.Id;
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            row.AddChild(new Label
            {
                Text = Loc.GetString("wf-hideout-registry-world", ("world", world.Name), ("free", world.FreePlots), ("plots", world.Plots)),
                HorizontalExpand = true,
            });
            row.AddChild(Button(Loc.GetString("wf-hideout-registry-found", ("price", state.FoundPrice)),
                world.FreePlots == 0 || state.FoundPrice > state.Balance, () => OnFound?.Invoke(id)));
            _body.AddChild(row);
        }
    }

    private static Button Button(string text, bool disabled, Action pressed)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.OnPressed += _ => pressed();
        return button;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }
}
