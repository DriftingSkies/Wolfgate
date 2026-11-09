using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Colonies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Colonies;

/// <summary>The yard console: the builds on the berths with what each stage still needs, and the designs to lay down.</summary>
public sealed class WFYardConsoleWindow : FancyWindow
{
    /// <summary>The viewer lays down a design for their company.</summary>
    public event Action<string>? OnOrder;

    /// <summary>The viewer scraps one of their company's builds.</summary>
    public event Action<int>? OnScrap;

    private readonly BoxContainer _body;

    public WFYardConsoleWindow()
    {
        Title = Loc.GetString("wf-yard-ui-title");
        MinSize = new Vector2i(520, 420);
        SetSize = new Vector2i(640, 560);
        _body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(8) };
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_body);
        ContentsContainer.AddChild(scroll);
    }

    private static PanelContainer Card(Control content)
    {
        var card = new PanelContainer { HorizontalExpand = true };
        card.AddStyleClass("AngleRect");
        content.Margin = new Thickness(10, 6);
        card.AddChild(content);
        return card;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text, ClipText = true };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    public void UpdateState(WFYardConsoleState state)
    {
        _body.RemoveAllChildren();

        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
        header.AddChild(Heading(Loc.GetString("wf-yard-ui-world", ("world", state.World))));
        header.AddChild(Secondary(state.Governor is { } governor
            ? Loc.GetString("wf-colony-ui-governor", ("company", governor))
            : Loc.GetString("wf-colony-ui-no-governor")));
        if (state.Reason is { } reason)
            header.AddChild(new Label { Text = reason, FontColorOverride = Color.FromHex("#E8A33D") });
        _body.AddChild(Card(header));

        var berths = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        berths.AddChild(Heading(Loc.GetString("wf-yard-ui-berths")));
        if (state.Jobs.Count == 0)
            berths.AddChild(Secondary(Loc.GetString("wf-yard-ui-no-jobs")));
        foreach (var job in state.Jobs)
        {
            var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2 };
            var top = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            top.AddChild(new Label
            {
                Text = Loc.GetString("wf-yard-ui-job", ("design", job.Design), ("company", job.Company)),
                HorizontalExpand = true,
                ClipText = true,
            });
            if (job.Yours)
            {
                var scrap = new ConfirmButton { Text = Loc.GetString("wf-yard-ui-scrap") };
                scrap.AddStyleClass("Caution");
                var id = job.Id;
                scrap.OnPressed += _ => OnScrap?.Invoke(id);
                top.AddChild(scrap);
            }

            box.AddChild(top);
            box.AddChild(Secondary(Loc.GetString("wf-yard-ui-stage", ("stage", job.Stage + 1), ("stages", job.Stages), ("name", job.StageName))));
            box.AddChild(Secondary(Loc.GetString("wf-yard-ui-crates",
                ("components", job.Components), ("componentsNeeded", job.ComponentsNeeded),
                ("metals", job.Metals), ("metalsNeeded", job.MetalsNeeded))));
            box.AddChild(new ProgressBar { MinValue = 0, MaxValue = 1, Value = job.Progress, MinHeight = 10, HorizontalExpand = true });
            berths.AddChild(box);
        }

        _body.AddChild(Card(berths));

        var designs = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        designs.AddChild(Heading(Loc.GetString("wf-yard-ui-designs")));
        foreach (var design in state.Designs)
        {
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
            var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            text.AddChild(new Label { Text = design.Name, ClipText = true });
            text.AddChild(Secondary(Loc.GetString("wf-yard-ui-design-cost", ("stages", design.Stages),
                ("components", design.Components), ("metals", design.Metals))));
            if (design.Licensed)
                text.AddChild(Secondary(Loc.GetString("wf-yard-ui-licensed")));
            row.AddChild(text);
            var order = new Button { Text = Loc.GetString("wf-yard-ui-order"), Disabled = state.Reason != null };
            var id = design.Id;
            order.OnPressed += _ => OnOrder?.Invoke(id);
            row.AddChild(order);
            row.ToolTip = design.Description;
            designs.AddChild(row);
        }

        _body.AddChild(Card(designs));
    }
}
