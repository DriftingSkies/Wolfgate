using System.Linq;
using Content.Shared._WF.Colonies;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Colonies;

/// <summary>
/// The outpost console's Development tab: the world's tier and holders, what the outpost's holder makes on the colony and
/// which goods it keeps, and the governor's charter controls.
/// </summary>
public sealed class WFColonyDevelopmentPanel : BoxContainer
{
    private static readonly Color GoodColor = Color.FromHex("#6FCF6F");
    private static readonly Color BadColor = Color.FromHex("#E05A4F");

    /// <summary>The holder keeps a good off the shelf, or sells it again.</summary>
    public event Action<string, bool>? OnKeep;

    /// <summary>The governor sets whether the batteries engage campaign ships in orbit.</summary>
    public event Action<bool>? OnDefence;

    /// <summary>The governor opens or closes the world to newcomers.</summary>
    public event Action<bool>? OnOpen;

    /// <summary>The governor grants or withdraws a holder's permit.</summary>
    public event Action<string, bool>? OnPermit;

    /// <summary>The governor opens or closes a yard berth to a company.</summary>
    public event Action<string, bool>? OnBerth;

    /// <summary>The governor asks for the yard platform kit.</summary>
    public event Action? OnYardKit;

    /// <summary>Whether the viewer's own output is shown; the charter office, which belongs to no holder, leaves it out.</summary>
    public bool ShowOutput = true;

    private WFWorldDevelopmentView? _built;

    public WFColonyDevelopmentPanel()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 8;
        Margin = new Thickness(8);
    }

    public void UpdateState(WFWorldDevelopmentView? view)
    {
        if (view == _built || view != null && _built != null && Same(view, _built))
            return;

        _built = view;
        RemoveAllChildren();
        if (view == null)
            return;

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        var body = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 8 };
        scroll.AddChild(body);
        AddChild(scroll);

        body.AddChild(Card(Summary(view)));
        body.AddChild(Card(Holders(view)));
        if (ShowOutput)
            body.AddChild(Card(Output(view)));
        if (view.Charter is { } charter && view.Holders.Any(holder => holder.Yours && holder.Governs))
            body.AddChild(Card(Charter(view, charter)));
    }

    /// <summary>Whether two views show the same thing, so a refresh doesn't rebuild under the cursor.</summary>
    private static bool Same(WFWorldDevelopmentView a, WFWorldDevelopmentView b)
    {
        return a.World == b.World && a.Tier == b.Tier && Math.Abs(a.Total - b.Total) < 0.5 && a.Next == b.Next
               && a.Governor == b.Governor && a.Population == b.Population && a.Housing == b.Housing
               && a.Blockaded == b.Blockaded && a.You == b.You && Math.Abs(a.Climate - b.Climate) < 0.01f
               && a.Holders.Count == b.Holders.Count && a.Holders.Zip(b.Holders).All(pair =>
                   pair.First.Key == pair.Second.Key && Math.Abs(pair.First.Points - pair.Second.Points) < 0.5
                   && pair.First.Governs == pair.Second.Governs)
               && a.Output.SequenceEqual(b.Output)
               && (a.Charter == null) == (b.Charter == null)
               && (a.Charter == null || a.Charter.Defence == b.Charter!.Defence && a.Charter.Open == b.Charter.Open
                   && a.Charter.YardKit == b.Charter.YardKit && a.Charter.Permits.SequenceEqual(b.Charter.Permits)
                   && a.Charter.Applicants.SequenceEqual(b.Charter.Applicants) && a.Charter.Berths.SequenceEqual(b.Charter.Berths));
    }

    private static PanelContainer Card(Control content)
    {
        var card = new PanelContainer { HorizontalExpand = true };
        card.AddStyleClass("AngleRect");
        content.Margin = new Thickness(10, 6);
        card.AddChild(content);
        return card;
    }

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddStyleClass("LabelHeading");
        return label;
    }

    private static Label Secondary(string text)
    {
        var label = new Label { Text = text, ClipText = true };
        label.AddStyleClass("LabelSecondaryColor");
        return label;
    }

    private static BoxContainer Row(params Control[] children)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 8 };
        foreach (var child in children)
        {
            row.AddChild(child);
        }

        return row;
    }

    private static string TierName(WFWorldTier tier) => Loc.GetString($"wf-colony-tier-{tier.ToString().ToLowerInvariant()}");

    private Control Summary(WFWorldDevelopmentView view)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-colony-ui-world", ("world", view.World), ("tier", TierName(view.Tier)))));
        if (view.Next is { } next)
        {
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-next", ("points", (int) view.Total), ("next", (int) next),
                ("tier", TierName(view.Tier + 1)))));
            box.AddChild(new ProgressBar
            {
                MinValue = 0, MaxValue = 1, MinHeight = 10, HorizontalExpand = true,
                Value = next <= 0 ? 1 : (float) Math.Clamp(view.Total / next, 0, 1),
            });
        }
        else
        {
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-top", ("points", (int) view.Total))));
        }

        box.AddChild(Secondary(view.Governor is { } governor
            ? Loc.GetString("wf-colony-ui-governor", ("company", governor))
            : Loc.GetString("wf-colony-ui-no-governor")));
        if (view.Tier >= WFWorldTier.Colony)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-people", ("people", view.Population), ("housing", view.Housing))));
        if (view.Blockaded)
            box.AddChild(new Label { Text = Loc.GetString("wf-colony-ui-blockaded"), FontColorOverride = BadColor });
        return box;
    }

    private Control Holders(WFWorldDevelopmentView view)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        box.AddChild(Heading(Loc.GetString("wf-colony-ui-holders")));
        if (view.Holders.Count == 0)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-no-holders")));
        foreach (var holder in view.Holders)
        {
            var name = new Label { Text = holder.Name, HorizontalExpand = true, ClipText = true };
            if (holder.Yours)
                name.FontColorOverride = GoodColor;
            var marks = holder.Governs ? Loc.GetString("wf-colony-ui-governs") : string.Empty;
            box.AddChild(Row(name, Secondary(marks),
                new Label { Text = Loc.GetString("wf-colony-ui-share", ("points", (int) holder.Points), ("share", (int) MathF.Round(holder.Share * 100))) }));
        }

        return box;
    }

    private Control Output(WFWorldDevelopmentView view)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 2 };
        box.AddChild(Heading(Loc.GetString("wf-colony-ui-output")));
        if (view.Tier < WFWorldTier.Colony)
        {
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-output-locked")));
            return box;
        }

        if (view.Climate < 0.99f)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-climate", ("percent", (int) MathF.Round(view.Climate * 100)))));
        if (view.Output.Count == 0)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-no-output")));
        foreach (var output in view.Output)
        {
            var keep = new CheckBox { Text = Loc.GetString("wf-colony-ui-keep"), Pressed = output.Kept };
            var good = output.Good;
            keep.OnToggled += args => OnKeep?.Invoke(good, args.Pressed);
            box.AddChild(Row(new Label { Text = output.Name, HorizontalExpand = true, ClipText = true },
                new Label { Text = Loc.GetString("wf-colony-ui-rate", ("rate", output.Rate.ToString("0.##"))) }, keep));
        }

        return box;
    }

    private Control Charter(WFWorldDevelopmentView view, WFColonyCharterView charter)
    {
        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4 };
        box.AddChild(Heading(Loc.GetString("wf-colony-ui-charter")));

        var defence = new CheckBox { Text = Loc.GetString("wf-colony-ui-defence"), Pressed = charter.Defence };
        defence.OnToggled += args => OnDefence?.Invoke(args.Pressed);
        var open = new CheckBox { Text = Loc.GetString("wf-colony-ui-open"), Pressed = charter.Open };
        open.OnToggled += args => OnOpen?.Invoke(args.Pressed);
        box.AddChild(Row(defence, open));

        box.AddChild(Secondary(Loc.GetString("wf-colony-ui-applicants")));
        if (charter.Applicants.Count == 0)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-none")));
        foreach (var applicant in charter.Applicants)
        {
            box.AddChild(HolderButton(applicant, "wf-colony-ui-grant", () => OnPermit?.Invoke(applicant.Key, true)));
        }

        box.AddChild(Secondary(Loc.GetString("wf-colony-ui-permits")));
        if (charter.Permits.Count == 0)
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-none")));
        foreach (var permit in charter.Permits)
        {
            box.AddChild(HolderButton(permit, "wf-colony-ui-revoke", () => OnPermit?.Invoke(permit.Key, false)));
        }

        if (view.Tier >= WFWorldTier.Dominion)
        {
            box.AddChild(Secondary(Loc.GetString("wf-colony-ui-berths")));
            var opened = charter.Berths.Select(berth => berth.Key).ToHashSet();
            foreach (var holder in view.Holders.Where(holder => holder.Company && !holder.Governs))
            {
                var isOpen = opened.Contains(holder.Key);
                var key = holder.Key;
                box.AddChild(HolderButton(holder, isOpen ? "wf-colony-ui-close-berth" : "wf-colony-ui-open-berth",
                    () => OnBerth?.Invoke(key, !isOpen)));
            }

            var kit = new Button { Text = Loc.GetString("wf-colony-ui-yard-kit"), Disabled = !charter.YardKit };
            kit.OnPressed += _ => OnYardKit?.Invoke();
            box.AddChild(kit);
        }

        return box;
    }

    private static Control HolderButton(WFWorldHolderView holder, string action, Action pressed)
    {
        var button = new Button { Text = Loc.GetString(action) };
        button.OnPressed += _ => pressed();
        return Row(new Label { Text = holder.Name, HorizontalExpand = true, ClipText = true }, button);
    }
}
